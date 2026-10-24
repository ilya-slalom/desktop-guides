using System.Net;
using System.Text;
using DesktopGuides.Core.Online;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;

namespace DesktopGuides.Infrastructure.Online;

/// <summary>A fetch that stopped: why, and at which step ("the guide page", "image 3 of 12").</summary>
public sealed class SteamFetchException(ProviderErrorKind kind, string step, Exception inner)
    : Exception($"The Steam fetch stopped at {step}.", inner)
{
    public ProviderErrorKind Kind { get; } = kind;
    public string Step { get; } = step;
}

/// <summary>
/// S22 PR 4: Steam guides are fetched by the app, not captured through the
/// browser: the page, its details and its images, one request at a time
/// and at least a second apart, into a draft that
/// <see cref="OnlineGuideSaver"/> saves. Any failure but a missing image
/// discards the draft, so the library never sees half a guide.
/// </summary>
public sealed class SteamGuideFetcher(
    SteamClient client, ProviderHttp http, CaptureStore store, TimeProvider clock,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    public const int MaxPageBytes = 4 * 1024 * 1024;
    public const int MaxImages = 200;
    public static readonly TimeSpan Pace = TimeSpan.FromSeconds(1);
    private readonly Func<TimeSpan, CancellationToken, Task> delay = delay ?? ((wait, token) => Task.Delay(wait, clock, token));
    private readonly SteamGuideAdapter adapter = new();
    private DateTimeOffset? lastStart;

    /// <summary>Reports (images done, images in the guide) as the images load.</summary>
    public async Task<CaptureDraft> FetchAsync(
        Guid gameId, string fileId, IProgress<(int Done, int Total)>? progress, CancellationToken token)
    {
        Uri url = SteamUrls.Guide(fileId);
        GuidePageReading reading = await StepAsync("the guide page", async () =>
        {
            await PaceAsync(token);
            ProviderResponse page = await http.SendAsync(HttpMethod.Get, url, SteamClient.Identify, MaxPageBytes, token);
            if (page.StatusCode != HttpStatusCode.OK)
            {
                throw new ProviderException(ProviderErrorKind.Unavailable, $"Steam answered {(int)page.StatusCode} for the guide.");
            }
            return adapter.Read(url, Encoding.UTF8.GetString(page.Body))
                ?? throw new ProviderException(ProviderErrorKind.MalformedData, "This Steam page couldn't be read as a guide.");
        });
        // The details only refine the update time; anything but a limit
        // leaves the page's own date.
        try
        {
            await PaceAsync(token);
            if (await client.GetDetailsAsync(fileId, token) is { Updated: { } updated }) reading = reading with { SourceUpdated = updated };
        }
        catch (ProviderException error) when (error.Kind != ProviderErrorKind.RateLimited)
        {
        }
        catch (ProviderException error)
        {
            throw new SteamFetchException(error.Kind, "the guide's details", error);
        }

        CaptureDraft draft = store.Begin(gameId, reading);
        try
        {
            Dictionary<Uri, string> files = [];
            Uri[] images = reading.Images.Take(MaxImages).ToArray();
            for (int index = 0; index < images.Length; index++)
            {
                Uri image = images[index];
                // An image on a host the app doesn't contact is the copy's placeholder, without a wait.
                if (ProviderHttp.AllowedHosts.Contains(image.IdnHost))
                {
                    byte[]? bytes = await StepAsync($"image {index + 1} of {images.Length}", async () =>
                    {
                        await PaceAsync(token);
                        return await ImageAsync(image, token);
                    });
                    if (bytes is not null) files[image] = draft.SaveImage(image, bytes);
                }
                progress?.Report((index + 1, images.Length));
            }
            draft.Add(reading, files);
            return draft;
        }
        catch
        {
            try
            {
                draft.Discard();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Swept at a later start; the fetch's own failure is the one to report.
            }
            throw;
        }
    }

    private static async Task<T> StepAsync<T>(string step, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (ProviderException error)
        {
            throw new SteamFetchException(error.Kind, step, error);
        }
    }

    // A missing image becomes the copy's placeholder; a limit or a
    // timeout stops the whole fetch.
    private async Task<byte[]?> ImageAsync(Uri image, CancellationToken token)
    {
        try
        {
            ProviderResponse response = await http.SendAsync(HttpMethod.Get, image, SteamClient.Identify, ProviderHttp.MaxImageBytes, token);
            // A 429 is thrown by ProviderHttp itself.
            return response.StatusCode == HttpStatusCode.OK ? response.Body : null;
        }
        catch (ProviderException error) when (error.Kind is ProviderErrorKind.Unavailable or ProviderErrorKind.MalformedData)
        {
            return null;
        }
    }

    private async Task PaceAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (lastStart is { } previous && clock.GetUtcNow() - previous is { } since && since < Pace)
        {
            await delay(Pace - since, token);
        }
        lastStart = clock.GetUtcNow();
    }
}
