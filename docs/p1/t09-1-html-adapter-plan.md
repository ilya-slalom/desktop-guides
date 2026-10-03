# T09.1 Managed-guide HTML adapter — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An HTML guide that can't open says why and offers the one action
that helps, a crashed WebView2 shows an error with **Reopen** instead of a
blank page, and leftover WebView2 profiles are removed at startup.

**Architecture:** Core gains the new errors, their copy and an
error-to-action mapping. Infrastructure gains `WebView2ProfileSweeper` and
tells a missing entry file apart from a changed one. The Production session
probes the runtime before creating the environment and reports process
failures through a `Failed` event. The shell shows an action button under
the Reader error, wires it to the external-link launcher or a re-render, and
runs the sweep once at startup. The installed smoke covers the sweep, a
killed renderer, a missing runtime and a missing entry file.

**Tech Stack:** .NET 10, C#, xUnit, WinUI 3 with WebView2, PowerShell UI
Automation smoke.

**Spec:** [t09-1-html-adapter-design.md](t09-1-html-adapter-design.md)

## Global Constraints

- Copy, verbatim:
  - RuntimeMissing: "Web page guides need the Microsoft Edge WebView2 Runtime."
  - RuntimeFailed: "Web page guides couldn't start."
  - Crashed: "This guide stopped responding."
  - Missing: "This guide's file is missing from the library."
  - NoManifest and Changed: unchanged.
  - Button labels: "Get WebView2 Runtime", "Reopen".
  - Launch failure: "This link couldn't be opened."
- `RuntimeDownloadUrl` = `https://developer.microsoft.com/microsoft-edge/webview2/`.
- New AutomationId: `ReaderLoadErrorAction`. `ReaderLoadError` keeps its id.
- New gate: `Local\DesktopGuides.Preview.WebView2Missing.<pid>`; its browser
  folder is `<cacheRoot>\missing-runtime-test\`.
- Profiles: `<cacheRoot>\WebView2\<Guid "N">`. The sweeper touches nothing
  else, never follows a reparse point, and never throws for IO or access
  errors.
- Append new `HtmlGuideLoadError` members at the end, so existing values
  keep their numbers.
- Treat imported HTML and asset paths as untrusted; never fall back to the
  source path or the network.
- PowerShell stays ASCII-only. UI tests assert only what app code controls.
- No local `dotnet`: RED/GREEN is read from CI (`gh workflow run
  windows-ci.yml --ref feat/p1-t09-1-html-adapter`, `gh run watch <id>
  --exit-status --interval 60`, `gh run view <id> --log-failed`). Unit
  tests run in the `core-tests` job; the installed gate is
  `production-shell-ui`.
- There is no Production unit-test project. Session and shell behavior is
  tested by the installed smoke (Task 4), which goes RED before Tasks 5-6.
- The ExternalLaunch gate stays open in every HTML pass, so no real browser
  ever opens. The canary is loopback-only; add no firewall rules.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **A crash while `OpenAsync` still waits for navigation** (a large guide
   on a slow disk). The user should see "This guide stopped responding."
   with **Reopen**, not the "changed, re-import" message. Task 5 routes it
   through the navigation TCS. There is no automated test (no Production
   test host, and the timing can't be forced from the smoke), so the
   reviewer checks the code path by hand.
2. **A read-only file inside a leftover profile.** It is still removed.
   Pinned in Task 3 (`RemovesReadOnlyFiles`).
3. **`WebView2\` itself is a junction** to a folder elsewhere. Nothing is
   deleted through it. Pinned in Task 3 (`ProfilesFolderThatIsALinkIsLeftAlone`,
   Windows only).
4. **The entry path is now a folder.** It reads as `Changed`, not
   `Missing`, because something is there that isn't the imported file.
   Pinned in Task 2 (`EntryThatIsAFolderIsChanged`).
5. **A TXT guide opened after an HTML error with an action.** No stale
   button shows. Pinned in Task 4 (`Assert-Absent 'ReaderLoadErrorAction'`
   on the TXT guide in the runtime-missing pass).

## Rulings (planning decisions that refine the spec)

- **R1. The crash phase runs in both the online and offline passes.** Both
  passes use the `html-reader` smoke mode, so each expects sessions A, B,
  B. It costs one extra open of B per pass, and it gives the light (online)
  and dark (offline) screenshots of the error surface with its button. The
  runtime-missing pass adds a second dark screenshot with the other label.
- **R2. The shell handles `Failed` inside the navigation queue.** A render
  holding the queue finishes first, so a crash just after `OpenAsync`
  returns is still torn down, and no render can interleave with the
  teardown. That replaces a separate generation check after `OpenAsync`.
- **R3. `ReaderSurface` is collapsed when it has no view**, so an empty
  `ContentControl` above the error never sits over the button.
- **R4. The loader checks the planned entry path before reading it.**
  `ManagedHtmlAssetReader.Read` returns `Missing` for link and access
  errors too, so its status alone can't tell missing from changed.

---

### Task 1: Core errors, copy and actions

**Files:**
- Modify: `src/DesktopGuides.Core/Html/HtmlGuideLoadMessages.cs`
- Test: `tests/DesktopGuides.Core.Tests/HtmlGuideLoadMessagesTests.cs`

**Interfaces:**
- Produces:
  - `enum HtmlGuideLoadError { RuntimeMissing, NoManifest, Changed, Missing, RuntimeFailed, Crashed }`
  - `enum HtmlGuideLoadAction { None, GetRuntime, Reopen }`
  - `HtmlGuideLoadMessages.RuntimeDownloadUrl` (const string)
  - `HtmlGuideLoadMessages.For(HtmlGuideLoadError) : string`
  - `HtmlGuideLoadMessages.ActionFor(HtmlGuideLoadError) : HtmlGuideLoadAction`
  - `HtmlGuideLoadMessages.ActionLabel(HtmlGuideLoadAction) : string` (throws for `None`)

- [ ] **Step 1: Write the failing tests.** Replace the test file with:

```csharp
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlGuideLoadMessagesTests
{
    [Theory]
    [InlineData(HtmlGuideLoadError.RuntimeMissing, "Web page guides need the Microsoft Edge WebView2 Runtime.")]
    [InlineData(HtmlGuideLoadError.RuntimeFailed, "Web page guides couldn't start.")]
    [InlineData(HtmlGuideLoadError.Crashed, "This guide stopped responding.")]
    [InlineData(HtmlGuideLoadError.Missing, "This guide's file is missing from the library.")]
    [InlineData(HtmlGuideLoadError.NoManifest, "Re-import this guide to read it.")]
    [InlineData(HtmlGuideLoadError.Changed, "This guide's files have changed. Re-import it to read it.")]
    public void EachErrorHasOneSentence(HtmlGuideLoadError error, string message) =>
        Assert.Equal(message, HtmlGuideLoadMessages.For(error));

    [Theory]
    [InlineData(HtmlGuideLoadError.RuntimeMissing, HtmlGuideLoadAction.GetRuntime)]
    [InlineData(HtmlGuideLoadError.RuntimeFailed, HtmlGuideLoadAction.Reopen)]
    [InlineData(HtmlGuideLoadError.Crashed, HtmlGuideLoadAction.Reopen)]
    [InlineData(HtmlGuideLoadError.Missing, HtmlGuideLoadAction.None)]
    [InlineData(HtmlGuideLoadError.NoManifest, HtmlGuideLoadAction.None)]
    [InlineData(HtmlGuideLoadError.Changed, HtmlGuideLoadAction.None)]
    public void EachErrorHasOneAction(HtmlGuideLoadError error, HtmlGuideLoadAction action) =>
        Assert.Equal(action, HtmlGuideLoadMessages.ActionFor(error));

    [Theory]
    [InlineData(HtmlGuideLoadAction.GetRuntime, "Get WebView2 Runtime")]
    [InlineData(HtmlGuideLoadAction.Reopen, "Reopen")]
    public void EachActionHasALabel(HtmlGuideLoadAction action, string label) =>
        Assert.Equal(label, HtmlGuideLoadMessages.ActionLabel(action));

    [Fact]
    public void NoActionHasNoLabel() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => HtmlGuideLoadMessages.ActionLabel(HtmlGuideLoadAction.None));

    [Fact]
    public void MissingMatchesTheTextGuideCopy() =>
        Assert.Equal(TextGuideLoadMessages.For(TextGuideLoadError.Missing),
            HtmlGuideLoadMessages.For(HtmlGuideLoadError.Missing));

    [Fact]
    public void RuntimeDownloadUrlIsMicrosoftsHttpsPage()
    {
        Uri url = new(HtmlGuideLoadMessages.RuntimeDownloadUrl, UriKind.Absolute);
        Assert.Equal(Uri.UriSchemeHttps, url.Scheme);
        Assert.Equal("developer.microsoft.com", url.Host);
    }

    [Fact]
    public void UndefinedErrorsThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HtmlGuideLoadMessages.For((HtmlGuideLoadError)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => HtmlGuideLoadMessages.ActionFor((HtmlGuideLoadError)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => HtmlGuideLoadMessages.ActionLabel((HtmlGuideLoadAction)99));
    }
}
```

- [ ] **Step 2: Watch it fail.** Push and run CI (Global Constraints).
  Expected: `core-tests` fails to compile Core.Tests (`CS0117`
  `RuntimeFailed`, `CS0246` `HtmlGuideLoadAction`). This step can share a
  CI run with Tasks 2 and 3's RED steps.

- [ ] **Step 3: Implement.** Replace the source file with:

```csharp
namespace DesktopGuides.Core.Html;

