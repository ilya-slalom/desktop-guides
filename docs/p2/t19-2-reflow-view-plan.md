# T19.2 PR b (The reflow view) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A TXT guide can be read in a Reflow view, chosen per guide with
an "Original | Reflow" choice beside the reader toolbar. Prose wraps;
maps, tables and art keep their layout. Search, contents, bookmarks and
the saved place work in both views.

**Architecture:**
- **Core** adds:
  - `ReaderCapabilities.Reflow`;
  - `ReflowHighlights`, which turns a match into per-block highlight
    ranges;
  - `TextViewMessages`, the status messages.
- **Production:**
  - **`TextReflowView`** is a virtualized list with one item per
    `ReflowBlock`.
  - **`TextReaderSession`** keeps the line view and adds the reflow view.
    It plans off the UI thread under `TextReflowPlanner.Budget`, works in
    offsets in either view, and raises `ViewChanged`.
  - **The shell** shows the choice, opens a guide in its saved view, and
    swaps the reading surface.
- **Installed `reflow-*` modes** in the `navigation` group. A test-gated
  `txt-place-<pid>.json` reports the view and the first visible offset.

**Tech Stack:** .NET 10, WinUI 3, CommunityToolkit Segmented (through
`BoundedChoice`), xUnit, PowerShell UI Automation smoke tests.

**Spec:** [t19-2-reflow-design.md](t19-2-reflow-design.md) (approved 16
October 2026). PR a merged as #130.

## Global Constraints

- **TR19.1:** the imported bytes, the normalized text and every stored
  locator stay as they are. The view only displays.
- **Places are offsets, kept to the block:**
  - in Reflow the first visible place is the first visible block's
    `Start`;
  - going to an offset shows `plan.Blocks[plan.BlockAt(offset)]`;
  - a capture uses `TextLocator.CaptureAt`.
- **Planning** runs off the UI thread with
  `CancelAfter(TextReflowPlanner.Budget)`, once per session, and only when
  Reflow is first wanted.
- **Switching:**
  1. take the first visible offset;
  2. plan if needed;
  3. open the other view at that offset;
  4. save the choice with `SaveTextViewAsync`;
  5. announce "Reflow view." or "Original view." without the bar.
- **No prose,** or planning that fails or runs out of time: "This guide
  has no prose to reflow." The choice returns to Original, and nothing is
  saved.
- **Opening a guide saved as Reflow** plans before the guide shows. A
  `null` plan opens it in Original, quietly; the message appears only when
  the user chooses Reflow.
- **The choice** is a `BoundedChoice` with the AutomationIds
  `ReaderTextView`, `ReaderTextViewOriginal` and `ReaderTextViewReflow`.
  It's shown only while the session has `ReaderCapabilities.Reflow`, that
  is, only for TXT.
- **Rendering:**
  - prose wraps in the reading font (`DesktopGuidesBodyStyle`);
  - preserved blocks use `DesktopGuidesReaderMonoFontFamily` with no
    wrapping, inside their own horizontal `ScrollViewer`;
  - text size scales both.
- **Shell rules:** PowerShell stays ASCII-only and runs under 5.1 and 7.
  There is no `cd` in local shell commands.

## Review Focus

1. **Switching views while a search match is showing.** The match must
   stay highlighted and current in the other view, and stepping must
   continue from it. Pinned by Task 4's `reflow-find`, which steps after a
   switch.
2. **Text size changed in Reflow, then switching to Original.** Both views
   must use the new size. The session keeps one scale and applies it to
   both. Pinned by Task 4's `reflow-switch`, which changes size in Reflow
   and reads the line view's applied scale after switching back.
3. **A guide whose plan is `null`, opened when saved as Reflow** (its file
   changed to tables only). It must open in Original without an error.
   Pinned by Task 4's `reflow-none`, which first saves Reflow for Numbered
   Lines Guide with the seed.
4. **The window resized in Reflow.** Prose must re-wrap, and the place
   must not jump to another block. The list's `ScrollIntoView` anchor is
   the first visible block. Checked by the review.
5. **Leaving the guide while planning.** The plan's result must not touch
   a disposed session. The session checks `disposed` after the await.
   Checked by the review.

## Host commands

```bash
s(){ ssh -o BatchMode=yes -o ConnectTimeout=15 -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t19-2b) { Remove-Item -Recurse -Force E:\work\desktop-guides\t19-2b }; New-Item -ItemType Directory E:\work\desktop-guides\t19-2b | Out-Null"'
  git -C /Users/ilya.lissoboi/work/desktop-guides ls-files -z -co --exclude-standard | grep -z -v -E '^(\.claude/|az2aws)' | COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --null -cf - -T - | s 'tar -xf - -C E:\work\desktop-guides\t19-2b'
  s 'cd /d E:\work\desktop-guides\t19-2b && python tools\p0\make_fixtures.py > E:\work\t19-2b-fixtures.log 2>&1'
}
```

- **Tests:** `stage && s 'cd /d E:\work\desktop-guides\t19-2b && dotnet test tests\<project> --filter "<filter>" -nologo > E:\work\t19-2b-test.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" /c:"error CS" /c:"[FAIL]" E:\work\t19-2b-test.log'`
- **Production build:** `s 'cd /d E:\work\desktop-guides\t19-2b && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64 -nologo -warnaserror > E:\work\t19-2b-build.log 2>&1 & findstr /c:"Warning(s)" /c:"Error(s)" /c:"error " E:\work\t19-2b-build.log'`
- **Installed modes:** `gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p2-t19-2-reflow-view -f shell-scope=navigation -f shell-lane=<msix|portable>`

---
### Task 1: Highlights, messages and the capability (Core)

**Files:**
- Create: `src/DesktopGuides.Core/Text/ReflowHighlights.cs`
- Modify: `src/DesktopGuides.Core/Reading/ReaderContract.cs` (`ReaderCapabilities.Reflow = 512`)
- Test: `tests/DesktopGuides.Core.Tests/ReflowHighlightsTests.cs`

