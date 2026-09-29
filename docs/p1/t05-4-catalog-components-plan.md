# T05.4 Catalog and Workflow Components Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the artwork row, cover frame, facts, empty-state, busy-row
and status `InfoBar` patterns into one shared resource dictionary, and adopt
them in Add game, Game detail, the status surfaces and the Library game list.

**Architecture:** `Styles/Catalog.xaml` is a `ResourceDictionary` with a
code-behind, so its `DataTemplate` can use compiled `x:Bind`. Rows bind to an
abstract `ArtworkItem`. `GameSearchItem` and a new `LibraryGameItem` derive
from it. `ArtworkListLoader` names containers for UIA and loads Library
thumbnails in phase 1 of `ContainerContentChanging`. It uses the Core
`ArtworkLoadTickets` rule, so a recycled container never shows a stale cover.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK 2.5.1), CommunityToolkit
`MetadataControl` 8.2.251219, xUnit, PowerShell 5.1 UI Automation harness,
GitHub Actions `windows-ci.yml`.

**Spec:** `docs/p1/t05-4-catalog-components-design.md`

## Global Constraints

- Use a style or a data template. Don't wrap a standard control in a user control.
- Add no new packages. `CommunityToolkit.WinUI.Controls.MetadataControl` stays at `8.2.251219`.
- `HeaderedContentControl` stays with T06.1.
- Catalog rendering makes no provider request. The Library artwork loader reads only
  `ManagedArtworkStore.ResolveFile` paths.
- Keep every existing `x:Name`, `AutomationProperties.AutomationId`, live setting and
  closing behavior at each adopted use site.
- Add game result rows keep the UIA name `"{Title}, {Summary}, {Platforms}"`.
- Library rows keep the UIA name equal to the game title.
- Library rows decode at 90 px wide. Game detail keeps 240 px.
- The tile is 45×60 DIPs and the cover frame is 120×180 DIPs, both with 4-DIP corners and
  `ControlFillColorSecondaryBrush`.
- PowerShell scripts stay ASCII-only, because Windows PowerShell 5.1 reads BOM-less files
  as ANSI. Build non-ASCII strings from code points.
- CI `production-shell-ui` is the gate of record for the installed UI. The Windows host
  runs only `-ProviderOnly` live checks, or debugging.
- Never print, copy or log provider credential values.

## Rulings against the spec

These rulings resolve spec text that conflicts with the code. Task 6 records them in
the design doc's verification record.

- **Title length: 160, not 200.** `GameDetails.TitleLimit` is 160. The long title is
  `"Catalog A " + new string('W', 150)`, which is exactly 160 characters. It is still
  far wider than any row.
- **Keyboard sequence.** `GameList` uses single selection, and selection follows
  focus, so plain Down opens a game. The check is: Tab into the list, then Ctrl+Down
  (moves focus without selecting), then Enter, which opens the focused game. After
  Back, Tab in again and press End. End selects the last row, which opens the last
  game. The existing guide-list check uses the same Ctrl+arrow approach.
- **`LibraryGamePresentation` location.** It goes in `src/DesktopGuides.Core/Library/`,
  next to the `Game` model, rather than in the provider presentation folder, because
  it describes Library games, not provider results.
- **Realized-row bound.** "Fewer than 80" counts `ListItem` descendants of `GameList`
  that have non-empty bounds, so virtualized items without a container are
  excluded. The count includes realized off-screen containers in the list's cache.

## Review Focus

1. A recycled container must not show another game's cover after fast scrolling. Tests:
   the `ArtworkLoadTickets` superseded-ticket test in Task 1, and the end-of-list
   screenshot in Task 5.
2. A Library re-render while thumbnails are loading must not apply old results to new
   rows. Tests: `ReleaseAll` makes every ticket stale (Task 1), and `CancelAll` runs
   before `ItemsSource` is replaced (Task 3).
3. Add game rows must keep their `"Title, Summary, Platforms"` names, or the provider
   scenarios break. Test: the existing `provider-*` smoke modes, run on CI (skipped
   without keys) and on the host with `-ProviderOnly` (Task 5, Step 6).
4. Tabbing into the Library must not open a game. Test: the catalog keyboard step
   checks that the Library heading is still shown after Tab (Task 5).
5. Corrupt or missing artwork must fall back to the placeholder with no error
   status. Tests: the corrupt-art and missing-art seed rows, and the `ShellStatus`
   check in the catalog scenario (Tasks 4 and 5).

## File Structure

| File | Responsibility |
| --- | --- |
| `src/DesktopGuides.Core/Library/LibraryGamePresentation.cs` (new) | Row strings for a Library game |
| `src/DesktopGuides.Core/Library/ArtworkLoadTickets.cs` (new) | Per-container ticket rule for applying decoded artwork |
| `tests/DesktopGuides.Core.Tests/LibraryGamePresentationTests.cs` (new) | Unit tests |
| `tests/DesktopGuides.Core.Tests/ArtworkLoadTicketsTests.cs` (new) | Unit tests |
| `src/DesktopGuides.Production/Styles/Catalog.xaml` + `.xaml.cs` (new) | Shared styles and the artwork row template |
| `src/DesktopGuides.Production/ArtworkItem.cs` (new) | Abstract row view model |
| `src/DesktopGuides.Production/ArtworkListLoader.cs` (new) | Container naming and phased thumbnail loading |
| `src/DesktopGuides.Production/LibraryGameItem.cs` (new) | Library row view model |
| `src/DesktopGuides.Production/App.xaml` | Merge `Catalog` |
| `src/DesktopGuides.Production/AddGameDialog.xaml` + `.cs` | Adopt the template and styles |
| `src/DesktopGuides.Production/ShellWindow.xaml` + `.cs` | Adopt the template, loader and styles |
| `src/DesktopGuides.Production/GameEditorDialog.xaml` | Adopt the InfoBar style |
| `src/DesktopGuides.Production/ProviderSettingsCard.xaml` | Adopt the InfoBar style |
| `tools/p1/DesktopGuides.ShellSeed/Program.cs` | `seed-catalog` mode |
| `tools/p1/windows_shell_ui_smoke.ps1` | `catalog` mode |
| `tools/p1/windows_shell_install.ps1` | `-CatalogOnly`, `Run-CatalogScenarios`, full-run step |
| `docs/p1/e2e-testing.md`, `docs/p1/implementation-plan.md`, `docs/progress.md`, design doc | Status and gates |

## Host commands

The Mac has no `dotnet` or `pwsh`. Build and unit-test on the Windows host over SSH.
Stage a fresh copy for each run:

```bash
s(){ ssh -o BatchMode=yes -o LogLevel=ERROR pcsx2-win "$@"; }
s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t05-4) { Remove-Item -Recurse -Force E:\work\desktop-guides\t05-4 }; New-Item -ItemType Directory E:\work\desktop-guides\t05-4 | Out-Null"'
tar --exclude=.claude --exclude=.git --exclude=.superpowers -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t05-4'
```

Commands referred to below as **Core tests**, **Production build** and **Seed build**:

```bash
s 'cd /d E:\work\desktop-guides\t05-4 && dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release'
s 'cd /d E:\work\desktop-guides\t05-4 && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64'
s 'cd /d E:\work\desktop-guides\t05-4 && dotnet build tools\p1\DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj -c Release'
```

Infrastructure tests use the same pattern with
`tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj`.
Run PowerShell scripts with `powershell -NoProfile -ExecutionPolicy Bypass -File`.

---

### Task 1: Core presentation and artwork ticket rules

**Files:**
- Create: `src/DesktopGuides.Core/Library/LibraryGamePresentation.cs`
- Create: `src/DesktopGuides.Core/Library/ArtworkLoadTickets.cs`
- Test: `tests/DesktopGuides.Core.Tests/LibraryGamePresentationTests.cs`
- Test: `tests/DesktopGuides.Core.Tests/ArtworkLoadTicketsTests.cs`

**Interfaces:**
- Consumes: `DesktopGuides.Core.Library.Game` (`Id`, `Title`, `Platform`, `Notes`, `CreatedUtc`, `UpdatedUtc`, `Link`, `Metadata`, `ArtworkRelativePath`).
- Produces:
  - `public static class LibraryGamePresentation` with `string? Summary(Game game)` and `string AccessibleName(Game game)`.
  - `public sealed class ArtworkLoadTicket` with `CancellationToken Token` and `bool IsCancelled`.
  - `public sealed class ArtworkLoadTickets<TContainer> where TContainer : class` with
    `ArtworkLoadTicket Issue(TContainer container)`, `void Release(TContainer container)`,
    `void ReleaseAll()` and `bool IsCurrent(TContainer container, ArtworkLoadTicket ticket)`.