// New members go at the end, so existing values keep their numbers.
public enum HtmlGuideLoadError { RuntimeMissing, NoManifest, Changed, Missing, RuntimeFailed, Crashed }

// The one thing the Reader offers to do about an error.
public enum HtmlGuideLoadAction { None, GetRuntime, Reopen }

public static class HtmlGuideLoadMessages
{
    public const string RuntimeDownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    public static string For(HtmlGuideLoadError error) => error switch
    {
        HtmlGuideLoadError.RuntimeMissing => "Web page guides need the Microsoft Edge WebView2 Runtime.",
        HtmlGuideLoadError.RuntimeFailed => "Web page guides couldn't start.",
        HtmlGuideLoadError.Crashed => "This guide stopped responding.",
        HtmlGuideLoadError.Missing => "This guide's file is missing from the library.",
        HtmlGuideLoadError.NoManifest => "Re-import this guide to read it.",
        HtmlGuideLoadError.Changed => "This guide's files have changed. Re-import it to read it.",
        _ => throw new ArgumentOutOfRangeException(nameof(error))
    };

    public static HtmlGuideLoadAction ActionFor(HtmlGuideLoadError error) => error switch
    {
        HtmlGuideLoadError.RuntimeMissing => HtmlGuideLoadAction.GetRuntime,
        HtmlGuideLoadError.RuntimeFailed or HtmlGuideLoadError.Crashed => HtmlGuideLoadAction.Reopen,
        HtmlGuideLoadError.Missing or HtmlGuideLoadError.NoManifest or HtmlGuideLoadError.Changed =>
            HtmlGuideLoadAction.None,
        _ => throw new ArgumentOutOfRangeException(nameof(error))
    };

    public static string ActionLabel(HtmlGuideLoadAction action) => action switch
    {
        HtmlGuideLoadAction.GetRuntime => "Get WebView2 Runtime",
        HtmlGuideLoadAction.Reopen => "Reopen",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}
```

- [ ] **Step 4: Watch it pass.** Push, run CI. Expected: `core-tests`
  passes Core.Tests. Can share a run with Tasks 2 and 3's GREEN steps.

- [ ] **Step 5: Commit.**

```bash
git add src/DesktopGuides.Core/Html/HtmlGuideLoadMessages.cs tests/DesktopGuides.Core.Tests/HtmlGuideLoadMessagesTests.cs
git commit -m "feat(p1): T09.1 HTML load errors with actions"
```

### Task 2: Loader tells a missing entry from a changed one

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Reading/ManagedHtmlGuideLoader.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Reading/ManagedHtmlGuideLoaderTests.cs`

**Interfaces:**
- Consumes: `HtmlGuideLoadError.Missing` (Task 1).
- Produces: `LoadAsync` returns `HtmlGuideLoadFailed(Missing)` when the
  entry's planned managed file doesn't exist. Signatures are unchanged.

- [ ] **Step 1: Write the failing tests.** Rename `MissingEntryIsChanged`
  to `MissingEntryIsMissing` and expect `Missing`, as below, and add the
  other three tests to the class.

```csharp
    [Fact]
    public async Task MissingEntryIsMissing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);
        File.Delete(harness.Paths.ResolveExistingGuideFile(guide.Id, "guide.html"));

        Assert.Equal(HtmlGuideLoadError.Missing, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task DeletedGuideFolderIsMissing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);
        Directory.Delete(harness.Paths.GetGuideRoot(guide.Id), recursive: true);

        Assert.Equal(HtmlGuideLoadError.Missing, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task EntryThatIsAFolderIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);
        string entry = harness.Paths.ResolveExistingGuideFile(guide.Id, "guide.html");
        File.Delete(entry);
        Directory.CreateDirectory(entry);

        Assert.Equal(HtmlGuideLoadError.Changed, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task GuideFolderThatIsALinkIsChanged()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);
        string root = harness.Paths.GetGuideRoot(guide.Id);
        string elsewhere = Path.Combine(Path.GetTempPath(), "desktop-guides-link-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.Move(root, elsewhere);
            RemovalLibrary.CreateJunction(root, elsewhere);

            Assert.Equal(HtmlGuideLoadError.Changed, await FailedAsync(harness, guide));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root);
            if (Directory.Exists(elsewhere)) Directory.Delete(elsewhere, recursive: true);
        }
    }
```