**Interfaces:**
- Consumes: PR a's `ReflowPlan`, `ReflowBlock.DisplayIndexOf`, `ReflowPlan.BlockAt`, `TextViewMode`.
- Produces:
  - `static class ReflowHighlights { IReadOnlyList<(int Block, int Start, int Length)> For(ReflowPlan plan, int start, int length); }`
  - `static class TextViewMessages { const string NoProse; static string For(TextViewMode mode); }`
  - `ReaderCapabilities.Reflow` (512)

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ReflowHighlightsTests
{
    private const string Wrapped =
        "The forest path starts east of the village. Follow the stream until the\n" +
        "old bridge, then take the left fork past the ruined watchtower. Wolves\n" +
        "roam here at night, so travel by day and keep a torch in your pack.";

    private static (TextGuideDocument Document, ReflowPlan Plan) Planned(string text)
    {
        TextGuideDocument document = TextGuideDocument.Decode(Encoding.UTF8.GetBytes(text));
        return (document, TextReflowPlanner.Plan(document, [], default)!);
    }

    private static string Shown(ReflowPlan plan, (int Block, int Start, int Length) range) =>
        plan.Blocks[range.Block].Text.Substring(range.Start, range.Length);

    [Fact]
    public void AMatchInsideALineHighlightsThoseCharacters()
    {
        (TextGuideDocument document, ReflowPlan plan) = Planned(Wrapped + "\n\n" + Wrapped + "\n");
        int start = document.Text.IndexOf("village. Follow", StringComparison.Ordinal);

        var range = Assert.Single(ReflowHighlights.For(plan, start, "village. Follow".Length));

        Assert.Equal(0, range.Block);
        Assert.Equal("village. Follow", Shown(plan, range));
    }

    // A match across a source line break shows across the joining space.
    [Fact]
    public void AMatchAcrossALineBreakHighlightsTheJoinedText()
    {
        (TextGuideDocument document, ReflowPlan plan) = Planned(Wrapped + "\n\n" + Wrapped + "\n");
        int start = document.Text.IndexOf("the\nold", StringComparison.Ordinal);

        var range = Assert.Single(ReflowHighlights.For(plan, start, "the\nold".Length));

        Assert.Equal("the old", Shown(plan, range));
    }

    [Fact]
    public void AMatchInTheSecondBlockNamesThatBlock()
    {
        (TextGuideDocument document, ReflowPlan plan) = Planned(Wrapped + "\n\n" + Wrapped + "\n");
        int start = document.Text.LastIndexOf("Wolves", StringComparison.Ordinal);

        var range = Assert.Single(ReflowHighlights.For(plan, start, "Wolves".Length));

        Assert.Equal(1, range.Block);
        Assert.Equal("Wolves", Shown(plan, range));
    }

    [Fact]
    public void AnEmptyMatchHighlightsNothing()
    {
        (_, ReflowPlan plan) = Planned(Wrapped + "\n\n" + Wrapped + "\n");

        Assert.Empty(ReflowHighlights.For(plan, 10, 0));
    }

    [Theory]
    [InlineData(TextViewMode.Original, "Original view.")]
    [InlineData(TextViewMode.Reflow, "Reflow view.")]
    public void EachViewIsAnnounced(TextViewMode mode, string message) =>
        Assert.Equal(message, TextViewMessages.For(mode));

    [Fact]
    public void ReflowIsItsOwnCapability() =>
        Assert.Equal(512, (int)Reading.ReaderCapabilities.Reflow);
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `stage && s 'cd /d E:\work\desktop-guides\t19-2b && dotnet test tests\DesktopGuides.Core.Tests --filter "FullyQualifiedName~ReflowHighlightsTests" -nologo > E:\work\t19-2b-test.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" /c:"error CS" E:\work\t19-2b-test.log'`
Expected: FAIL to compile, with `CS0103` for `ReflowHighlights`.

- [ ] **Step 3: Write them.** `src/DesktopGuides.Core/Text/ReflowHighlights.cs`:

```csharp
using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Text;

// T19.2: a search match's source range as display ranges, one per block it
// touches, for the reflow view's highlight.
public static class ReflowHighlights
{
    public static IReadOnlyList<(int Block, int Start, int Length)> For(ReflowPlan plan, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(plan);
        List<(int Block, int Start, int Length)> ranges = [];
        if (length <= 0 || plan.Blocks.Count == 0) return ranges;
        int end = start + length;
        for (int index = plan.BlockAt(start); index < plan.Blocks.Count && plan.Blocks[index].Start < end; index++)
        {
            ReflowBlock block = plan.Blocks[index];
            int from = block.DisplayIndexOf(Math.Max(start, block.Start));
            int to = block.DisplayIndexOf(Math.Min(end, block.End));
            if (to > from) ranges.Add((index, from, to - from));
        }
        return ranges;
    }
}

public static class TextViewMessages
{
    public const string NoProse = "This guide has no prose to reflow.";

    public static string For(TextViewMode mode) => mode == TextViewMode.Reflow ? "Reflow view." : "Original view.";
}
```

  In `ReaderContract.cs`, after `Outline = 256` add `,` and a new member
  with a comment:

```csharp
    // T19.2: TXT's Original or Reflow view.
    Reflow = 512
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: the Step 2 command, then the whole Core suite.
Expected: `ReflowHighlightsTests` passes 7/7, and Core passes in full
(1317 + 7 = 1324).

- [ ] **Step 5: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Core/Text/ReflowHighlights.cs src/DesktopGuides.Core/Reading/ReaderContract.cs tests/DesktopGuides.Core.Tests/ReflowHighlightsTests.cs
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): reflow highlights, view messages and the Reflow capability"
```

---
### Task 2: `TextReflowView` and the two-view session (Production)

**Files:**
- Create: `src/DesktopGuides.Production/TextReflowView.xaml` and `.xaml.cs`
- Modify: `src/DesktopGuides.Production/TextReaderSession.cs`

**Interfaces:**
- Consumes: Task 1's `ReflowHighlights` and `ReaderCapabilities.Reflow`; PR a's planner, `TextLocator.CaptureAt` and `TextViewMode`.
- Produces (for Task 3):
  - `TextReaderSession.Mode` (`TextViewMode`), `UIElement Surface`, `event EventHandler? ViewChanged`
  - `Task<bool> SetViewAsync(TextViewMode mode, CancellationToken token)`: false when Reflow has no plan
  - `bool FocusSurface()`
  - `static string? PlaceFileForTest(string cacheRoot)`, and a constructor with the optional parameter `string? placeFile = null`

Production has no unit-test project. This task's checks are a warning-free
build and Task 4's installed modes.

- [ ] **Step 1: `TextReflowView.xaml`**

```xml
<?xml version="1.0" encoding="utf-8"?>
<UserControl x:Class="DesktopGuides.Production.TextReflowView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <ListView x:Name="Blocks"
              AutomationProperties.AutomationId="ReaderReflowBlocks"
              AutomationProperties.Name="Guide text"
              SelectionMode="None"
              ScrollViewer.HorizontalScrollMode="Disabled"
              ScrollViewer.HorizontalScrollBarVisibility="Disabled"
              ContainerContentChanging="BlockContainerChanging">
        <ListView.ItemsPanel>
            <ItemsPanelTemplate>
                <ItemsStackPanel />
            </ItemsPanelTemplate>
        </ListView.ItemsPanel>
        <ListView.ItemContainerStyle>
            <Style TargetType="ListViewItem">
                <Setter Property="HorizontalContentAlignment" Value="Stretch" />
                <Setter Property="Padding" Value="0,6" />
            </Style>
        </ListView.ItemContainerStyle>
        <ListView.ItemTemplate>
            <DataTemplate>
                <Grid>
                    <!-- Prose wraps to the window in the reading font. -->
                    <TextBlock Style="{StaticResource DesktopGuidesBodyStyle}" TextWrapping="Wrap" />
                    <!-- Preserved blocks keep their layout and scroll sideways on their own. -->
                    <ScrollViewer HorizontalScrollMode="Enabled" HorizontalScrollBarVisibility="Auto"
                                  VerticalScrollMode="Disabled" VerticalScrollBarVisibility="Disabled">
                        <TextBlock FontFamily="{StaticResource DesktopGuidesReaderMonoFontFamily}"
                                   TextWrapping="NoWrap" />
                    </ScrollViewer>
                </Grid>
            </DataTemplate>
        </ListView.ItemTemplate>
    </ListView>
</UserControl>
```

- [ ] **Step 2: `TextReflowView.xaml.cs`**

```csharp
using CommunityToolkit.WinUI;
using DesktopGuides.Core.Reading;
using DesktopGuides.Core.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace DesktopGuides.Production;

// T19.2: a TXT guide's Reflow view, one list item per planned block. Its
// place is the first visible block's start offset (the user's choice:
// switching keeps the place to the block).
public sealed partial class TextReflowView : UserControl
{
    private readonly ReflowPlan plan;
    private double textScale;
    private double baseFontSize = 14;
    private int anchorBlock;
    private int reportedTop = -1;
    private bool restoring;
    private ScrollViewer? viewer;
    private DispatcherQueue? dispatcher;
    private IReadOnlyDictionary<int, (int Start, int Length)> highlights = new Dictionary<int, (int, int)>();

    public TextReflowView(ReflowPlan plan, double textScale)
    {
        ArgumentNullException.ThrowIfNull(plan);
        this.plan = plan;
        this.textScale = textScale;
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    public event EventHandler? TopBlockChanged;

    public int FirstVisibleIndex =>
        Blocks.ItemsPanelRoot is ItemsStackPanel panel ? Math.Max(panel.FirstVisibleIndex, 0) : anchorBlock;

    public int FirstVisibleOffset => plan.Blocks[Math.Clamp(FirstVisibleIndex, 0, plan.Blocks.Count - 1)].Start;

    public double TextScale
    {
        get => textScale;
        set
        {
            textScale = value;
            RefreshRealized();
            ScrollToBlock(anchorBlock);
        }
    }

    // The block holding the offset goes to the top; before the first layout
    // it is kept and applied once items exist.
    public void ScrollToOffset(int offset) => ScrollToBlock(plan.BlockAt(offset));

    public void ScrollToEdge(ReaderEdge edge) => ScrollToBlock(edge == ReaderEdge.Start ? 0 : plan.Blocks.Count - 1);

    // A page is a viewport: blocks have no common height.
    public void PageBy(int delta) => ScrollByViewport(delta);

    public void ScrollByViewport(double fraction)
    {
        if (!double.IsFinite(fraction))
        {
            throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "A scroll must be a finite fraction.");
        }
        viewer?.ChangeView(null, viewer.VerticalOffset + fraction * viewer.ViewportHeight, null, true);
    }

    public void ShowMatch(IReadOnlyList<(int Block, int Start, int Length)> ranges)
    {
        highlights = ranges.ToDictionary(range => range.Block, range => (range.Start, range.Length));
        RefreshRealized();
    }

    public void ClearMatch()
    {
        highlights = new Dictionary<int, (int, int)>();
        RefreshRealized();
    }

    public bool FocusBlocks() => Blocks.Focus(FocusState.Programmatic);

    public void Clear()
    {
        Loaded -= OnLoaded;
        SizeChanged -= OnSizeChanged;
        if (viewer is not null) viewer.ViewChanged -= OnViewChanged;
        viewer = null;
        Blocks.ItemsSource = null;
    }

    private void ScrollToBlock(int index)
    {
        anchorBlock = Math.Clamp(index, 0, plan.Blocks.Count - 1);
        if (Blocks.ItemsSource is null) return;
        restoring = true;
        Blocks.ScrollIntoView(plan.Blocks[anchorBlock], ScrollIntoViewAlignment.Leading);
        dispatcher?.TryEnqueue(DispatcherQueuePriority.Low, () => restoring = false);
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoaded;
        dispatcher = DispatcherQueue;
        if (Application.Current.Resources.TryGetValue("ControlContentThemeFontSize", out object size) && size is double font)
        {
            baseFontSize = font;
        }
        Blocks.ItemsSource = plan.Blocks;
        viewer = Blocks.FindDescendant<ScrollViewer>();
        if (viewer is not null) viewer.ViewChanged += OnViewChanged;
        ScrollToBlock(anchorBlock);
    }

    // A wider or narrower window re-wraps prose; the top block stays.
    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => ScrollToBlock(anchorBlock);

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (args.IsIntermediate) return;
        int top = FirstVisibleIndex;
        if (!restoring) anchorBlock = top;
        if (top != reportedTop)
        {
            reportedTop = top;
            TopBlockChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RefreshRealized()
    {
        for (int index = 0; index < plan.Blocks.Count; index++)
        {
            if (Blocks.ContainerFromIndex(index) is ListViewItem item) Apply(item, index);
        }
    }

    private void BlockContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.ItemContainer is not ListViewItem item) return;
        Apply(item, args.ItemIndex);
    }

    private void Apply(ListViewItem item, int index)
    {
        if (item.ContentTemplateRoot is not Grid { Children: [TextBlock prose, ScrollViewer { Content: TextBlock preserved } scroll] })
        {
            return;
        }
        ReflowBlock block = plan.Blocks[index];
        bool isProse = block.Kind == ReflowKind.Prose;
        TextBlock text = isProse ? prose : preserved;
        prose.Visibility = isProse ? Visibility.Visible : Visibility.Collapsed;
        scroll.Visibility = isProse ? Visibility.Collapsed : Visibility.Visible;
        text.Text = block.Text;
        text.FontSize = baseFontSize * textScale;
        text.TextHighlighters.Clear();
        if (highlights.TryGetValue(index, out (int Start, int Length) range) && range.Length > 0)
        {
            TextHighlighter highlighter = new();
            highlighter.Ranges.Add(new TextRange { StartIndex = range.Start, Length = range.Length });
            text.TextHighlighters.Add(highlighter);
        }
        AutomationProperties.SetName(item, block.Text.Length == 0 ? "Blank block" : block.Text);
    }
}
```

- [ ] **Step 3: The session holds both views.** In `TextReaderSession.cs`:
  - add `using DesktopGuides.Infrastructure.Storage;` and `using Microsoft.UI.Xaml;`;
  - the constructor gains `string? placeFile = null` and stores it;
  - `Capabilities` gains `| ReaderCapabilities.Reflow`.

  Then add these members, and replace the bodies named below:

```csharp
    private readonly string? placeFile;
    private double textScale;
    private bool planned;
    private ReflowPlan? plan;
    private TextReflowView? reflowView;
    private TextMatch? shownMatch;

    // T19.2: the place file for installed tests, behind the progress gate.
    public static string? PlaceFileForTest(string cacheRoot) =>
        TestGate.IsOpen(AppLane.Current.LocalEvent("ProgressDiagnostics", Environment.ProcessId))
            ? Path.Combine(cacheRoot, "diagnostics", $"txt-place-{Environment.ProcessId}.json")
            : null;

    public TextViewMode Mode { get; private set; } = TextViewMode.Original;
    public UIElement Surface => Mode == TextViewMode.Reflow && reflowView is not null ? reflowView : View;
    public event EventHandler? ViewChanged;

    private int FirstVisibleOffset => Mode == TextViewMode.Reflow && reflowView is not null
        ? reflowView.FirstVisibleOffset
        : document.LineStarts[Math.Clamp(View.FirstVisibleIndex, 0, document.LineStarts.Count - 1)];

    // T19.2: shows the guide in the other view at the same block. Reflow is
    // planned once, off the UI thread, within the planner's budget. False
    // when Reflow has no plan: no prose, a failure, or out of time.
    public async Task<bool> SetViewAsync(TextViewMode mode, CancellationToken token)
    {
        if (disposed) return false;
        if (mode == Mode) return true;
        int offset = FirstVisibleOffset;
        if (mode == TextViewMode.Reflow)
        {
            if (!planned)
            {
                planned = true;
                TextGuideDocument source = document;
                using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(token);
                budget.CancelAfter(TextReflowPlanner.Budget);
                plan = await Task.Run(
                    () => TextReflowPlanner.Plan(source, TextSectionDetector.Detect(source.Text, budget.Token), budget.Token),
                    CancellationToken.None);
                token.ThrowIfCancellationRequested();
            }
            if (disposed || plan is null) return false;
            if (reflowView is null)
            {
                reflowView = new TextReflowView(plan, textScale);
                reflowView.TopBlockChanged += OnTopLineChanged;
                if (shownMatch is TextMatch match) reflowView.ShowMatch(ReflowHighlights.For(plan, match.Start, match.Length));
            }
            reflowView.ScrollToOffset(offset);
        }
        else
        {
            View.ScrollToLine(document.LineAtOffset(offset));
        }
        Mode = mode;
        ViewChanged?.Invoke(this, EventArgs.Empty);
        WritePlaceForTest();
        return true;
    }

    public bool FocusSurface() =>
        Mode == TextViewMode.Reflow && reflowView is not null ? reflowView.FocusBlocks() : View.FocusLines();

    // Test gate only: the view and its first visible offset.
    private void WritePlaceForTest()
    {
        if (placeFile is null || disposed) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(placeFile)!);
            DiagnosticFile.Write(placeFile, string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{{\"view\":\"{Mode}\",\"offset\":{FirstVisibleOffset},\"scale\":{textScale:R},\"lineScale\":{View.TextScale:R}}}"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Test diagnostics must never affect reading.
        }
    }
```

  - **Constructor:** set `this.textScale = textScale;` and
    `this.placeFile = placeFile;`.
  - **`FindAsync`:** replace the `int top = …` line with
    `int top = FirstVisibleOffset;`. Replace the block that shows the
    match, from `TextMatch match = matches[index];` to the
    `View.ScrollToLine(...)` call, with:

```csharp
        TextMatch match = matches[index];
        shownMatch = match;
        View.ShowMatch(Segments(match));
        reflowView?.ShowMatch(ReflowHighlights.For(plan!, match.Start, match.Length));
        if (Mode == TextViewMode.Reflow && reflowView is not null)
        {
            reflowView.ScrollToOffset(match.Start);
        }
        else
        {
            // A little context above the match, as a reader expects.
            View.ScrollToLine(Math.Max(0, document.LineAtOffset(match.Start) - 3));
        }
```

  - **The no-match branch** (`View.ClearMatch(); return FindResult.None;`)
    and **`ClearFind`:** also set `shownMatch = null;` and call
    `reflowView?.ClearMatch();`.
  - **`GetLocationAsync`:** return
    `TextLocator.CaptureAt(document, FirstVisibleOffset)`.
  - **`RestoreLocationAsync`:** inside the `if`, scroll whichever view
    shows:

```csharp
            if (Mode == TextViewMode.Reflow && reflowView is not null)
            {
                reflowView.ScrollToOffset(document.LineStarts[Math.Clamp(restore.Line, 0, document.LineStarts.Count - 1)]);
            }
            else
            {
                View.ScrollToLine(restore.Line);
            }
```

  - **`ExecuteAsync`:** each case acts on the view that shows. Text size
    applies to both:

```csharp
            case ScrollAction scroll:
                if (Mode == TextViewMode.Reflow && reflowView is not null) reflowView.ScrollByViewport(scroll.VerticalViewportFraction);
                else View.ScrollByViewport(scroll.VerticalViewportFraction);
                break;
            case PageTurnAction page:
                if (Mode == TextViewMode.Reflow && reflowView is not null) reflowView.PageBy(page.Delta);
                else View.PageBy(page.Delta);
                break;
            case PageEdgeAction edge:
                if (Mode == TextViewMode.Reflow && reflowView is not null) reflowView.ScrollToEdge(edge.Edge);
                else View.ScrollToEdge(edge.Edge);
                break;
            case TextSizeAction size:
                textScale = size.Scale;
                View.TextScale = size.Scale;
                if (reflowView is not null) reflowView.TextScale = size.Scale;
                WritePlaceForTest();
                break;
```

  - **`DisposeAsync`:** also unsubscribe and clear the reflow view:

```csharp
            if (reflowView is not null)
            {
                reflowView.TopBlockChanged -= OnTopLineChanged;
                reflowView.Clear();
            }
```

  - **`OnTopLineChanged`:** call `WritePlaceForTest();` before raising
    `LocationChanged`.
  - **`TextReaderView.TextScale`:** if it has no getter, add
    `get => textScale;`.

- [ ] **Step 4: Build**

Run: stage, then the Production build.
Expected: 0 warnings, 0 errors. If the `Grid { Children: [...] }` list
pattern doesn't compile against `UIElementCollection`, index
`grid.Children[0]` and `grid.Children[1]` instead, and ledger it.

- [ ] **Step 5: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Production/TextReflowView.xaml src/DesktopGuides.Production/TextReflowView.xaml.cs src/DesktopGuides.Production/TextReaderSession.cs src/DesktopGuides.Production/TextReaderView.xaml.cs
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): the reflow view, and a TXT session with both views"
```

---
### Task 3: The choice and the shell (Production)

**Files:**
- Create: `src/DesktopGuides.Production/ShellWindow.TextView.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (the choice beside the toolbar)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (the TXT open path, the reader route start, the initialisation)
- Modify: `src/DesktopGuides.Production/ShellWindow.Contents.cs` (`FocusReaderContent`)

