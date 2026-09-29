using DesktopGuides.Core.Providers;
using Xunit;

namespace DesktopGuides.Core.Tests.Providers;

public sealed class ProviderMessagesTests
{
    [Theory]
    [InlineData(ProviderErrorKind.NotConfigured, "Add IGDB credentials in Settings to search.")]
    [InlineData(ProviderErrorKind.InvalidCredentials, "IGDB rejected the client ID or secret.")]
    [InlineData(ProviderErrorKind.Unavailable, "Can't reach IGDB. Check your connection.")]
    [InlineData(ProviderErrorKind.Timeout, "IGDB took too long to respond.")]
    [InlineData(ProviderErrorKind.RateLimited, "IGDB is busy. Try again in a moment.")]
    [InlineData(ProviderErrorKind.MalformedData, "IGDB returned data the app couldn't read.")]
    public void IgdbMessagesMatchTheSpecTable(ProviderErrorKind kind, string expected) =>
        Assert.Equal(expected, ProviderMessages.ForIgdb(kind));

    [Theory]
    [InlineData(ProviderErrorKind.InvalidCredentials, "SteamGridDB rejected the API key.")]
    [InlineData(ProviderErrorKind.Unavailable, "Can't reach SteamGridDB. Check your connection.")]
    [InlineData(ProviderErrorKind.Timeout, "Can't reach SteamGridDB. Check your connection.")]
    public void SteamGridDbMessagesNameTheService(ProviderErrorKind kind, string expected) =>
        Assert.Equal(expected, ProviderMessages.ForSteamGridDb(kind));

    [Fact]
    public void NoResultsMessageQuotesTheQuery() =>
        Assert.Equal("No games match \"zelda\".", ProviderMessages.NoResults("zelda"));

    [Theory]
    [InlineData(ProviderErrorKind.NotConfigured, ProviderRecovery.OpenSettings | ProviderRecovery.AddManually)]
    [InlineData(ProviderErrorKind.InvalidCredentials, ProviderRecovery.OpenSettings)]
    [InlineData(ProviderErrorKind.Unavailable, ProviderRecovery.Retry | ProviderRecovery.AddManually)]
    [InlineData(ProviderErrorKind.Timeout, ProviderRecovery.Retry)]
    [InlineData(ProviderErrorKind.RateLimited, ProviderRecovery.Retry)]
    [InlineData(ProviderErrorKind.MalformedData, ProviderRecovery.AddManually)]
    public void RecoveryActionsMatchTheSpecTable(ProviderErrorKind kind, ProviderRecovery expected) =>
        Assert.Equal(expected, ProviderMessages.RecoveryFor(kind));
}