- [ ] **Step 1: Write the failing presentation tests**

Create `tests/DesktopGuides.Core.Tests/LibraryGamePresentationTests.cs`:

```csharp
using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Tests;

public sealed class LibraryGamePresentationTests
{
    private static Game GameWith(string title, string? platform) => new(
        Guid.NewGuid(), title, platform, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    [Fact]
    public void SummaryIsTheTrimmedPlatform()
    {
        Assert.Equal("Nintendo Switch", LibraryGamePresentation.Summary(GameWith("Zelda", "  Nintendo Switch ")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SummaryIsNullWhenThePlatformIsBlank(string? platform)
    {
        Assert.Null(LibraryGamePresentation.Summary(GameWith("Zelda", platform)));
    }

    [Theory]
    [InlineData("ゼルダの伝説")]
    [InlineData("كتالوج الألعاب")]
    [InlineData("Catalog D Größe Überfall Äpfel")]
    public void AccessibleNameIsTheFullTitle(string title)
    {
        Assert.Equal(title, LibraryGamePresentation.AccessibleName(GameWith(title, "PC")));
    }

    [Fact]
    public void AccessibleNameKeepsATitleAtTheLengthLimit()
    {
        string title = "Catalog A " + new string('W', 150);
        Assert.Equal(GameDetails.TitleLimit, title.Length);
        Assert.Equal(title, LibraryGamePresentation.AccessibleName(GameWith(title, "PC")));
    }
}
```

The limit test uses the same 160-character title that the Task 4 seed writes.

- [ ] **Step 2: Write the failing ticket tests**

Create `tests/DesktopGuides.Core.Tests/ArtworkLoadTicketsTests.cs`:

```csharp
using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Tests;

public sealed class ArtworkLoadTicketsTests
{
    private sealed class Container;

    [Fact]
    public void AnIssuedTicketIsCurrent()
    {
        ArtworkLoadTickets<Container> tickets = new();
        Container row = new();
        ArtworkLoadTicket ticket = tickets.Issue(row);
        Assert.True(tickets.IsCurrent(row, ticket));
        Assert.False(ticket.IsCancelled);
        Assert.False(ticket.Token.IsCancellationRequested);
    }

    [Fact]
    public void ASupersededTicketIsNotCurrentAndIsCancelled()
    {
        ArtworkLoadTickets<Container> tickets = new();
        Container row = new();
        ArtworkLoadTicket first = tickets.Issue(row);
        ArtworkLoadTicket second = tickets.Issue(row);
        Assert.False(tickets.IsCurrent(row, first));
        Assert.True(first.IsCancelled);
        Assert.True(first.Token.IsCancellationRequested);
        Assert.True(tickets.IsCurrent(row, second));
    }

    [Fact]
    public void AReleasedTicketIsNotCurrentAndIsCancelled()
    {
        ArtworkLoadTickets<Container> tickets = new();
        Container row = new();
        ArtworkLoadTicket ticket = tickets.Issue(row);
        tickets.Release(row);
        Assert.False(tickets.IsCurrent(row, ticket));
        Assert.True(ticket.IsCancelled);
    }

    [Fact]
    public void ReleaseAllCancelsEveryTicket()
    {
        ArtworkLoadTickets<Container> tickets = new();
        Container first = new();
        Container second = new();
        ArtworkLoadTicket a = tickets.Issue(first);
        ArtworkLoadTicket b = tickets.Issue(second);
        tickets.ReleaseAll();
        Assert.False(tickets.IsCurrent(first, a));
        Assert.False(tickets.IsCurrent(second, b));
        Assert.True(a.IsCancelled);
        Assert.True(b.IsCancelled);
    }

    [Fact]
    public void ContainersAreIndependent()
    {
        ArtworkLoadTickets<Container> tickets = new();
        Container first = new();
        Container second = new();
        ArtworkLoadTicket a = tickets.Issue(first);
        ArtworkLoadTicket b = tickets.Issue(second);
        tickets.Release(second);
        Assert.True(tickets.IsCurrent(first, a));
        Assert.False(tickets.IsCurrent(second, b));
    }

    [Fact]
    public void ATicketIsNotCurrentForAnotherContainer()
    {
        ArtworkLoadTickets<Container> tickets = new();
        Container first = new();
        Container second = new();
        ArtworkLoadTicket a = tickets.Issue(first);
        tickets.Issue(second);
        Assert.False(tickets.IsCurrent(second, a));
    }

    [Fact]
    public void ACancelledTokenStaysReadable()
    {
        ArtworkLoadTickets<Container> tickets = new();
        Container row = new();
        ArtworkLoadTicket ticket = tickets.Issue(row);
        tickets.ReleaseAll();
        Assert.Throws<OperationCanceledException>(() => ticket.Token.ThrowIfCancellationRequested());
    }
}
```

`ACancelledTokenStaysReadable` pins the no-dispose rule: a disposed source makes
`Token` throw `ObjectDisposedException`, and a late `await` would crash the UI thread.

- [ ] **Step 3: Run the tests to verify they fail**

Stage the tree, then run **Core tests**.
Expected: the build fails with `CS0103`/`CS0246` for `LibraryGamePresentation`,
`ArtworkLoadTickets` and `ArtworkLoadTicket`.

- [ ] **Step 4: Implement `LibraryGamePresentation`**

Create `src/DesktopGuides.Core/Library/LibraryGamePresentation.cs`:

```csharp
namespace DesktopGuides.Core.Library;

// Row strings for a Library game. The accessible name stays the title, because
// Library automation and screen-reader users identify rows by title.
public static class LibraryGamePresentation
{
    public static string? Summary(Game game) =>
        string.IsNullOrWhiteSpace(game.Platform) ? null : game.Platform.Trim();

    public static string AccessibleName(Game game) => game.Title;
}
```

- [ ] **Step 5: Implement the ticket rule**

Create `src/DesktopGuides.Core/Library/ArtworkLoadTickets.cs`:

```csharp
namespace DesktopGuides.Core.Library;

public sealed class ArtworkLoadTicket
{
    // The source is never disposed. A decode that finishes after the ticket is
    // cancelled may still read Token, and a disposed source would throw.
    private readonly CancellationTokenSource source = new();

    public CancellationToken Token => source.Token;

    public bool IsCancelled => source.IsCancellationRequested;

    internal void Cancel() => source.Cancel();
}

// Pairs each list container with the artwork load it is waiting for. A decoded
// image may be applied only while its ticket is still the container's current one,
// so a recycled container never shows another item's artwork.
public sealed class ArtworkLoadTickets<TContainer> where TContainer : class
{
    private readonly Dictionary<TContainer, ArtworkLoadTicket> current =
        new(ReferenceEqualityComparer.Instance);

    public ArtworkLoadTicket Issue(TContainer container)
    {
        Release(container);
        ArtworkLoadTicket ticket = new();
        current[container] = ticket;
        return ticket;
    }

    public void Release(TContainer container)
    {
        if (current.Remove(container, out ArtworkLoadTicket? ticket)) ticket.Cancel();
    }

    public void ReleaseAll()
    {
        foreach (ArtworkLoadTicket ticket in current.Values) ticket.Cancel();
        current.Clear();
    }

    public bool IsCurrent(TContainer container, ArtworkLoadTicket ticket) =>
        !ticket.IsCancelled &&
        current.TryGetValue(container, out ArtworkLoadTicket? active) &&
        ReferenceEquals(active, ticket);
}
```

`Dictionary<TKey, TValue>` takes an `IEqualityComparer<TKey>`, and
`ReferenceEqualityComparer` implements `IEqualityComparer<object?>`. Because the
interface is contravariant, it converts to `IEqualityComparer<TContainer>` when
`TContainer` is a reference type, which the `class` constraint guarantees.

- [ ] **Step 6: Run the tests to verify they pass**

Stage the tree, then run **Core tests** and the Infrastructure tests.
Expected: all tests pass, including 8 new presentation cases and 7 new ticket tests.

- [ ] **Step 7: Commit**

