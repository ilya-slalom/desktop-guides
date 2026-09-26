using DesktopGuides.Core.Navigation;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ShellNavigatorTests
{
    [Fact]
    public void LaunchStartsAtLibraryWithoutBackHistory()
    {
        ShellNavigator navigator = new();

        Assert.Equal(new LibraryRoute(), navigator.Current);
        Assert.False(navigator.CanGoBack);
        Assert.False(navigator.GoBack());
    }

    [Fact]
    public void ReaderBackReturnsToItsGameAndThenLibrary()
    {
        ShellNavigator navigator = new();
        Guid gameId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();

        navigator.OpenGame(gameId);
        navigator.OpenReader(guideId, gameId);

        Assert.Equal(new ReaderRoute(guideId, gameId), navigator.Current);
        Assert.True(navigator.GoBack());
        Assert.Equal(new GameRoute(gameId), navigator.Current);
        Assert.True(navigator.GoBack());
        Assert.Equal(new LibraryRoute(), navigator.Current);
        Assert.False(navigator.CanGoBack);
    }

    [Fact]
    public void ResumeFromLibraryInsertsTheGuidesGameIntoBackHistory()
    {
        ShellNavigator navigator = new();
        Guid gameId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();

        navigator.OpenReader(guideId, gameId);

        Assert.True(navigator.GoBack());
        Assert.Equal(new GameRoute(gameId), navigator.Current);
        Assert.True(navigator.GoBack());
        Assert.Equal(new LibraryRoute(), navigator.Current);
    }

    [Fact]
    public void SettingsBackRestoresThePriorRoute()
    {
        ShellNavigator navigator = new();
        Guid gameId = Guid.NewGuid();
        navigator.OpenGame(gameId);

        navigator.OpenSettings();
        navigator.OpenSettings();

        Assert.True(navigator.GoBack());
        Assert.Equal(new GameRoute(gameId), navigator.Current);
        Assert.True(navigator.GoBack());
        Assert.Equal(new LibraryRoute(), navigator.Current);
    }

    [Fact]
    public void ReopeningTheSameRouteDoesNotAddBackHistory()
    {
        ShellNavigator navigator = new();
        Guid gameId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();

        navigator.OpenGame(gameId);
        navigator.OpenGame(gameId);
        navigator.OpenReader(guideId, gameId);
        navigator.OpenReader(guideId, gameId);

        Assert.True(navigator.GoBack());
        Assert.Equal(new GameRoute(gameId), navigator.Current);
        Assert.True(navigator.GoBack());
        Assert.Equal(new LibraryRoute(), navigator.Current);
        Assert.False(navigator.CanGoBack);
    }

    [Fact]
    public void EmptyIdsCannotEnterHistory()
    {
        ShellNavigator navigator = new();

        Assert.Throws<ArgumentException>(() => navigator.OpenGame(Guid.Empty));
        Assert.Throws<ArgumentException>(
            () => navigator.OpenReader(Guid.NewGuid(), Guid.Empty));
        Assert.Throws<ArgumentException>(
            () => navigator.OpenReader(Guid.Empty, Guid.NewGuid()));
        Assert.Equal(new LibraryRoute(), navigator.Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingCurrentGameOrGuideResetsToLibrary(bool isReader)
    {
        ShellNavigator navigator = new();
        Guid gameId = Guid.NewGuid();
        navigator.OpenGame(gameId);
        if (isReader)
        {
            navigator.OpenReader(Guid.NewGuid(), gameId);
        }

        navigator.ResetToLibrary();

        Assert.Equal(new LibraryRoute(), navigator.Current);
        Assert.False(navigator.CanGoBack);
        Assert.False(navigator.GoBack());
    }
}