  `RemovalLibrary` is in namespace `DesktopGuides.Infrastructure.Tests`;
  add `using DesktopGuides.Infrastructure.Tests;` if the compiler can't
  see it from `DesktopGuides.Infrastructure.Tests.Reading`.
  `Directory.Move` across volumes fails; if the CI temp folder is on
  another volume than the harness root, put `elsewhere` beside the
  harness's data root instead (`Path.Combine(harness.Paths.DataRoot,
  "elsewhere")`) and ledger the change.

- [ ] **Step 2: Watch it fail.** Push, run CI. Expected: `core-tests`
  fails `MissingEntryIsMissing` and `DeletedGuideFolderIsMissing` (actual
  `Changed`). `EntryThatIsAFolderIsChanged` and
  `GuideFolderThatIsALinkIsChanged` pass already; they pin behavior Step 3
  must keep.

- [ ] **Step 3: Implement.** In `LoadAsync`, between the entry-row check
  and `ManagedHtmlAssetReader reader = new(...)`, add:

```csharp
        // The reader reports links and access errors as missing too, so
        // look at the planned path first: only an absent file is Missing.
        try
        {
            string planned = paths.GetPlannedGuideFile(guide.Id, entries[0].RelativePath);
            if (!File.Exists(planned) && !Directory.Exists(planned))
            {
                return new HtmlGuideLoadFailed(HtmlGuideLoadError.Missing);
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                          UnauthorizedAccessException or ArgumentException)
        {
            return new HtmlGuideLoadFailed(HtmlGuideLoadError.Changed);
        }
```

  And in the `ResolveExistingGuideFile` try, add a first catch, before the
  existing one, for a file deleted between the check and the read:

```csharp
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return new HtmlGuideLoadFailed(HtmlGuideLoadError.Missing);
        }
```

- [ ] **Step 4: Watch it pass.** Push, run CI. Expected: all
  `ManagedHtmlGuideLoaderTests` pass, along with the rest of
  Infrastructure.Tests.

- [ ] **Step 5: Commit.**

```bash
git add src/DesktopGuides.Infrastructure/Reading/ManagedHtmlGuideLoader.cs tests/DesktopGuides.Infrastructure.Tests/Reading/ManagedHtmlGuideLoaderTests.cs
git commit -m "feat(p1): T09.1 missing HTML entry reads as missing"
```

### Task 3: `WebView2ProfileSweeper`

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Storage/WebView2ProfileSweeper.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/WebView2ProfileSweeperTests.cs`

**Interfaces:**
- Produces: `public static class WebView2ProfileSweeper { public static int Sweep(string cacheRoot); }`
  in namespace `DesktopGuides.Infrastructure.Storage`. It returns the number
  of profile folders removed.

- [ ] **Step 1: Write the failing tests.**

```csharp
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class WebView2ProfileSweeperTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "desktop-guides-sweep-" + Guid.NewGuid().ToString("N"));

    private string Cache => Path.Combine(root, "Cache");
    private string Profiles => Path.Combine(Cache, "WebView2");

    public void Dispose()
    {
        if (!Directory.Exists(root)) return;
        // Remove test links first, so cleanup never follows one.
        foreach (string folder in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .Where(path => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                     .ToList())
        {
            Directory.Delete(folder);
        }
        Directory.Delete(root, recursive: true);
    }

    private string Profile(string? name = null)
    {
        string path = Path.Combine(Profiles, name ?? Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(path, "Default", "Cache"));
        File.WriteAllText(Path.Combine(path, "Default", "Cache", "data_0"), "cache");
        File.WriteAllText(Path.Combine(path, "Local State"), "{}");
        return path;
    }

    [Fact]
    public void RemovesProfileFoldersWithTheirFiles()
    {
        string first = Profile();
        string second = Profile();

        Assert.Equal(2, WebView2ProfileSweeper.Sweep(Cache));

        Assert.False(Directory.Exists(first));
        Assert.False(Directory.Exists(second));
        Assert.True(Directory.Exists(Profiles));
    }

    [Fact]
    public void KeepsEverythingThatIsNotAProfile()
    {
        string other = Profile("keep-me");
        string dashed = Profile(Guid.NewGuid().ToString("D"));
        string file = Path.Combine(Profiles, Guid.NewGuid().ToString("N"));
        File.WriteAllText(file, "not a folder");
        string diagnostics = Path.Combine(Cache, "diagnostics", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(diagnostics);

        Assert.Equal(0, WebView2ProfileSweeper.Sweep(Cache));

        Assert.True(Directory.Exists(other));
        Assert.True(Directory.Exists(dashed));
        Assert.True(File.Exists(file));
        Assert.True(Directory.Exists(diagnostics));
    }

    [Fact]
    public void MissingProfilesFolderIsANoOp()
    {
        Assert.Equal(0, WebView2ProfileSweeper.Sweep(Cache));
        Assert.False(Directory.Exists(Cache));
    }

    [Fact]
    public void RemovesReadOnlyFiles()
    {
        string profile = Profile();
        string locked = Path.Combine(profile, "Local State");
        File.SetAttributes(locked, FileAttributes.ReadOnly);

        Assert.Equal(1, WebView2ProfileSweeper.Sweep(Cache));
        Assert.False(Directory.Exists(profile));
    }

    [Fact]
    public void LockedProfileIsSkippedAndTheRestRemoved()
    {
        if (!OperatingSystem.IsWindows()) return;
        string held = Profile();
        string free = Profile();
        using (new FileStream(Path.Combine(held, "Local State"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(1, WebView2ProfileSweeper.Sweep(Cache));
        }

        Assert.True(File.Exists(Path.Combine(held, "Local State")));
        Assert.False(Directory.Exists(free));
    }

    [Fact]
    public void LinkInsideAProfileIsRemovedWithoutEnteringIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        string target = Path.Combine(root, "outside");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "user file");
        string profile = Profile();
        RemovalLibrary.CreateJunction(Path.Combine(profile, "Default", "link"), target);

        Assert.Equal(1, WebView2ProfileSweeper.Sweep(Cache));

        Assert.False(Directory.Exists(profile));
        Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
    }

    [Fact]
    public void ProfileThatIsALinkIsRemovedWithoutEnteringIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        string target = Path.Combine(root, "outside");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "user file");
        Directory.CreateDirectory(Profiles);
        string link = Path.Combine(Profiles, Guid.NewGuid().ToString("N"));
        RemovalLibrary.CreateJunction(link, target);

        Assert.Equal(1, WebView2ProfileSweeper.Sweep(Cache));

        Assert.False(Directory.Exists(link));
        Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
    }

    [Fact]
    public void ProfilesFolderThatIsALinkIsLeftAlone()
    {
        if (!OperatingSystem.IsWindows()) return;
        string target = Path.Combine(root, "outside");
        string inside = Path.Combine(target, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(inside);
        File.WriteAllText(Path.Combine(inside, "keep.txt"), "user file");
        Directory.CreateDirectory(Cache);
        RemovalLibrary.CreateJunction(Profiles, target);

        Assert.Equal(0, WebView2ProfileSweeper.Sweep(Cache));

        Assert.True(File.Exists(Path.Combine(inside, "keep.txt")));
    }
}
```