**Interfaces:**
- Consumes: Task 2's session members; Task 1's `TextViewMessages`; PR a's `GetReaderPreferencesAsync(...).TextView` and `SaveTextViewAsync`, through `RequireRepository()`.
- Produces (for Task 4): the `BoundedChoice` with the AutomationIds `ReaderTextView`, `ReaderTextViewOriginal` and `ReaderTextViewReflow`; the statuses "Reflow view.", "Original view." (quiet) and "This guide has no prose to reflow." (warning bar).

**Ruling carried into this task:** the choice sits beside the toolbar, not
inside its `CommandBar`. `tools/p1/DesktopGuides.ReaderToolbarSmoke`
compiles `ReaderToolbar.xaml` on its own, without the Toolkit, so a
`BoundedChoice` can't live there.

- [ ] **Step 1: The choice in `ShellWindow.xaml`.** Replace
  `<local:ReaderToolbar x:Name="ReaderActions" />` with:

```xml
                    <Grid ColumnSpacing="12">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <local:ReaderToolbar x:Name="ReaderActions" />
                        <!-- T19.2: TXT only; beside the toolbar, which compiles without the Toolkit. -->
                        <local:BoundedChoice x:Name="ReaderTextView" Grid.Column="1"
                                             VerticalAlignment="Center" Visibility="Collapsed"
                                             AutomationProperties.AutomationId="ReaderTextView"
                                             AutomationProperties.Name="Text view" />
                    </Grid>
```

