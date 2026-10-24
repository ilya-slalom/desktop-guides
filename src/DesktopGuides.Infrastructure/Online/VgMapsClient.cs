using System.Net;
using System.Text;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Maps;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;

namespace DesktopGuides.Infrastructure.Online;

/// <summary>
/// S22 PR 5: The Video Game Atlas's pages and maps, fetched by the app one
/// at a time and at least a second apart. Its pages are windows-1252.
/// </summary>
public sealed class VgMapsClient(
    ProviderHttp http, Func<TimeSpan, CancellationToken, Task>? delay = null, long maxMapBytes = MapImageLimits.MaxBytes)
{
    public const int MaxPageBytes = 16 * 1024 * 1024;
    public static readonly Uri Atlas = new("https://vgmaps.com/Atlas/");
    public static readonly TimeSpan Pace = TimeSpan.FromSeconds(1);
    private readonly Func<TimeSpan, CancellationToken, Task> delay = delay ?? Task.Delay;
    private bool started;

    static VgMapsClient() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public async Task<string> GetIndexAsync(CancellationToken token) =>
        Decode(await GetAsync(new Uri(Atlas, "index.htm"), MaxPageBytes, token));

    public async Task<string> GetSystemPageAsync(string systemPath, CancellationToken token) =>
        Decode(await GetAsync(SystemPage(systemPath), MaxPageBytes, token));

    /// <summary>
    /// Streams a map to <paramref name="path"/>. One past the limits is
    /// refused as the import refuses it, too large, not unreadable.
    /// </summary>
    public async Task DownloadMapAsync(Uri map, string path, CancellationToken token)
    {
        if (started) await delay(Pace, token);
        started = true;
        HttpStatusCode status;
        try
        {
            status = await http.DownloadAsync(map, path, SteamClient.Identify, maxMapBytes, token);
        }
        catch (ProviderException error) when (error.Kind == ProviderErrorKind.MalformedData)
        {
            throw new GuideImportException(ImportIssue.TooLarge,
                $"This map is larger than the {maxMapBytes / (1024 * 1024):N0} MB Desktop Guides saves.");
        }
        if (status != HttpStatusCode.OK)
        {
            throw new ProviderException(ProviderErrorKind.Unavailable, $"VGMaps answered {(int)status} for the map.");
        }
    }

    public static Uri SystemPage(string systemPath) => new(Atlas, systemPath);

    /// <summary>UTF-8 when the bytes are, else windows-1252, as the atlas writes its pages.</summary>
    public static string Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    private async Task<byte[]> GetAsync(Uri uri, int maxBytes, CancellationToken token)
    {
        if (started) await delay(Pace, token);
        started = true;
        ProviderResponse response = await http.SendAsync(HttpMethod.Get, uri, SteamClient.Identify, maxBytes, token);
        return response.StatusCode switch
        {
            HttpStatusCode.OK => response.Body,
            HttpStatusCode.TooManyRequests => throw new ProviderException(
                ProviderErrorKind.RateLimited, "VGMaps is limiting requests. Try again in a few minutes."),
            _ => throw new ProviderException(ProviderErrorKind.Unavailable, $"VGMaps answered {(int)response.StatusCode}."),
        };
    }
}
