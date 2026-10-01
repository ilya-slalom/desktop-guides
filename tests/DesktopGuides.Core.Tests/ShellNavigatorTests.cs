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

    [Fact]
    public void OpenGameFromLibraryAnchorsTheLibraryOnThatGame()
    {
        ShellNavigator navigator = new();
        Guid gameId = Guid.NewGuid();

        navigator.OpenGame(gameId);

        Assert.Null(navigator.CurrentAnchor);
        Assert.True(navigator.GoBack());
        Assert.Equal(new LibraryRoute(), navigator.Current);
        Assert.Equal(gameId, navigator.CurrentAnchor);
    }

    [Fact]
    public void OpenReaderAnchorsTheGameOnThatGuide()
    {
        ShellNavigator navigator = new();
        Guid gameId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        navigator.OpenGame(gameId);

        navigator.OpenReader(guideId, gameId);

        Assert.Null(navigator.CurrentAnchor);
        Assert.True(navigator.GoBack());
        Assert.Equal(new GameRoute(gameId), navigator.Current);
        Assert.Equal(guideId, navigator.CurrentAnchor);
    }

    [Fact]
    public void ResumeFromLibraryAnchorsTheGameAndTheLibrary()
    {
        ShellNavigator navigator = new();
        Guid gameId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();

        navigator.OpenReader(guideId, gameId);

        Assert.True(navigator.GoBack());
        Assert.Equal(new GameRoute(gameId), navigator.Current);
        Assert.Equal(guideId, navigator.CurrentAnchor);
        Assert.True(navigator.GoBack());
        Assert.Equal(new LibraryRoute(), navigator.Current);
        Assert.Equal(gameId, navigator.CurrentAnchor);
    }

    [Fact]
    public void SetAnchorIsKeptThroughSettingsAndBack()
    {
        ShellNavigator navigator = new();
        Guid gameId = Guid.NewGuid();
        Guid firstGuide = Guid.NewGuid();
        Guid secondGuide = Guid.NewGuid();
        navigator.OpenGame(gameId);

        navigator.SetAnchor(firstGuide);
        navigator.SetAnchor(secondGuide);
        navigator.OpenSettings();

        Assert.True(navigator.GoBack());
        Assert.Equal(secondGuide, navigator.CurrentAnchor);
    }

    [Fact]
    public void SetAnchorCanClearTheAnchor()
    {
        ShellNavigator navigator = new();
        navigator.OpenGame(Guid.NewGuid());
        navigator.SetAnchor(Guid.NewGuid());

        navigator.SetAnchor(null);

        Assert.Null(navigator.CurrentAnchor);
    }

    [Fact]
    public void SetAnchorThrowsOnReaderAndSettings()
    {
        ShellNavigator navigator = new();
        Guid gameId = Guid.NewGuid();
        navigator.OpenReader(Guid.NewGuid(), gameId);

        Assert.Throws<InvalidOperationException>(() => navigator.SetAnchor(Guid.NewGuid()));
        navigator.OpenSettings();
        Assert.Throws<InvalidOperationException>(() => navigator.SetAnchor(null));
    }

    [Fact]
    public void BackToAnotherGameRestoresItsAnchor()
    {
        ShellNavigator navigator = new();
        Guid firstGame = Guid.NewGuid();
        Guid secondGame = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        navigator.OpenGame(firstGame);
        navigator.SetAnchor(guideId);

        navigator.OpenLibrary();
        navigator.OpenGame(secondGame);

        Assert.True(navigator.GoBack());
        Assert.Equal(new LibraryRoute(), navigator.Current);
        Assert.Equal(secondGame, navigator.CurrentAnchor);
        Assert.True(navigator.GoBack());
        Assert.Equal(new GameRoute(firstGame), navigator.Current);
        Assert.Equal(guideId, navigator.CurrentAnchor);
    }

    [Fact]
    public void ResetToLibraryClearsEveryAnchor()
    {
        ShellNavigator navigator = new();
        Guid gameId = Guid.NewGuid();
        navigator.OpenGame(gameId);
        navigator.SetAnchor(Guid.NewGuid());

        navigator.ResetToLibrary();

        Assert.Null(navigator.CurrentAnchor);
        Assert.False(navigator.CanGoBack);
    }

    [Fact]
    public void OpeningTheCurrentRouteKeepsItsAnchor()
    {
        ShellNavigator navigator = new();
        Guid gameId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        navigator.OpenGame(gameId);
        navigator.SetAnchor(guideId);

        navigator.OpenGame(gameId);

        Assert.Equal(guideId, navigator.CurrentAnchor);
        Assert.True(navigator.GoBack());
        Assert.Equal(new LibraryRoute(), navigator.Current);
        Assert.False(navigator.CanGoBack);
    }

    [Fact]
    public void RouteEqualityIgnoresAnchors()
    {
        ShellNavigator navigator = new();
        Guid gameId = Guid.NewGuid();
        navigator.OpenGame(gameId);
        navigator.SetAnchor(Guid.NewGuid());

        Assert.Equal(new GameRoute(gameId), navigator.Current);
    }
}
