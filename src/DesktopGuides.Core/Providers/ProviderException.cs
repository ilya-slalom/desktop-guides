namespace DesktopGuides.Core.Providers;

public enum ProviderErrorKind
{
    NotConfigured,
    InvalidCredentials,
    Unavailable,
    Timeout,
    RateLimited,
    MalformedData
}

public sealed class ProviderException(
    ProviderErrorKind kind, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public ProviderErrorKind Kind { get; } = kind;
}