- [ ] **Step 2: `ShellWindow.TextView.cs`**

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Core.Text;
using Microsoft.UI.Xaml;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    // T19.2: a TXT guide's Original or Reflow view, chosen and saved per guide.
    private const string TextViewSaveFailed = "Couldn't save the view for this guide.";
    private bool settingTextView;
    private bool switchingTextView;
    private Guid? textViewGuide;

    private void InitializeTextView()
    {
        ReaderTextView.SetOptions(
        [
            new BoundedChoiceOption("Original", "ReaderTextViewOriginal"),
            new BoundedChoiceOption("Reflow", "ReaderTextViewReflow")
        ]);
        ShowTextViewChoice(TextViewMode.Original);
        ReaderTextView.ChoiceChanged += (_, _) =>
        {
            if (settingTextView) return;
            _ = ChooseTextViewAsync(ReaderTextView.SelectedIndex == 1 ? TextViewMode.Reflow : TextViewMode.Original);
        };
    }

    // Sets the choice without acting on it.
    private void ShowTextViewChoice(TextViewMode mode)
    {
        settingTextView = true;
        try
        {
            ReaderTextView.SelectedIndex = mode == TextViewMode.Reflow ? 1 : 0;
        }
        finally
        {
            settingTextView = false;
        }
    }

    private void HideTextViewChoice()
    {
        ReaderTextView.Visibility = Visibility.Collapsed;
        textViewGuide = null;
    }

    private async Task<TextViewMode> ReadTextViewAsync(Guid guideId)
    {
        try
        {
            return (await RequireRepository().GetReaderPreferencesAsync(guideId))?.TextView ?? TextViewMode.Original;
        }
        catch (Exception)
        {
            // An unreadable preference is Original; reading goes on.
            return TextViewMode.Original;
        }
    }

    // Called once the TXT session exists, before its surface shows. A saved
    // Reflow with no plan opens in Original, quietly.
    private async Task StartTextViewAsync(Guid guideId, TextReaderSession session, CancellationToken token)
    {
        textViewGuide = guideId;
        session.ViewChanged += OnTextViewChanged;
        if (await ReadTextViewAsync(guideId) == TextViewMode.Reflow)
        {
            await session.SetViewAsync(TextViewMode.Reflow, token);
        }
        ShowTextViewChoice(session.Mode);
        ReaderTextView.Visibility = (session.Capabilities & ReaderCapabilities.Reflow) != 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task ChooseTextViewAsync(TextViewMode mode)
    {
        if (readerSession is not TextReaderSession session || textViewGuide is not Guid guide || switchingTextView)
        {
            return;
        }
        switchingTextView = true;
        try
        {
            bool shown = await session.SetViewAsync(mode, CancellationToken.None);
            if (!ReferenceEquals(session, readerSession)) return;
            if (!shown)
            {
                ShowTextViewChoice(TextViewMode.Original);
                ShowWarningStatus(TextViewMessages.NoProse);
                return;
            }
            try
            {
                await RequireRepository().SaveTextViewAsync(guide, mode);
            }
            catch (Exception)
            {
                ShowErrorStatus(TextViewSaveFailed);
                return;
            }
            AnnounceStatus(TextViewMessages.For(mode));
        }
        finally
        {
            switchingTextView = false;
        }
    }

    private void OnTextViewChanged(object? sender, EventArgs args)
    {
        if (sender is TextReaderSession session && ReferenceEquals(session, readerSession))
        {
            ReaderSurface.Content = session.Surface;
        }
    }
}
```

  `RequireRepository()` is the repository `ReadTextScaleAsync` uses.

- [ ] **Step 3: Wire it into the shell**
  - **`ShellWindow.xaml.cs`, where `InitializeContents();` and
    `InitializeBookmarks();` run:** add `InitializeTextView();`.
  - **The reader route start** (`case ReaderRoute readerRoute:`): after
    `ReaderPanel.Visibility = Visibility.Visible;`, add
    `HideTextViewChoice();`.
  - **The TXT open path:**
    - the session is built with
      `TextReaderSession.PlaceFileForTest(cacheRoot!)` as its fifth
      argument;
    - after `readerSession = session;` and before `ShowReaderSurface(...)`,
      add:

```csharp
                    await StartTextViewAsync(guide.Id, session, readerToken);
                    if (generation != renderGeneration)
                    {
                        return false;
                    }