- [ ] **Step 2: Watch it fail.** Push, run CI. Expected: `core-tests`
  fails to compile Infrastructure.Tests (`CS0103`
  `WebView2ProfileSweeper`).

- [ ] **Step 3: Implement.**

```csharp
namespace DesktopGuides.Infrastructure.Storage;

/// <summary>
/// Removes the WebView2 profile folders a crash or a slow exit left behind.
/// Only names <c>HtmlReaderSession</c> generates are touched, and a link is
/// removed as a link: its target is never entered.
/// </summary>
public static class WebView2ProfileSweeper
{
    // Returns the number of profile folders removed.
    public static int Sweep(string cacheRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(cacheRoot);
        DirectoryInfo profiles = new(Path.Combine(cacheRoot, "WebView2"));
        int removed = 0;
        try
        {
            if (!profiles.Exists || IsLink(profiles)) return 0;
            foreach (DirectoryInfo profile in profiles.EnumerateDirectories())
            {
                if (!Guid.TryParseExact(profile.Name, "N", out _)) continue;
                try
                {
                    Delete(profile);
                    removed++;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // Still locked; the next startup tries again.
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // An unreadable cache never blocks startup.
        }
        return removed;
    }

    private static bool IsLink(FileSystemInfo entry) =>
        (entry.Attributes & FileAttributes.ReparsePoint) != 0;

    private static void Delete(DirectoryInfo folder)
    {
        if (!IsLink(folder))
        {
            foreach (FileSystemInfo entry in folder.EnumerateFileSystemInfos())
            {
                if (entry is DirectoryInfo child)
                {
                    Delete(child);
                    continue;
                }
                if (!IsLink(entry) && (entry.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    entry.Attributes &= ~FileAttributes.ReadOnly;
                }
                entry.Delete();
            }
        }
        // A link's own entry goes; its target is untouched.
        folder.Delete();
    }
}
```

- [ ] **Step 4: Watch it pass.** Push, run CI. Expected: all
  `WebView2ProfileSweeperTests` pass on the Windows runner, along with the
  rest of Infrastructure.Tests.

- [ ] **Step 5: Commit.**

```bash
git add src/DesktopGuides.Infrastructure/Storage/WebView2ProfileSweeper.cs tests/DesktopGuides.Infrastructure.Tests/WebView2ProfileSweeperTests.cs
git commit -m "feat(p1): T09.1 sweep leftover WebView2 profiles"
```

### Task 4: Installed smoke for sweep, crash, missing runtime and missing entry

This task writes the installed tests for Tasks 5 and 6 first. Its CI run is
the RED step for those tasks.

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (the `seed-html-reader` verb)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1`
- Modify: `tools/p1/windows_shell_install.ps1` (`Invoke-HtmlReaderPass`,
  `Assert-HtmlReaderPass`, `Run-HtmlReaderScenarios`)

**Interfaces:**
- Consumes: the copy, labels and URL from Task 1; the `Missing` loader
  result from Task 2; the sweeper from Task 3 (via Task 6's startup call);
  the gate name and `ReaderLoadErrorAction` id from Global Constraints.
- Produces:
  - `seed-html-reader` output gains `guideBEntry` (absolute path of Guide
    B's managed entry file). The game gains a TXT guide "Plain Text Guide"
    with `p0/txt-ascii.txt`.
  - Smoke mode `html-runtime-missing`. Mode `html-reader` gains the
    `html-crash` phase.
  - Report keys `htmlCrashScreenshot`, `htmlRuntimeMissingScreenshot`,
    `htmlReader.profileSweep`, `htmlReader.runtimeMissing`.

- [ ] **Step 1: Seed.** In `seed-html-reader`, after `Guid guideA = ...`:

```csharp
        // The runtime-missing pass checks that TXT guides still open.
        await InsertTextGuideAsync(htmlPaths, htmlGame.Id, Guid.NewGuid(), "Plain Text Guide",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            File.ReadAllBytes(Path.Combine(Path.GetFullPath(args[2]), "p0", "txt-ascii.txt")));
        Guide seededB = (await htmlRepository.GetGuideAsync(guideB))!;
```

  and add to the printed object:

```csharp
            // The runtime-missing pass deletes it to show the Missing error.
            guideBEntry = htmlPaths.ResolveExistingGuideFile(guideB, seededB.PrimaryRelativePath),
```

  Add `using DesktopGuides.Core.Library;` if `Guide` isn't already in
  scope.

- [ ] **Step 2: Smoke, shared setup.** In `windows_shell_ui_smoke.ps1`:
  - Add `'html-runtime-missing'` to the `$Mode` `ValidateSet`, after
    `'html-reader'`.
  - Add it to the `$Mode -in @('txt-reader', ..., 'html-reader')` list
    that opens the TXT/HTML block.
  - Change the `$textGame` line to:

```powershell
        $textGame = if ($Mode -in @('html-reader', 'html-runtime-missing')) { 'Web Reader Game' } else { 'Text Reader Game' }
```

- [ ] **Step 3: Smoke, `html-crash`.** In the `html-reader` branch, after
  `$report.phases += 'html-canary-b'` and before `Mark-CanaryLines
  'opened-b'`, add:

```powershell
            # html-crash: a stopped renderer shows an error with Reopen,
            # not a blank page, and Reopen opens the guide again.
            Mark-CanaryLines 'before-crash'
            $browsers = @(Get-CimInstance Win32_Process -Filter (
                "ParentProcessId = $ProcessId AND Name = 'msedgewebview2.exe'") |
                ForEach-Object { $_.ProcessId })
            $renderers = @(foreach ($browser in $browsers) {
                Get-CimInstance Win32_Process -Filter (
                    "ParentProcessId = $browser AND Name = 'msedgewebview2.exe'") |
                    Where-Object { $_.CommandLine -match '--type=renderer' }
            })
            if ($renderers.Count -eq 0) {
                throw 'Canary Guide B has no WebView2 renderer to stop.'
            }
            foreach ($renderer in $renderers) {
                Stop-Process -Id $renderer.ProcessId -Force -ErrorAction SilentlyContinue
            }
            [void](Wait-Name 'ReaderLoadError' 'This guide stopped responding.')
            [void](Wait-Name 'ReaderLoadErrorAction' 'Reopen')
            Assert-NoReaderCommands 'Canary Guide B (stopped)'
            $report.htmlCrashScreenshot = Save-WindowScreenshot 'html-crash'
            Invoke-Element (Find-ById 'ReaderLoadErrorAction')
            [void](Wait-Status 'Guide ready.')
            [void](Wait-PageName 'Canary guide B loaded')
            Assert-Absent 'ReaderLoadError'
            Assert-Absent 'ReaderLoadErrorAction'
            $report.phases += 'html-crash'