```bash
git add src/DesktopGuides.Core/Library/LibraryGamePresentation.cs \
  src/DesktopGuides.Core/Library/ArtworkLoadTickets.cs \
  tests/DesktopGuides.Core.Tests/LibraryGamePresentationTests.cs \
  tests/DesktopGuides.Core.Tests/ArtworkLoadTicketsTests.cs
git commit -m "feat(core): add Library row presentation and artwork load tickets" \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Shared catalog resources, adopted by Add game

No unit test harness exists for WinUI XAML in this repo, so this task has no
red/green cycle. Its gates are the Production build and the existing
`provider-*` smoke modes, which find Add game rows by their UIA names. This
TDD skip is deliberate.

**Files:**
- Create: `src/DesktopGuides.Production/Styles/Catalog.xaml`
- Create: `src/DesktopGuides.Production/Styles/Catalog.xaml.cs`
- Create: `src/DesktopGuides.Production/ArtworkItem.cs`
- Create: `src/DesktopGuides.Production/ArtworkListLoader.cs`
- Modify: `src/DesktopGuides.Production/App.xaml`
- Modify: `src/DesktopGuides.Production/AddGameDialog.xaml`
- Modify: `src/DesktopGuides.Production/AddGameDialog.xaml.cs:15-45` (`GameSearchItem`) and the constructor at `:60-69`

**Interfaces:**
- Consumes: `ArtworkLoadTickets<TContainer>` and `ArtworkLoadTicket` from Task 1.
- Produces:
  - Resource keys: `DesktopGuidesArtworkTileStyle` (`Border`), `DesktopGuidesCoverFrameStyle`
    (`Border`), `DesktopGuidesArtworkRowTemplate` (`DataTemplate` over `ArtworkItem`),
    `DesktopGuidesFactsStyle` (`toolkit:MetadataControl`), `DesktopGuidesEmptyStateStyle`
    (`Border`), `DesktopGuidesBusyRowStyle` (`StackPanel`), `DesktopGuidesStatusInfoBarStyle`
    (`InfoBar`).
  - `public abstract class ArtworkItem : INotifyPropertyChanged` with `Title`, `Summary`,
    `Detail`, `AccessibleName`, `HasSummary`, `HasDetail` and `ImageSource? Thumbnail`
    (internal setter).
  - `internal sealed class ArtworkListLoader` with `static void NameRows(ListViewBase list)`,
    `static ArtworkListLoader Attach(ListViewBase list, Func<ArtworkItem, CancellationToken, Task<ImageSource?>> load)`
    and `void CancelAll()`.

- [ ] **Step 1: Create the shared dictionary**

Create `src/DesktopGuides.Production/Styles/Catalog.xaml`:

```xml
<ResourceDictionary
    x:Class="DesktopGuides.Production.Styles.Catalog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:DesktopGuides.Production"
    xmlns:toolkit="using:CommunityToolkit.WinUI.Controls">

    <Style x:Key="DesktopGuidesArtworkTileStyle" TargetType="Border">
        <Setter Property="Width" Value="45" />
        <Setter Property="Height" Value="60" />
        <Setter Property="CornerRadius" Value="4" />
        <Setter Property="Background" Value="{ThemeResource ControlFillColorSecondaryBrush}" />
    </Style>

    <Style x:Key="DesktopGuidesCoverFrameStyle"
           TargetType="Border"
           BasedOn="{StaticResource DesktopGuidesArtworkTileStyle}">
        <Setter Property="Width" Value="120" />
        <Setter Property="Height" Value="180" />
        <Setter Property="VerticalAlignment" Value="Top" />
    </Style>

    <!-- One fixed element tree per row: no nested items host and no per-item
         Toolkit control, so virtualized lists stay cheap. MinHeight comes from
         the tile, so text scaling can still grow the row. -->
    <DataTemplate x:Key="DesktopGuidesArtworkRowTemplate" x:DataType="local:ArtworkItem">
        <Grid Padding="0,8"
              ColumnSpacing="{StaticResource DesktopGuidesSpacing12}"
              AutomationProperties.Name="{x:Bind AccessibleName}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="45" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>
            <Border Style="{StaticResource DesktopGuidesArtworkTileStyle}">
                <Grid>
                    <FontIcon Glyph="&#xE7FC;"
                              FontSize="16"
                              Foreground="{StaticResource DesktopGuidesSecondaryTextBrush}"
                              AutomationProperties.AccessibilityView="Raw" />
                    <Image Source="{x:Bind Thumbnail, Mode=OneWay}"
                           Stretch="UniformToFill"
                           AutomationProperties.AccessibilityView="Raw" />
                </Grid>
            </Border>
            <StackPanel Grid.Column="1"
                        VerticalAlignment="Center"
                        Spacing="{StaticResource DesktopGuidesSpacing4}">
                <TextBlock Text="{x:Bind Title}"
                           Style="{StaticResource DesktopGuidesBodyStyle}"
                           TextWrapping="NoWrap"
                           TextTrimming="CharacterEllipsis" />
                <TextBlock Text="{x:Bind Summary}"
                           Visibility="{x:Bind HasSummary}"
                           Style="{StaticResource DesktopGuidesMetadataStyle}"
                           TextWrapping="NoWrap"
                           TextTrimming="CharacterEllipsis" />
                <TextBlock Text="{x:Bind Detail}"
                           Visibility="{x:Bind HasDetail}"
                           Style="{StaticResource DesktopGuidesMetadataStyle}"
                           TextWrapping="NoWrap"
                           TextTrimming="CharacterEllipsis" />
            </StackPanel>
        </Grid>
    </DataTemplate>

    <Style x:Key="DesktopGuidesFactsStyle" TargetType="toolkit:MetadataControl">
        <Setter Property="TextBlockStyle" Value="{StaticResource DesktopGuidesSecondaryBodyStyle}" />
    </Style>

    <Style x:Key="DesktopGuidesEmptyStateStyle"
           TargetType="Border"
           BasedOn="{StaticResource DesktopGuidesElevatedSurfaceStyle}">
        <Setter Property="VerticalAlignment" Value="Top" />
    </Style>

    <Style x:Key="DesktopGuidesBusyRowStyle" TargetType="StackPanel">
        <Setter Property="Orientation" Value="Horizontal" />
        <Setter Property="Spacing" Value="{StaticResource DesktopGuidesSpacing12}" />
    </Style>

    <Style x:Key="DesktopGuidesStatusInfoBarStyle" TargetType="InfoBar">
        <Setter Property="IsClosable" Value="False" />
        <Setter Property="HorizontalAlignment" Value="Stretch" />
    </Style>
</ResourceDictionary>
```

The template root keeps `AutomationProperties.Name`, as the Add game template does
today. `ArtworkListLoader` also names the container, because UIA reads the row name
from the `ListViewItem`.

Create `src/DesktopGuides.Production/Styles/Catalog.xaml.cs`:

```csharp
using Microsoft.UI.Xaml;

namespace DesktopGuides.Production.Styles;

// The code-behind lets DesktopGuidesArtworkRowTemplate use compiled x:Bind.
public sealed partial class Catalog : ResourceDictionary
{
    public Catalog() => InitializeComponent();
}
```

- [ ] **Step 2: Merge it in `App.xaml`**

Add `xmlns:styles="using:DesktopGuides.Production.Styles"` to the `Application`
element, and add the instance after `Controls.xaml`:

```xml
                <ResourceDictionary Source="ms-appx:///Styles/Controls.xaml" />
                <styles:Catalog />
                <ResourceDictionary Source="ms-appx:///Styles/Dialogs.xaml" />
```

A dictionary with `x:Class` must be merged as an instance. `Source=` would skip the
generated `x:Bind` code.

- [ ] **Step 3: Create `ArtworkItem`**

Create `src/DesktopGuides.Production/ArtworkItem.cs`:

```csharp
using System.ComponentModel;
using Microsoft.UI.Xaml.Media;

namespace DesktopGuides.Production;

// The view model behind DesktopGuidesArtworkRowTemplate.
public abstract class ArtworkItem : INotifyPropertyChanged
{
    private ImageSource? thumbnail;

    protected ArtworkItem(string title, string? summary, string? detail, string accessibleName)
    {
        Title = title;
        Summary = summary;
        Detail = detail;
        AccessibleName = accessibleName;
    }

    public string Title { get; }
    public string? Summary { get; }
    public string? Detail { get; }
    public string AccessibleName { get; }
    public bool HasSummary => Summary is not null;
    public bool HasDetail => Detail is not null;

    public event PropertyChangedEventHandler? PropertyChanged;

    // Starts empty, so the placeholder tile shows, and is set once artwork has loaded.
    public ImageSource? Thumbnail
    {
        get => thumbnail;
        internal set
        {
            if (ReferenceEquals(thumbnail, value)) return;
            thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        }
    }
}
```

- [ ] **Step 4: Create `ArtworkListLoader`**

Create `src/DesktopGuides.Production/ArtworkListLoader.cs`:

```csharp
using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DesktopGuides.Production;