```

    - and the surface it shows is `session.Surface`:
      `ShowReaderSurface(loading: false, view: session.Surface);`.
  - **`ShellWindow.Contents.cs` `FocusReaderContent`:** the TXT case
    becomes `TextReaderSession text => text.FocusSurface(),`.

- [ ] **Step 4: Build, and run Core**

Run: stage, the Production build (expected: 0 warnings, 0 errors), then
the whole Core suite (expected: 1324). Also build the toolbar smoke
project, since this task leaves its files unchanged:
`s 'cd /d E:\work\desktop-guides\t19-2b && dotnet build tools\p1\DesktopGuides.ReaderToolbarSmoke -c Release -p:Platform=x64 -nologo > E:\work\t19-2b-toolbar.log 2>&1 & findstr /c:"Warning(s)" /c:"Error(s)" E:\work\t19-2b-toolbar.log'`
(expected: 0 errors).

- [ ] **Step 5: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Production
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): choose a TXT guide's Original or Reflow view"
```

---
### Task 4: The installed reflow modes

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (`seed-reflow`)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (the modes, helpers and quiet pattern)
- Modify: `tools/p1/windows_shell_install.ps1` (`Run-NavigationScenarios`)

**Interfaces:**
- Consumes:
  - Task 2's `txt-place-<pid>.json` (`view`, `offset`, `scale`, `lineScale`);
  - Task 3's AutomationIds and statuses;
  - `ReaderReflowBlocks`.