```

- [ ] **Step 4: Smoke, `html-runtime-missing`.** Add a branch right after
  the `html-reader` branch's closing brace (before the final `else`):

```powershell
        elseif ($Mode -eq 'html-runtime-missing') {
            [void](Wait-Name 'LibraryHeading' 'Library')
            Select-Element $textGame
            [void](Wait-Name 'GameHeading' $textGame)
            [void](Wait-Status 'Game ready.')

            # html-runtime-missing: the guide names the missing runtime and
            # offers Microsoft's page; the click goes to the test launcher.
            Open-TextGuide 'Canary Guide A'
            $runtimeMessage = 'Web page guides need the Microsoft Edge WebView2 Runtime.'
            [void](Wait-Name 'ReaderLoadError' $runtimeMessage)
            [void](Wait-Name 'ReaderLoadErrorAction' 'Get WebView2 Runtime')
            Assert-NoReaderCommands 'Canary Guide A (no runtime)'
            $report.htmlRuntimeMissingScreenshot = Save-WindowScreenshot 'html-runtime-missing'
            Invoke-Element (Find-ById 'ReaderLoadErrorAction')
            Start-Sleep -Seconds 1
            [void](Wait-Name 'ReaderLoadError' $runtimeMessage)
            $report.phases += 'html-runtime-missing'

            # html-missing-entry: the loader runs before WebView2, so a
            # deleted entry still says it is missing, with no action.
            Back-ToTextGame
            Open-TextGuide 'Canary Guide B'
            [void](Wait-Name 'ReaderLoadError' $missingMessage)
            Assert-Absent 'ReaderLoadErrorAction'
            $report.phases += 'html-missing-entry'

            # TXT guides still open, and show no HTML action.
            Back-ToTextGame
            Open-TextGuide 'Plain Text Guide'
            [void](Wait-Status 'Guide ready.')
            Assert-RowNames 'Plain Text Guide' $asciiNames
            Assert-Absent 'ReaderLoadError'
            Assert-Absent 'ReaderLoadErrorAction'
            $report.phases += 'html-runtime-missing-txt'
            Back-ToTextGame
        }
```

- [ ] **Step 5: Install script, pass runner and assertion.** In
  `windows_shell_install.ps1`, replace `Invoke-HtmlReaderPass` and
  `Assert-HtmlReaderPass` with:

```powershell
function Invoke-HtmlReaderPass([string] $resultName, [string] $mode = 'html-reader', [switch] $NoRuntime) {
    Start-InstalledShell
    $processId = $report.launchedProcessId
    $diagnosticsGate = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        "Local\DesktopGuides.Preview.HtmlDiagnostics.$processId")
    $launchGate = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        "Local\DesktopGuides.Preview.ExternalLaunch.$processId")
    # Points the runtime probe at an empty folder, as on a PC without WebView2.
    $runtimeGate = $null
    if ($NoRuntime) {
        $runtimeGate = [System.Threading.EventWaitHandle]::new(
            $false, [System.Threading.EventResetMode]::ManualReset,
            "Local\DesktopGuides.Preview.WebView2Missing.$processId")
    }
    try {
        $report.htmlReader[$resultName] = Run-ShellSmoke $mode -ResultName $resultName
        Close-InstalledShell
    }
    finally {
        if ($runtimeGate) { $runtimeGate.Dispose() }
        $launchGate.Dispose()
        $diagnosticsGate.Dispose()
    }
}

