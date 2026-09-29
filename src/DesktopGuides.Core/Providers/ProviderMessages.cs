namespace DesktopGuides.Core.Providers;

public static class ProviderMessages
{
    public static string ForIgdb(ProviderErrorKind kind) => kind switch
    {
        ProviderErrorKind.NotConfigured => "Add IGDB credentials in Settings to search.",
        ProviderErrorKind.InvalidCredentials => "IGDB rejected the client ID or secret.",
        ProviderErrorKind.Timeout => "IGDB took too long to respond.",
        ProviderErrorKind.RateLimited => "IGDB is busy. Try again in a moment.",
        ProviderErrorKind.MalformedData => "IGDB returned data the app couldn't read.",
        _ => "Can't reach IGDB. Check your connection.",
    };

    public static string ForSteamGridDb(ProviderErrorKind kind) => kind switch
    {
        ProviderErrorKind.InvalidCredentials => "SteamGridDB rejected the API key.",
        ProviderErrorKind.NotConfigured => "Add a SteamGridDB API key to test it.",
        ProviderErrorKind.MalformedData => "SteamGridDB returned data the app couldn't read.",
        _ => "Can't reach SteamGridDB. Check your connection.",
    };

    public static string NoResults(string query) => $"No games match \"{query}\".";
}
