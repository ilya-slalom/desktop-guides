using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ReaderContractTests
{
    [Fact]
    public void CommandVisibilityComesFromCapabilities()
    {
        using FakeReader text = new(GuideFormat.Txt,
            ReaderCapabilities.TextSize | ReaderCapabilities.Scroll);
        using FakeReader pdf = new(GuideFormat.Pdf,
            ReaderCapabilities.PageNavigation | ReaderCapabilities.PageJump |
            ReaderCapabilities.FitWidth | ReaderCapabilities.Zoom |
            ReaderCapabilities.SelectableText);

        Assert.Contains(ReaderCommand.TextSize, ReaderCommandPolicy.VisibleCommands(text));
        Assert.DoesNotContain(ReaderCommand.PageJump, ReaderCommandPolicy.VisibleCommands(text));
        Assert.Contains(ReaderCommand.PageJump, ReaderCommandPolicy.VisibleCommands(pdf));
        Assert.DoesNotContain(ReaderCommand.TextSize, ReaderCommandPolicy.VisibleCommands(pdf));
        Assert.DoesNotContain(ReaderCommand.Find, ReaderCommandPolicy.VisibleCommands(pdf));
    }

    [Fact]
    public async Task DispatchesTypedCommandsOnlyToCapableReader()
    {
        using FakeReader text = new(GuideFormat.Txt, ReaderCapabilities.Scroll);
        ReaderAction scroll = new ScrollAction(0.5);

        await ReaderCommandPolicy.ExecuteAsync(text, scroll, CancellationToken.None);
        Assert.Same(scroll, text.LastAction);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            ReaderCommandPolicy.ExecuteAsync(
                text, new PageTurnAction(-1), CancellationToken.None));
        Assert.Same(scroll, text.LastAction);
    }

    [Fact]
    public void CapabilityChangesRefreshAvailableCommands()
    {
        using FakeReader reader = new(GuideFormat.Pdf, ReaderCapabilities.None);
        int notifications = 0;
        reader.CapabilitiesChanged += (_, _) => notifications++;

        reader.SetCapabilities(ReaderCapabilities.PageNavigation);

        Assert.Equal(1, notifications);
        Assert.Contains(ReaderCommand.PageTurn,
            ReaderCommandPolicy.VisibleCommands(reader));
    }

    private sealed class FakeReader(GuideFormat format, ReaderCapabilities capabilities)
        : IReaderSession, IDisposable
    {
        public GuideFormat Format => format;
        public ReaderCapabilities Capabilities { get; private set; } = capabilities;
        public ReaderAction? LastAction { get; private set; }
        public event EventHandler? CapabilitiesChanged;
        public event EventHandler<LocationChangedEventArgs>? LocationChanged
        {
            add { }
            remove { }
        }

        public Task OpenAsync(ManagedGuideSource source, CancellationToken token) =>
            Task.CompletedTask;

        public Task<ReaderLocation> GetLocationAsync(CancellationToken token) =>
            throw new NotSupportedException();

        public Task<RestoreOutcome> RestoreLocationAsync(
            ReaderLocation location, CancellationToken token) =>
            throw new NotSupportedException();

        public Task ApplyAppearanceAsync(
            ReaderAppearance appearance, CancellationToken token) =>
            Task.CompletedTask;

        public Task ExecuteAsync(ReaderAction action, CancellationToken token)
        {
            LastAction = action;
            return Task.CompletedTask;
        }

        public void SetCapabilities(ReaderCapabilities value)
        {
            Capabilities = value;
            CapabilitiesChanged?.Invoke(this, EventArgs.Empty);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose() => GC.SuppressFinalize(this);
    }
}
