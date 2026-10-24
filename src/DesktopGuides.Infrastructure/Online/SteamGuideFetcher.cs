using System.Net;
using System.Text;
using DesktopGuides.Core.Online;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;

namespace DesktopGuides.Infrastructure.Online;

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
        await PaceAsync(token);
        ProviderResponse page = await http.SendAsync(HttpMethod.Get, url, SteamClient.Identify, MaxPageBytes, token);
        if (page.StatusCode != HttpStatusCode.OK)
        {
            throw new ProviderException(ProviderErrorKind.Unavailable, $"Steam answered {(int)page.StatusCode} for the guide.");
        }
        GuidePageReading reading = adapter.Read(url, Encoding.UTF8.GetString(page.Body))
            ?? throw new ProviderException(ProviderErrorKind.MalformedData, "This Steam page couldn't be read as a guide.");
        await PaceAsync(token);
        if (await client.GetDetailsAsync(fileId, token) is { } details)
        {
            reading = reading with { SourceUpdated = details.Updated };
        }

        CaptureDraft draft = store.Begin(gameId, reading);
        try
        {
            Dictionary<Uri, string> files = [];
            Uri[] images = reading.Images.Take(MaxImages).ToArray();
            for (int index = 0; index < images.Length; index++)
            {
                await PaceAsync(token);
                if (await ImageAsync(images[index], token) is { } bytes) files[images[index]] = draft.SaveImage(images[index], bytes);
                progress?.Report((index + 1, images.Length));
            }
            draft.Add(reading, files);
            return draft;
        }
        catch
        {
            draft.Discard();
            throw;
        }
    }

    // A missing image becomes the copy's placeholder; a limit or a
    // timeout stops the whole fetch.
    private async Task<byte[]?> ImageAsync(Uri image, CancellationToken token)
    {
        try
        {
            ProviderResponse response = await http.SendAsync(HttpMethod.Get, image, SteamClient.Identify, ProviderHttp.MaxImageBytes, token);
            return response.StatusCode switch
            {
                HttpStatusCode.OK => response.Body,
                HttpStatusCode.TooManyRequests => throw new ProviderException(
                    ProviderErrorKind.RateLimited, "Steam is limiting requests. Try again in a few minutes."),
                _ => null,
            };
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