function Assert-HtmlReaderPass(
    [string] $pass, [string] $cacheRoot, $expectedSessions, [string[]] $expectedLaunches,
    [string] $logPath, [int] $baseline) {
    # R19: isolation is shown by exactly what each session served. Deny
    # counts are kept as evidence only, because the CSP can stop a
    # reference before the request handler sees it.
    $diagnostics = Join-Path $cacheRoot 'diagnostics'
    $remaining = [System.Collections.ArrayList]::new()
    foreach ($expected in $expectedSessions) { [void]$remaining.Add($expected) }
    $files = @(Get-ChildItem -LiteralPath $diagnostics -Filter 'html-session-*.json' -ErrorAction SilentlyContinue)
    if ($files.Count -ne $remaining.Count) {
        throw "The $pass pass wrote $($files.Count) HTML session diagnostics; expected $($remaining.Count)."
    }
    $sessions = @()
    foreach ($file in $files) {
        $session = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $served = @($session.served) -join ','
        $match = $null
        foreach ($candidate in $remaining) {
            if ($candidate.guideId -eq $session.guideId -and $candidate.served -ceq $served) {
                $match = $candidate
                break
            }
        }
        if (-not $match) {
            throw "Guide $($session.guideId) served '$served' in the $pass pass, which matches no expected session."
        }
        $remaining.Remove($match)
        $sessions += $session
    }

    $launchesPath = Join-Path $diagnostics 'external-launches.json'
    $launches = @()
    if (Test-Path -LiteralPath $launchesPath) {
        # Two statements, so Windows PowerShell doesn't wrap the parsed array.
        $launches = Get-Content -LiteralPath $launchesPath -Raw | ConvertFrom-Json
        $launches = @($launches)
    }
    if (($launches -join "`n") -cne ($expectedLaunches -join "`n")) {
        throw "The $pass pass launched '$($launches -join ', ')'; expected '$($expectedLaunches -join ', ')'."
    }

    $newLines = @(Get-HtmlCanaryLines $logPath | Select-Object -Skip $baseline)
    if ($newLines.Count -ne 0) {
        throw "The canary recorded guide-originated traffic in the $pass pass: $($newLines -join '; ')."
    }
    return [ordered]@{
        sessions = $sessions
        externalLaunches = $launches
        canaryLinesBefore = $baseline
    }
}
```

- [ ] **Step 6: Install script, scenarios.** Replace
  `Run-HtmlReaderScenarios` with:

```powershell
function Run-HtmlReaderScenarios {
    # Each canary guide opens with the loopback canary listening (light) and
    # stopped (dark): TR07.1-TR07.3. Both passes stop Guide B's renderer and
    # reopen it. A third pass has no runtime and a deleted entry: TR09.2-TR09.3.
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    $ids = Invoke-ShellSeed @('seed-html-reader', $dataRoot, $fixtureRoot) | ConvertFrom-Json
    $cacheRoot = Get-HtmlCacheRoot
    $diagnostics = Join-Path $cacheRoot 'diagnostics'
    $logPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath(
        (Join-Path $ResultDirectory 'html-canary.log'))
    Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
    $report.htmlReader = [ordered]@{ guideA = $ids.guideA; guideB = $ids.guideB }

    # Guide B is a Save Page As export named after its page title. The '%'
    # makes the import alias its entry to guide.html; its companion folder
    # keeps the source name. Non-ASCII characters are built so this file
    # stays ASCII.
    $titleB = "Canary Guide B (PS1) - Walkthrough's 100% Caf" + [char]0x00E9 + ' ' + [char]0x2013 + ' v2'
    $servedA = 'guide.html,images/a.png,style.css'
    $servedB = "${titleB}_files/b.png,${titleB}_files/style.css,guide.html"
    # html-crash: Guide B's stopped session and its reopened one.
    $readerSessions = @(
        @{ guideId = $ids.guideA; served = $servedA },
        @{ guideId = $ids.guideB; served = $servedB },
        @{ guideId = $ids.guideB; served = $servedB })
    $readerLaunches = @('https://example.com/desktop-guides-canary')

    # html-profile-sweep: startup removes a leftover profile and nothing else.
    $profiles = Join-Path $cacheRoot 'WebView2'
    $leftover = Join-Path $profiles ([Guid]::NewGuid().ToString('N'))
    $keep = Join-Path $profiles 'keep-me'
    New-Item -ItemType Directory -Force -Path $leftover, $keep | Out-Null
    Set-Content -LiteralPath (Join-Path $leftover 'leftover.txt') -Value 'leftover' -Encoding ASCII

    $originalTheme = Get-AppThemePreference
    $canary = $null
    try {
        $canary = Start-HtmlCanary $logPath
        $baseline = @(Get-HtmlCanaryLines $logPath).Count
        Set-AppThemePreference $true
        Invoke-HtmlReaderPass 'html-reader-online'
        Save-HtmlDiagnostics 'html-reader-online' $cacheRoot
        if (Test-Path -LiteralPath $leftover) {
            throw 'Startup left a leftover WebView2 profile in place.'
        }
        if (-not (Test-Path -LiteralPath $keep)) {
            throw 'The profile sweep removed a folder that is not a profile.'
        }
        $report.htmlReader.profileSweep = 'removed'
        $report.htmlReader.online = Assert-HtmlReaderPass 'online' $cacheRoot $readerSessions $readerLaunches $logPath $baseline

        Stop-HtmlCanary $canary
        $canary = $null
        if (Test-HtmlCanaryListening) {
            throw 'The loopback canary still answered after it was stopped.'
        }
        Remove-Item -LiteralPath $diagnostics -Recurse -Force
        $baseline = @(Get-HtmlCanaryLines $logPath).Count
        Set-AppThemePreference $false
        Invoke-HtmlReaderPass 'html-reader-offline'
        Save-HtmlDiagnostics 'html-reader-offline' $cacheRoot
        $report.htmlReader.offline = Assert-HtmlReaderPass 'offline' $cacheRoot $readerSessions $readerLaunches $logPath $baseline

        # The loader runs before WebView2, so Guide B reports its deleted
        # entry; Guide A's session fails at the runtime probe and serves nothing.
        Remove-Item -LiteralPath $ids.guideBEntry -Force
        Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
        $canary = Start-HtmlCanary $logPath
        $baseline = @(Get-HtmlCanaryLines $logPath).Count
        Invoke-HtmlReaderPass 'html-runtime-missing' 'html-runtime-missing' -NoRuntime
        Save-HtmlDiagnostics 'html-runtime-missing' $cacheRoot
        $report.htmlReader.runtimeMissing = Assert-HtmlReaderPass 'runtime-missing' $cacheRoot `
            @(@{ guideId = $ids.guideA; served = '' }) `
            @('https://developer.microsoft.com/microsoft-edge/webview2/') $logPath $baseline
    }
    finally {
        Stop-HtmlCanary $canary
        Remove-Item -LiteralPath $keep -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath (Join-Path $cacheRoot 'missing-runtime-test') -Recurse -Force -ErrorAction SilentlyContinue
        Restore-AppThemePreference $originalTheme
    }
}
```

  The runtime-missing pass keeps the dark theme from the offline pass.

- [ ] **Step 7: Watch it fail.** Check the edited scripts are ASCII
  (`LC_ALL=C grep -nP '[^\x00-\x7F]' tools/p1/*.ps1` prints nothing), push,
  run CI. Expected: `production-shell-ui` fails in the `html-reader-online`
  smoke at `html-crash` with "Expected visible 'ReaderLoadErrorAction'
  named 'Reopen'." (the error text may already show, through T07.3's
  generic path, or the page may simply go blank; either way the button
  doesn't exist yet). Unit-test jobs stay green.

- [ ] **Step 8: Commit.**

```bash
git add tools/p1/DesktopGuides.ShellSeed/Program.cs tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
git commit -m "test(p1): T09.1 installed sweep, crash and missing-runtime smoke"
```

### Task 5: Session runtime probe and process failure

**Files:**
- Modify: `src/DesktopGuides.Production/HtmlReaderSession.cs`

**Interfaces:**
- Consumes: `HtmlGuideLoadError.RuntimeFailed`, `Crashed` (Task 1).
- Produces:
  - `public event EventHandler<HtmlGuideLoadError>? Failed;` on
    `HtmlReaderSession`, raised once with `Crashed` after `OpenAsync`
    returns, never after dispose.
  - `OpenAsync` throws `HtmlGuideLoadException` with `RuntimeMissing`
    (probe failed), `RuntimeFailed` (environment or view failed), `Crashed`
    (process died while waiting for navigation) or `Changed` (as before).

TDD note: there is no Production test host. Task 4's smoke is this task's
failing test; Task 6's push is its GREEN run.

- [ ] **Step 1: Fields, event and gate.** Add beside the existing fields:

```csharp
    private TaskCompletionSource<bool>? pendingNavigation;
    private bool failed;
```

  beside `ExternalLinkRequested`:

```csharp
    // Raised once, with Crashed, when the browser or the page's renderer
    // dies after the guide opened.
    public event EventHandler<HtmlGuideLoadError>? Failed;
```

  and after `DiagnosticsForTest`:

```csharp
    // The installed smoke points the probe at an empty folder, so it fails
    // the way it does on a PC without WebView2.
    private static string? MissingRuntimeFolderForTest(string cacheRoot) =>
        TestGate.IsOpen($@"Local\DesktopGuides.Preview.WebView2Missing.{Environment.ProcessId}")
            ? Path.Combine(cacheRoot, "missing-runtime-test")
            : null;
```

- [ ] **Step 2: Probe, then create.** Replace the first `try`/`catch` in
  `OpenAsync` (from `try { Directory.CreateDirectory(profile); ...` to the
  `RuntimeMissing` throw) with:

```csharp
        string? browserFolder = MissingRuntimeFolderForTest(cacheRoot);
        string? version;
        try
        {
            if (browserFolder is not null) Directory.CreateDirectory(browserFolder);
            version = CoreWebView2Environment.GetAvailableBrowserVersionString(browserFolder);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new HtmlGuideLoadException(HtmlGuideLoadError.RuntimeMissing);
        }
        if (string.IsNullOrEmpty(version))
        {
            throw new HtmlGuideLoadException(HtmlGuideLoadError.RuntimeMissing);
        }
        try
        {
            Directory.CreateDirectory(profile);
            environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                browserFolder, profile, new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments = HtmlBrowserEnvironment.Arguments,
                });
            await View.EnsureCoreWebView2Async(environment);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // The runtime is installed but didn't start.
            throw new HtmlGuideLoadException(HtmlGuideLoadError.RuntimeFailed);
        }
```

- [ ] **Step 3: Crash during navigation.** In the navigation block:
  - Add `core.ProcessFailed += OnProcessFailed;` after
    `core.LaunchingExternalUriScheme += ...`.
  - Add `pendingNavigation = completion;` right before `core.Navigate(...)`.
  - Change the catch filter to
    `catch (Exception error) when (error is not OperationCanceledException and not HtmlGuideLoadException)`.
  - Replace the `finally` body with:

```csharp
            pendingNavigation = null;
            // A closed or crashed view raises nothing more, and touching it could throw.
            if (!disposed && !failed) core.NavigationCompleted -= Completed;
```

- [ ] **Step 4: The handler.** Add after `OnLaunchingExternalUriScheme`:

```csharp
    // Chromium restarts GPU, utility and frame processes by itself; only a
    // lost browser or page renderer leaves the guide unreadable.
    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
    {
        if (disposed || failed || args.ProcessFailedKind is not (
                CoreWebView2ProcessFailedKind.BrowserProcessExited or
                CoreWebView2ProcessFailedKind.RenderProcessExited or
                CoreWebView2ProcessFailedKind.RenderProcessUnresponsive))
        {
            return;
        }
        failed = true;
        if (pendingNavigation?.TrySetException(new HtmlGuideLoadException(HtmlGuideLoadError.Crashed)) == true)
        {
            return;
        }
        Failed?.Invoke(this, HtmlGuideLoadError.Crashed);
    }
```

- [ ] **Step 5: Dispose.** In `DisposeAsync`:
  - Add `Failed = null;` after `ExternalLinkRequested = null;`.
  - Wrap the `core.… -= …` block in a try, add
    `core.ProcessFailed -= OnProcessFailed;` to it, and catch a dead view:

```csharp
        if (core is not null)
        {
            try
            {
                core.WebResourceRequested -= OnWebResourceRequested;
                core.NavigationStarting -= OnNavigationStarting;
                core.FrameNavigationStarting -= OnFrameNavigationStarting;
                core.NewWindowRequested -= OnNewWindowRequested;
                core.PermissionRequested -= OnPermissionRequested;
                core.DownloadStarting -= OnDownloadStarting;
                core.LaunchingExternalUriScheme -= OnLaunchingExternalUriScheme;
                core.ProcessFailed -= OnProcessFailed;
            }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or
                                              InvalidOperationException)
            {
                // A crashed browser leaves a view that can't be touched; the
                // handlers check disposed anyway.
            }
        }
```

  - Replace the comment `// T09.1 sweeps profiles a crash or a slow exit
    leaves behind.` with `// A profile still locked here is removed by the
    next startup's sweep.`

- [ ] **Step 6: Build check.** Push with Task 6 (the shell consumes
  `Failed`). Expected after Task 6: `production-packages` builds both
  architectures with no new warnings.

- [ ] **Step 7: Commit.**

```bash
git add src/DesktopGuides.Production/HtmlReaderSession.cs
git commit -m "feat(p1): T09.1 runtime probe and crash reporting in the HTML session"
```

### Task 6: Shell error action, crash handling and startup sweep

**Files:**
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (the
  `ReaderLoadError` `TextBlock`, ~line 488)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs`
  (`ShowReaderSurface` ~line 1248; startup after `AppCacheRoot.Resolve`
  ~line 273)
- Modify: `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs`

**Interfaces:**
- Consumes: `HtmlGuideLoadAction`, `HtmlGuideLoadMessages.ActionFor`,
  `ActionLabel`, `RuntimeDownloadUrl` (Task 1);
  `WebView2ProfileSweeper.Sweep(string)` (Task 3);
  `HtmlReaderSession.Failed` (Task 5).
- Produces: the `ReaderLoadErrorAction` button that Task 4's smoke drives.

- [ ] **Step 1: XAML.** Replace the `ReaderLoadError` `TextBlock` with:

```xml
                        <StackPanel Spacing="{StaticResource DesktopGuidesSpacing12}">
                            <TextBlock x:Name="ReaderLoadError"
                                       Visibility="Collapsed"
                                       TextWrapping="Wrap"
                                       Style="{StaticResource DesktopGuidesSecondaryBodyStyle}"
                                       AutomationProperties.AutomationId="ReaderLoadError" />
                            <Button x:Name="ReaderLoadErrorAction"
                                    Visibility="Collapsed"
                                    HorizontalAlignment="Left"
                                    Click="ReaderLoadErrorActionClicked"
                                    AutomationProperties.AutomationId="ReaderLoadErrorAction" />
                        </StackPanel>
```

- [ ] **Step 2: `ShowReaderSurface`.** Add the fields next to
  `renderGeneration`:

```csharp
    private HtmlGuideLoadAction readerErrorAction;
    private int readerErrorGeneration;
```

  and replace the method (R3: an empty surface is collapsed, so it never
  takes clicks meant for the button):

```csharp
    private void ShowReaderSurface(
        bool placeholder, string? error = null, UIElement? view = null,
        HtmlGuideLoadAction action = HtmlGuideLoadAction.None)
    {
        ReaderPlaceholder.Visibility = placeholder ? Visibility.Visible : Visibility.Collapsed;
        ReaderLoadError.Text = error ?? string.Empty;
        ReaderLoadError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        readerErrorAction = error is null ? HtmlGuideLoadAction.None : action;
        readerErrorGeneration = renderGeneration;
        ReaderLoadErrorAction.Content = readerErrorAction == HtmlGuideLoadAction.None
            ? null
            : HtmlGuideLoadMessages.ActionLabel(readerErrorAction);
        ReaderLoadErrorAction.Visibility = readerErrorAction == HtmlGuideLoadAction.None
            ? Visibility.Collapsed
            : Visibility.Visible;
        ReaderSurface.Content = view;
        ReaderSurface.Visibility = view is null ? Visibility.Collapsed : Visibility.Visible;
    }
```

  Add `using DesktopGuides.Core.Html;` to `ShellWindow.xaml.cs` if it isn't
  there.

- [ ] **Step 3: `ShellWindow.HtmlReader.cs`.**
  - `ShowHtmlLoadError` passes the action:

```csharp
    private void ShowHtmlLoadError(HtmlGuideLoadError error)
    {
        string message = HtmlGuideLoadMessages.For(error);
        ShowReaderSurface(placeholder: false, error: message, action: HtmlGuideLoadMessages.ActionFor(error));
        ShowWarningStatus(message);
    }
```

  - In `OpenHtmlGuideAsync`, add
    `session.Failed += OnReaderSessionFailed;` after
    `session.ExternalLinkRequested += OnExternalLinkRequested;`.
  - Add the crash handler. R2: it runs in the navigation queue, so it can't
    interleave with a render that is still opening this session:

```csharp
    // A late event from a session that a newer render replaced is ignored.
    private async void OnReaderSessionFailed(object? sender, HtmlGuideLoadError error)
    {
        await RunNavigationAsync(async () =>
        {
            if (!ReferenceEquals(sender, readerSession))
            {
                return;
            }
            IReaderSession failed = readerSession!;
            readerSession = null;
            ReaderActions.SetSession(null);
            HideExternalLinkBar();
            ShowReaderSurface(placeholder: false);
            await failed.DisposeAsync();
            ShowHtmlLoadError(error);
        });
    }
```

  - Extract the launcher from `ReaderExternalLinkOpenClicked` and add the
    action handler:

```csharp
    private async void ReaderExternalLinkOpenClicked(object sender, RoutedEventArgs args)
    {
        Uri? uri = pendingExternalLink;
        HideExternalLinkBar();
        if (uri is not null)
        {
            await LaunchExternalAsync(uri);
        }
    }

    private async Task LaunchExternalAsync(Uri uri)
    {
        if (cacheRoot is null)
        {
            return;
        }
        bool launched;
        try
        {
            launched = await ExternalLinkLaunchers.Create(cacheRoot).LaunchAsync(uri);
        }
        catch (Exception)
        {
            launched = false;
        }
        if (!launched)
        {
            ShowWarningStatus("This link couldn't be opened.");
        }
    }

    private async void ReaderLoadErrorActionClicked(object sender, RoutedEventArgs args)
    {
        int generation = readerErrorGeneration;
        switch (readerErrorAction)
        {
            case HtmlGuideLoadAction.GetRuntime:
                await LaunchExternalAsync(new Uri(HtmlGuideLoadMessages.RuntimeDownloadUrl));
                break;
            case HtmlGuideLoadAction.Reopen:
                // A click queued behind a navigation away does nothing.
                await RunNavigationAsync(async () =>
                {
                    if (generation != renderGeneration || navigator.Current is not ReaderRoute)
                    {
                        return;
                    }
                    await RenderCurrentAsync();
                });
                break;
        }
    }
```

  Add `using System.Threading.Tasks;` only if the file doesn't build
  without it (implicit usings are on in this project).

- [ ] **Step 4: Startup sweep.** In `ShellWindow.xaml.cs`, right after the
  `cacheRoot = AppCacheRoot.Resolve(...);` statement:

```csharp
            // The library lease is held, so no live session owns a profile here.
            string sweptRoot = cacheRoot;
            await Task.Run(() => WebView2ProfileSweeper.Sweep(sweptRoot));
```

  Add `using DesktopGuides.Infrastructure.Storage;` if it isn't there.

- [ ] **Step 5: Commit Tasks 5 and 6 and push.**

```bash
git add src/DesktopGuides.Production/ShellWindow.xaml src/DesktopGuides.Production/ShellWindow.xaml.cs src/DesktopGuides.Production/ShellWindow.HtmlReader.cs
git commit -m "feat(p1): T09.1 Reader error actions, crash recovery and profile sweep"
git push
gh workflow run windows-ci.yml --ref feat/p1-t09-1-html-adapter
```

- [ ] **Step 6: GREEN.** Watch the run
  (`gh run watch <id> --exit-status --interval 60`).
  Expected: every job passes. In `production-shell-ui`, the report has
  `htmlReader.profileSweep` = `removed`, lists the phases `html-crash`
  (once per pass), `html-runtime-missing`, `html-missing-entry` and
  `html-runtime-missing-txt`, and the artifact holds the `html-crash`
  screenshots in light and dark and the `html-runtime-missing` screenshot.
  Open each screenshot and check that the message and button are visible
  and not overlapped. On failure, use superpowers:systematic-debugging with
  `gh run view <id> --log-failed`.

### Task 7: Docs and evidence

**Files:**
- Modify: `docs/p1/implementation-plan.md` (~line 799)
- Modify: `docs/work-breakdown.md` (S09 T09.1 bullet, ~line 294)
- Modify: `docs/p1-technical-design.md` (§6 S09 T09.1, ~line 671)
- Modify: `docs/p1/t09-1-html-adapter-design.md` (Status line; an
  implementation-notes section)
- Create: `docs/p1/evidence/t09-1-html-adapter/` screenshots from the GREEN run

TDD skip: docs only.

- [ ] **Step 1: implementation-plan.md.** Replace "T07.3 is in review in
  PR #34; see the" with the merged wording: "T07.3 was merged through PR #34
  on 3 October 2026 (merge commit `3339fc7`; final-HEAD CI run
  37084631667); see the". Then add a paragraph after it: "T09.1 is in
  review in PR #<N>; see the [design and implementation
  notes](t09-1-html-adapter-design.md). It sweeps leftover WebView2
  profiles at startup, tells a missing runtime, a failed start, a crash, a
  missing entry and a changed file apart, and offers Get WebView2 Runtime
  or Reopen where they help."
- [ ] **Step 2: work-breakdown.md.** Add to the T09.1 bullet, in the T07.3
  style at ~line 247: "Implemented in PR #<N>; see
  [p1/t09-1-html-adapter-design.md](p1/t09-1-html-adapter-design.md)."
- [ ] **Step 3: p1-technical-design.md.** Under §6 S09 T09.1, link the
  design and add two sentences: the sweep runs once at startup, after the
  library lease, and never per open; the error-to-action table lives in
  `HtmlGuideLoadMessages` (RuntimeMissing → Get WebView2 Runtime;
  RuntimeFailed and Crashed → Reopen; Missing, NoManifest and Changed → no
  action).
- [ ] **Step 4: design doc.** Set Status to "implemented in PR #<N>; CI run
  <id>." Add "## Implementation notes" recording rulings R1–R4 and any
  ruling made during execution.
- [ ] **Step 5: evidence.** Download the GREEN run's artifact
  (`gh run download <id>`), copy the `html-crash` light and dark and the
  `html-runtime-missing` screenshots into `docs/p1/evidence/t09-1-html-adapter/`, and
  reference them from the PR body.
- [ ] **Step 6: Commit and push.**

```bash
git add docs
git commit -m "docs(p1): T09.1 implementation notes and evidence"
git push
```

  Expected: the docs-only push needs no new CI gate beyond the run the PR
  triggers; check it passes.