// Names each realized row for UIA and, when a loader is supplied, decodes its
// artwork in phase 1 of ContainerContentChanging. Recycled rows cancel their
// load and drop their thumbnail, so a reused container never shows stale art.
internal sealed class ArtworkListLoader
{
    private readonly Func<ArtworkItem, CancellationToken, Task<ImageSource?>>? load;
    private readonly ArtworkLoadTickets<SelectorItem> tickets = new();

    private ArtworkListLoader(Func<ArtworkItem, CancellationToken, Task<ImageSource?>>? load) =>
        this.load = load;

    // Add game uses naming only: its thumbnails come from the provider loader.
    public static void NameRows(ListViewBase list) =>
        list.ContainerContentChanging += new ArtworkListLoader(null).ContainerContentChanging;

    public static ArtworkListLoader Attach(
        ListViewBase list, Func<ArtworkItem, CancellationToken, Task<ImageSource?>> load)
    {
        ArtworkListLoader loader = new(load);
        list.ContainerContentChanging += loader.ContainerContentChanging;
        return loader;
    }

    // Call before replacing ItemsSource, so loads in flight can't apply to new rows.
    public void CancelAll() => tickets.ReleaseAll();

    private void ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is not SelectorItem container) return;
        if (args.InRecycleQueue)
        {
            tickets.Release(container);
            if (args.Item is ArtworkItem recycled && load is not null) recycled.Thumbnail = null;
            return;
        }
        if (args.Item is not ArtworkItem item) return;
        if (args.Phase == 0)
        {
            AutomationProperties.SetName(container, item.AccessibleName);
            if (load is not null) args.RegisterUpdateCallback(1, ContainerContentChanging);
            return;
        }
        if (args.Phase == 1 && load is not null) _ = LoadAsync(container, item);
    }

    private async Task LoadAsync(SelectorItem container, ArtworkItem item)
    {
        ArtworkLoadTicket ticket = tickets.Issue(container);
        ImageSource? image;
        try
        {
            image = await load!(item, ticket.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (tickets.IsCurrent(container, ticket)) item.Thumbnail = image;
    }
}
```

`args.Handled` stays false, so the template still renders its `x:Bind` values in
phase 0. The `load` delegate must catch its own I/O errors and return null; Task 3's
delegate does.

- [ ] **Step 5: Derive `GameSearchItem` from `ArtworkItem`**

In `src/DesktopGuides.Production/AddGameDialog.xaml.cs`, replace the whole
`GameSearchItem` class (lines 15–45) with:

```csharp
public sealed class GameSearchItem : ArtworkItem
{
    internal GameSearchItem(ProviderSearchResult result)
        : this(
            result,
            GameMetadataPresentation.ResultSummary(result),
            GameMetadataPresentation.PlatformSummary(result.Platforms) ?? "No platforms listed")
    {
    }

    private GameSearchItem(ProviderSearchResult result, string summary, string platforms)
        : base(result.Title, summary, platforms, $"{result.Title}, {summary}, {platforms}")
    {
        Result = result;
    }

    internal ProviderSearchResult Result { get; }
}
```

`GameMetadataPresentation.ResultSummary` returns a non-null `string`, and
`PlatformSummary` is coalesced, so the UIA name is unchanged.

Remove `using System.ComponentModel;` if nothing else in the file uses it. The line
`item.Thumbnail = await DecodeThumbnailAsync(bytes);` keeps compiling, because the
setter is internal to the same assembly.

In the `AddGameDialog` constructor, after `InitializeComponent();`, add:

```csharp
        ArtworkListLoader.NameRows(GameSearchResults);
```

- [ ] **Step 6: Adopt the template and styles in `AddGameDialog.xaml`**

Replace the `GameSearchBusy` opening tag with:

```xml
        <StackPanel x:Name="GameSearchBusy"
                    Grid.Row="1"
                    Style="{StaticResource DesktopGuidesBusyRowStyle}"
                    Visibility="Collapsed">
```

Replace the `GameSearchStatus` opening tag with:

```xml
        <InfoBar x:Name="GameSearchStatus"
                 Grid.Row="2"
                 Style="{StaticResource DesktopGuidesStatusInfoBarStyle}"
                 IsOpen="False"
                 AutomationProperties.AutomationId="GameSearchStatus">
```

Replace the whole `GameSearchResults` `ListView`, including its inline
`ListView.ItemTemplate`, with:

```xml
        <ListView x:Name="GameSearchResults"
                  Grid.Row="3"
                  SelectionMode="None"
                  IsItemClickEnabled="True"
                  ItemClick="ResultClicked"
                  ItemTemplate="{StaticResource DesktopGuidesArtworkRowTemplate}"
                  AutomationProperties.Name="Search results"
                  AutomationProperties.AutomationId="GameSearchResults" />
```

Remove `xmlns:local` from the dialog if nothing else uses it.

- [ ] **Step 7: Build and run the existing checks**

Stage the tree, then run **Production build** and **Core tests**.
Expected: the build succeeds with no new warnings, and all tests pass.
If the build reports `WMC1110` or a missing `InitializeComponent` for `Catalog`,
confirm the file is a `Page` item (the SDK globbing includes `Styles\*.xaml`) and
that `x:Class` matches the code-behind namespace.

- [ ] **Step 8: Commit**

```bash
git add src/DesktopGuides.Production/Styles/Catalog.xaml \
  src/DesktopGuides.Production/Styles/Catalog.xaml.cs \
  src/DesktopGuides.Production/ArtworkItem.cs \
  src/DesktopGuides.Production/ArtworkListLoader.cs \
  src/DesktopGuides.Production/App.xaml \
  src/DesktopGuides.Production/AddGameDialog.xaml \
  src/DesktopGuides.Production/AddGameDialog.xaml.cs
git commit -m "feat(ui): add shared catalog resources and move Add game onto them" \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Library list and Game detail on the shared resources

The WinUI glue has no unit harness either. Task 1's tests cover the rules this
task relies on (`LibraryGamePresentation`, the tickets), and Task 5's installed
`catalog` scenario checks the behavior. This TDD skip is deliberate.

**Files:**
- Create: `src/DesktopGuides.Production/LibraryGameItem.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml:79-87` (`ShellStatusInfoBar`), `:129-155` (`LibraryEmptyState`, `GameList`), `:203-212` (`GameCoverFrame`), `:233-234` (`GameFacts`), `:288-291` (`GameEmptyState`)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs:34`, `:67-70`, `:360-398`, `:868`, `:930`, `:1047-1064`
- Modify: `src/DesktopGuides.Production/GameEditorDialog.xaml:11-15`, `:54-58`
- Modify: `src/DesktopGuides.Production/ProviderSettingsCard.xaml:38-40`

**Interfaces:**
- Consumes: `ArtworkItem`, `ArtworkListLoader.Attach`, `ArtworkListLoader.CancelAll` and the
  resource keys from Task 2; `LibraryGamePresentation.Summary` and `.AccessibleName` from Task 1.
- Produces: `public sealed class LibraryGameItem : ArtworkItem` with `Game Game` and
  `string? ArtworkRelativePath`. Library `ListViewItem`s have UIA names equal to the game title.

- [ ] **Step 1: Create `LibraryGameItem`**

Create `src/DesktopGuides.Production/LibraryGameItem.cs`:

```csharp
using DesktopGuides.Core.Library;

namespace DesktopGuides.Production;

public sealed class LibraryGameItem : ArtworkItem
{
    internal LibraryGameItem(Game game)
        : base(
            game.Title,
            LibraryGamePresentation.Summary(game),
            null,
            LibraryGamePresentation.AccessibleName(game))
    {
        Game = game;
    }

    internal Game Game { get; }
    internal string? ArtworkRelativePath => Game.ArtworkRelativePath;
}
```

`Detail` is null until T05.1 adds per-row facts.

- [ ] **Step 2: Put `GameList` on the row template**

In `ShellWindow.xaml`, replace the `GameList` element with:

```xml
                    <ListView x:Name="GameList"
                              SelectionMode="Single"
                              Style="{StaticResource DesktopGuidesListStyle}"
                              ItemTemplate="{StaticResource DesktopGuidesArtworkRowTemplate}"
                              SelectionChanged="GameSelected"
                              AutomationProperties.AutomationId="GameList"
                              AutomationProperties.Name="Games" />
```

`DisplayMemberPath` is removed: it can't be combined with `ItemTemplate`.

- [ ] **Step 3: Attach the loader and read `LibraryGameItem`**

In `ShellWindow.xaml.cs`, add fields next to `private ManagedArtworkStore? artwork;` (line 34):

```csharp
    private const int RowArtworkDecodeWidth = 90;
    private const int DetailCoverDecodeWidth = 240;
    private readonly ArtworkListLoader gameArtwork;
```

In the constructor, before the `GameList.AddHandler` calls (line 67), add:

```csharp
        gameArtwork = ArtworkListLoader.Attach(GameList, LoadRowArtworkAsync);
```

Replace `GameSelected`, `GameTapped`, `GameFromRow` and `GameKeyDown` (lines 360–398) with:

```csharp
    private async void GameSelected(object sender, SelectionChangedEventArgs args)
    {
        if (GameList.SelectedItem is LibraryGameItem item)
        {
            await RunNavigationAsync(() => OpenGameAsync(item.Game.Id));
        }
    }

    private async void GameTapped(object sender, TappedRoutedEventArgs args)
    {
        if (GameFromRow(args.OriginalSource as DependencyObject) is Game game)
        {
            await RunNavigationAsync(() => OpenGameAsync(game.Id));
        }
    }

    private Game? GameFromRow(DependencyObject? source)
    {
        while (source is not null && !ReferenceEquals(source, GameList))
        {
            if (source is ListViewItem row && row.Content is LibraryGameItem item)
            {
                return item.Game;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private async void GameKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter &&
            GameFromRow(args.OriginalSource as DependencyObject) is Game game)
        {
            args.Handled = true;
            await RunNavigationAsync(() => OpenGameAsync(game.Id));
        }
    }
```

Only `GameSelected` and the `row.Content` check change. `GameTapped` and
`GameKeyDown` are unchanged and are repeated here for completeness.

- [ ] **Step 4: Cancel loads before each Library render**

Replace `GameList.ItemsSource = games;` (line 868) with:

```csharp
                    gameArtwork.CancelAll();
                    GameList.ItemsSource = games.Select(game => new LibraryGameItem(game)).ToList();
```

The two visibility lines after it keep using `games.Count`.

- [ ] **Step 5: Give the cover loader a decode width and a token**

Replace `LoadCoverAsync` (lines 1045–1064, with its comment) with:

```csharp
    // Reads the managed file through a stream, so artwork never triggers a
    // network request and a damaged or missing file only leaves the placeholder.
    private async Task<ImageSource?> LoadCoverAsync(
        string? relativePath, int decodeWidth, CancellationToken token = default)
    {
        if (relativePath is null || artwork?.ResolveFile(relativePath) is not { } path)
        {
            return null;
        }
        try
        {
            using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            token.ThrowIfCancellationRequested();
            BitmapImage bitmap = new() { DecodePixelWidth = decodeWidth };
            await bitmap.SetSourceAsync(file.AsRandomAccessStream());
            token.ThrowIfCancellationRequested();
            return bitmap;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException)
        {
            return null;
        }
    }

    private Task<ImageSource?> LoadRowArtworkAsync(ArtworkItem item, CancellationToken token) =>
        item is LibraryGameItem game
            ? LoadCoverAsync(game.ArtworkRelativePath, RowArtworkDecodeWidth, token)
            : Task.FromResult<ImageSource?>(null);
```

`ArtworkListLoader.LoadAsync` catches the `OperationCanceledException`.
Change the Game detail call at line 930 to:

```csharp
                    ImageSource? cover = await LoadCoverAsync(game.ArtworkRelativePath, DetailCoverDecodeWidth);
```

`artwork` is set during startup (line 213). Rows realized before that return null
and keep the placeholder. The Library renders only after startup, so this
doesn't happen in practice.

- [ ] **Step 6: Apply the shared styles in `ShellWindow.xaml`**

`ShellStatusInfoBar` (line 79): add `Style="{StaticResource DesktopGuidesStatusInfoBarStyle}"`
and remove `IsClosable="False"`, which the style sets. Keep every other attribute.

`LibraryEmptyState` (line 129) and `GameEmptyState` (line 288): replace
`Style="{StaticResource DesktopGuidesElevatedSurfaceStyle}"` and `VerticalAlignment="Top"`
with `Style="{StaticResource DesktopGuidesEmptyStateStyle}"`. Keep the names,
`AutomationId`s and content.

`GameCoverFrame` (line 203): replace the element's opening tag with:

```xml
                        <Border x:Name="GameCoverFrame"
                                Style="{StaticResource DesktopGuidesCoverFrameStyle}"
                                Visibility="Collapsed">
```

The `GameCover` image inside is unchanged. The frame still collapses when there's
no cover, so Game detail looks as it does today, plus the shared fill behind
transparent artwork.

`GameFacts` (line 234): change to
`<toolkit:MetadataControl x:Name="GameFacts" Style="{StaticResource DesktopGuidesFactsStyle}" />`.
The `AutomationGroup` host stays.

- [ ] **Step 7: Apply the InfoBar style in the dialogs**

`GameEditorDialog.xaml`: on `EditorNotice` (line 11) and `SaveError` (line 54),
add `Style="{StaticResource DesktopGuidesStatusInfoBarStyle}"` and remove
`IsClosable="False"`. Keep `IsOpen`, `Severity`, `AutomationId` and the action button.

`ProviderSettingsCard.xaml`: on `ProviderSettingsStatus` (line 38), add the same
style and keep `IsClosable="True"`. The local value overrides the style.

- [ ] **Step 8: Build and run the tests**

Stage the tree, then run **Production build**, **Core tests** and the
Infrastructure tests.
Expected: the build succeeds with no new warnings, and all tests pass.

Then search for leftovers:

```bash
grep -n "DisplayMemberPath" src/DesktopGuides.Production/ShellWindow.xaml
grep -n "is Game game\|Content is Game" src/DesktopGuides.Production/ShellWindow.xaml.cs
```

Expected: only `GuideList` still uses `DisplayMemberPath`. The second search finds
only the `GameTapped` and `GameKeyDown` matches on `GameFromRow(...) is Game game`.

- [ ] **Step 9: Commit**

```bash
git add src/DesktopGuides.Production/LibraryGameItem.cs \
  src/DesktopGuides.Production/ShellWindow.xaml \
  src/DesktopGuides.Production/ShellWindow.xaml.cs \
  src/DesktopGuides.Production/GameEditorDialog.xaml \
  src/DesktopGuides.Production/ProviderSettingsCard.xaml
git commit -m "feat(ui): show Library games as artwork rows and adopt the shared styles" \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: `seed-catalog` in `DesktopGuides.ShellSeed`

The seed tool is test infrastructure with no test project of its own. Its
self-check is the read-back assertion in Step 1, which throws when the seeded
order or count is wrong.

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs:277-290` (usage check) and after the `seed-design` block (line 343)

**Interfaces:**
- Consumes: `SqliteLibraryRepository.AddGameAsync(title, platform, notes)`, `AddLinkedGameAsync(NewLinkedGame)`,
  `ListGamesAsync()`; `ManagedArtworkStore.StoreAsync`, `ResolveFile`, `Delete`; the file's `SolidPng` helper.
- Produces: `DesktopGuides.ShellSeed seed-catalog <app-data-root>`, which writes 500 games and prints
  `Seeded 500 catalog games.` Task 5 relies on these exact titles:

| Title | Platform | Artwork | Order |
| --- | --- | --- | --- |
| `"Catalog A " + 150 × "W"` (160 chars) | `PC` | valid | 1st |
| `Catalog B Short` | `Windows` | valid | 2nd |
| `Catalog C Corrupt Art` | `PC` | file overwritten with 4 bytes | 3rd |
| `Catalog C Missing Art 0` … `9` | `PC` | file deleted | 4th–13th |
| `Catalog D Größe Überfall Äpfel` | `PlayStation 5` | none | 14th |
| `Catalog Game 000` … `483` | `Windows` when even, else none | valid when `i % 3 == 0` | 15th–498th |
| `كتالوج الألعاب` | `PC` | none | 499th |
| `ゼルダの伝説` | `Nintendo Switch` | valid | 500th (last) |

The Library sorts by `Title, Id` with SQLite's BINARY collation, which is
code-point order. Arabic (U+06xx) sorts after Latin, and Japanese (U+30xx)
after Arabic.

- [ ] **Step 1: Add the mode**

In the usage check (lines 277–290), add `"seed-catalog"` to the pattern and the
usage text:

```csharp
if (args.Length != 2 ||
    args[0] is not ("seed" or "stale" or "seed-long" or "seed-second" or
        "seed-design" or "seed-catalog"))
{
    Console.Error.WriteLine(
        "Usage: DesktopGuides.ShellSeed seed|stale|seed-long|seed-second|seed-design|seed-catalog " +
```

Keep the rest of the usage text unchanged. After the `seed-design` block ends, add:

```csharp
if (args[0] == "seed-catalog")
{
    if ((await repository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The catalog seed needs an empty library.");
    }
    ManagedArtworkStore catalogArt = new(paths);
    int nextExternalId = 950000;

    async Task AddWithArtwork(string title, string? platform, byte shade)
    {
        Guid id = Guid.NewGuid();
        StoredArtwork stored = await catalogArt.StoreAsync(
            id, SolidPng(60, 90, shade, (byte)(255 - shade), 0x80), CancellationToken.None);
        GameMetadataSnapshot snapshot = new(
            GameMetadataSnapshot.CurrentSchemaVersion, null, null, [], [], [], [], null,
            GameTypeTag.MainGame, "SteamGridDB");
        await repository.AddLinkedGameAsync(new NewLinkedGame(
            id, title, platform,
            new ProviderGameLink(ProviderGameLink.Igdb, (nextExternalId++).ToString(), DateTimeOffset.UtcNow),
            snapshot, stored.RelativePath), CancellationToken.None);
    }

    string longTitle = "Catalog A " + new string('W', 150);
    await AddWithArtwork(longTitle, "PC", 0x20);
    await AddWithArtwork("Catalog B Short", "Windows", 0x40);

    await AddWithArtwork("Catalog C Corrupt Art", "PC", 0x60);
    for (int i = 0; i < 10; i++)
    {
        await AddWithArtwork($"Catalog C Missing Art {i}", "PC", 0x70);
    }
    foreach (Game game in await repository.ListGamesAsync())
    {
        if (game.ArtworkRelativePath is not { } relative) continue;
        if (game.Title == "Catalog C Corrupt Art")
        {
            File.WriteAllBytes(catalogArt.ResolveFile(relative)!, [0, 1, 2, 3]);
        }
        else if (game.Title.StartsWith("Catalog C Missing Art ", StringComparison.Ordinal))
        {
            catalogArt.Delete(relative);
        }
    }

    await repository.AddGameAsync("Catalog D Größe Überfall Äpfel", "PlayStation 5", null);
    for (int i = 0; i < 484; i++)
    {
        string title = $"Catalog Game {i:D3}";
        string? platform = i % 2 == 0 ? "Windows" : null;
        if (i % 3 == 0) await AddWithArtwork(title, platform, (byte)(i % 200));
        else await repository.AddGameAsync(title, platform, null);
    }
    await repository.AddGameAsync("كتالوج الألعاب", "PC", null);
    await AddWithArtwork("ゼルダの伝説", "Nintendo Switch", 0xA0);

    IReadOnlyList<Game> catalog = await repository.ListGamesAsync();
    string[] expectedHead = [longTitle, "Catalog B Short", "Catalog C Corrupt Art", "Catalog C Missing Art 0"];
    if (catalog.Count != 500 ||
        !catalog.Take(4).Select(game => game.Title).SequenceEqual(expectedHead) ||
        catalog[^1].Title != "ゼルダの伝説" ||
        catalog.Count(game => game.Title.StartsWith("Catalog C Missing Art ", StringComparison.Ordinal) &&
            game.ArtworkRelativePath is { } path && catalogArt.ResolveFile(path) is null) != 10)
    {
        throw new InvalidOperationException("The catalog seed did not read back in the expected order.");
    }
    Console.WriteLine("Seeded 500 catalog games.");
    return 0;
}
```

The C# file is UTF-8, so the non-ASCII titles can be literals here. Only the
PowerShell scripts must stay ASCII.

- [ ] **Step 2: Build and run the seed against a scratch folder**

Stage the tree, run **Seed build**, then:

```bash
s 'cd /d E:\work\desktop-guides\t05-4 && dotnet run --no-build -c Release --project tools\p1\DesktopGuides.ShellSeed -- seed-catalog E:\work\desktop-guides\t05-4-scratch'
s 'cd /d E:\work\desktop-guides\t05-4 && dotnet run --no-build -c Release --project tools\p1\DesktopGuides.ShellSeed -- describe-providers E:\work\desktop-guides\t05-4-scratch' > /tmp/t05-4-describe.json
python3 -c "import json;d=json.load(open('/tmp/t05-4-describe.json'));g=d['Games'];print(len(g), sum(1 for x in g if x['Title'].startswith('Catalog C Missing Art') and not x['ArtworkExists']), d['CredentialBlobExists'])"
s 'rmdir /s /q E:\work\desktop-guides\t05-4-scratch'
```

Expected: the seed prints `Seeded 500 catalog games.`, and the check prints `500 10 False`.
A second `seed-catalog` run against the same folder must fail with
`The catalog seed needs an empty library.` Check this before deleting the folder.

- [ ] **Step 3: Commit**

```bash
git add tools/p1/DesktopGuides.ShellSeed/Program.cs
git commit -m "test(p1): seed a 500-game catalog for the Library list checks" \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Installed `catalog` scenario and harness wiring

This scenario is the integration test for Tasks 2–4. It needs the installed
package, so it can't run before them, and the Mac can't run it at all. Its
checks are written to fail on the defects they target: a non-virtualized list
exceeds the 80-row bound, a wrapping title makes the long row taller, and a
selection on Tab-in opens the Game view before `Wait-HiddenById 'GameHeading'`.
Everything in both scripts stays ASCII.

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1:3-12` (`ValidateSet`), after `Wait-FocusedGuide` (line 445), before `elseif ($Mode -eq 'long-list')` (line 1226), and the failure-inspection condition (line ~1770)
- Modify: `tools/p1/windows_shell_install.ps1:11-13` (switches), `Run-ShellSmoke` timeout (line ~699), after `Run-DesignLanguageScenarios` (line ~758), the early returns (line ~1067) and the full run tail (line ~1140)

**Interfaces:**
- Consumes: the Task 4 seed titles and `seed-catalog`; the Task 3 row names (title) and `GameList` template.
- Produces: `windows_shell_ui_smoke.ps1 -Mode catalog`; `windows_shell_install.ps1 -CatalogOnly`;
  result files `catalog-light.json` and `catalog-dark.json`, with screenshots
  `catalog-light.library-wide.png`, `.library-end.png`, `.library-narrow.png` and the same for `catalog-dark`.

- [ ] **Step 1: Add the mode name and a focus helper to the smoke script**

Add `'catalog'` to the `-Mode` `ValidateSet`, after `'material'`:

```powershell
        'late-guide-after-close', 'waiting-handoff', 'material', 'catalog',
```

After the `Wait-FocusedGuide` function, add:

```powershell
    function Wait-FocusedGameRow([string] $expected) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
            if ($focused -and
                $focused.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
                (-not $expected -or $focused.Current.Name -eq $expected)) {
                return $focused
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Expected keyboard focus on game row '$expected'."
    }

    function Get-RealizedGameRows {
        $list = Wait-VisibleById 'GameList'
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)
        # Virtualized items without a container have empty bounds. Count only
        # rows that have a container, including the off-screen cache.
        return @($list.FindAll($scope, $condition) | Where-Object {
            -not $_.Current.BoundingRectangle.IsEmpty -and
            $_.Current.BoundingRectangle.Height -gt 0
        })
    }
```

Pass an empty string to `Wait-FocusedGameRow` to accept any row.

- [ ] **Step 2: Add the `catalog` branch**

Insert before `elseif ($Mode -eq 'long-list') {`:

```powershell
    elseif ($Mode -eq 'catalog') {
        $longTitle = 'Catalog A ' + ('W' * 150)
        $shortTitle = 'Catalog B Short'
        # Built from code points: Windows PowerShell 5.1 reads this file as ANSI.
        $lastTitle = [string]::new([char[]]@(
            0x30BC, 0x30EB, 0x30C0, 0x306E, 0x4F1D, 0x8AAC))
        $wideWidth = 1500
        $narrowWidth = 600
        $windowHeight = 720
        $realizedLimit = 80

        $workingArea = [System.Windows.Forms.SystemInformation]::WorkingArea
        $report.workingArea = "$($workingArea.Width)x$($workingArea.Height)"
        $dpi = [DesktopGuidesForegroundProbe]::Dpi($process.MainWindowHandle)
        $report.windowDpi = $dpi
        $report.scalePercent = [int][Math]::Round(($dpi / 96.0) * 100)

        Resize-ShellWindow $wideWidth $windowHeight
        [void](Wait-HiddenById 'ShellStatus')
        $longRow = Wait-GameRow $longTitle
        $shortRow = Wait-GameRow $shortTitle
        [void](Wait-GameRow 'Catalog C Corrupt Art')
        [void](Wait-GameRow 'Catalog C Missing Art 0')
        $longHeight = $longRow.Current.BoundingRectangle.Height
        $shortHeight = $shortRow.Current.BoundingRectangle.Height
        $report.longRowHeight = $longHeight
        $report.shortRowHeight = $shortHeight
        if ($longHeight -gt ($shortHeight + 1)) {
            throw "The long-title row is taller than a short row: $longHeight versus $shortHeight."
        }
        $topCount = (Get-RealizedGameRows).Count
        $report.realizedRowsAtTop = $topCount
        if ($topCount -ge $realizedLimit) {
            throw "GameList realized $topCount rows at the top of a 500-game library."
        }
        $report.libraryWideScreenshot = Save-WindowScreenshot 'library-wide'
        $report.phases += 'catalog-top-virtualized'

        Focus-And-Verify 'AddGameButton'
        [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
        [void](Wait-FocusedGameRow $longTitle)
        Start-Sleep -Milliseconds 500
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-HiddenById 'GameHeading')
        [System.Windows.Forms.SendKeys]::SendWait('^{DOWN}')
        [void](Wait-FocusedGameRow $shortTitle)
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        [void](Wait-Name 'GameHeading' $shortTitle)
        [void](Wait-Status 'Game ready.')
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
        Focus-And-Verify 'AddGameButton'
        [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
        [void](Wait-FocusedGameRow '')
        [System.Windows.Forms.SendKeys]::SendWait('{END}')
        [void](Wait-Name 'GameHeading' $lastTitle)
        [void](Wait-Status 'Game ready.')
        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
        $report.phases += 'catalog-keyboard'

        [void](Wait-HiddenById 'ShellStatus')
        $list = Wait-VisibleById 'GameList'
        $scroll = $list.GetCurrentPattern(
            [System.Windows.Automation.ScrollPattern]::Pattern)
        $scroll.SetScrollPercent(
            [System.Windows.Automation.ScrollPattern]::NoScroll, 100)
        [void](Wait-GameRow $lastTitle)
        $endCount = (Get-RealizedGameRows).Count
        $report.realizedRowsAtEnd = $endCount
        if ($endCount -ge $realizedLimit) {
            throw "GameList realized $endCount rows at the end of a 500-game library."
        }
        $report.libraryEndScreenshot = Save-WindowScreenshot 'library-end'
        $report.phases += 'catalog-end-virtualized'

        Resize-ShellWindow $narrowWidth $windowHeight
        [void](Wait-GameRow $lastTitle)
        $listBounds = (Wait-VisibleById 'GameList').Current.BoundingRectangle
        foreach ($row in Get-RealizedGameRows) {
            if ($row.Current.IsOffscreen) { continue }
            $bounds = $row.Current.BoundingRectangle
            if ($bounds.Left -lt ($listBounds.Left - 2) -or
                $bounds.Right -gt ($listBounds.Right + 2)) {
                throw "Game row '$($row.Current.Name)' is wider than GameList: $bounds in $listBounds."
            }
        }
        $report.libraryNarrowScreenshot = Save-WindowScreenshot 'library-narrow'
        $report.phases += 'catalog-narrow'

        Start-Sleep -Milliseconds 1000
        $status = Find-ById 'ShellStatus'
        if ($status -and -not $status.Current.IsOffscreen) {
            throw "The catalog showed a status: '$($status.Current.Name)'."
        }
        $loopback = @('127.0.0.1', '::1', '0.0.0.0', '::')
        $remote = @(Get-NetTCPConnection -OwningProcess $ProcessId -ErrorAction SilentlyContinue |
            Where-Object { $_.State -ne 'Listen' -and $_.RemoteAddress -notin $loopback })
        $report.remoteConnections = $remote.Count
        if ($remote.Count -gt 0) {
            throw "The catalog opened $($remote.Count) non-loopback connection(s)."
        }
        $report.phases += 'catalog-no-provider-traffic'
    }
```

`Wait-GameRow` needs `SelectionItemPattern`, so it matches the row, not the title
`TextBlock` inside it, which has the same name.

- [ ] **Step 3: Include `catalog` in failure inspection**

Change the condition at the start of the `catch` block to:

```powershell
    if (($Mode -eq 'game-editor' -or $Mode -eq 'catalog' -or $Mode -like 'provider-*') -and $root) {
```

- [ ] **Step 4: Wire the harness**

In `windows_shell_install.ps1`, add the switch after `[switch] $ProviderOnly,`:

```powershell
    [switch] $CatalogOnly,
```

In `Run-ShellSmoke`, replace the timeout line with:

```powershell
    $timeoutSeconds = if ($mode -like 'provider-*') { 240 }
        elseif ($mode -eq 'catalog') { 120 }
        else { 60 }
```

After `Run-DesignLanguageScenarios`, add:

```powershell
function Run-CatalogScenarios {
    Invoke-ShellSeed @('seed-catalog', $dataRoot) | Out-Null
    $originalTheme = Get-AppThemePreference
    try {
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.catalogLight = Run-ShellSmoke 'catalog' -ResultName 'catalog-light'
        Close-InstalledShell

        Set-AppThemePreference $false
        Start-InstalledShell
        $report.catalogDark = Run-ShellSmoke 'catalog' -ResultName 'catalog-dark'
        Close-InstalledShell
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
    $state = Get-ProviderState
    if ($state.CredentialBlobExists) {
        throw 'The catalog run found a provider credential blob.'
    }
    $missing = @($state.Games | Where-Object {
        $_.Title -like 'Catalog C Missing Art*' -and -not $_.ArtworkExists })
    if ($missing.Count -ne 10) {
        throw "Expected 10 catalog games with missing artwork, found $($missing.Count)."
    }
    $report.catalogGames = @($state.Games).Count
}
```

After the `$ProviderOnly` early return, add:

```powershell
    if ($CatalogOnly) {
        Run-CatalogScenarios
        $report.success = $true
        return
    }
```

In the full run, replace the tail

```powershell
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    Run-ProviderScenarios
```

with:

```powershell
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    Run-CatalogScenarios

    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    Run-ProviderScenarios
```

- [ ] **Step 5: Check the scripts parse and stay ASCII**

```bash
LC_ALL=C grep -nP '[^\x00-\x7F]' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1 || echo ascii-ok
s 'powershell -NoProfile -Command "foreach ($f in ''E:\work\desktop-guides\t05-4\tools\p1\windows_shell_ui_smoke.ps1'',''E:\work\desktop-guides\t05-4\tools\p1\windows_shell_install.ps1'') { $e=$null; [void][System.Management.Automation.Language.Parser]::ParseFile($f,[ref]$null,[ref]$e); if ($e) { $e; exit 1 } }; ''parse-ok''"'
```

Expected: `ascii-ok` (macOS `grep` needs `-P` from GNU grep; if it's missing, use
`perl -ne 'print "$ARGV:$.\n" if /[^\x00-\x7F]/' <files>`) and `parse-ok`.
Then run the headless script guards, as CI `core-tests` does:

```bash
s 'cd /d E:\work\desktop-guides\t05-4 && for %f in (tools\p1\test_windows_*.ps1) do powershell -NoProfile -ExecutionPolicy Bypass -File %f || exit /b 1'
```

Expected: every guard prints its pass line.

- [ ] **Step 6: Commit, push and read the CI result**

```bash
git add tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
git commit -m "test(p1): add the installed catalog scenario and -CatalogOnly" \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push -u origin feat/p1-t05-4-catalog-components
```

Pushing a shared branch needs the user's OK. Reading CI with `gh` also needs it,
because the GitHub MCP has no Actions tools. Once approved:

```bash
gh run list --branch feat/p1-t05-4-catalog-components --limit 1
gh run view <run-id> --log-failed
gh run download <run-id> -n <production-shell-ui artifact name> -D /tmp/t05-4-ci
```

Expected: `core-tests`, `packages`, `production-packages` and `production-shell-ui` pass.
`catalog-light.json` and `catalog-dark.json` report `success: true`, realized counts
under 80, `remoteConnections: 0`, and a `workingArea` of at least 1500×720. If the
working area is smaller, `Resize-ShellWindow` clamps the wide size. Record the actual
value in Task 6 instead of changing the check. On failure, use
superpowers:systematic-debugging with the uploaded JSON and `failure` screenshot
before changing code. Use the host `-CatalogOnly` run only to reproduce.

Then run the live provider regression on the host (Add game on the shared template):

```bash
s 'powershell -NoProfile -ExecutionPolicy Bypass -File E:\work\desktop-guides\t05-4\tools\p1\windows_shell_install.ps1 -PackagePath <built x64 msix> -ResultDirectory E:\work\desktop-guides\t05-4-results -ProviderOnly'
```

Follow `docs/p1/e2e-testing.md` for the scheduled-task entry point, data backup and
cleanup. Expected: `provider-live` passes and finds `Half-Life, Main game, 1998, …`.
No firewall rule is used.

---

### Task 6: Status, gate and verification docs

Documentation only; no TDD cycle.

**Files:**
- Modify: `docs/p1/e2e-testing.md` (Runner contract, Scenario checklist)
- Modify: `docs/p1/implementation-plan.md` (status prose after the T04.4 paragraph, about line 600)
- Modify: `docs/progress.md` (date line 3, T11.4 row line 14, new rows)
- Modify: `docs/p1/t05-4-catalog-components-design.md` (status line 3, new verification record)

**Interfaces:**
- Consumes: the CI run ID and artifact names from Task 5 Step 6, the host `-ProviderOnly` result, and the Task 1 test counts.
- Produces: docs that name CI `production-shell-ui` as the installed-UI gate of record.

- [ ] **Step 1: Record the gate split in `e2e-testing.md`**

At the end of the "Runner contract" section, add:

```markdown
### Gates of record

CI `production-shell-ui` runs the full installed harness on every PR and is the
gate of record for the shell, design-language, material and catalog scenarios.
It uploads the JSON results and screenshots, and PRs link to that artifact.
Provider live scenarios skip on CI, because the runner has no credential files.
Run them on the Windows host with `-ProviderOnly`, using the user's keys and
normal provider requests. Use the host for other scenarios only to debug a CI
failure; `-CatalogOnly` and `-DesignOnly` run one scenario group against a fresh
install. A portable CI job, a fake-provider CI lane and a CI scheduled-task entry
point are T17.2 work.
```

Add a row to the Scenario checklist table after "Design language":

```markdown
| Library catalog | Seed 500 games with valid, corrupt and missing managed artwork, a 160-character title, and German, Arabic and Japanese titles. In light and dark: fewer than 80 realized `GameList` rows at the top and after scrolling to the last game; the long-title row no taller than a short one; Tab into the list without opening a game, Ctrl+Down then Enter opens the focused game, and End opens the last; rows stay inside `GameList` at 600 px; no status, no non-loopback TCP connection and no credential blob. | T05.4, TR05.3, TR11.3 |
```

- [ ] **Step 2: Record T05.4 status in `implementation-plan.md`**

After the T04.4 paragraph that ends "…blocked-network controller removal record.",
add a paragraph. Fill in the PR number, date and run ID from the actual run, and
keep the sentence structure:

```markdown
T04.4 was merged through PR #14, merge commit `<full hash of 3e490de>`, and
the portable build through PR #15, merge commit `<full hash of a4bb44c>`. The
[T05.4 catalog components design](t05-4-catalog-components-design.md) and
[plan](t05-4-catalog-components-plan.md) extract the artwork row, cover frame,
facts, empty-state, busy-row and status `InfoBar` patterns into
`Styles/Catalog.xaml` and move the Library game list onto the shared row.
T05.4 was implemented and verified on <date> through PR #<n>: CI run <run-id>
passed `core-tests` (including `LibraryGamePresentationTests` and
`ArtworkLoadTicketsTests`), both package builds and `production-shell-ui` with
the new catalog scenario in light and dark. The host `-ProviderOnly` run passed
Add game on the shared template.
```

Get the full hashes with `git rev-parse 3e490de a4bb44c`.

- [ ] **Step 3: Update `progress.md`**

- Change line 3 to `Updated <date>.` and keep the rest of the sentence.
- In the T11.4 row, replace "Implementation and available Windows 11 x64
  verification complete on the feature branch; merge pending." with
  "Merged through [PR #13](https://github.com/ilya-slalom/desktop-guides/pull/13) on 29 September 2026, merge commit `23e0694faae1938993c8f228116324d61fe5b1e1`."
- After the T11.4 row, add three rows:

```markdown
| P1 T04.4 provider search | Merged through [PR #14](https://github.com/ilya-slalom/desktop-guides/pull/14), merge commit `<full hash>`. | IGDB metadata with user-supplied Twitch credentials and SteamGridDB artwork with a user-supplied key; the [installed result](p1/results.md#m2-t044-provider-search--implementation-check-29-september-2026) records the tests and harness phases. |
| P1 portable build | Merged through [PR #15](https://github.com/ilya-slalom/desktop-guides/pull/15), merge commit `<full hash>`. | Releases ship a signed MSIX and a portable self-contained exe; see [Portable build](p1/e2e-testing.md#portable-build). |
| P1 T05.4 catalog components | <Merged through PR #n / PR open>. | Shared catalog styles and the artwork row template, adopted by Add game, Game detail, the status surfaces and the Library list. CI `production-shell-ui` passed the catalog scenario in light and dark; see the [verification record](p1/t05-4-catalog-components-design.md#t054-verification-record). |
```

- [ ] **Step 4: Add the verification record to the design doc**

Change the status line to `Status: implemented and verified <date>. Prerequisites T04.4 and T11.4 are merged.`
Append:

```markdown
## T05.4 verification record

- **Unit tests.** `core-tests` in CI run <run-id>: <n> Core and <n> Infrastructure
  passes, including `LibraryGamePresentationTests` and `ArtworkLoadTicketsTests`.
- **Installed catalog scenario.** `production-shell-ui` in the same run, light and
  dark. Working area <w>×<h> at <scale>%. Realized rows: <top> at the top and
  <end> at the end (limit 80). Long row <h1> DIPs, short row <h2>. No status, no
  remote connections, no credential blob, 10 missing-artwork games.
- **Live provider regression.** Host `-ProviderOnly` on <date>: `provider-live` found
  the Half-Life result rows on the shared template.
- **Rulings.** The long title is 160 characters, because `GameDetails.TitleLimit`
  is 160. The keyboard check uses Ctrl+Down and End instead of Down, because
  `GameList` selection follows focus and plain Down opens a game.
  `LibraryGamePresentation` lives in `Core/Library`. The realized-row count
  includes cached off-screen containers.
- **Not run.** Portable build checks: T05.4 changes nothing in packaging.
```

Replace every `<…>` with the observed value. Leave none behind.

- [ ] **Step 5: Check the docs and commit**

```bash
grep -n "<full hash\|<date>\|<run-id>\|<n>\|PR #<\|<Merged" docs/progress.md docs/p1/implementation-plan.md docs/p1/t05-4-catalog-components-design.md docs/p1/e2e-testing.md || echo no-placeholders
git add docs/p1/e2e-testing.md docs/p1/implementation-plan.md docs/progress.md docs/p1/t05-4-catalog-components-design.md
git commit -m "docs(p1): record T05.4 verification and the CI gate split" \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Expected: `no-placeholders`.

---

## Self-review

- **Spec coverage.** Shared resources (Task 2); `ArtworkItem`, `GameSearchItem`,
  `ArtworkListLoader` (Task 2); `LibraryGameItem`, the Library switch, decode
  widths and managed-only loading (Task 3); `LibraryGamePresentation` and tickets
  with unit tests (Task 1); the 500-game seed (Task 4); virtualization, missing
  artwork, long text, keyboard, narrow width, no traffic, screenshots and
  `-CatalogOnly` (Task 5); gates and docs (Task 6). The spec's "Down, End, Enter"
  and "200 characters" are replaced by recorded rulings.
- **Placeholders.** Only the Task 6 observed values (`<date>`, `<run-id>`, hashes),
  which Step 5 checks for.
- **Type consistency.** `ArtworkLoadTickets<SelectorItem>`, `Issue`/`Release`/`ReleaseAll`/`IsCurrent`,
  `ArtworkItem(title, summary, detail, accessibleName)`, `ArtworkListLoader.NameRows`/`Attach`/`CancelAll`,
  `LoadCoverAsync(relativePath, decodeWidth, token)` and `LibraryGameItem.Game`/`ArtworkRelativePath`
  match across Tasks 1–3.
- **Review Focus.** Each item names its owning test above.