- Produces: `reflow-switch`, `reflow-restart`, `reflow-none`, `reflow-find` and `reflow-bookmark`, and the screenshots `reflow-switch` and `reflow-find`.

- [ ] **Step 1: The seed.** In `ShellSeed/Program.cs`, beside
  `seed-bookmarks`:

```csharp
if (args.Length == 3 && args[0] == "seed-reflow")
{
    // T19.2: a long TXT guide, twelve chapters of the reflow fixture, and
    // Numbered Lines Guide saved as Reflow. It has no prose, so it opens in Original.
    ManagedPathResolver reflowPaths = new(args[1]);
    await using (SqliteLibraryRepository reflowRepository = new(reflowPaths))
    {
        await reflowRepository.InitializeAsync();
        Game reflowGame = (await reflowRepository.ListGamesAsync()).Single(game => game.Title == "Progress Game");
        string chapter = File.ReadAllText(Path.Combine(Path.GetFullPath(args[2]), "p2", "txt-reflow.txt"));
        string folder = Directory.CreateTempSubdirectory("desktop-guides-reflow-").FullName;
        string file = Path.Combine(folder, "reflow-guide.txt");
        File.WriteAllText(file, string.Concat(Enumerable.Range(1, 12).Select(n => $"Chapter {n:00}\n\n{chapter}\n")));
        if (await new GuideImportValidator().InspectAsync(file, CancellationToken.None) is not ImportReady ready)
        {
            throw new InvalidOperationException("The reflow guide failed the import preview.");
        }
        await new GuideImportPublisher(reflowRepository, reflowPaths).PublishAsync(
            ready.Manifest, reflowGame.Id, "Reflow Guide", false, null, CancellationToken.None);
        Directory.Delete(folder, recursive: true);
    }
    ExecuteSql(reflowPaths,
        "UPDATE ReaderPreferences SET TextView = 'Reflow' WHERE GuideId = (SELECT Id FROM Guides WHERE Title = 'Numbered Lines Guide')");
    return 0;
}
```

- [ ] **Step 2: Register the modes in the smoke.** In
  `windows_shell_ui_smoke.ps1`:
  - **the `ValidateSet` (line 29) and the reader-mode `-in` list:** add
    `'reflow-switch', 'reflow-restart', 'reflow-none', 'reflow-find',
    'reflow-bookmark'` after `'bookmark-changed'`;
  - **the game mapping** (`-like 'bookmark-*') { 'Progress Game' }`): add
    `-or $Mode -like 'reflow-*'`;
  - **the dispatch block** (`… -or $Mode -like 'bookmark-*') {`): add
    `-or $Mode -like 'reflow-*'`;
  - **`$quietStatusPatterns`:** add `'^(Reflow|Original) view\.$',`.

- [ ] **Step 3: The helpers,** after `Wait-SavedAfter`:

```powershell
            # T19.2: the TXT session's view and first visible offset (test gate).
            function Read-TextPlace {
                $path = Join-Path $AppCacheRoot "diagnostics\txt-place-$ProcessId.json"
                if (-not (Test-Path -LiteralPath $path)) { return $null }
                try { return Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json } catch { return $null }
            }
            function Wait-TextPlace([scriptblock] $until, [string] $what) {
                $deadline = (Get-Date).AddSeconds(10)
                $place = $null
                do {
                    $place = Read-TextPlace
                    if ($place -and (& $until $place)) { return $place }
                    Start-Sleep -Milliseconds 250
                } while ((Get-Date) -lt $deadline)
                [void](Save-WindowScreenshot 'text-place-timeout')
                throw "The TXT place never showed $what. Last: view=$($place.view) offset=$($place.offset)."
            }
            function Select-TextView([string] $view) {
                (Wait-VisibleById "ReaderTextView$view").GetCurrentPattern(
                    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            }
            function Wait-TextViewChosen([string] $view) {
                $deadline = (Get-Date).AddSeconds(10)
                do {
                    $item = Find-ById "ReaderTextView$view"
                    if ($item -and $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { return }
                    Start-Sleep -Milliseconds 200
                } while ((Get-Date) -lt $deadline)
                throw "Expected the $view view chosen."
            }
            function Find-Next([string] $query, [string] $count) {
                [System.Windows.Forms.SendKeys]::SendWait('^f')
                [void](Wait-VisibleById 'ReaderFindBar')
                (Get-SearchPattern 'FindQueryInput').SetValue($query)
                (Wait-VisibleById 'FindQueryInput').SetFocus()
                [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
                [void](Wait-Name 'FindCount' $count)
            }
```

- [ ] **Step 4: The modes,** after `bookmark-changed`'s block:

```powershell
            elseif ($Mode -eq 'reflow-switch') {
                Open-TextGuide 'Reflow Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                Wait-FirstTextRow
                Wait-TextViewChosen 'Original'
                # Down to chapter 7 in Original, by search.
                Find-Next 'Chapter 07' '1 of 1'
                [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
                Wait-HiddenById 'ReaderFindBar'
                $original = Wait-TextPlace { param($p) $p.view -eq 'Original' -and $p.offset -gt 0 } 'an Original place'
                # Reflow keeps the block that held the top line.
                Select-TextView 'Reflow'
                [void](Wait-Status 'Reflow view.')
                [void](Wait-VisibleById 'ReaderReflowBlocks')
                $reflow = Wait-TextPlace { param($p) $p.view -eq 'Reflow' } 'the Reflow view'
                if ($reflow.offset -gt $original.offset -or ($original.offset - $reflow.offset) -gt 2000) {
                    throw "Reflow opened at offset $($reflow.offset); Original was at $($original.offset)."
                }
                # Text size applies to both views.
                (Wait-VisibleById 'ReaderReflowBlocks').SetFocus()
                [System.Windows.Forms.SendKeys]::SendWait('^{ADD}')
                [void](Wait-TextPlace { param($p) [double] $p.scale -gt 1 } 'a larger text size')
                $report.reflowSwitchScreenshot = Save-WindowScreenshot 'reflow-switch'
                # Back to Original: that block's first line, at the same size.
                Select-TextView 'Original'
                [void](Wait-Status 'Original view.')
                $back = Wait-TextPlace { param($p) $p.view -eq 'Original' } 'Original again'
                if ($back.offset -ne $reflow.offset) { throw "Original came back at $($back.offset); the block started at $($reflow.offset)." }
                if ([double] $back.lineScale -ne [double] $back.scale) { throw "The line view kept size $($back.lineScale), not $($back.scale)." }
                # Leave it saved as Reflow, for reflow-restart.
                Select-TextView 'Reflow'
                [void](Wait-Status 'Reflow view.')
                $report.reflowSwitch = [ordered]@{ original = $original.offset; reflow = $reflow.offset; back = $back.offset }
                $report.phases += 'reflow-switch'
            }
            elseif ($Mode -eq 'reflow-restart') {
                # A new launch opens the guide in its saved view.
                Open-TextGuide 'Reflow Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                [void](Wait-VisibleById 'ReaderReflowBlocks')
                Wait-TextViewChosen 'Reflow'
                [void](Wait-TextPlace { param($p) $p.view -eq 'Reflow' } 'Reflow after a restart')
                $report.phases += 'reflow-restart'
            }
            elseif ($Mode -eq 'reflow-none') {
                # Seeded as Reflow, but with no prose: it opens in Original, quietly.
                Open-TextGuide 'Numbered Lines Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                Wait-FirstTextRow
                Wait-TextViewChosen 'Original'
                Select-TextView 'Reflow'
                [void](Wait-Status 'This guide has no prose to reflow.')
                Wait-TextViewChosen 'Original'
                if (Find-ById 'ReaderReflowBlocks') { throw 'A guide with no prose showed the Reflow view.' }
                $report.phases += 'reflow-none'
            }
            elseif ($Mode -eq 'reflow-find') {
                Open-TextGuide 'Reflow Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                [void](Wait-VisibleById 'ReaderReflowBlocks')
                Find-Next 'eastern marsh' '1 of 12'
                $first = Wait-TextPlace { param($p) $p.view -eq 'Reflow' -and $p.offset -gt 0 } 'the first match'
                [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
                [void](Wait-Name 'FindCount' '2 of 12')
                $second = Wait-TextPlace { param($p) $p.offset -gt $first.offset } 'the second match'
                $report.reflowFindScreenshot = Save-WindowScreenshot 'reflow-find'
                # Switching keeps the search: the next step goes on from the second match.
                Select-TextView 'Original'
                [void](Wait-Status 'Original view.')
                (Wait-VisibleById 'FindQueryInput').SetFocus()
                [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
                [void](Wait-Name 'FindCount' '3 of 12')
                [void](Wait-TextPlace { param($p) $p.view -eq 'Original' -and $p.offset -gt $second.offset } 'the third match in Original')
                Select-TextView 'Reflow'
                [void](Wait-Status 'Reflow view.')
                $report.phases += 'reflow-find'
            }
            elseif ($Mode -eq 'reflow-bookmark') {
                # A bookmark added in Reflow is followed exactly in Original.
                Open-TextGuide 'Reflow Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                [void](Wait-VisibleById 'ReaderReflowBlocks')
                Find-Next 'Chapter 05' '1 of 1'
                [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
                Wait-HiddenById 'ReaderFindBar'
                $marked = Wait-TextPlace { param($p) $p.view -eq 'Reflow' -and $p.offset -gt 0 } 'the place to bookmark'
                (Wait-VisibleById 'ReaderReflowBlocks').SetFocus()
                [System.Windows.Forms.SendKeys]::SendWait('^d')
                [void](Wait-Status 'Bookmark added.')
                [void](Wait-BookmarkNames @('*') 'adding in Reflow')
                [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
                Select-TextView 'Original'
                [void](Wait-Status 'Original view.')
                [System.Windows.Forms.SendKeys]::SendWait('^{HOME}')
                [void](Wait-TextPlace { param($p) $p.view -eq 'Original' -and $p.offset -eq 0 } 'the start')
                [System.Windows.Forms.SendKeys]::SendWait('^+b')
                [void](Wait-BookmarkNames @('*') 'opening the bookmarks')
                [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
                [void](Wait-TextPlace { param($p) $p.view -eq 'Original' -and $p.offset -eq $marked.offset } 'the bookmark in Original')
                $report.phases += 'reflow-bookmark'
            }
```

- [ ] **Step 5: The installer passes.** In `Run-NavigationScenarios`,
  after the bookmark passes:

```powershell
    # T19.2: the reflow view.
    Invoke-ShellSeed @('seed-reflow', $dataRoot, $fixtureRoot) | Out-Null
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.reflowSwitch = Invoke-ProgressPass 'reflow-switch' 0
    $report.reflowRestart = Invoke-ProgressPass 'reflow-restart' 0
    $report.reflowNone = Invoke-ProgressPass 'reflow-none' 0
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.reflowFind = Invoke-ProgressPass 'reflow-find' 0
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    Invoke-ShellSeed @('clear-bookmarks', $dataRoot) | Out-Null
    $report.reflowBookmark = Invoke-ProgressPass 'reflow-bookmark' 0
```

- [ ] **Step 6: Check the scripts and the seed on the host**

Run:
- stage, then the script parse check:
  `s 'powershell -NoProfile -ExecutionPolicy Bypass -File E:\work\desktop-guides\t19-2b\tools\p1\test_windows_scripts_parse.ps1'`
  (expected: all scripts pass);
- the seed build:
  `s 'cd /d E:\work\desktop-guides\t19-2b && dotnet build tools\p1\DesktopGuides.ShellSeed -nologo -warnaserror > E:\work\t19-2b-seed.log 2>&1 & findstr /c:"Warning(s)" /c:"Error(s)" E:\work\t19-2b-seed.log'`
  (expected: 0 warnings, 0 errors);
- the ASCII check:
  `LC_ALL=C grep -c -P '[^\x00-\x7F]' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1`
  (expected: 0 for both).

- [ ] **Step 7: Commit, push, and run the navigation group on both lanes**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add tools/p1
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "test(p2): the installed reflow modes"
git -C /Users/ilya.lissoboi/work/desktop-guides push -u origin feat/p2-t19-2-reflow-view
```

Then dispatch `shell-scope=navigation` on `msix` and `portable`.
Expected: both pass, with the five `reflow-*` modes beside the existing
modes. Look at the `reflow-switch` and `reflow-find` screenshots:
- prose wraps;
- the map and table keep their layout;
- the choice reads Reflow.

A failure is debugged under superpowers:systematic-debugging; never
weaken a check to pass it.

---
### Task 5: Records, the final review, the PR run and the PR

**Files:**
- Modify: `docs/p2/t19-2-reflow-design.md` (Status; a "PR b verification record")
- Modify: `docs/p2/results.md` (the #130 row)
- Modify: `docs/p2/implementation-plan.md` and `docs/progress.md`
- Create: `docs/p2/evidence/ci/t19-2/reflow-switch.png` and `reflow-find.png`, from Task 4's run

- [ ] **Step 1: The full suites and the build.** Run Core (expected:
  1324), Infrastructure (expected: 837), the Production build (expected:
  0 warnings) and the script parse check on the host.
- [ ] **Step 2: The final review.** Run the whole-branch review first.
  Its fixes land before the records.
- [ ] **Step 3: The records.**
  - **The design's Status:** "Approved in conversation, 16 October 2026.
    PR a merged as #130. PR b (the reflow view) implemented on
    `feat/p2-t19-2-reflow-view`."
  - **The "PR b verification record":** the tests, the navigation
    dispatches on both lanes, the screenshots, the review and every
    ruling. That includes the choice sitting beside the toolbar rather
    than in it, recorded as deviation 5.
  - **`results.md`:** #130's row: 2026-10-16, `09b6bf9`, T19.2 (PR a), run
    38905245907, Core 1317, Infra 837, linking the design's "PR a
    verification record". The note: "The reflow planner and the view
    preference. Passed on the first attempt."
  - **The status pages:** "M10 in progress: T19.2 PR a merged (#130); PR b
    (the reflow view) in review." The `progress.md` "Next" becomes "T21.1
    format survey".
- [ ] **Step 4: Commit, push, open the PR, and wait for its run.**
  - **Commit:** "docs(p2): record T19.2 PR b".
  - **The PR:** "T19.2 PR b: the reflow view", based on `main`. It names
    the task, the prerequisite (PR a, merged as #130) and the outcome. It
    includes the two screenshots (this is a UI change), the dispatches,
    every ruling and the deferred minors.
  - **Expected:** every job passes. Record the run in the PR, then stop
    for the user's merge decision.

## Traceability

| Design item | Task |
| --- | --- |
| The reflow view: prose wraps, preserved blocks keep their layout and scroll sideways | 2, 4 |
| Text size applies to both views | 2, 4 |
| Places are offsets kept to the block; `CaptureAt` | 2, 4 |
| Search, contents and bookmarks in both views | 1, 2, 4 |
| The choice, shown only for TXT (`ReaderCapabilities.Reflow`) | 1, 3 |
| Switching keeps the block and saves the choice; quiet announcement | 2, 3, 4 |
| Opening in the saved view; a `null` plan opens in Original | 3, 4 |
| "This guide has no prose to reflow." | 1, 3, 4 |
| Planning off the UI thread within the budget | 2 |
| TR19.1: the view only displays | 2 |
