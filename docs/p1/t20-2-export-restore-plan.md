# T20.2 Export and Restore Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Someone can export their whole library to a `.zip` outside app
data from Settings, is reminded once after their first import that
uninstalling removes the library, and can replace the library with a
backup after the app has checked the whole archive in staging. An
interrupted or failed swap always puts the previous library back.

**Architecture:** Core gains `BackupDestinationPolicy`, the restore
contracts and `LibraryBackupMessages`. Infrastructure gains
`LibraryRestorer` (stage, validate, swap) and `LibraryRestoreRecovery`
(the marker-driven finish or rollback that runs before the repository
opens), reusing T20.1's `LibraryArchiveVerifier` and `LibraryArchivePlan`.
The shell gains a `ShellWindow.Backup.cs` partial with the Export card, the
Restore expander, the Replace confirmation and the in-session swap, which
re-runs `InitializeCoreAsync` as T15.1's **Try again** does.

**Tech Stack:** .NET 10, WinUI 3, Windows App SDK pickers,
CommunityToolkit SettingsControls, Microsoft.Data.Sqlite,
System.IO.Compression, xUnit, the PowerShell 5.1 UI Automation harness.

**Spec:** `docs/p1/t20-2-export-restore-design.md`

**Target:** T20.2, in three PRs. **Prerequisites:** T14.4 (#54), T15.1
(#74–#76), T15.4 (#62) and T20.1 (#59), all merged.

| PR | Branch | Tasks |
|---|---|---|
| a: export UI and reminder | `feat/p1-t20-2-export-restore` | 1–5 |
| b: restore engine | `feat/p1-t20-2-restore-engine` (from `main` after PR a merges) | 6–11 |
| c: restore UI and swap | `feat/p1-t20-2-restore-ui` (from `main` after PR b merges) | 12–16 |

## Global Constraints

- `library/` is never changed until every staging check has passed, and
  never left without a library: every swap failure or interruption puts
  the prior root back, or leaves no library if there was none.
- A backup is untrusted input: every name, size, hash and database row is
  checked in staging. Nothing follows or creates a reparse point.
- Restore never touches `providers.bin`, the cache root or the session
  lock. Export includes no credential, cache or diagnostics file (TR20.2).
- Export and restore are user initiated. Nothing runs automatically.
- No message contains exception text, SQL or a path other than a file name
  the user chose, the data folder or the `.recovery` folder.
- All copy is verbatim from this plan, with a straight ASCII apostrophe
  and `…` (U+2026) where shown.
- New enum members go at the end, so existing values keep their numbers.
- No schema migration. The reminder flag is a `Settings` key.
- UI tests assert only what app code controls.
- PowerShell scripts stay ASCII-only.
- Never print, copy or log provider credential values.

## Rulings against the spec

Tasks 5, 11 and 16 record these in the design's verification record.

1. **The marker has a phase.** The spec's marker records the stage and
   whether a prior library existed. A crash while confirming or rolling
   back would otherwise be read as "promoted, unverified" and roll back
   the wrong root. The marker adds `Phase`: `Swapping`, `Confirmed` or
   `RollingBack`. `Complete` writes `Confirmed` before it deletes
   anything; `RollBack` writes `RollingBack` before it moves anything.
2. **The unverified copy goes under `.restore-staging/<id>-unverified`,**
   not `.recovery/`. A copy left in `.recovery` would make `IsFirstRun`
   raise Missing when there was no prior library. Staging leftovers are
   deleted at every startup.
3. **Staging accepts schema 4 and newer-but-supported only.** Version 4 is
   the first schema any exporter wrote (T20.1 writes
   `LibrarySchema.CurrentVersion`), and `LibraryArchivePlan` reads v4
   columns. A lower `user_version` fails with `DatabaseInvalid`. The
   manifest's `schemaVersion` must equal the database's `user_version`.
4. **`LibraryRestorer` takes only `ILibraryPaths`;** `paths.DataRoot` is
   the data root.
5. **The save picker may create an empty file.** If the destination was a
   0-byte file when picked and the export doesn't finish, the shell
   deletes it if it is still 0 bytes.
6. **The reminder has its own `import-reminder` smoke mode** in the
   `backup` group. Checking **Go to Export** inside `import-publish`
   would break that mode's Back-navigation checks.
7. **Startup recovery is wired in PR c,** with the in-session swap. Before
   PR c no marker can exist, so PR b stays headless.
8. **The installed smokes form a new `backup` scenario group,** run in the
   `design` CI shard.
9. **`LibraryRestoreRecovery.Run` returns `RolledBack`** when it rolled a
   restore back, so the shell can say the previous library was kept.
10. **Export cancel is exercised at a `Backup` test gate** before
    `ExportAsync` starts, following the `TextLoad` gate. The exporter's
    own cancellation points are covered by T20.1's tests; a real export
    of the seeded library finishes too fast to cancel reliably. Restore
    staging uses the same gate.
11. **Smoke backups go to Documents.** `%TEMP%` is under `%LOCALAPPDATA%`,
    which the packaged build refuses.
12. **`restore-clean` opens a restored guide but doesn't assert its saved
    position.** The headless round trip (Task 10) compares every restored
    `ReadingStates` row with the source.
13. **A `ReferencesInvalid` error carries the titles.** The stage is
    deleted on failure, so the restorer reads the affected titles from the
    staged database first and puts them on `LibraryRestoreException.Titles`.

## Review Focus

1. **A crash between `Complete`'s deletions.** The next start must finish
   the cleanup and keep the restored library, never roll back. Test
   `ACrashWhileCompletingKeepsTheRestoredLibrary` (Task 10).
2. **A crash after the prior root is back but before the marker is
   deleted.** The next start must not move the prior root aside. Test
   `ACrashAfterThePriorRootReturnsKeepsIt` (Task 10).
3. **The user cancels the save dialog, or the export fails, after the
   picker created an empty file.** No file may remain. Smoke phase
   `export-cancel-no-file` (Task 4).
4. **An entry larger than its manifest says.** Staging must refuse it
   before writing past the declared size; extraction also never writes
   more than the declared bytes. Test
   `AnEntryLongerThanItsManifestSizeIsInvalid` (Task 7).
5. **The window closes during an export or staging.** Close must cancel
   it, wait, and leave no `.tmp` or stage folder. Checked in code review
   against `WindowClosing` and `CloseWhenIdleAsync` (Tasks 3 and 12).

## Host commands

The Mac has no dotnet, so every build and test runs on `pcsx2-win`:

```bash
s(){ ssh -o BatchMode=yes -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t20-2) { Remove-Item -Recurse -Force E:\work\desktop-guides\t20-2 }; New-Item -ItemType Directory E:\work\desktop-guides\t20-2 | Out-Null"'
  COPYFILE_DISABLE=1 tar --exclude=.claude --exclude=.git --exclude=.superpowers -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t20-2'
}
```

- **Core tests:** `stage && s 'cd /d E:\work\desktop-guides\t20-2 && dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release'`
- **Infrastructure tests:** `stage && s 'cd /d E:\work\desktop-guides\t20-2 && dotnet test tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj -c Release'`
- **One class:** append `--filter "FullyQualifiedName~<Class>"` inside the
  quoted command.
- **Production build:** `stage && s 'cd /d E:\work\desktop-guides\t20-2 && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64'`
- **Seed build:** `stage && s 'cd /d E:\work\desktop-guides\t20-2 && dotnet build tools\p1\DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj -c Release'`
- **Installed group:** `gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref <branch> -f shell-scope=backup`

---

# PR a: export UI and the first-import reminder

### Task 1: Core backup copy and the destination policy

**Files:**
- Create: `src/DesktopGuides.Core/Backup/BackupDestinationPolicy.cs`
- Create: `src/DesktopGuides.Core/Backup/LibraryBackupMessages.cs`
- Test: `tests/DesktopGuides.Core.Tests/BackupDestinationPolicyTests.cs`
- Test: `tests/DesktopGuides.Core.Tests/LibraryBackupMessagesTests.cs`

**Interfaces:**
- Consumes: `LibraryExportIssue`, `LibraryExportPhase` (T20.1,
  `Core/Backup/LibraryExportContracts.cs`).
- Produces:
  - `BackupDestinationPolicy.ProtectedRoots(bool packaged, string dataRoot, string localAppData, string roamingAppData) → IReadOnlyList<string>`
  - `LibraryBackupMessages.SuggestedFileName(DateTime localDate)`,
    `Counts(int games, int guides)`, `Size(long bytes)`,
    `Titles(IReadOnlyList<string> titles)`,
    `ExportSaved(string fileName, int games, int guides, long bytes)`,
    `ExportFailed(LibraryExportIssue issue, IReadOnlyList<string> titles)`,
    `ExportPhase(LibraryExportPhase phase)`, and the constants
    `ExportCanceled`, `ExportReminder`, `GoToExport`, `PickerFailed`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/DesktopGuides.Core.Tests/BackupDestinationPolicyTests.cs
using DesktopGuides.Core.Backup;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class BackupDestinationPolicyTests
{
    private const string Local = @"C:\Users\reader\AppData\Local";
    private const string Roaming = @"C:\Users\reader\AppData\Roaming";

    [Fact]
    public void PackagedProtectsThePackageFolderAndBothAppDataFolders()
    {
        IReadOnlyList<string> roots = BackupDestinationPolicy.ProtectedRoots(
            true, Local + @"\Packages\DesktopGuides.Preview_abc\LocalState\", Local, Roaming);

        Assert.Equal([Local + @"\Packages\DesktopGuides.Preview_abc", Local, Roaming], roots);
    }

    [Fact]
    public void PortableProtectsOnlyItsOwnFolder()
    {
        IReadOnlyList<string> roots = BackupDestinationPolicy.ProtectedRoots(
            false, Local + @"\DesktopGuides", Local, Roaming);

        Assert.Equal([Local + @"\DesktopGuides"], roots);
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"relative\folder")]
    public void APathThatIsntFullIsRejected(string local)
    {
        Assert.Throws<ArgumentException>(() =>
            BackupDestinationPolicy.ProtectedRoots(false, Local + @"\DesktopGuides", local, Roaming));
    }
}
```

```csharp
// tests/DesktopGuides.Core.Tests/LibraryBackupMessagesTests.cs
using DesktopGuides.Core.Backup;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class LibraryBackupMessagesTests
{
    [Fact]
    public void TheSuggestedNameCarriesTheLocalDate() =>
        Assert.Equal("DesktopGuides-backup-2026-10-08.zip",
            LibraryBackupMessages.SuggestedFileName(new DateTime(2026, 10, 8, 23, 59, 0)));

    [Theory]
    [InlineData(1, 1, "1 game, 1 guide")]
    [InlineData(12, 40, "12 games, 40 guides")]
    [InlineData(0, 0, "0 games, 0 guides")]
    public void CountsUseSingularAndPlural(int games, int guides, string expected) =>
        Assert.Equal(expected, LibraryBackupMessages.Counts(games, guides));

    [Theory]
    [InlineData(0L, "1 KB")]
    [InlineData(1500L, "2 KB")]
    [InlineData(310L * 1024 * 1024, "310 MB")]
    [InlineData(1536L * 1024 * 1024, "1.5 GB")]
    public void SizesAreRoundedForReading(long bytes, string expected) =>
        Assert.Equal(expected, LibraryBackupMessages.Size(bytes));

    [Theory]
    [InlineData(new[] { "A" }, "A")]
    [InlineData(new[] { "A", "B" }, "A and B")]
    [InlineData(new[] { "A", "B", "C" }, "A, B and C")]
    [InlineData(new[] { "A", "B", "C", "D", "E" }, "A, B, C and 2 more")]
    public void TitlesNameAtMostThree(string[] titles, string expected) =>
        Assert.Equal(expected, LibraryBackupMessages.Titles(titles));

    [Fact]
    public void TheSavedMessageNamesTheFileCountsAndSize() =>
        Assert.Equal("Backup saved: DesktopGuides-backup-2026-10-10.zip (12 games, 40 guides, 310 MB).",
            LibraryBackupMessages.ExportSaved(
                "DesktopGuides-backup-2026-10-10.zip", 12, 40, 310L * 1024 * 1024));

    [Fact]
    public void EveryExportIssueHasAMessage()
    {
        foreach (LibraryExportIssue issue in Enum.GetValues<LibraryExportIssue>())
        {
            Assert.False(string.IsNullOrWhiteSpace(LibraryBackupMessages.ExportFailed(issue, [])));
        }
    }

    [Fact]
    public void AProtectedFolderSaysWhy() =>
        Assert.Equal(
            "Choose a folder outside the app's data, such as Documents or a USB drive. Backups saved in app data are removed when the app is uninstalled.",
            LibraryBackupMessages.ExportFailed(LibraryExportIssue.DestinationNotAllowed, []));

    [Fact]
    public void DamagedFilesNameTheGuides() =>
        Assert.Equal(
            "Some guide files are missing or damaged: Maps, Manual, Walkthrough and 1 more. Remove or re-import them, then try again.",
            LibraryBackupMessages.ExportFailed(
                LibraryExportIssue.ManagedFilesDamaged, ["Maps", "Manual", "Walkthrough", "Notes"]));

    [Fact]
    public void DamagedFilesWithoutTitlesStillSayWhatToDo() =>
        Assert.Equal(
            "Some guide files are missing or damaged. Remove or re-import those guides, then try again.",
            LibraryBackupMessages.ExportFailed(LibraryExportIssue.ManagedFilesDamaged, []));

    [Fact]
    public void EveryPhaseHasALabel()
    {
        foreach (LibraryExportPhase phase in Enum.GetValues<LibraryExportPhase>())
        {
            Assert.EndsWith("…", LibraryBackupMessages.ExportPhase(phase));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: Core tests with `--filter "FullyQualifiedName~BackupDestinationPolicyTests|FullyQualifiedName~LibraryBackupMessagesTests"`.
Expected: the build fails: `BackupDestinationPolicy` and
`LibraryBackupMessages` don't exist.

- [ ] **Step 3: Write the implementation**

```csharp
// src/DesktopGuides.Core/Backup/BackupDestinationPolicy.cs
namespace DesktopGuides.Core.Backup;

/// <summary>
/// Folders a backup may not be saved in. Uninstalling removes the app's own
/// folder, and for MSIX Windows redirects writes under %LOCALAPPDATA% and
/// %APPDATA% into the package store, which uninstall also removes.
/// </summary>
public static class BackupDestinationPolicy
{
    public const string PortableFolderName = "DesktopGuides";

    public static IReadOnlyList<string> ProtectedRoots(
        bool packaged, string dataRoot, string localAppData, string roamingAppData)
    {
        RequireFull(dataRoot, nameof(dataRoot));
        RequireFull(localAppData, nameof(localAppData));
        RequireFull(roamingAppData, nameof(roamingAppData));
        if (!packaged)
        {
            return [Path.Combine(localAppData, PortableFolderName)];
        }
        string package = Path.GetDirectoryName(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot)))!;
        return [package, localAppData, roamingAppData];
    }

    private static void RequireFull(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("A full path is required.", name);
        }
    }
}
```

```csharp
// src/DesktopGuides.Core/Backup/LibraryBackupMessages.cs
using System.Globalization;

namespace DesktopGuides.Core.Backup;

public static class LibraryBackupMessages
{
    public const string ExportCanceled = "Export canceled.";
    public const string ExportReminder =
        "Your library lives in app data, and uninstalling the app removes it. Export a backup from Settings.";
    public const string GoToExport = "Go to Export";
    public const string PickerFailed = "Couldn't open the file dialog. Try again.";

    private const double KiB = 1024;
    private const double MiB = KiB * 1024;
    private const double GiB = MiB * 1024;

    public static string SuggestedFileName(DateTime localDate) =>
        string.Create(CultureInfo.InvariantCulture, $"DesktopGuides-backup-{localDate:yyyy-MM-dd}.zip");

    public static string Counts(int games, int guides) =>
        $"{Plural(games, "game")}, {Plural(guides, "guide")}";

    public static string Size(long bytes) => bytes switch
    {
        < (long)MiB => string.Create(CultureInfo.InvariantCulture,
            $"{Math.Max(1L, (long)Math.Ceiling(bytes / KiB))} KB"),
        < (long)GiB => string.Create(CultureInfo.InvariantCulture,
            $"{Math.Round(bytes / MiB, MidpointRounding.AwayFromZero):0} MB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / GiB:0.0} GB")
    };

    /// <summary>Up to three titles, then "and N more".</summary>
    public static string Titles(IReadOnlyList<string> titles) => titles.Count switch
    {
        0 => "",
        1 => titles[0],
        2 or 3 => $"{string.Join(", ", titles.Take(titles.Count - 1))} and {titles[^1]}",
        _ => $"{string.Join(", ", titles.Take(3))} and {titles.Count - 3} more"
    };

    public static string ExportSaved(string fileName, int games, int guides, long bytes) =>
        $"Backup saved: {fileName} ({Counts(games, guides)}, {Size(bytes)}).";

    public static string ExportPhase(LibraryExportPhase phase) => phase switch
    {
        LibraryExportPhase.Preparing => "Preparing backup…",
        LibraryExportPhase.Writing => "Writing backup…",
        LibraryExportPhase.Verifying => "Checking backup…",
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };

    public static string ExportFailed(LibraryExportIssue issue, IReadOnlyList<string> titles) => issue switch
    {
        LibraryExportIssue.DestinationNotAllowed =>
            "Choose a folder outside the app's data, such as Documents or a USB drive. Backups saved in app data are removed when the app is uninstalled.",
        LibraryExportIssue.DestinationUnavailable => "That folder isn't available. Choose another folder.",
        LibraryExportIssue.DestinationExists => "A file with that name appeared while saving. Try again.",
        LibraryExportIssue.RecoveryIncomplete =>
            "An earlier change to the library didn't finish. Restart the app, then try again.",
        LibraryExportIssue.DatabaseInvalid =>
            "The library database couldn't be copied. Restart the app, then try again.",
        LibraryExportIssue.ManagedFilesDamaged => titles.Count == 0
            ? "Some guide files are missing or damaged. Remove or re-import those guides, then try again."
            : $"Some guide files are missing or damaged: {Titles(titles)}. Remove or re-import them, then try again.",
        LibraryExportIssue.LibraryTooLarge => "The library is too large to back up in one file.",
        LibraryExportIssue.WriteFailed =>
            "The backup couldn't be written. Check the drive has free space, then try again.",
        LibraryExportIssue.VerificationFailed =>
            "The saved backup didn't match the library, so it was deleted. Try again, or choose another drive.",
        _ => throw new ArgumentOutOfRangeException(nameof(issue))
    };

    private static string Plural(int count, string noun) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {noun}{(count == 1 ? "" : "s")}");
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: the Step 2 command.
Expected: PASS, 22 tests.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Core/Backup tests/DesktopGuides.Core.Tests/BackupDestinationPolicyTests.cs tests/DesktopGuides.Core.Tests/LibraryBackupMessagesTests.cs
git commit -m "feat(core): add backup destination policy and export copy"
```

### Task 2: The export reminder flag and the packaged roots

**Files:**
- Modify: `src/DesktopGuides.Core/Library/LibraryModels.cs:60-63` (`AppSettings`)
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs`
  (`ReadSettings`, `WriteSettings`)
- Test: `tests/DesktopGuides.Infrastructure.Tests/ExportReminderSettingTests.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/LibraryExporterTests.cs` (one new test)

**Interfaces:**
- Consumes: `BackupDestinationPolicy.ProtectedRoots` (Task 1).
- Produces: `AppSettings.ExportReminderShown` (`bool`, default `false`),
  stored as the `Settings` key `ExportReminderShown` with the value `true`
  or `false`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/DesktopGuides.Infrastructure.Tests/ExportReminderSettingTests.cs
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class ExportReminderSettingTests
{
    [Fact]
    public async Task TheReminderStartsNotShown()
    {
        await using RemovalLibrary library = await RemovalLibrary.CreateAsync();

        Assert.False((await library.Repository.GetSettingsAsync()).ExportReminderShown);
    }

    [Fact]
    public async Task TheReminderFlagSurvivesARestart()
    {
        await using RemovalLibrary library = await RemovalLibrary.CreateAsync();

        await library.Repository.UpdateSettingsAsync(settings => settings with { ExportReminderShown = true });
        await library.RestartAsync();

        Assert.True((await library.Repository.GetSettingsAsync()).ExportReminderShown);
        Assert.Equal("true", library.Scalar("SELECT Value FROM Settings WHERE Key = 'ExportReminderShown'"));
    }

    [Fact]
    public async Task AnUnknownValueReadsAsNotShown()
    {
        await using RemovalLibrary library = await RemovalLibrary.CreateAsync();
        library.Execute("INSERT OR REPLACE INTO Settings (Key, Value) VALUES ('ExportReminderShown', 'maybe')");

        Assert.False((await library.Repository.GetSettingsAsync()).ExportReminderShown);
    }
}
```

Add to `LibraryExporterTests` (characterization tests of T20.1's check
with Task 1's roots; they are expected to pass on first run). The
fixture's data root sits directly under `Library.Root`, so its parent would
cover every test folder; the policy is given a fake package path instead:

```csharp
    private string[] PackagedRoots()
    {
        string root = fixture.Library.Root;
        return BackupDestinationPolicy.ProtectedRoots(
            true, Path.Combine(root, "Local", "Packages", "DesktopGuides.Preview_abc", "LocalState"),
            Path.Combine(root, "Local"), Path.Combine(root, "Roaming")).ToArray();
    }

    [Theory]
    [InlineData("Local", "Packages", "DesktopGuides.Preview_abc")]
    [InlineData("Local", "Other")]
    [InlineData("Roaming", "Other")]
    public async Task ExportRefusesEveryPackagedProtectedRoot(params string[] folders)
    {
        string target = Path.Combine([fixture.Library.Root, .. folders]);
        Directory.CreateDirectory(target);

        LibraryExportException error = await Assert.ThrowsAsync<LibraryExportException>(() =>
            Export(Exporter(protectedRoots: PackagedRoots()), Path.Combine(target, "backup.zip")));

        Assert.Equal(LibraryExportIssue.DestinationNotAllowed, error.Issue);
        Assert.Empty(Directory.EnumerateFiles(target));
    }

    [Fact]
    public async Task ExportAllowsAFolderOutsideThePackagedRoots()
    {
        LibraryExportResult result = await Export(Exporter(protectedRoots: PackagedRoots()));

        Assert.True(File.Exists(result.Path));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: Infrastructure tests with `--filter "FullyQualifiedName~ExportReminderSettingTests|FullyQualifiedName~PackagedProtectedRoot|FullyQualifiedName~OutsideThePackagedRoots"`.
Expected: the build fails: `AppSettings` has no `ExportReminderShown`.

- [ ] **Step 3: Write the implementation**

In `LibraryModels.cs`:

```csharp
public sealed record AppSettings(
    ThemePreference Theme,
    Guid? LastActiveGuideId,
    WindowMaterial WindowMaterial = WindowMaterial.Mica,
    bool ExportReminderShown = false);
```

In `ReadSettings`, declare `bool reminderShown = false;` beside `material`,
add this branch after the `WindowMaterial` branch, and return
`new AppSettings(theme, lastGuide, material, reminderShown)`:

```csharp
            else if (key == "ExportReminderShown")
            {
                // Anything but "true", such as a value from a newer build, shows the reminder again.
                reminderShown = value == "true";
            }
```

At the end of `WriteSettings`:

```csharp
        using (SqliteCommand reminder = connection.CreateCommand())
        {
            reminder.Transaction = transaction;
            reminder.CommandText = """
                INSERT INTO Settings (Key, Value) VALUES ('ExportReminderShown', $value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
                """;
            reminder.Parameters.AddWithValue("$value", settings.ExportReminderShown ? "true" : "false");
            reminder.ExecuteNonQuery();
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: the Step 2 command, then the whole Infrastructure suite.
Expected: PASS, 7 new tests. The whole suite passes. In particular
`SqliteLibraryRepositoryTests`, which compares `AppSettings` values, still
passes, because the new member defaults to `false`.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Core/Library/LibraryModels.cs src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs tests/DesktopGuides.Infrastructure.Tests/ExportReminderSettingTests.cs tests/DesktopGuides.Infrastructure.Tests/LibraryExporterTests.cs
git commit -m "feat(storage): store whether the export reminder was shown"
```

### Task 3: The Export card

TDD exception: this task is WinUI wiring with no unit-testable logic left
after Tasks 1 and 2. It is verified by the Production build here and by the
installed smoke in Task 4, which must fail first against the old card.

**Files:**
- Create: `src/DesktopGuides.Production/ShellWindow.Backup.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (replace
  `LibraryStorageSettingsCard`)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs`
  (`InitializeCoreAsync`, `WindowClosing`, `CloseWhenIdleAsync`,
  `NavigationInvoked`, `ShowStatus`)
- Modify: `src/DesktopGuides.Production/ShellWindow.Startup.cs`
  (`WarnIfRuntimeMissingAsync`, `ShellStatusActionClicked`)

**Interfaces:**
- Consumes: `LibraryExporter`, `LibraryExportOptions` (T20.1);
  `BackupDestinationPolicy`, `LibraryBackupMessages` (Task 1).
- Produces, for Tasks 4, 12 and 13:
  - fields `libraryPaths` (`ManagedPathResolver?`), `exporter`,
    `backupCancel` (`CancellationTokenSource?`), `backupTask` (`Task`),
    `libraryBusy` (`bool`);
  - `SetLibraryBusy(bool busy)`;
  - `ShowStatusAction(string label, Func<Task> action)`;
  - `DamagedTitlesAsync(IReadOnlyList<Guid> guideIds, IReadOnlyList<Guid> gameIds) → Task<IReadOnlyList<string>>`;
  - AutomationIds `ExportSettingsCard`, `ExportBackupButton`,
    `ExportCancelButton`, `ExportProgress`.

- [ ] **Step 1: Replace the static card**

In `ShellWindow.xaml`, replace the whole `LibraryStorageSettingsCard`
element with:

```xml
                        <toolkit:SettingsCard
                            x:Name="ExportSettingsCard"
                            Header="Back up library"
                            Description="Save your games, guides and reading progress to a .zip file. Uninstalling the app removes your library, so keep the backup outside app data."
                            HorizontalAlignment="Stretch"
                            AutomationProperties.AutomationId="ExportSettingsCard"
                            AutomationProperties.Name="Back up library. Save your games, guides and reading progress to a .zip file. Uninstalling the app removes your library, so keep the backup outside app data.">
                            <toolkit:SettingsCard.HeaderIcon>
                                <FontIcon Glyph="&#xE896;"
                                          AutomationProperties.AccessibilityView="Raw" />
                            </toolkit:SettingsCard.HeaderIcon>
                            <StackPanel Orientation="Horizontal"
                                        Spacing="{StaticResource DesktopGuidesSpacing8}">
                                <ProgressBar x:Name="ExportProgress"
                                             Width="160"
                                             VerticalAlignment="Center"
                                             Visibility="Collapsed"
                                             AutomationProperties.AutomationId="ExportProgress"
                                             AutomationProperties.Name="Preparing backup…" />
                                <Button x:Name="ExportCancelButton"
                                        Content="Cancel"
                                        Visibility="Collapsed"
                                        Click="ExportCancelClicked"
                                        AutomationProperties.AutomationId="ExportCancelButton" />
                                <Button x:Name="ExportBackupButton"
                                        Content="Export…"
                                        IsEnabled="False"
                                        Click="ExportBackupClicked"
                                        AutomationProperties.AutomationId="ExportBackupButton" />
                            </StackPanel>
                        </toolkit:SettingsCard>
```

- [ ] **Step 2: Add the partial**

```csharp
// src/DesktopGuides.Production/ShellWindow.Backup.cs
using DesktopGuides.Core.Backup;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace DesktopGuides.Production;

// T20.2: export and restore in Settings. While either runs, libraryBusy
// keeps the user on Settings: export holds the library's write gate, and
// restore is about to replace the library.
public sealed partial class ShellWindow
{
    private ManagedPathResolver? libraryPaths;
    private LibraryExporter? exporter;
    private CancellationTokenSource? backupCancel;
    private Task backupTask = Task.CompletedTask;
    private bool libraryBusy;

    private static LibraryExporter CreateExporter(
        SqliteLibraryRepository library, ManagedPathResolver paths, bool packaged) =>
        new(library, paths, new LibraryExportOptions(
            typeof(ShellWindow).Assembly.GetName().Version?.ToString() ?? "0.0.0.0",
            packaged ? "msix" : "portable",
            BackupDestinationPolicy.ProtectedRoots(
                packaged, paths.DataRoot,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))));

    private void SetLibraryBusy(bool busy)
    {
        libraryBusy = busy;
        LibraryItem.IsEnabled = !busy;
        AppThemeSelector.IsEnabled = !busy && ready;
        WindowMaterialSelector.IsEnabled = !busy && ready;
        AppTitleBar.IsBackButtonEnabled = !busy && navigator.CanGoBack;
    }

    private async void ExportBackupClicked(object sender, RoutedEventArgs args)
    {
        if (libraryBusy || !ready || closeRequested || exporter is not LibraryExporter active)
        {
            return;
        }
        string? path;
        try
        {
            path = await PickBackupDestinationAsync();
        }
        catch (Exception)
        {
            ShowWarningStatus(LibraryBackupMessages.PickerFailed);
            return;
        }
        if (path is null || closeRequested || libraryBusy)
        {
            return;
        }
        backupTask = RunExportAsync(active, path);
        await backupTask;
    }

    private async Task<string?> PickBackupDestinationAsync()
    {
        FileSavePicker picker = new(AppWindow.Id)
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(
                LibraryBackupMessages.SuggestedFileName(DateTime.Now))
        };
        picker.FileTypeChoices.Add("Desktop Guides backup", new List<string> { ".zip" });
        PickFileResult? result = await picker.PickSaveFileAsync();
        return result?.Path;
    }

    private async Task RunExportAsync(LibraryExporter active, string path)
    {
        // Ruling 5: the picker may have created an empty file for us.
        bool emptyWhenPicked = new FileInfo(path) is { Exists: true, Length: 0 };
        using CancellationTokenSource cancel = new();
        backupCancel = cancel;
        bool saved = false;
        SetLibraryBusy(true);
        ExportBackupButton.IsEnabled = false;
        ExportCancelButton.IsEnabled = true;
        ExportCancelButton.Visibility = Visibility.Visible;
        ShowExportProgress(new LibraryExportProgress(LibraryExportPhase.Preparing, 0, 0));
        ExportCancelButton.Focus(FocusState.Programmatic);
        try
        {
            Progress<LibraryExportProgress> progress = new(report =>
            {
                if (ReferenceEquals(backupCancel, cancel) && !closeRequested)
                {
                    ShowExportProgress(report);
                }
            });
            LibraryExportResult result = await active.ExportAsync(path, true, progress, cancel.Token);
            saved = true;
            if (!closeRequested)
            {
                ShowStatus(LibraryBackupMessages.ExportSaved(
                        Path.GetFileName(result.Path), result.Games, result.Guides, result.Bytes),
                    InfoBarSeverity.Success, true, false);
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            if (!closeRequested)
            {
                ShowTransientStatus(LibraryBackupMessages.ExportCanceled);
            }
        }
        catch (LibraryExportException error)
        {
            if (!closeRequested)
            {
                IReadOnlyList<string> titles = await DamagedTitlesAsync(error.GuideIds, error.GameIds);
                ShowErrorStatus(LibraryBackupMessages.ExportFailed(error.Issue, titles));
            }
        }
        catch (Exception)
        {
            if (!closeRequested)
            {
                ShowErrorStatus(LibraryBackupMessages.ExportFailed(LibraryExportIssue.WriteFailed, []));
            }
        }
        finally
        {
            if (!saved && emptyWhenPicked)
            {
                DeleteIfStillEmpty(path);
            }
            backupCancel = null;
            ExportProgress.Visibility = Visibility.Collapsed;
            ExportCancelButton.Visibility = Visibility.Collapsed;
            if (!closeRequested)
            {
                SetLibraryBusy(false);
                ExportBackupButton.IsEnabled = ready;
                ExportBackupButton.Focus(FocusState.Programmatic);
            }
        }
    }

    private void ShowExportProgress(LibraryExportProgress report)
    {
        ExportProgress.Visibility = Visibility.Visible;
        ExportProgress.IsIndeterminate = report.BytesTotal <= 0;
        ExportProgress.Value = report.BytesTotal > 0 ? 100.0 * report.BytesDone / report.BytesTotal : 0;
        AutomationProperties.SetName(ExportProgress, LibraryBackupMessages.ExportPhase(report.Phase));
    }

    private void ExportCancelClicked(object sender, RoutedEventArgs args)
    {
        ExportCancelButton.IsEnabled = false;
        backupCancel?.Cancel();
    }

    private static void DeleteIfStillEmpty(string path)
    {
        try
        {
            if (new FileInfo(path) is { Exists: true, Length: 0 })
            {
                File.Delete(path);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // Titles for an error message. A row that can't be read is left out.
    private async Task<IReadOnlyList<string>> DamagedTitlesAsync(
        IReadOnlyList<Guid> guideIds, IReadOnlyList<Guid> gameIds)
    {
        List<string> titles = [];
        if (repository is not SqliteLibraryRepository library)
        {
            return titles;
        }
        foreach (Guid id in guideIds)
        {
            try
            {
                if (await library.GetGuideAsync(id) is Guide guide) titles.Add(guide.Title);
            }
            catch (Exception) { }
        }
        foreach (Guid id in gameIds)
        {
            try
            {
                if (await library.GetGameAsync(id) is Game game) titles.Add(game.Title);
            }
            catch (Exception) { }
        }
        return titles;
    }

    private void ShowStatusAction(string label, Func<Task> action)
    {
        ShellStatusAction.Content = label;
        statusAction = action;
        ShellStatusAction.Visibility = Visibility.Visible;
    }
}
```

- [ ] **Step 3: Generalize the status action**

In `ShellWindow.xaml.cs`, add the field `private Func<Task>? statusAction;`
beside `statusSequence`, and in `ShowStatus` set `statusAction = null;`
next to `ShellStatusAction.Visibility = Visibility.Collapsed;`.

In `ShellWindow.Startup.cs`, replace the last two lines of
`WarnIfRuntimeMissingAsync` and the whole `ShellStatusActionClicked` with:

```csharp
        ShowStatusAction(
            HtmlGuideLoadMessages.ActionLabel(GuideLoadAction.GetRuntime),
            () => LaunchExternalAsync(new Uri(HtmlGuideLoadMessages.RuntimeDownloadUrl)));
    }

    private async void ShellStatusActionClicked(object sender, RoutedEventArgs args)
    {
        if (statusAction is { } action)
        {
            await action();
        }
    }
```

- [ ] **Step 4: Wire the exporter, the busy gate and closing**

In `InitializeCoreAsync`:
- first line inside `try`: `ExportBackupButton.IsEnabled = false;`
- after `ManagedPathResolver paths = new(dataRoot);`: `libraryPaths = paths;`
- after `gameRemover = new GameRemover(...)`:
  `exporter = CreateExporter(repository, paths, AppDataRoot.HasPackageIdentity());`
- after `ready = true;`: `ExportBackupButton.IsEnabled = true;`

In `NavigationInvoked`, add as the first statement:

```csharp
        if (libraryBusy)
        {
            return;
        }
```

In `WindowClosing`, after `refreshCancel?.Cancel();`, add
`backupCancel?.Cancel();`. In `CloseWhenIdleAsync`, change the wait to
`await Task.WhenAll(initializationTask, pendingNavigation, refreshTask, backupTask);`.

- [ ] **Step 5: Build**

Run: the Production build.
Expected: `Build succeeded`, 0 warnings, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/DesktopGuides.Production/ShellWindow.Backup.cs src/DesktopGuides.Production/ShellWindow.xaml src/DesktopGuides.Production/ShellWindow.xaml.cs src/DesktopGuides.Production/ShellWindow.Startup.cs
git commit -m "feat(shell): export the library from Settings"
```

### Task 4: The first-import reminder and the installed export smokes

**Files:**
- Modify: `src/DesktopGuides.Production/ShellWindow.Backup.cs` (reminder)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs`
  (`ImportGuideClicked`; `PauseTextLoadForTestAsync` becomes `PauseForTestAsync`)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (modes, picker helpers,
  `design-language` card id)
- Modify: `tools/p1/windows_shell_install.ps1` (`backup` group)
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (`describe-backup`)
- Modify: `.github/workflows/windows-ci.yml` (`backup` scope, `design` shard)

**Interfaces:**
- Consumes: Task 3's `ShowStatusAction`, `RunExportAsync`, AutomationIds;
  Task 2's `ExportReminderShown`.
- Produces:
  - `PauseForTestAsync(string gate, CancellationToken token)`: holds when
    `Local\DesktopGuides.Preview.<gate>.<pid>.Reached` and `.Continue`
    exist. `TextLoad` keeps its names; export uses `Backup`.
  - smoke modes `import-reminder`, `export-backup`, `export-cancel`,
    `export-protected`, and the smoke parameter `-BackupPath`;
  - seed command `describe-backup <zip>`, which prints
    `{"verified":bool,"games":n,"guides":n,"files":n,"credentials":bool}`;
  - installed group `backup` (`-BackupOnly`, CI `shell-scope=backup`).

- [ ] **Step 1: Point the design-language smoke at the new card and add the modes (RED)**

In `windows_shell_ui_smoke.ps1`:
- Add `'import-reminder', 'export-backup', 'export-cancel', 'export-protected'`
  to the `-Mode` `ValidateSet`, and the parameter
  `[string] $BackupPath = ''` after `$AppCacheRoot`.
- In the `design-language` mode, replace both `LibraryStorageSettingsCard`
  checks:

```powershell
        [void](Wait-Name 'ExportSettingsCard' (
            'Back up library. Save your games, guides and reading progress to a .zip file. ' +
            'Uninstalling the app removes your library, so keep the backup outside app data.'))
```

  and change every other `'LibraryStorageSettingsCard'` in that mode to
  `'ExportSettingsCard'`.
- Move `Wait-FilePicker`, `Find-InPicker`, `Send-PickerCommand` and
  `Wait-PickerClosed` unchanged from the `import-*` branch to the shared
  helpers, directly after `Show-SettingsCard`. Add beside them:

```powershell
    function Choose-SavePath([string] $path) {
        $picker = Wait-FilePicker
        # The Save dialog's file-name box is an Edit with control id 1001.
        [DesktopGuidesForegroundProbe]::SetText((Find-InPicker $picker '1001' 'Edit'), $path)
        Send-PickerCommand $picker 1
        Wait-PickerClosed $picker
    }
```

- In the `import-*` branch, before the `else` that handles
  `import-publish` and the duplicate modes, add:

```powershell
        elseif ($Mode -eq 'import-reminder') {
            # T20.2: the first import reminds once; a later import doesn't.
            $reminder = 'Your library lives in app data, and uninstalling the app removes it. ' +
                'Export a backup from Settings.'
            $title = 'Reminder Guide ' + [guid]::NewGuid().ToString('N').Substring(0, 8)
            Select-Element 'Import Test Game'
            [void](Wait-Status 'Game ready.')
            Click-Element (Wait-EnabledById 'ImportGuideButton')
            Choose-PickerFile 'txt-legacy.txt'
            [void](Wait-VisibleById 'ImportGuideDialog')
            [void](Select-ById 'ImportEncodingCp437')
            Set-Text 'GuideTitleInput' $title
            Invoke-Element (Wait-EnabledById 'PrimaryButton')
            [void](Wait-HiddenById 'ImportGuideDialog')
            [void](Wait-SelectedGuide $title)
            [void](Wait-Status $reminder)
            [void](Wait-Name 'ShellStatusAction' 'Go to Export')
            $report.reminderScreenshot = Save-WindowScreenshot 'export-reminder'
            $report.phases += 'export-reminder-shown'

            Invoke-Element (Wait-VisibleById 'ShellStatusAction')
            [void](Wait-Name 'SettingsHeading' 'Settings')
            Wait-FocusedId 'ExportBackupButton'
            $report.phases += 'export-reminder-opens-settings'

            Select-Element 'Library'
            [void](Wait-Status 'Library ready.')
            Select-Element 'Import Test Game'
            [void](Wait-Status 'Game ready.')
            Click-Element (Wait-EnabledById 'ImportGuideButton')
            Choose-PickerFile 'txt-legacy.txt'
            [void](Wait-VisibleById 'ImportGuideDialog')
            [void](Select-ById 'ImportEncodingCp437')
            [void](Wait-Name 'PrimaryButton' 'Import another copy')
            Set-Text 'GuideTitleInput' ($title + ' copy')
            Invoke-Element (Wait-EnabledById 'PrimaryButton')
            [void](Wait-HiddenById 'ImportGuideDialog')
            [void](Wait-SelectedGuide ($title + ' copy'))
            if (Test-QuietStatus $reminder) {
                throw 'The export reminder showed again after a later import.'
            }
            $status = Find-ById 'ShellStatus'
            if ($status -and -not $status.Current.IsOffscreen -and $status.Current.Name -eq $reminder) {
                throw 'The export reminder showed again after a later import.'
            }
            $report.phases += 'export-reminder-once'
        }
```

- Before the final `else` of the mode chain, add:

```powershell
    elseif ($Mode -like 'export-*') {
        # T20.2: export from Settings through the real Save dialog.
        if (-not $BackupPath) { throw "$Mode needs -BackupPath." }
        Select-Element 'Settings'
        [void](Wait-Status 'Settings ready.')
        [void](Show-SettingsCard 'ExportSettingsCard')
        Invoke-Element (Wait-EnabledById 'ExportBackupButton')
        Choose-SavePath $BackupPath
        if ($Mode -eq 'export-backup') {
            [void](Wait-Status ('Backup saved: ' + (Split-Path -Leaf $BackupPath) + ' (') -Prefix -Seconds 120)
            Wait-FocusedId 'ExportBackupButton'
            $report.exportSavedScreenshot = Save-WindowScreenshot 'export-saved'
            $report.phases += 'export-saved'
        }
        elseif ($Mode -eq 'export-cancel') {
            # The install script holds the export at its Backup test gate.
            Invoke-Element (Wait-EnabledById 'ExportCancelButton')
            [void](Wait-Status 'Export canceled.')
            [void](Wait-HiddenById 'ExportCancelButton')
            Wait-FocusedId 'ExportBackupButton'
            if (Test-Path -LiteralPath $BackupPath) { throw 'A canceled export left its file.' }
            $report.phases += 'export-cancel-no-file'
        }
        else {
            [void](Wait-Status ("Choose a folder outside the app's data, such as Documents or a USB drive. " +
                'Backups saved in app data are removed when the app is uninstalled.'))
            if (Test-Path -LiteralPath $BackupPath) { throw 'A refused export left a file in app data.' }
            $report.phases += 'export-protected-refused'
        }
    }
```

In `windows_shell_install.ps1`:
- Add `[switch] $BackupOnly,` after `$ProviderOnly`, `'backup' = $BackupOnly.IsPresent`
  at the end of `$scenarioGroups`, and
  `if (Enter-ScenarioGroup 'backup') { Run-BackupScenarios }` after the
  `provider` line.
- Add `[string] $BackupPath = ''` to `Run-ShellSmoke`'s parameters and,
  with the other optional arguments,
  `if ($BackupPath) { $arguments += ' -BackupPath "' + $BackupPath + '"' }`.
- Add the group. Backups go to Documents, because `%TEMP%` is under
  `%LOCALAPPDATA%`, which the packaged build refuses:

```powershell
function Run-BackupScenarios {
    # T20.2 PR a: the first-import reminder, export, cancel and a refused folder.
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    Invoke-ShellSeed @('seed-import', $dataRoot) | Out-Null
    $backupRoot = Join-Path ([Environment]::GetFolderPath('MyDocuments')) (
        'desktop-guides-backup-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $backupRoot | Out-Null
    try {
        Start-InstalledShell
        $report.importReminder = Run-ShellSmoke 'import-reminder'
        Close-InstalledShell

        $backup = Join-Path $backupRoot 'DesktopGuides-backup.zip'
        Start-InstalledShell
        $report.exportBackup = Run-ShellSmoke 'export-backup' -BackupPath $backup
        Close-InstalledShell
        $described = Invoke-ShellSeed @('describe-backup', $backup) | ConvertFrom-Json
        $report.exportBackupDescribed = $described
        if (-not $described.verified -or $described.guides -lt 2 -or $described.credentials) {
            throw "The exported backup isn't complete: $($described | ConvertTo-Json -Compress)."
        }

        $canceled = Join-Path $backupRoot 'canceled.zip'
        Start-InstalledShell
        $prefix = "Local\DesktopGuides.Preview.Backup.$($report.launchedProcessId)"
        $reached = [System.Threading.EventWaitHandle]::new(
            $false, [System.Threading.EventResetMode]::AutoReset, "$prefix.Reached")
        $resume = [System.Threading.EventWaitHandle]::new(
            $false, [System.Threading.EventResetMode]::AutoReset, "$prefix.Continue")
        try {
            $report.exportCancel = Run-ShellSmoke 'export-cancel' -BackupPath $canceled
        }
        finally {
            $resume.Set() | Out-Null
            $resume.Dispose()
            $reached.Dispose()
        }
        Close-InstalledShell
        if ((Test-Path -LiteralPath $canceled) -or @(Get-ChildItem -LiteralPath $backupRoot -Filter '*.tmp').Count) {
            throw 'A canceled export left a file behind.'
        }

        $refused = Join-Path $dataRoot 'inside-app-data.zip'
        Start-InstalledShell
        $report.exportProtected = Run-ShellSmoke 'export-protected' -BackupPath $refused
        Close-InstalledShell
        if (Test-Path -LiteralPath $refused) { throw 'A refused export left a file in app data.' }
    }
    finally {
        Remove-Item -LiteralPath $backupRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
```

In `DesktopGuides.ShellSeed/Program.cs`, add `using DesktopGuides.Core.Backup;`
and, before the final `seed` dispatch:

```csharp
if (args.Length == 2 && args[0] == "describe-backup")
{
    // A test-side check of the archive against its manifest, with public types only.
    using ZipArchive archive = ZipFile.OpenRead(args[1]);
    ZipArchiveEntry first = archive.Entries[0];
    using MemoryStream json = new();
    using (Stream stream = first.Open())
    {
        stream.CopyTo(json);
    }
    LibraryArchiveManifest manifest = LibraryArchiveManifest.Parse(json.ToArray());
    bool verified = first.FullName == LibraryArchiveManifest.EntryName &&
        archive.Entries.Count == manifest.Entries.Count + 1;
    for (int index = 0; verified && index < manifest.Entries.Count; index++)
    {
        ZipArchiveEntry entry = archive.Entries[index + 1];
        using Stream stream = entry.Open();
        verified = entry.FullName == manifest.Entries[index].Path &&
            Convert.ToHexStringLower(SHA256.HashData(stream)) == manifest.Entries[index].Sha256;
    }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        verified,
        games = manifest.Games,
        guides = manifest.Guides,
        files = manifest.Entries.Count,
        credentials = archive.Entries.Any(entry =>
            entry.FullName.Contains("providers", StringComparison.OrdinalIgnoreCase))
    }));
    return 0;
}
```

In `windows-ci.yml`, add `- backup` to the `shell-scope` options after
`provider`, and change the `design` shard's groups from
`design,catalog,completion` to `design,catalog,completion,backup`.

- [ ] **Step 2: Run the group to verify it fails**

Commit Step 1's harness changes (`test(shell): add the backup smoke group`), push, and run the installed group
(`shell-scope=backup`) on the branch.
Expected: FAIL in `import-reminder` at
`Wait-Status 'Your library lives in app data…'`, because no reminder
exists yet. (If `Choose-SavePath` fails first with "The Open dialog has
no '1001' Edit", dump the dialog's Edit controls with
`$picker.FindAll($scope, [System.Windows.Automation.PropertyCondition]::new($uia::ClassNameProperty, 'Edit'))`,
take the file-name box's AutomationId, fix the helper, and rerun.)

- [ ] **Step 3: Add the reminder and the test gate**

In `ShellWindow.xaml.cs`, rename `PauseTextLoadForTestAsync(CancellationToken token)`
to `PauseForTestAsync(string gate, CancellationToken token)`, build the
prefix as `$@"Local\DesktopGuides.Preview.{gate}.{Environment.ProcessId}"`,
make the timeout message `$"{gate} test gate timed out."`, and call it
from the TXT load as `PauseForTestAsync("TextLoad", readerToken)`.

In `ImportGuideClicked`, after `await RenderCurrentAsync();` in the
`dialog.ImportedGuideId is Guid guideId` branch, add
`await RemindToExportAsync();`.

In `ShellWindow.Backup.cs`, in `RunExportAsync` add as the first statement
of the `try`, before `Progress<…> progress = …`:

```csharp
            await PauseForTestAsync("Backup", cancel.Token);
```

and add:

```csharp
    // After the first import: uninstalling removes the live library.
    private async Task RemindToExportAsync()
    {
        if (repository is not SqliteLibraryRepository library)
        {
            return;
        }
        try
        {
            if ((await library.GetSettingsAsync()).ExportReminderShown)
            {
                return;
            }
        }
        catch (Exception)
        {
            return;
        }
        try
        {
            await library.UpdateSettingsAsync(settings => settings with { ExportReminderShown = true });
        }
        catch (Exception)
        {
            // It shows again after the next import.
        }
        if (closeRequested)
        {
            return;
        }
        ShowStatus(LibraryBackupMessages.ExportReminder, InfoBarSeverity.Informational, true, false);
        ShowStatusAction(LibraryBackupMessages.GoToExport, GoToExportAsync);
    }

    private async Task GoToExportAsync()
    {
        if (libraryBusy)
        {
            return;
        }
        CancelReaderLoad();
        await RunNavigationAsync(async () =>
        {
            navigator.OpenSettings();
            await RenderCurrentAsync();
            ExportSettingsCard.StartBringIntoView();
            ExportBackupButton.Focus(FocusState.Programmatic);
        });
    }
```

- [ ] **Step 4: Run the group and the full CI to verify they pass**

Run: the Production build, then push and run `shell-scope=backup`, then a
full CI run (`gh pr checks` or `gh run watch` on the PR run).
Expected: the build has 0 warnings. The `backup` group passes with
phases `export-reminder-shown`, `export-reminder-opens-settings`,
`export-reminder-once`, `export-saved`, `export-cancel-no-file` and
`export-protected-refused`, and `describe-backup` reports
`verified: true`, `credentials: false`. The full run passes every job,
including `design-language` with `ExportSettingsCard`.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Production tools/p1 .github/workflows/windows-ci.yml
git commit -m "feat(shell): remind once after the first import to export a backup"
```

### Task 5: PR a documentation, verification record and PR

**Files:**
- Modify: `docs/p1/t20-2-export-restore-design.md` (status line, verification record)
- Modify: `docs/p1/implementation-plan.md` (T20.2 paragraph)
- Modify: `docs/progress.md` (T20.2 row)
- Create: `docs/p1/evidence/t20-2-export-restore/export-reminder.png`, `export-saved.png`

- [ ] **Step 1: Record the verification**

Under `## T20.2 verification record` in the design, replace "Not yet run."
with a `### PR a: export UI and the first-import reminder` subsection
listing: the Core and Infrastructure counts from the host run; the CI run
IDs (the `backup` dispatch and the full PR run) and any reruns with
their cause; the `describe-backup` output; rulings 5, 6, 8, 10 and 11
from this plan; and anything not run. Copy the `export-reminder` and
`export-saved` screenshots from the run's artifacts into the evidence
folder.

- [ ] **Step 2: Update the status pages**

- Design status line: "Status: PR a (export UI and reminder) in review;
  PRs b and c planned."
- `implementation-plan.md`: after the T15.1 paragraph, add a T20.2
  paragraph saying PR a adds Settings export, the protected-folder check
  and the one-time reminder, with a link to
  `t20-2-export-restore-design.md#t202-verification-record`.
- `progress.md`: mark T20.2 "In progress: PR a (export UI) in review".

- [ ] **Step 3: Check and commit**

Run: `git diff --check`
Expected: no output.

```bash
git add docs
git commit -m "docs(p1): record T20.2 PR a verification"
```

- [ ] **Step 4: Open PR a**

Push and open the PR against `main`. The body names the target task
(T20.2 PR a), the prerequisites and their status (T14.4, T15.1, T15.4,
T20.1: merged), the intended outcome (Settings export to a `.zip`
outside app data, with progress and Cancel; the one-time reminder), and
embeds the `export-reminder` and `export-saved` screenshots. It ends with
the Claude Code line.

---

# PR b: the restore engine

### Task 6: Restore contracts and copy

**Files:**
- Create: `src/DesktopGuides.Core/Backup/LibraryRestoreContracts.cs`
- Modify: `src/DesktopGuides.Core/Backup/LibraryBackupMessages.cs`
- Modify: `src/DesktopGuides.Core/Library/LibraryOpen.cs` (`RestoreIncomplete`)
- Test: `tests/DesktopGuides.Core.Tests/LibraryBackupMessagesTests.cs`
- Test: `tests/DesktopGuides.Core.Tests/LibraryOpenMessagesTests.cs`

**Interfaces:**
- Produces:
  - `enum LibraryRestorePhase { Copying, Checking, Extracting }`
  - `readonly record struct LibraryRestoreProgress(LibraryRestorePhase Phase, long BytesDone, long BytesTotal)`
  - `enum LibraryRestoreIssue { SourceUnavailable, ArchiveInvalid, ArchiveUnsafe, NotEnoughSpace, NewerVersion, DatabaseInvalid, ReferencesInvalid, SwapFailed }`
  - `LibraryRestoreException(LibraryRestoreIssue issue, IReadOnlyList<Guid>? guideIds = null, IReadOnlyList<Guid>? gameIds = null, IReadOnlyList<string>? titles = null, long? bytesNeeded = null, Exception? inner = null)`
    with `Issue`, `GuideIds`, `GameIds`, `Titles`, `BytesNeeded`. The
    staged database is deleted when staging fails, so the restorer reads
    the titles first and carries them.
  - `record LibraryRestoreStage(Guid StageId, DateTimeOffset CreatedUtc, string AppVersion, int SchemaVersion, int Games, int Guides, long Bytes)`
  - `LibraryOpenIssue.RestoreIncomplete` (appended)
  - `LibraryBackupMessages.RestoreFailed(LibraryRestoreIssue issue, IReadOnlyList<string> titles, long? bytesNeeded)`,
    `RestorePhase(LibraryRestorePhase phase)`,
    `BackupMade(DateTime localCreated, string appVersion)`,
    `BackupHolds(int games, int guides, long bytes)`,
    `LibraryHas(int games, int guides)`,
    `ReplaceBody(int currentGames, int currentGuides, int games, int guides)`,
    `Restored(int games, int guides)`, constants `ReplaceTitle`,
    `RestoreKept`, `RestoreCanceled`.

- [ ] **Step 1: Write the failing tests**

Add to `LibraryBackupMessagesTests`:

```csharp
    [Fact]
    public void EveryRestoreIssueHasAMessage()
    {
        foreach (LibraryRestoreIssue issue in Enum.GetValues<LibraryRestoreIssue>())
        {
            Assert.False(string.IsNullOrWhiteSpace(LibraryBackupMessages.RestoreFailed(issue, [], null)));
        }
    }

    [Fact]
    public void ANewerBackupSaysToUpdate() =>
        Assert.Equal("This backup is from a newer version of Desktop Guides. Update the app, then try again.",
            LibraryBackupMessages.RestoreFailed(LibraryRestoreIssue.NewerVersion, [], null));

    [Fact]
    public void MissingFilesNameTheGuides() =>
        Assert.Equal("This backup is missing files for: Maps and Linked Game.",
            LibraryBackupMessages.RestoreFailed(LibraryRestoreIssue.ReferencesInvalid, ["Maps", "Linked Game"], null));

    [Fact]
    public void NotEnoughSpaceSaysHowMuch() =>
        Assert.Equal("There isn't enough free space to restore this backup. It needs 1.5 GB.",
            LibraryBackupMessages.RestoreFailed(LibraryRestoreIssue.NotEnoughSpace, [], 1536L * 1024 * 1024));

    [Fact]
    public void TheStagedDetailsReadAsSentences()
    {
        // The shell passes the creation time already converted to local time.
        Assert.Equal("Backup made: 8 October 2026, 16:12, by version 1.0.0.0",
            LibraryBackupMessages.BackupMade(new DateTime(2026, 10, 8, 16, 12, 0), "1.0.0.0"));
        Assert.Equal("Backup holds: 12 games, 40 guides, 310 MB",
            LibraryBackupMessages.BackupHolds(12, 40, 310L * 1024 * 1024));
        Assert.Equal("This library has: 9 games, 31 guides", LibraryBackupMessages.LibraryHas(9, 31));
    }

    [Fact]
    public void ReplacingAPopulatedLibraryWarns() =>
        Assert.Equal(
            "Your 9 games and 31 guides, with their reading progress, will be replaced by the backup's 12 games and 40 guides. This can't be undone. To keep the current library, export it first.",
            LibraryBackupMessages.ReplaceBody(9, 31, 12, 40));

    [Fact]
    public void ReplacingAnEmptyLibraryDoesnt() =>
        Assert.Equal("The backup's 12 games and 40 guides will be restored.",
            LibraryBackupMessages.ReplaceBody(0, 0, 12, 40));

    [Fact]
    public void TheRestoredMessageCountsTheLibrary() =>
        Assert.Equal("Library restored: 1 game, 2 guides.", LibraryBackupMessages.Restored(1, 2));
```

Add to `LibraryOpenMessagesTests`:

```csharp
    [Fact]
    public void AnUnfinishedRestoreNamesTheRecoveryFolder()
    {
        LibraryOpenMessage message = LibraryOpenMessages.For(LibraryOpenIssue.RestoreIncomplete);

        Assert.Equal("Restore didn't finish", message.Title);
        Assert.Equal(
            "A restore didn't finish and Desktop Guides couldn't put your previous library back. Your previous library is in the .recovery folder inside the data folder. Close other programs that might be using it, then try again.",
            message.Body);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: Core tests with `--filter "FullyQualifiedName~LibraryBackupMessagesTests|FullyQualifiedName~LibraryOpenMessagesTests"`.
Expected: the build fails: `LibraryRestoreIssue` and
`LibraryOpenIssue.RestoreIncomplete` don't exist.

- [ ] **Step 3: Write the implementation**

```csharp
// src/DesktopGuides.Core/Backup/LibraryRestoreContracts.cs
namespace DesktopGuides.Core.Backup;

public enum LibraryRestorePhase { Copying, Checking, Extracting }

public readonly record struct LibraryRestoreProgress(
    LibraryRestorePhase Phase, long BytesDone, long BytesTotal);

public enum LibraryRestoreIssue
{
    SourceUnavailable,
    ArchiveInvalid,
    ArchiveUnsafe,
    NotEnoughSpace,
    NewerVersion,
    DatabaseInvalid,
    ReferencesInvalid,
    SwapFailed
}

public sealed class LibraryRestoreException : Exception
{
    public LibraryRestoreException(
        LibraryRestoreIssue issue, IReadOnlyList<Guid>? guideIds = null,
        IReadOnlyList<Guid>? gameIds = null, IReadOnlyList<string>? titles = null,
        long? bytesNeeded = null, Exception? inner = null)
        : base($"Library restore failed: {issue}.", inner)
    {
        Issue = issue;
        GuideIds = guideIds ?? [];
        GameIds = gameIds ?? [];
        Titles = titles ?? [];
        BytesNeeded = bytesNeeded;
    }

    public LibraryRestoreIssue Issue { get; }

    /// <summary>For <see cref="LibraryRestoreIssue.ReferencesInvalid"/>: guides whose files are missing.</summary>
    public IReadOnlyList<Guid> GuideIds { get; }

    /// <summary>For <see cref="LibraryRestoreIssue.ReferencesInvalid"/>: games whose artwork is missing.</summary>
    public IReadOnlyList<Guid> GameIds { get; }

    /// <summary>The titles of those guides and games, read from the backup before it was discarded.</summary>
    public IReadOnlyList<string> Titles { get; }

    /// <summary>For <see cref="LibraryRestoreIssue.NotEnoughSpace"/>: the free space needed.</summary>
    public long? BytesNeeded { get; }
}

/// <summary>A backup checked and extracted into staging, ready to replace the library.</summary>
public sealed record LibraryRestoreStage(
    Guid StageId, DateTimeOffset CreatedUtc, string AppVersion,
    int SchemaVersion, int Games, int Guides, long Bytes);
```

Append to `LibraryBackupMessages`:

```csharp
    public const string ReplaceTitle = "Replace your library?";
    public const string RestoreKept = "The backup couldn't be opened, so your previous library was kept.";
    public const string RestoreCanceled = "Restore canceled.";

    public static string RestorePhase(LibraryRestorePhase phase) => phase switch
    {
        LibraryRestorePhase.Copying => "Copying backup…",
        LibraryRestorePhase.Checking => "Checking backup…",
        LibraryRestorePhase.Extracting => "Unpacking backup…",
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };

    public static string BackupMade(DateTime localCreated, string appVersion) =>
        string.Create(CultureInfo.InvariantCulture,
            $"Backup made: {localCreated:d MMMM yyyy, HH:mm}, by version {appVersion}");

    public static string BackupHolds(int games, int guides, long bytes) =>
        $"Backup holds: {Counts(games, guides)}, {Size(bytes)}";

    public static string LibraryHas(int games, int guides) => $"This library has: {Counts(games, guides)}";

    public static string ReplaceBody(int currentGames, int currentGuides, int games, int guides) =>
        currentGames == 0 && currentGuides == 0
            ? $"The backup's {Pair(games, guides)} will be restored."
            : $"Your {Pair(currentGames, currentGuides)}, with their reading progress, will be replaced by the backup's {Pair(games, guides)}. This can't be undone. To keep the current library, export it first.";

    public static string Restored(int games, int guides) => $"Library restored: {Counts(games, guides)}.";

    public static string RestoreFailed(LibraryRestoreIssue issue, IReadOnlyList<string> titles, long? bytesNeeded) => issue switch
    {
        LibraryRestoreIssue.SourceUnavailable =>
            "The backup file couldn't be read. Check it's still there, then try again.",
        LibraryRestoreIssue.ArchiveInvalid => "This file isn't a Desktop Guides backup, or it's damaged.",
        LibraryRestoreIssue.ArchiveUnsafe =>
            "This backup contains file names that aren't allowed, so it wasn't opened.",
        LibraryRestoreIssue.NotEnoughSpace => bytesNeeded is long needed
            ? $"There isn't enough free space to restore this backup. It needs {Size(needed)}."
            : "There isn't enough free space to restore this backup.",
        LibraryRestoreIssue.NewerVersion =>
            "This backup is from a newer version of Desktop Guides. Update the app, then try again.",
        LibraryRestoreIssue.DatabaseInvalid => "The library database in this backup is damaged.",
        LibraryRestoreIssue.ReferencesInvalid => titles.Count == 0
            ? "This backup is missing some guide or artwork files."
            : $"This backup is missing files for: {Titles(titles)}.",
        LibraryRestoreIssue.SwapFailed =>
            "The library couldn't be replaced. Close other programs that might be using it, then try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(issue))
    };

    private static string Pair(int games, int guides) =>
        $"{Plural(games, "game")} and {Plural(guides, "guide")}";
```

In `LibraryOpen.cs`, append `RestoreIncomplete` to `LibraryOpenIssue`
(after `DiskFull`) and add this arm before the discard arm in `For`:

```csharp
        LibraryOpenIssue.RestoreIncomplete => new("Restore didn't finish",
            "A restore didn't finish and Desktop Guides couldn't put your previous library back. Your previous library is in the .recovery folder inside the data folder. Close other programs that might be using it, then try again."),
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: the Step 2 command, then the whole Core suite.
Expected: PASS. The 9 new tests and every existing test pass, including
`LibraryOpenMessagesTests`'s check that every issue has a message.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Core tests/DesktopGuides.Core.Tests
git commit -m "feat(core): add restore contracts and copy"
```

### Task 7: Stage a backup: copy, archive checks, names, space and extraction

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Storage/LibraryRestorer.cs`
- Create: `tests/DesktopGuides.Infrastructure.Tests/Restore/RestoreFixture.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Restore/LibraryRestorerStageTests.cs`

**Interfaces:**
- Consumes: Task 6's contracts; `LibraryArchiveVerifier.Verify`,
  `LibraryArchiveManifest` (T20.1); `ManagedPathResolver.RejectFilesystemLinks`.
- Produces:
  - `internal enum RestoreCheckpoint { Copied, Extracted, Validated, MarkerWritten, PriorMoved, Promoted, Confirmed, RolledAside, PriorReturned }`
  - `public sealed class LibraryRestorer` with `LibraryRestorer(ILibraryPaths paths)`,
    `internal LibraryRestorer(ILibraryPaths paths, Action<RestoreCheckpoint> checkpoint, Func<string, long>? freeBytes = null, Func<Guid>? newId = null)`,
    `Task<LibraryRestoreStage> StageAsync(string zipPath, IProgress<LibraryRestoreProgress>? progress, CancellationToken token)`,
    `void DiscardStage(LibraryRestoreStage stage)`
  - `public const string StagingFolderName = ".restore-staging"`;
    `internal static string StageRoot(ILibraryPaths paths, Guid stageId)` →
    `<data>/.restore-staging/<id:N>`; the extracted tree is its `library`
    folder.
  - Test fixture `RestoreFixture` (Tasks 8–10): `Source` (`ExportFixture`),
    `Target` (`RemovalLibrary`), `Backup` (path), `Restorer(...)`,
    `Rewrite(...)`, `RewriteDatabase(...)`, `LiveEntries()`, `AssertNoStage()`.

- [ ] **Step 1: Write the fixture**

```csharp
// tests/DesktopGuides.Infrastructure.Tests/Restore/RestoreFixture.cs
using System.IO.Compression;
using System.Security.Cryptography;
using DesktopGuides.Core.Backup;
using DesktopGuides.Infrastructure.Storage;
using DesktopGuides.Infrastructure.Tests.FaultInjection;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Restore;

internal sealed record ArchiveFile(string Name, byte[] Bytes);

/// <summary>
/// A backup exported from <see cref="ExportFixture"/> and a separate target
/// library to restore it into. Rewrite builds hostile variants of the backup.
/// </summary>
internal sealed class RestoreFixture : IAsyncDisposable
{
    private RestoreFixture(ExportFixture source, RemovalLibrary target, string backup)
    {
        Source = source;
        Target = target;
        Backup = backup;
    }

    public ExportFixture Source { get; }
    public RemovalLibrary Target { get; }
    public string Backup { get; }

    public string StagingParent => Path.Combine(Target.Paths.DataRoot, LibraryRestorer.StagingFolderName);

    public static async Task<RestoreFixture> CreateAsync()
    {
        ExportFixture source = await ExportFixture.CreateAsync();
        string folder = Path.Combine(source.Library.Root, "backups");
        Directory.CreateDirectory(folder);
        LibraryExportResult result = await new LibraryExporter(
                source.Library.Repository, source.Library.Paths,
                new LibraryExportOptions("1.0.0.0", "msix", []))
            .ExportAsync(Path.Combine(folder, "backup.zip"), false, null, CancellationToken.None);
        return new RestoreFixture(source, await RemovalLibrary.CreateAsync(), result.Path);
    }

    public LibraryRestorer Restorer(
        Action<RestoreCheckpoint>? checkpoint = null, Func<string, long>? freeBytes = null) =>
        new(Target.Paths, checkpoint ?? (_ => { }), freeBytes);

    public Task<LibraryRestoreStage> StageAsync(
        string? backup = null, Action<RestoreCheckpoint>? checkpoint = null,
        Func<string, long>? freeBytes = null, CancellationToken token = default) =>
        Restorer(checkpoint, freeBytes).StageAsync(backup ?? Backup, null, token);

    /// <summary>Every live entry except staging, which a stage is allowed to add.</summary>
    public IReadOnlyDictionary<string, string> LiveEntries() =>
        LibrarySnapshot.Capture(Target.Paths).Entries
            .Where(entry => !entry.Key.StartsWith("fs:" + LibraryRestorer.StagingFolderName, StringComparison.Ordinal))
            .ToDictionary(entry => entry.Key, entry => entry.Value);

    public void AssertNoStage() =>
        Assert.True(!Directory.Exists(StagingParent) || !Directory.EnumerateFileSystemEntries(StagingParent).Any(),
            "A stage folder was left behind.");

    /// <summary>
    /// Copies the backup with its entries edited. With <paramref name="rewriteManifest"/>
    /// the manifest lists the edited entries, so the archive stays self-consistent;
    /// otherwise the original manifest (optionally edited) is kept.
    /// </summary>
    public string Rewrite(
        Action<List<ArchiveFile>> edit, bool rewriteManifest = true,
        Func<LibraryArchiveManifest, LibraryArchiveManifest>? editManifest = null)
    {
        List<ArchiveFile> files;
        using (ZipArchive zip = ZipFile.OpenRead(Backup))
        {
            files = zip.Entries.Select(entry => new ArchiveFile(entry.FullName, Read(entry))).ToList();
        }
        LibraryArchiveManifest manifest = LibraryArchiveManifest.Parse(files[0].Bytes);
        files.RemoveAt(0);
        edit(files);
        if (rewriteManifest)
        {
            files = files.OrderBy(file => file.Name, StringComparer.Ordinal).ToList();
            manifest = manifest with
            {
                Entries = files.Select(file => new LibraryArchiveEntry(file.Name, file.Bytes.Length, Sha(file.Bytes))).ToArray()
            };
        }
        manifest = editManifest?.Invoke(manifest) ?? manifest;
        string path = Path.Combine(Path.GetDirectoryName(Backup)!, $"edited-{Guid.NewGuid():N}.zip");
        using ZipArchive output = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(output, LibraryArchiveManifest.EntryName, LibraryArchiveManifest.Write(manifest));
        foreach (ArchiveFile file in files)
        {
            Add(output, file.Name, file.Bytes);
        }
        return path;
    }

    /// <summary>A self-consistent backup whose database was changed by <paramref name="sql"/>.</summary>
    public string RewriteDatabase(string sql) => Rewrite(files =>
    {
        int index = files.FindIndex(file => file.Name == LibraryArchiveManifest.DatabasePath);
        string copy = Path.Combine(Target.Root, $"db-{Guid.NewGuid():N}.sqlite");
        File.WriteAllBytes(copy, files[index].Bytes);
        using (SqliteConnection connection = new($"Data Source={copy};Pooling=False"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=DELETE; " + sql;
            command.ExecuteNonQuery();
        }
        files[index] = files[index] with { Bytes = File.ReadAllBytes(copy) };
    });

    public string WriteBytes(byte[] bytes)
    {
        string path = Path.Combine(Path.GetDirectoryName(Backup)!, $"raw-{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using MemoryStream copy = new();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static void Add(ZipArchive zip, string name, byte[] bytes)
    {
        using Stream stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        stream.Write(bytes);
    }

    public async ValueTask DisposeAsync()
    {
        await Target.DisposeAsync();
        await Source.DisposeAsync();
    }
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/DesktopGuides.Infrastructure.Tests/Restore/LibraryRestorerStageTests.cs
using DesktopGuides.Core.Backup;
using DesktopGuides.Infrastructure.Storage;
using DesktopGuides.Infrastructure.Tests.FaultInjection;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Restore;

public sealed class LibraryRestorerStageTests : IAsyncLifetime
{
    private RestoreFixture fixture = null!;

    public async Task InitializeAsync() => fixture = await RestoreFixture.CreateAsync();

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private async Task<LibraryRestoreException> Refused(
        string backup, LibraryRestoreIssue issue, Func<string, long>? freeBytes = null)
    {
        IReadOnlyDictionary<string, string> before = fixture.LiveEntries();
        LibraryRestoreException error = await Assert.ThrowsAsync<LibraryRestoreException>(
            () => fixture.StageAsync(backup, freeBytes: freeBytes));
        Assert.Equal(issue, error.Issue);
        fixture.AssertNoStage();
        Assert.Equal(before, fixture.LiveEntries());
        return error;
    }

    [Fact]
    public async Task AStagedBackupHasItsCountsAndLeavesTheLibraryAlone()
    {
        IReadOnlyDictionary<string, string> before = fixture.LiveEntries();

        LibraryRestoreStage stage = await fixture.StageAsync();

        Assert.Equal((2, 3, "1.0.0.0", 4), (stage.Games, stage.Guides, stage.AppVersion, stage.SchemaVersion));
        string library = Path.Combine(LibraryRestorer.StageRoot(fixture.Target.Paths, stage.StageId), "library");
        Assert.True(File.Exists(Path.Combine(library, "library.sqlite")));
        Assert.True(File.Exists(Path.Combine(library, "content", fixture.Source.TxtGuide.ToString("N"), "guide.txt")));
        Assert.Equal(before, fixture.LiveEntries());
    }

    [Fact]
    public async Task DiscardDeletesTheStage()
    {
        LibraryRestoreStage stage = await fixture.StageAsync();

        fixture.Restorer().DiscardStage(stage);

        fixture.AssertNoStage();
    }

    [Fact]
    public async Task AMissingFileIsUnavailable() =>
        await Refused(Path.Combine(fixture.Target.Root, "nowhere.zip"), LibraryRestoreIssue.SourceUnavailable);

    [Fact]
    public async Task AFileThatIsntAZipIsInvalid() =>
        await Refused(fixture.WriteBytes("not a zip"u8.ToArray()), LibraryRestoreIssue.ArchiveInvalid);

    [Fact]
    public async Task ATruncatedZipIsInvalid()
    {
        byte[] bytes = File.ReadAllBytes(fixture.Backup);
        await Refused(fixture.WriteBytes(bytes[..(bytes.Length / 2)]), LibraryRestoreIssue.ArchiveInvalid);
    }

    [Fact]
    public async Task AChangedFileIsInvalid()
    {
        string backup = fixture.Rewrite(files =>
        {
            int index = files.FindIndex(file => file.Name.EndsWith("/guide.txt", StringComparison.Ordinal));
            byte[] changed = files[index].Bytes.ToArray();
            changed[0] ^= 0xFF;
            files[index] = files[index] with { Bytes = changed };
        }, rewriteManifest: false);

        await Refused(backup, LibraryRestoreIssue.ArchiveInvalid);
    }

    [Fact]
    public async Task AnEntryTheManifestDoesntListIsInvalid() =>
        await Refused(fixture.Rewrite(files => files.Add(new ArchiveFile(
                $"library/content/{fixture.Source.TxtGuide:N}/extra.txt", "extra"u8.ToArray())),
            rewriteManifest: false),
            LibraryRestoreIssue.ArchiveInvalid);

    [Fact]
    public async Task AListedEntryMissingFromTheArchiveIsInvalid() =>
        await Refused(fixture.Rewrite(files => files.RemoveAll(file => file.Name.EndsWith("/manual.pdf", StringComparison.Ordinal)),
            rewriteManifest: false),
            LibraryRestoreIssue.ArchiveInvalid);

    [Fact]
    public async Task AnEntryLongerThanItsManifestSizeIsInvalid() =>
        await Refused(fixture.Rewrite(_ => { }, editManifest: manifest => manifest with
        {
            Entries = manifest.Entries
                .Select(entry => entry.Path.EndsWith("/guide.txt", StringComparison.Ordinal)
                    ? entry with { Bytes = entry.Bytes - 1 }
                    : entry)
                .ToArray()
        }), LibraryRestoreIssue.ArchiveInvalid);

    [Theory]
    [InlineData("library/content/../../evil.txt")]
    [InlineData("C:/evil.txt")]
    [InlineData("/library/library.sqlite")]
    [InlineData("library/content/0123456789abcdef0123456789abcdef/..\\..\\evil.txt")]
    public async Task ANameOutsideTheLibraryIsUnsafe(string name) =>
        await Refused(fixture.Rewrite(files => files.Add(new ArchiveFile(name, "x"u8.ToArray())),
            rewriteManifest: false),
            LibraryRestoreIssue.ArchiveUnsafe);

    [Theory]
    [InlineData("Notes.txt", "notes.txt")]
    [InlineData("caf\u00e9.txt", "cafe\u0301.txt")]
    public async Task NamesThatCollideOnDiskAreUnsafe(string first, string second) =>
        await Refused(fixture.Rewrite(files =>
        {
            files.Add(new ArchiveFile($"library/content/{fixture.Source.TxtGuide:N}/{first}", "a"u8.ToArray()));
            files.Add(new ArchiveFile($"library/content/{fixture.Source.TxtGuide:N}/{second}", "b"u8.ToArray()));
        }), LibraryRestoreIssue.ArchiveUnsafe);

    [Fact]
    public async Task NotEnoughSpaceForTheCopyStopsBeforeCopying()
    {
        LibraryRestoreException error = await Refused(fixture.Backup, LibraryRestoreIssue.NotEnoughSpace, _ => 0);

        Assert.True(error.BytesNeeded > new FileInfo(fixture.Backup).Length);
    }

    [Fact]
    public async Task NotEnoughSpaceToUnpackStopsBeforeExtracting()
    {
        int calls = 0;
        await Refused(fixture.Backup, LibraryRestoreIssue.NotEnoughSpace,
            _ => ++calls == 1 ? long.MaxValue : 0);
    }

    [Theory]
    [InlineData(RestoreCheckpoint.Copied)]
    [InlineData(RestoreCheckpoint.Extracted)]
    public async Task CancellationLeavesNoStage(RestoreCheckpoint point)
    {
        using CancellationTokenSource cancel = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.StageAsync(checkpoint: FaultFixture.CancelAt(point, cancel), token: cancel.Token));

        fixture.AssertNoStage();
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: Infrastructure tests with `--filter "FullyQualifiedName~LibraryRestorerStageTests"`.
Expected: the build fails: `LibraryRestorer` and `RestoreCheckpoint` don't exist.

- [ ] **Step 4: Write the implementation**

```csharp
// src/DesktopGuides.Infrastructure/Storage/LibraryRestorer.cs
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DesktopGuides.Core.Backup;
using DesktopGuides.Core.Paths;

namespace DesktopGuides.Infrastructure.Storage;

internal enum RestoreCheckpoint
{
    Copied, Extracted, Validated, MarkerWritten, PriorMoved, Promoted, Confirmed, RolledAside, PriorReturned
}

/// <summary>
/// Restores a library archive. StageAsync copies the backup next to the
/// library, checks every name, size, hash and database reference, and
/// extracts it into its own folder; nothing under library/ changes until
/// ReplaceAsync. A failed or cancelled stage leaves no folder behind.
/// </summary>
public sealed class LibraryRestorer
{
    public const string StagingFolderName = ".restore-staging";
    internal const string ArchiveFileName = "archive.zip";
    internal const string LibraryFolderName = "library";
    private const int BufferBytes = 81920;
    private const long ProgressStepBytes = 1024 * 1024;

    private readonly ILibraryPaths paths;
    private readonly Action<RestoreCheckpoint> checkpoint;
    private readonly Func<string, long> freeBytes;
    private readonly Func<Guid> newId;

    public LibraryRestorer(ILibraryPaths paths)
        : this(paths, _ => { })
    {
    }

    internal LibraryRestorer(
        ILibraryPaths paths, Action<RestoreCheckpoint> checkpoint,
        Func<string, long>? freeBytes = null, Func<Guid>? newId = null)
    {
        this.paths = paths;
        this.checkpoint = checkpoint;
        this.freeBytes = freeBytes ?? (path => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace);
        this.newId = newId ?? Guid.NewGuid;
    }

    internal static string StageRoot(ILibraryPaths paths, Guid stageId) =>
        Path.Combine(paths.DataRoot, StagingFolderName, stageId.ToString("N"));

    public Task<LibraryRestoreStage> StageAsync(
        string zipPath, IProgress<LibraryRestoreProgress>? progress, CancellationToken token) =>
        Task.Run(() => Stage(zipPath, progress, token), token);

    public void DiscardStage(LibraryRestoreStage stage) => DeleteTree(StageRoot(paths, stage.StageId));

    private LibraryRestoreStage Stage(
        string zipPath, IProgress<LibraryRestoreProgress>? progress, CancellationToken token)
    {
        Guid stageId = newId();
        string stageRoot = StageRoot(paths, stageId);
        try
        {
            ManagedPathResolver.RejectFilesystemLinks(Path.GetDirectoryName(stageRoot)!);
            Directory.CreateDirectory(stageRoot);
            ManagedPathResolver.RejectFilesystemLinks(stageRoot);
            string archive = Path.Combine(stageRoot, ArchiveFileName);
            Copy(zipPath, archive, progress, token);
            checkpoint(RestoreCheckpoint.Copied);
            token.ThrowIfCancellationRequested();

            LibraryArchiveManifest manifest = Check(archive, progress, token);
            RequireSpace(manifest.TotalBytes);
            Extract(archive, manifest, Path.Combine(stageRoot, LibraryFolderName), progress, token);
            checkpoint(RestoreCheckpoint.Extracted);
            token.ThrowIfCancellationRequested();

            return new LibraryRestoreStage(stageId, manifest.CreatedUtc, manifest.AppVersion,
                manifest.SchemaVersion, manifest.Games, manifest.Guides, manifest.TotalBytes);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            DeleteTree(stageRoot);
            throw new LibraryRestoreException(LibraryRestoreIssue.SourceUnavailable, inner: error);
        }
        catch
        {
            DeleteTree(stageRoot);
            throw;
        }
    }

    private void Copy(string source, string target, IProgress<LibraryRestoreProgress>? progress, CancellationToken token)
    {
        FileStream input;
        try
        {
            input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, FileOptions.SequentialScan);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new LibraryRestoreException(LibraryRestoreIssue.SourceUnavailable, inner: error);
        }
        using (input)
        {
            long total = input.Length;
            RequireSpace(total);
            using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            byte[] buffer = new byte[BufferBytes];
            long done = 0;
            long reported = 0;
            int count;
            while ((count = ReadSource(input, buffer)) > 0)
            {
                token.ThrowIfCancellationRequested();
                output.Write(buffer, 0, count);
                done += count;
                if (done - reported >= ProgressStepBytes)
                {
                    reported = done;
                    progress?.Report(new LibraryRestoreProgress(LibraryRestorePhase.Copying, done, total));
                }
            }
            output.Flush(flushToDisk: true);
            progress?.Report(new LibraryRestoreProgress(LibraryRestorePhase.Copying, done, total));
        }
    }

    private static int ReadSource(FileStream input, byte[] buffer)
    {
        try
        {
            return input.Read(buffer);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new LibraryRestoreException(LibraryRestoreIssue.SourceUnavailable, inner: error);
        }
    }

    // The volume needs the bytes plus a tenth, so a restore can't fill the disk.
    private void RequireSpace(long bytes)
    {
        long needed = bytes + bytes / 10;
        if (freeBytes(paths.DataRoot) < needed)
        {
            throw new LibraryRestoreException(LibraryRestoreIssue.NotEnoughSpace, bytesNeeded: needed);
        }
    }

    private static LibraryArchiveManifest Check(
        string archive, IProgress<LibraryRestoreProgress>? progress, CancellationToken token)
    {
        progress?.Report(new LibraryRestoreProgress(LibraryRestorePhase.Checking, 0, 0));
        using FileStream stream = new(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            using (ZipArchive zip = new(stream, ZipArchiveMode.Read, leaveOpen: true))
            {
                RejectUnsafeNames(zip.Entries.Select(entry => entry.FullName).ToArray());
            }
            stream.Position = 0;
            return LibraryArchiveVerifier.Verify(stream, token);
        }
        catch (InvalidDataException error)
        {
            throw new LibraryRestoreException(LibraryRestoreIssue.ArchiveInvalid, inner: error);
        }
    }

    // Every name must be a library path, and no two may land on the same
    // file on a case-insensitive disk once Unicode-normalized.
    private static void RejectUnsafeNames(IReadOnlyList<string> names)
    {
        HashSet<string> folded = new(StringComparer.Ordinal);
        for (int index = 0; index < names.Count; index++)
        {
            string name = names[index];
            if (!(index == 0 && name == LibraryArchiveManifest.EntryName))
            {
                try
                {
                    LibraryArchiveManifest.ValidatePath(name);
                }
                catch (InvalidDataException error)
                {
                    throw new LibraryRestoreException(LibraryRestoreIssue.ArchiveUnsafe, inner: error);
                }
            }
            if (!folded.Add(name.Normalize(NormalizationForm.FormC).ToUpperInvariant()))
            {
                throw new LibraryRestoreException(LibraryRestoreIssue.ArchiveUnsafe);
            }
        }
    }

    private static void Extract(
        string archive, LibraryArchiveManifest manifest, string libraryRoot,
        IProgress<LibraryRestoreProgress>? progress, CancellationToken token)
    {
        string root = Path.GetFullPath(libraryRoot);
        string stageRoot = Path.GetDirectoryName(root)!;
        long total = manifest.TotalBytes;
        long done = 0;
        long reported = 0;
        byte[] buffer = new byte[BufferBytes];
        using ZipArchive zip = ZipFile.OpenRead(archive);
        for (int index = 0; index < manifest.Entries.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            LibraryArchiveEntry expected = manifest.Entries[index];
            ZipArchiveEntry entry = zip.Entries[index + 1];
            if (entry.FullName != expected.Path)
            {
                throw new LibraryRestoreException(LibraryRestoreIssue.ArchiveInvalid);
            }
            string target = Path.GetFullPath(Path.Combine(stageRoot, expected.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new LibraryRestoreException(LibraryRestoreIssue.ArchiveUnsafe);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long written = 0;
            try
            {
                using Stream input = entry.Open();
                using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                int count;
                while ((count = input.Read(buffer)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    // Never write past the declared size, whatever the entry inflates to.
                    if (written + count > expected.Bytes)
                    {
                        throw new LibraryRestoreException(LibraryRestoreIssue.ArchiveInvalid);
                    }
                    output.Write(buffer, 0, count);
                    hash.AppendData(buffer, 0, count);
                    written += count;
                    done += count;
                    if (done - reported >= ProgressStepBytes)
                    {
                        reported = done;
                        progress?.Report(new LibraryRestoreProgress(LibraryRestorePhase.Extracting, done, total));
                    }
                }
            }
            catch (InvalidDataException error)
            {
                throw new LibraryRestoreException(LibraryRestoreIssue.ArchiveInvalid, inner: error);
            }
            if (written != expected.Bytes || Convert.ToHexStringLower(hash.GetHashAndReset()) != expected.Sha256)
            {
                throw new LibraryRestoreException(LibraryRestoreIssue.ArchiveInvalid);
            }
        }
        progress?.Report(new LibraryRestoreProgress(LibraryRestorePhase.Extracting, done, total));
    }

    internal static void DeleteTree(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
```

`ManagedPathResolver.RejectFilesystemLinks` is `internal static`, so it is
callable here. `Directory.Delete(recursive: true)` removes a junction
itself without following it, so `DeleteTree` can't reach outside staging.

- [ ] **Step 5: Run the tests to verify they pass**

Run: the Step 3 command.
Expected: PASS, 19 tests.

- [ ] **Step 6: Commit**

```bash
git add src/DesktopGuides.Infrastructure/Storage/LibraryRestorer.cs tests/DesktopGuides.Infrastructure.Tests/Restore
git commit -m "feat(storage): stage a library backup and check the archive"
```

### Task 8: Stage a backup: database and reference checks

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Storage/LibraryRestorer.cs`
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs`
  (`ValidateDatabase` becomes `internal static`)
- Test: `tests/DesktopGuides.Infrastructure.Tests/Restore/LibraryRestorerValidationTests.cs`

**Interfaces:**
- Consumes: Task 7's `Stage`; `LibraryArchivePlan.Read(string, ILibraryPaths, int)`;
  `SqliteLibraryRepository.ValidateDatabase(SqliteConnection, SqliteTransaction?, int)`;
  `LibrarySchema.CurrentVersion`.
- Produces: `LibraryRestorer.FirstArchivedSchema = 4`; a stage only
  returns after these checks pass, then reaches `RestoreCheckpoint.Validated`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/DesktopGuides.Infrastructure.Tests/Restore/LibraryRestorerValidationTests.cs
using DesktopGuides.Core.Backup;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Restore;

public sealed class LibraryRestorerValidationTests : IAsyncLifetime
{
    private RestoreFixture fixture = null!;

    public async Task InitializeAsync() => fixture = await RestoreFixture.CreateAsync();

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private async Task<LibraryRestoreException> Refused(string backup, LibraryRestoreIssue issue)
    {
        IReadOnlyDictionary<string, string> before = fixture.LiveEntries();
        LibraryRestoreException error = await Assert.ThrowsAsync<LibraryRestoreException>(
            () => fixture.StageAsync(backup));
        Assert.Equal(issue, error.Issue);
        fixture.AssertNoStage();
        Assert.Equal(before, fixture.LiveEntries());
        return error;
    }

    [Fact]
    public async Task ANewerSchemaSaysUpdate() =>
        await Refused(fixture.RewriteDatabase("PRAGMA user_version = 99;"), LibraryRestoreIssue.NewerVersion);

    [Fact]
    public async Task ASchemaOlderThanAnyExportIsInvalid() =>
        await Refused(fixture.RewriteDatabase("PRAGMA user_version = 3;"), LibraryRestoreIssue.DatabaseInvalid);

    [Fact]
    public async Task ADamagedDatabaseIsInvalid()
    {
        string backup = fixture.Rewrite(files =>
        {
            int index = files.FindIndex(file => file.Name == LibraryArchiveManifest.DatabasePath);
            byte[] damaged = files[index].Bytes.ToArray();
            Array.Fill(damaged, (byte)0x5A, 4096, Math.Min(4096, damaged.Length - 4096));
            files[index] = files[index] with { Bytes = damaged };
        });

        await Refused(backup, LibraryRestoreIssue.DatabaseInvalid);
    }

    [Fact]
    public async Task ADanglingForeignKeyIsInvalid() =>
        await Refused(fixture.RewriteDatabase(
                $"PRAGMA foreign_keys = OFF; UPDATE Guides SET GameId = '{Guid.NewGuid():N}' WHERE Id = '{fixture.Source.PdfGuide:N}';"),
            LibraryRestoreIssue.DatabaseInvalid);

    [Fact]
    public async Task AnAlteredSchemaIsInvalid() =>
        await Refused(fixture.RewriteDatabase("CREATE TABLE Extra (Id INTEGER);"), LibraryRestoreIssue.DatabaseInvalid);

    [Fact]
    public async Task AnUnfinishedFileOperationIsInvalid() =>
        await Refused(fixture.RewriteDatabase(
                $"INSERT INTO FileOperations (Id, Kind, Phase, ManifestJson, CreatedUtcMs) VALUES ('{Guid.NewGuid():N}', 'Import', 'Prepared', '{{}}', 0);"),
            LibraryRestoreIssue.DatabaseInvalid);

    [Fact]
    public async Task MissingArtworkNamesItsGame()
    {
        string backup = fixture.Rewrite(files => files.RemoveAll(file => file.Name.StartsWith("library/artwork/", StringComparison.Ordinal)));

        LibraryRestoreException error = await Refused(backup, LibraryRestoreIssue.ReferencesInvalid);

        Assert.Equal([fixture.Source.LinkedGame], error.GameIds);
        Assert.Empty(error.GuideIds);
        Assert.Equal(["Linked Game"], error.Titles);
    }

    [Fact]
    public async Task AMissingGuideFileNamesItsGuide()
    {
        string backup = fixture.Rewrite(files => files.RemoveAll(file => file.Name.EndsWith("/manual.pdf", StringComparison.Ordinal)));

        LibraryRestoreException error = await Refused(backup, LibraryRestoreIssue.ReferencesInvalid);

        Assert.Equal([fixture.Source.PdfGuide], error.GuideIds);
        Assert.Equal(["Manual"], error.Titles);
    }

    [Fact]
    public async Task AFileTheDatabaseDoesntReferenceIsInvalid() =>
        await Refused(fixture.Rewrite(files => files.Add(new ArchiveFile(
                $"library/content/{fixture.Source.TxtGuide:N}/stray.txt", "stray"u8.ToArray()))),
            LibraryRestoreIssue.ReferencesInvalid);

    [Fact]
    public async Task AFileWhoseContentDisagreesWithTheDatabaseIsInvalid()
    {
        // Self-consistent archive, but guide.txt no longer matches the hash the database recorded.
        string backup = fixture.Rewrite(files =>
        {
            int index = files.FindIndex(file => file.Name.EndsWith("/guide.txt", StringComparison.Ordinal));
            byte[] changed = files[index].Bytes.ToArray();
            changed[0] ^= 0xFF;
            files[index] = files[index] with { Bytes = changed };
        });

        LibraryRestoreException error = await Refused(backup, LibraryRestoreIssue.ReferencesInvalid);

        Assert.Equal([fixture.Source.TxtGuide], error.GuideIds);
    }

    [Fact]
    public async Task HtmlAssetsThatDontSumToTheGuideAreInvalid() =>
        await Refused(fixture.RewriteDatabase(
                $"UPDATE Guides SET ContentBytes = ContentBytes + 1 WHERE Id = '{fixture.Source.HtmlGuide:N}';"),
            LibraryRestoreIssue.ReferencesInvalid);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: Infrastructure tests with `--filter "FullyQualifiedName~LibraryRestorerValidationTests"`.
Expected: FAIL. Every test fails with "Assert.Throws() Failure: No
exception was thrown", because staging doesn't check the database yet.

- [ ] **Step 3: Write the implementation**

In `SqliteLibraryRepository.cs`, change `private static void ValidateDatabase(`
to `internal static void ValidateDatabase(`.

In `LibraryRestorer.cs`, add `using Microsoft.Data.Sqlite;`, the constant
below, call `ValidateStagedLibrary(stageRoot, manifest);` then
`checkpoint(RestoreCheckpoint.Validated);` after
`checkpoint(RestoreCheckpoint.Extracted); token.ThrowIfCancellationRequested();`
in `Stage`, and add the methods:

```csharp
    /// <summary>The first schema an exporter wrote (T20.1); older archives can't exist.</summary>
    internal const int FirstArchivedSchema = 4;

    private static void ValidateStagedLibrary(string stageRoot, LibraryArchiveManifest manifest)
    {
        ManagedPathResolver staged = new(stageRoot);
        ValidateStagedDatabase(staged.DatabasePath, manifest.SchemaVersion);
        LibraryArchivePlan plan;
        try
        {
            plan = LibraryArchivePlan.Read(staged.DatabasePath, staged);
        }
        catch (LibraryExportException error) when (error.Issue == LibraryExportIssue.ManagedFilesDamaged)
        {
            throw new LibraryRestoreException(LibraryRestoreIssue.ReferencesInvalid, error.GuideIds, error.GameIds,
                TitlesFor(staged.DatabasePath, error.GuideIds, error.GameIds), inner: error);
        }
        catch (LibraryExportException error)
        {
            throw new LibraryRestoreException(LibraryRestoreIssue.ArchiveInvalid, inner: error);
        }
        catch (Exception error) when (error is SqliteException or InvalidDataException or FormatException)
        {
            throw new LibraryRestoreException(LibraryRestoreIssue.DatabaseInvalid, inner: error);
        }

        // The archive must hold exactly what the database references, with the
        // sizes and hashes the database recorded, and nothing else.
        HashSet<(string, long, string)> listed = manifest.Entries
            .Where(entry => entry.Path != LibraryArchiveManifest.DatabasePath)
            .Select(entry => (entry.Path, entry.Bytes, entry.Sha256))
            .ToHashSet();
        PlannedArchiveFile[] unmatched = plan.Files
            .Where(file => !listed.Contains((file.ArchivePath, file.Bytes, file.Sha256)))
            .ToArray();
        if (unmatched.Length > 0 || plan.Files.Count != listed.Count ||
            plan.Games != manifest.Games || plan.Guides != manifest.Guides)
        {
            Guid[] guideIds = unmatched.Select(file => file.GuideId).OfType<Guid>().Distinct().ToArray();
            Guid[] gameIds = unmatched.Select(file => file.GameId).OfType<Guid>().Distinct().ToArray();
            throw new LibraryRestoreException(LibraryRestoreIssue.ReferencesInvalid, guideIds, gameIds,
                TitlesFor(staged.DatabasePath, guideIds, gameIds));
        }
    }

    // Read before the stage is deleted, so the message can name what's missing.
    private static IReadOnlyList<string> TitlesFor(
        string database, IReadOnlyList<Guid> guideIds, IReadOnlyList<Guid> gameIds)
    {
        List<string> titles = [];
        try
        {
            using SqliteConnection connection = new(new SqliteConnectionStringBuilder
            {
                DataSource = database,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString());
            connection.Open();
            foreach ((string table, Guid id) in guideIds.Select(id => ("Guides", id))
                         .Concat(gameIds.Select(id => ("Games", id))))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT Title FROM {table} WHERE Id = $id";
                command.Parameters.AddWithValue("$id", id.ToString("N"));
                if (command.ExecuteScalar() is string title)
                {
                    titles.Add(title);
                }
            }
        }
        catch (SqliteException)
        {
            // Unnamed is better than no message.
        }
        return titles;
    }

    private static void ValidateStagedDatabase(string database, int manifestVersion)
    {
        try
        {
            using SqliteConnection connection = new(new SqliteConnectionStringBuilder
            {
                DataSource = database,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString());
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version";
            long version = (long)command.ExecuteScalar()!;
            if (version > LibrarySchema.CurrentVersion)
            {
                throw new LibraryRestoreException(LibraryRestoreIssue.NewerVersion);
            }
            if (version < FirstArchivedSchema || version != manifestVersion)
            {
                throw new LibraryRestoreException(LibraryRestoreIssue.DatabaseInvalid);
            }
            SqliteLibraryRepository.ValidateDatabase(connection, null, (int)version);
            command.CommandText = "SELECT COUNT(*) FROM FileOperations";
            if ((long)command.ExecuteScalar()! != 0)
            {
                throw new LibraryRestoreException(LibraryRestoreIssue.DatabaseInvalid);
            }
        }
        catch (Exception error) when (error is SqliteException or InvalidDataException)
        {
            throw new LibraryRestoreException(LibraryRestoreIssue.DatabaseInvalid, inner: error);
        }
    }
```

The `ANewerSchemaSaysUpdate` backup still says `schemaVersion: 4` in its
manifest; the newer-version check runs first, so it reports
`NewerVersion` as an updated app would need.

- [ ] **Step 4: Run the tests to verify they pass**

Run: the Step 2 command, then `--filter "FullyQualifiedName~Restore"`.
Expected: PASS, 11 new tests; Task 7's 19 still pass. In particular
`AStagedBackupHasItsCountsAndLeavesTheLibraryAlone` still passes, which
shows a real export satisfies the reference check.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Infrastructure/Storage tests/DesktopGuides.Infrastructure.Tests/Restore
git commit -m "feat(storage): check a staged backup's database and references"
```

### Task 9: The restore marker and the swap

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Storage/RestoreMarker.cs`
- Modify: `src/DesktopGuides.Infrastructure/Storage/LibraryRestorer.cs` (`ReplaceAsync`)
- Modify: `tests/DesktopGuides.Infrastructure.Tests/Restore/RestoreFixture.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Restore/LibraryRestorerSwapTests.cs`

**Interfaces:**
- Consumes: Task 7's `StageRoot`, `LibraryFolderName`, `DeleteTree`.
- Produces:
  - `internal enum RestoreMarkerPhase { Swapping, Confirmed, RollingBack }`
  - `internal sealed record RestoreMarker(Guid StageId, bool PriorExists, RestoreMarkerPhase Phase)` with
    `FileName = "restore.marker"`, `static string PathFor(ILibraryPaths)`,
    `static string PriorRoot(ILibraryPaths, Guid)` → `<data>/.recovery/restore-<id>/library`,
    `static string StagedLibrary(ILibraryPaths, Guid)`,
    `static string Unverified(ILibraryPaths, Guid)` → `<data>/.restore-staging/<id>-unverified`,
    `void Write(ILibraryPaths)`, `static RestoreMarker? Read(ILibraryPaths)`,
    `static void Delete(ILibraryPaths)`.
  - `public Task ReplaceAsync(LibraryRestoreStage stage, CancellationToken token)`.
  - Fixture: `ClearTargetAsync()` (no `library/` at all), `AddPriorGameAsync()`,
    `MarkerPath`, and `LiveEntries()` returning `{"library": "absent"}` when there is no library.

- [ ] **Step 1: Extend the fixture**

In `RestoreFixture`, replace `LiveEntries` and add:

```csharp
    /// <summary>Every live entry except staging; a missing library reads as one entry.</summary>
    public IReadOnlyDictionary<string, string> LiveEntries()
    {
        if (!Directory.Exists(Target.Paths.LibraryRoot))
        {
            return new Dictionary<string, string> { ["library"] = "absent" };
        }
        return LibrarySnapshot.Capture(Target.Paths).Entries
            .Where(entry => !entry.Key.StartsWith("fs:" + LibraryRestorer.StagingFolderName, StringComparison.Ordinal))
            .ToDictionary(entry => entry.Key, entry => entry.Value);
    }

    /// <summary>A target with no library folder, as before the first run.</summary>
    public async Task ClearTargetAsync()
    {
        await Target.Repository.DisposeAsync();
        Directory.Delete(Target.Paths.LibraryRoot, recursive: true);
    }

    public Task<Guid> AddPriorGameAsync() => Target.AddGameAsync("Prior Game");

    public string MarkerPath => Path.Combine(Target.Paths.DataRoot, "restore.marker");
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/DesktopGuides.Infrastructure.Tests/Restore/LibraryRestorerSwapTests.cs
using DesktopGuides.Core.Backup;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Restore;

public sealed class LibraryRestorerSwapTests : IAsyncLifetime
{
    private RestoreFixture fixture = null!;

    public async Task InitializeAsync() => fixture = await RestoreFixture.CreateAsync();

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    [Fact]
    public async Task ReplaceParksThePriorLibraryAndPromotesTheStage()
    {
        await fixture.AddPriorGameAsync();
        LibraryRestoreStage stage = await fixture.StageAsync();

        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);

        Assert.Equal("2", fixture.Target.Scalar("SELECT COUNT(*) FROM Games"));
        string prior = RestoreMarker.PriorRoot(fixture.Target.Paths, stage.StageId);
        Assert.True(File.Exists(Path.Combine(prior, "library.sqlite")));
        Assert.Equal(new RestoreMarker(stage.StageId, true, RestoreMarkerPhase.Swapping),
            RestoreMarker.Read(fixture.Target.Paths));
    }

    [Fact]
    public async Task ReplacingNoLibraryRecordsThatThereWasNone()
    {
        LibraryRestoreStage stage = await fixture.StageAsync();
        await fixture.ClearTargetAsync();

        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);

        Assert.True(File.Exists(fixture.Target.Paths.DatabasePath));
        Assert.False(RestoreMarker.Read(fixture.Target.Paths)!.PriorExists);
    }

    [Fact]
    public async Task AMarkerRoundTrips()
    {
        RestoreMarker marker = new(Guid.NewGuid(), true, RestoreMarkerPhase.RollingBack);

        marker.Write(fixture.Target.Paths);

        Assert.Equal(marker, RestoreMarker.Read(fixture.Target.Paths));
        Assert.Empty(Directory.EnumerateFiles(fixture.Target.Paths.DataRoot, "restore.marker.*"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"stageId\":\"x\",\"priorExists\":true,\"phase\":\"Swapping\"}")]
    public void AMalformedMarkerIsInvalid(string text)
    {
        File.WriteAllText(fixture.MarkerPath, text);

        Assert.Throws<InvalidDataException>(() => RestoreMarker.Read(fixture.Target.Paths));
    }

    [Fact]
    public async Task AMissingStageFailsBeforeAnythingMoves()
    {
        LibraryRestoreStage stage = await fixture.StageAsync();
        fixture.Restorer().DiscardStage(stage);
        IReadOnlyDictionary<string, string> before = fixture.LiveEntries();

        LibraryRestoreException error = await Assert.ThrowsAsync<LibraryRestoreException>(
            () => fixture.Restorer().ReplaceAsync(stage, CancellationToken.None));

        Assert.Equal(LibraryRestoreIssue.SwapFailed, error.Issue);
        Assert.Equal(before, fixture.LiveEntries());
        Assert.False(File.Exists(fixture.MarkerPath));
    }

    [Fact]
    public async Task ALibraryHeldOpenFailsTheSwapAndChangesNothing()
    {
        if (!OperatingSystem.IsWindows()) return;
        LibraryRestoreStage stage = await fixture.StageAsync();
        IReadOnlyDictionary<string, string> before = fixture.LiveEntries();

        LibraryRestoreException error;
        using (new FileStream(fixture.Target.Paths.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            error = await Assert.ThrowsAsync<LibraryRestoreException>(
                () => fixture.Restorer().ReplaceAsync(stage, CancellationToken.None));
        }

        Assert.Equal(LibraryRestoreIssue.SwapFailed, error.Issue);
        Assert.Equal(before, fixture.LiveEntries());
        Assert.False(File.Exists(fixture.MarkerPath));
        Assert.True(Directory.Exists(RestoreMarker.StagedLibrary(fixture.Target.Paths, stage.StageId)));
    }

    [Fact]
    public async Task AJunctionedRecoveryFolderFailsTheSwap()
    {
        if (!OperatingSystem.IsWindows()) return;
        LibraryRestoreStage stage = await fixture.StageAsync();
        string outside = Path.Combine(fixture.Target.Root, "outside");
        Directory.CreateDirectory(outside);
        Directory.Delete(fixture.Target.Paths.RecoveryRoot, recursive: true);
        RemovalLibrary.CreateJunction(fixture.Target.Paths.RecoveryRoot, outside);

        LibraryRestoreException error = await Assert.ThrowsAsync<LibraryRestoreException>(
            () => fixture.Restorer().ReplaceAsync(stage, CancellationToken.None));

        Assert.Equal(LibraryRestoreIssue.SwapFailed, error.Issue);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        Assert.False(File.Exists(fixture.MarkerPath));
    }

    [Fact]
    public async Task AJunctionedStageFailsTheSwap()
    {
        if (!OperatingSystem.IsWindows()) return;
        LibraryRestoreStage stage = await fixture.StageAsync();
        string staged = RestoreMarker.StagedLibrary(fixture.Target.Paths, stage.StageId);
        string moved = Path.Combine(fixture.Target.Root, "moved-stage");
        Directory.Move(staged, moved);
        RemovalLibrary.CreateJunction(staged, moved);
        IReadOnlyDictionary<string, string> before = fixture.LiveEntries();

        LibraryRestoreException error = await Assert.ThrowsAsync<LibraryRestoreException>(
            () => fixture.Restorer().ReplaceAsync(stage, CancellationToken.None));

        Assert.Equal(LibraryRestoreIssue.SwapFailed, error.Issue);
        Assert.Equal(before, fixture.LiveEntries());
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: Infrastructure tests with `--filter "FullyQualifiedName~LibraryRestorerSwapTests"`.
Expected: the build fails: `RestoreMarker` and `ReplaceAsync` don't exist.

- [ ] **Step 4: Write the implementation**

```csharp
// src/DesktopGuides.Infrastructure/Storage/RestoreMarker.cs
using System.Buffers;
using System.Text.Json;
using DesktopGuides.Core.Paths;

namespace DesktopGuides.Infrastructure.Storage;

internal enum RestoreMarkerPhase { Swapping, Confirmed, RollingBack }

/// <summary>
/// restore.marker beside library/: which stage a swap promoted, whether a
/// prior library was parked, and how far the swap got. Startup reads it
/// before the repository opens.
/// </summary>
internal sealed record RestoreMarker(Guid StageId, bool PriorExists, RestoreMarkerPhase Phase)
{
    public const string FileName = "restore.marker";

    public static string PathFor(ILibraryPaths paths) => Path.Combine(paths.DataRoot, FileName);

    public static string PriorRoot(ILibraryPaths paths, Guid stageId) =>
        Path.Combine(paths.RecoveryRoot, $"restore-{stageId:N}", "library");

    public static string StagedLibrary(ILibraryPaths paths, Guid stageId) =>
        Path.Combine(LibraryRestorer.StageRoot(paths, stageId), LibraryRestorer.LibraryFolderName);

    public static string Unverified(ILibraryPaths paths, Guid stageId) =>
        Path.Combine(paths.DataRoot, LibraryRestorer.StagingFolderName, $"{stageId:N}-unverified");

    /// <summary>Writes a temporary file, flushes it, then renames it over the marker.</summary>
    public void Write(ILibraryPaths paths)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("stageId", StageId.ToString("N"));
            json.WriteBoolean("priorExists", PriorExists);
            json.WriteString("phase", Phase.ToString());
            json.WriteEndObject();
        }
        string target = PathFor(paths);
        string temp = $"{target}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (FileStream file = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(buffer.WrittenSpan);
                file.Flush(flushToDisk: true);
            }
            File.Move(temp, target, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    public static RestoreMarker? Read(ILibraryPaths paths)
    {
        string path = PathFor(paths);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = document.RootElement;
            return new RestoreMarker(
                Guid.ParseExact(root.GetProperty("stageId").GetString()!, "N"),
                root.GetProperty("priorExists").GetBoolean(),
                Enum.Parse<RestoreMarkerPhase>(root.GetProperty("phase").GetString()!));
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or FormatException or
                                      ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("The restore marker is malformed.", error);
        }
    }

    public static void Delete(ILibraryPaths paths) => File.Delete(PathFor(paths));
}
```

Add to `LibraryRestorer`:

```csharp
    /// <summary>
    /// Swaps the staged library in. The caller holds the session lease and has
    /// no repository open. The marker stays: startup confirms or rolls back.
    /// </summary>
    public Task ReplaceAsync(LibraryRestoreStage stage, CancellationToken token) =>
        Task.Run(() => Replace(stage.StageId), token);

    private void Replace(Guid stageId)
    {
        string staged = RestoreMarker.StagedLibrary(paths, stageId);
        string prior = RestoreMarker.PriorRoot(paths, stageId);
        string live = paths.LibraryRoot;
        bool priorExists;
        try
        {
            ManagedPathResolver.RejectFilesystemLinks(staged);
            ManagedPathResolver.RejectFilesystemLinks(live);
            ManagedPathResolver.RejectFilesystemLinks(paths.RecoveryRoot);
            if (!Directory.Exists(staged))
            {
                throw new DirectoryNotFoundException("The staged library is gone.");
            }
            priorExists = Directory.Exists(live);
            new RestoreMarker(stageId, priorExists, RestoreMarkerPhase.Swapping).Write(paths);
        }
        catch (Exception error) when (IsSwapError(error))
        {
            TryDeleteMarker();
            throw new LibraryRestoreException(LibraryRestoreIssue.SwapFailed, inner: error);
        }
        checkpoint(RestoreCheckpoint.MarkerWritten);

        if (priorExists)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(prior)!);
                ManagedPathResolver.RejectFilesystemLinks(Path.GetDirectoryName(prior)!);
                Directory.Move(live, prior);
            }
            catch (Exception error) when (IsSwapError(error))
            {
                DeleteTree(Path.GetDirectoryName(prior)!);
                TryDeleteMarker();
                throw new LibraryRestoreException(LibraryRestoreIssue.SwapFailed, inner: error);
            }
        }
        checkpoint(RestoreCheckpoint.PriorMoved);

        try
        {
            Directory.Move(staged, live);
        }
        catch (Exception error) when (IsSwapError(error))
        {
            // Put the prior root back. If that fails too, the marker stays and
            // startup finishes the rollback.
            if (priorExists)
            {
                Directory.Move(prior, live);
            }
            TryDeleteMarker();
            throw new LibraryRestoreException(LibraryRestoreIssue.SwapFailed, inner: error);
        }
        checkpoint(RestoreCheckpoint.Promoted);
    }

    private static bool IsSwapError(Exception error) =>
        error is IOException or UnauthorizedAccessException or InvalidDataException;

    private void TryDeleteMarker()
    {
        try { RestoreMarker.Delete(paths); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
```

`RejectFilesystemLinks` throws `InvalidDataException` for a link, so a
junction maps to `SwapFailed` before the marker is written.

- [ ] **Step 5: Run the tests to verify they pass**

Run: the Step 3 command, then `--filter "FullyQualifiedName~Restore"`.
Expected: PASS, 10 new tests; the 30 staging tests still pass.

- [ ] **Step 6: Commit**

```bash
git add src/DesktopGuides.Infrastructure/Storage tests/DesktopGuides.Infrastructure.Tests/Restore
git commit -m "feat(storage): swap a staged library in behind a restore marker"
```

### Task 10: Startup recovery: confirm or roll back

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Storage/LibraryRestoreRecovery.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Restore/LibraryRestoreRecoveryTests.cs`

**Interfaces:**
- Consumes: Task 9's `RestoreMarker`, `ReplaceAsync`; Task 6's
  `LibraryOpenIssue.RestoreIncomplete`.
- Produces, for Task 13:
  - `public enum RestoreRecoveryOutcome { None, RolledBack, PendingVerify }`
  - `public static class LibraryRestoreRecovery` with
    `RestoreRecoveryOutcome Run(ILibraryPaths paths, bool verifyRestore)`,
    `void Complete(ILibraryPaths paths)`, `void RollBack(ILibraryPaths paths)`,
    and internal overloads taking `Action<RestoreCheckpoint>`. All three
    throw only `LibraryOpenException(RestoreIncomplete)` for disk errors.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/DesktopGuides.Infrastructure.Tests/Restore/LibraryRestoreRecoveryTests.cs
using DesktopGuides.Core.Backup;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using DesktopGuides.Infrastructure.Tests.FaultInjection;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Restore;

public sealed class LibraryRestoreRecoveryTests : IAsyncLifetime
{
    private RestoreFixture fixture = null!;

    public async Task InitializeAsync() => fixture = await RestoreFixture.CreateAsync();

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private ManagedPathResolver Paths => fixture.Target.Paths;

    private async Task<(LibraryRestoreStage Stage, IReadOnlyDictionary<string, string> Before)> PrepareAsync(bool populated)
    {
        LibraryRestoreStage stage = await fixture.StageAsync();
        if (populated)
        {
            await fixture.AddPriorGameAsync();
        }
        else
        {
            await fixture.ClearTargetAsync();
        }
        return (stage, fixture.LiveEntries());
    }

    private void AssertRolledBackTo(IReadOnlyDictionary<string, string> before)
    {
        Assert.Equal(before, fixture.LiveEntries());
        Assert.False(File.Exists(fixture.MarkerPath));
        fixture.AssertNoStage();
        Assert.Empty(Directory.Exists(Paths.RecoveryRoot)
            ? Directory.EnumerateDirectories(Paths.RecoveryRoot, "restore-*")
            : []);
    }

    [Theory]
    [InlineData(RestoreCheckpoint.MarkerWritten, true)]
    [InlineData(RestoreCheckpoint.MarkerWritten, false)]
    [InlineData(RestoreCheckpoint.PriorMoved, true)]
    [InlineData(RestoreCheckpoint.PriorMoved, false)]
    [InlineData(RestoreCheckpoint.Promoted, true)]
    [InlineData(RestoreCheckpoint.Promoted, false)]
    public async Task AnInterruptedSwapRollsBackOnTheNextStart(RestoreCheckpoint point, bool populated)
    {
        (LibraryRestoreStage stage, IReadOnlyDictionary<string, string> before) = await PrepareAsync(populated);

        await Assert.ThrowsAsync<InjectedFault>(() =>
            fixture.Restorer(FaultFixture.FaultAt(point)).ReplaceAsync(stage, CancellationToken.None));
        RestoreRecoveryOutcome outcome = LibraryRestoreRecovery.Run(Paths, verifyRestore: false);

        Assert.Equal(RestoreRecoveryOutcome.RolledBack, outcome);
        AssertRolledBackTo(before);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AConfirmedRestoreReplacesTheLibraryAndCleansUp(bool populated)
    {
        (LibraryRestoreStage stage, _) = await PrepareAsync(populated);

        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);
        Assert.Equal(RestoreRecoveryOutcome.PendingVerify, LibraryRestoreRecovery.Run(Paths, verifyRestore: true));
        await fixture.Target.RestartAsync();
        LibraryRestoreRecovery.Complete(Paths);

        IReadOnlyDictionary<string, string> source = LibrarySnapshot.Capture(fixture.Source.Library.Paths).Entries;
        Assert.Equal(source, fixture.LiveEntries());
        Assert.False(File.Exists(fixture.MarkerPath));
        fixture.AssertNoStage();
        Assert.Empty(Directory.EnumerateDirectories(Paths.RecoveryRoot, "restore-*"));
    }

    [Fact]
    public async Task APromotedRestoreFoundByALaterStartRollsBack()
    {
        (LibraryRestoreStage stage, IReadOnlyDictionary<string, string> before) = await PrepareAsync(true);
        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);

        Assert.Equal(RestoreRecoveryOutcome.RolledBack, LibraryRestoreRecovery.Run(Paths, verifyRestore: false));

        AssertRolledBackTo(before);
    }

    [Fact]
    public async Task ARestoredLibraryThatWontOpenRollsBack()
    {
        (LibraryRestoreStage stage, IReadOnlyDictionary<string, string> before) = await PrepareAsync(true);
        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);
        Assert.Equal(RestoreRecoveryOutcome.PendingVerify, LibraryRestoreRecovery.Run(Paths, verifyRestore: true));
        File.WriteAllBytes(Paths.DatabasePath, Enumerable.Repeat((byte)0x5A, 4096).ToArray());

        await Assert.ThrowsAsync<LibraryOpenException>(() => fixture.Target.RestartAsync());
        LibraryRestoreRecovery.RollBack(Paths);
        await fixture.Target.RestartAsync();

        AssertRolledBackTo(before);
    }

    [Fact]
    public async Task ACrashWhileCompletingKeepsTheRestoredLibrary()
    {
        (LibraryRestoreStage stage, _) = await PrepareAsync(true);
        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);
        LibraryRestoreRecovery.Run(Paths, verifyRestore: true);
        await fixture.Target.RestartAsync();

        Assert.Throws<InjectedFault>(() =>
            LibraryRestoreRecovery.Complete(Paths, FaultFixture.FaultAt(RestoreCheckpoint.Confirmed)));
        RestoreRecoveryOutcome outcome = LibraryRestoreRecovery.Run(Paths, verifyRestore: false);

        Assert.Equal(RestoreRecoveryOutcome.None, outcome);
        Assert.Equal("2", fixture.Target.Scalar("SELECT COUNT(*) FROM Games"));
        Assert.False(File.Exists(fixture.MarkerPath));
        Assert.Empty(Directory.EnumerateDirectories(Paths.RecoveryRoot, "restore-*"));
    }

    [Theory]
    [InlineData(RestoreCheckpoint.RolledAside, true)]
    [InlineData(RestoreCheckpoint.RolledAside, false)]
    [InlineData(RestoreCheckpoint.PriorReturned, true)]
    [InlineData(RestoreCheckpoint.PriorReturned, false)]
    public async Task ACrashDuringRollbackFinishesOnTheNextStart(RestoreCheckpoint point, bool populated)
    {
        (LibraryRestoreStage stage, IReadOnlyDictionary<string, string> before) = await PrepareAsync(populated);
        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);

        Assert.Throws<InjectedFault>(() =>
            LibraryRestoreRecovery.Run(Paths, verifyRestore: false, FaultFixture.FaultAt(point)));
        RestoreRecoveryOutcome outcome = LibraryRestoreRecovery.Run(Paths, verifyRestore: false);

        Assert.Equal(RestoreRecoveryOutcome.RolledBack, outcome);
        AssertRolledBackTo(before);
    }

    [Fact]
    public async Task ACrashAfterThePriorRootReturnsKeepsIt()
    {
        (LibraryRestoreStage stage, IReadOnlyDictionary<string, string> before) = await PrepareAsync(true);
        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);
        Assert.Throws<InjectedFault>(() =>
            LibraryRestoreRecovery.Run(Paths, verifyRestore: false, FaultFixture.FaultAt(RestoreCheckpoint.PriorReturned)));

        // Twice more: each start must leave the returned prior root in place.
        LibraryRestoreRecovery.Run(Paths, verifyRestore: false);
        LibraryRestoreRecovery.Run(Paths, verifyRestore: false);

        AssertRolledBackTo(before);
    }

    [Fact]
    public async Task AMissingParkedLibraryIsIncomplete()
    {
        (LibraryRestoreStage stage, _) = await PrepareAsync(true);
        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);
        Directory.Delete(RestoreMarker.PriorRoot(Paths, stage.StageId), recursive: true);
        Directory.Delete(Paths.LibraryRoot, recursive: true);

        LibraryOpenException error = Assert.Throws<LibraryOpenException>(() =>
            LibraryRestoreRecovery.Run(Paths, verifyRestore: false));

        Assert.Equal(LibraryOpenIssue.RestoreIncomplete, error.Issue);
        Assert.True(File.Exists(fixture.MarkerPath));
    }

    [Fact]
    public void AMalformedMarkerIsIncomplete()
    {
        File.WriteAllText(fixture.MarkerPath, "not json");

        LibraryOpenException error = Assert.Throws<LibraryOpenException>(() =>
            LibraryRestoreRecovery.Run(Paths, verifyRestore: false));

        Assert.Equal(LibraryOpenIssue.RestoreIncomplete, error.Issue);
    }

    [Fact]
    public async Task WithoutAMarkerLeftoversAreDeletedAndMigrationCopiesKept()
    {
        await fixture.StageAsync();
        string parked = Path.Combine(Paths.RecoveryRoot, $"restore-{Guid.NewGuid():N}", "library");
        Directory.CreateDirectory(parked);
        string migrationCopy = Path.Combine(Paths.RecoveryRoot, "library-v3-20261010000000-x.sqlite");
        File.WriteAllText(migrationCopy, "copy");

        Assert.Equal(RestoreRecoveryOutcome.None, LibraryRestoreRecovery.Run(Paths, verifyRestore: false));

        fixture.AssertNoStage();
        Assert.False(Directory.Exists(Path.GetDirectoryName(parked)));
        Assert.True(File.Exists(migrationCopy));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: Infrastructure tests with `--filter "FullyQualifiedName~LibraryRestoreRecoveryTests"`.
Expected: the build fails: `LibraryRestoreRecovery` doesn't exist.

- [ ] **Step 3: Write the implementation**

```csharp
// src/DesktopGuides.Infrastructure/Storage/LibraryRestoreRecovery.cs
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Paths;

namespace DesktopGuides.Infrastructure.Storage;

public enum RestoreRecoveryOutcome { None, RolledBack, PendingVerify }

/// <summary>
/// Finishes or undoes a restore from what is on disk, before the repository
/// opens. Only the session that swapped (verifyRestore) may keep a promoted
/// library; any later start rolls it back. Every step is safe to repeat.
/// </summary>
public static class LibraryRestoreRecovery
{
    public static RestoreRecoveryOutcome Run(ILibraryPaths paths, bool verifyRestore) =>
        Run(paths, verifyRestore, _ => { });

    internal static RestoreRecoveryOutcome Run(
        ILibraryPaths paths, bool verifyRestore, Action<RestoreCheckpoint> checkpoint) =>
        Guarded(() =>
        {
            RestoreMarker? marker = RestoreMarker.Read(paths);
            if (marker is null)
            {
                DeleteLeftovers(paths);
                return RestoreRecoveryOutcome.None;
            }
            switch (marker.Phase)
            {
                case RestoreMarkerPhase.Confirmed:
                    FinishComplete(paths, marker);
                    return RestoreRecoveryOutcome.None;
                case RestoreMarkerPhase.RollingBack:
                    FinishRollBack(paths, marker, checkpoint);
                    return RestoreRecoveryOutcome.RolledBack;
            }
            if (Directory.Exists(RestoreMarker.StagedLibrary(paths, marker.StageId)))
            {
                // The swap never promoted the stage: return a parked prior root.
                string prior = RestoreMarker.PriorRoot(paths, marker.StageId);
                if (marker.PriorExists && Directory.Exists(prior))
                {
                    if (Directory.Exists(paths.LibraryRoot))
                    {
                        throw new InvalidDataException("Both the live and the parked library exist.");
                    }
                    Directory.Move(prior, paths.LibraryRoot);
                }
                RestoreMarker.Delete(paths);
                DeleteLeftovers(paths);
                return RestoreRecoveryOutcome.RolledBack;
            }
            if (verifyRestore)
            {
                return RestoreRecoveryOutcome.PendingVerify;
            }
            BeginRollBack(paths, marker, checkpoint);
            return RestoreRecoveryOutcome.RolledBack;
        });

    /// <summary>The restored library opened: keep it.</summary>
    public static void Complete(ILibraryPaths paths) => Complete(paths, _ => { });

    internal static void Complete(ILibraryPaths paths, Action<RestoreCheckpoint> checkpoint) =>
        Guarded(() =>
        {
            if (RestoreMarker.Read(paths) is not { } marker)
            {
                return 0;
            }
            // Confirmed first, so a crash below can only finish the cleanup.
            RestoreMarker confirmed = marker with { Phase = RestoreMarkerPhase.Confirmed };
            confirmed.Write(paths);
            checkpoint(RestoreCheckpoint.Confirmed);
            FinishComplete(paths, confirmed);
            return 0;
        });

    /// <summary>The restored library didn't open: put the prior one back.</summary>
    public static void RollBack(ILibraryPaths paths) => RollBack(paths, _ => { });

    internal static void RollBack(ILibraryPaths paths, Action<RestoreCheckpoint> checkpoint) =>
        Guarded(() =>
        {
            if (RestoreMarker.Read(paths) is { } marker)
            {
                BeginRollBack(paths, marker, checkpoint);
            }
            return 0;
        });

    private static void BeginRollBack(ILibraryPaths paths, RestoreMarker marker, Action<RestoreCheckpoint> checkpoint)
    {
        // RollingBack first, so a crash below never moves a returned prior root aside.
        RestoreMarker rolling = marker with { Phase = RestoreMarkerPhase.RollingBack };
        rolling.Write(paths);
        FinishRollBack(paths, rolling, checkpoint);
    }

    private static void FinishComplete(ILibraryPaths paths, RestoreMarker marker)
    {
        LibraryRestorer.DeleteTree(Path.GetDirectoryName(RestoreMarker.PriorRoot(paths, marker.StageId))!);
        LibraryRestorer.DeleteTree(LibraryRestorer.StageRoot(paths, marker.StageId));
        RestoreMarker.Delete(paths);
    }

    private static void FinishRollBack(ILibraryPaths paths, RestoreMarker marker, Action<RestoreCheckpoint> checkpoint)
    {
        string prior = RestoreMarker.PriorRoot(paths, marker.StageId);
        string unverified = RestoreMarker.Unverified(paths, marker.StageId);
        bool priorParked = marker.PriorExists && Directory.Exists(prior);
        // library/ is the promoted root while the prior is still parked, or when there was none.
        if (Directory.Exists(paths.LibraryRoot) && (priorParked || !marker.PriorExists))
        {
            ManagedPathResolver.RejectFilesystemLinks(paths.LibraryRoot);
            LibraryRestorer.DeleteTree(unverified);
            Directory.Move(paths.LibraryRoot, unverified);
        }
        checkpoint(RestoreCheckpoint.RolledAside);
        if (priorParked)
        {
            Directory.Move(prior, paths.LibraryRoot);
        }
        else if (marker.PriorExists && !Directory.Exists(paths.LibraryRoot))
        {
            throw new InvalidDataException("The parked library is missing.");
        }
        checkpoint(RestoreCheckpoint.PriorReturned);
        RestoreMarker.Delete(paths);
        DeleteLeftovers(paths);
    }

    // Only restore folders: .recovery also holds migration copies, which stay.
    private static void DeleteLeftovers(ILibraryPaths paths)
    {
        string staging = Path.Combine(paths.DataRoot, LibraryRestorer.StagingFolderName);
        if (Directory.Exists(staging))
        {
            foreach (string folder in Directory.EnumerateDirectories(staging))
            {
                LibraryRestorer.DeleteTree(folder);
            }
        }
        if (Directory.Exists(paths.RecoveryRoot))
        {
            foreach (string folder in Directory.EnumerateDirectories(paths.RecoveryRoot, "restore-*"))
            {
                LibraryRestorer.DeleteTree(folder);
            }
        }
    }

    private static T Guarded<T>(Func<T> work)
    {
        try
        {
            return work();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new LibraryOpenException(LibraryOpenIssue.RestoreIncomplete, inner: error);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: the Step 2 command, then the whole Infrastructure suite.
Expected: PASS, 19 new tests. The whole suite passes.

If `AConfirmedRestoreReplacesTheLibraryAndCleansUp` differs only in
`fs:.recovery` or empty `library/.staging`-style folders, compare the
`db:` entries and the `fs:library/content` and `fs:library/artwork`
entries instead, and record that as a ruling; the rows and managed files
are what must match.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Infrastructure/Storage/LibraryRestoreRecovery.cs tests/DesktopGuides.Infrastructure.Tests/Restore
git commit -m "feat(storage): confirm or roll back a restore at startup"
```

### Task 11: PR b documentation, verification record and PR

**Files:**
- Modify: `docs/p1/t20-2-export-restore-design.md` (status line, verification record)
- Modify: `docs/p1/implementation-plan.md`, `docs/progress.md`

- [ ] **Step 1: Record the verification**

Add `### PR b: the restore engine` to the design's verification record:
the Core and Infrastructure counts from the host run and the CI run ID;
the staging, swap and recovery cell counts (Tasks 7–10: 19, 11, 10 and
19); the NTFS cells that ran on Windows; rulings 1–4, 7, 9 and 13; and that
nothing is wired into the app yet, so there is no installed smoke and no
screenshot.

- [ ] **Step 2: Update the status pages**

Design status: "Status: PR a merged in #<a>; PR b (restore engine) in
review; PR c planned." Update the T20.2 paragraph in
`implementation-plan.md` and the T20.2 row in `progress.md` to match.

- [ ] **Step 3: Check, commit and open PR b**

Run: `git diff --check`
Expected: no output.

```bash
git add docs
git commit -m "docs(p1): record T20.2 PR b verification"
```

Push and open the PR. The body names T20.2 PR b, the prerequisites (PR a:
merged; T15.4's `LibrarySnapshot`), and the outcome: a backup can be
staged and fully checked, swapped in behind a marker, and confirmed or
rolled back at startup, with no UI yet. There is no UI change, so there
is no screenshot. It ends with the Claude Code line.

---

# PR c: restore UI and the in-session swap

### Task 12: The Restore expander and staging

TDD exception: WinUI wiring over Tasks 6–10. It is verified by the
Production build here and by the installed smokes in Task 15, which run
first against this task's commit and fail on the missing Replace flow.

**Files:**
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (below `ExportSettingsCard`)
- Modify: `src/DesktopGuides.Production/ShellWindow.Backup.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs`
  (`InitializeCoreAsync`, `CloseWhenIdleAsync`)

**Interfaces:**
- Consumes: `LibraryRestorer` (Tasks 7–9), Task 6's copy, Task 3's
  `SetLibraryBusy`, `backupCancel`, `backupTask`, Task 4's `PauseForTestAsync`.
- Produces, for Task 13: fields `restorer` (`LibraryRestorer?`) and
  `stagedBackup` (`LibraryRestoreStage?`); `CurrentCountsAsync() → Task<(int Games, int Guides)>`;
  `DiscardStagedBackup()`; AutomationIds `RestoreSettingsExpander`,
  `ChooseBackupButton`, `RestoreProgress`, `RestoreCancelButton`,
  `RestoreStatus`, `RestoreMadeCard`, `RestoreHoldsCard`,
  `RestoreCurrentCard`, `ReplaceLibraryButton`, `DiscardStageButton`.

- [ ] **Step 1: Add the expander**

In `ShellWindow.xaml`, directly after the `ExportSettingsCard` element:

```xml
                        <toolkit:SettingsExpander
                            x:Name="RestoreSettingsExpander"
                            Header="Restore from backup"
                            Description="Replace this library with one from a backup file."
                            HorizontalAlignment="Stretch"
                            AutomationProperties.AutomationId="RestoreSettingsExpander"
                            AutomationProperties.Name="Restore from backup. Replace this library with one from a backup file.">
                            <toolkit:SettingsExpander.HeaderIcon>
                                <FontIcon Glyph="&#xE777;"
                                          AutomationProperties.AccessibilityView="Raw" />
                            </toolkit:SettingsExpander.HeaderIcon>
                            <Button x:Name="ChooseBackupButton"
                                    Content="Choose backup…"
                                    IsEnabled="False"
                                    Click="ChooseBackupClicked"
                                    AutomationProperties.AutomationId="ChooseBackupButton" />
                            <toolkit:SettingsExpander.Items>
                                <toolkit:SettingsCard x:Name="RestoreProgressCard"
                                                      Header="Copying backup…"
                                                      Visibility="Collapsed"
                                                      AutomationProperties.AutomationId="RestoreProgressCard">
                                    <StackPanel Orientation="Horizontal"
                                                Spacing="{StaticResource DesktopGuidesSpacing8}">
                                        <ProgressBar x:Name="RestoreProgress"
                                                     Width="160"
                                                     VerticalAlignment="Center"
                                                     AutomationProperties.AutomationId="RestoreProgress" />
                                        <Button x:Name="RestoreCancelButton"
                                                Content="Cancel"
                                                Click="RestoreCancelClicked"
                                                AutomationProperties.AutomationId="RestoreCancelButton" />
                                    </StackPanel>
                                </toolkit:SettingsCard>
                                <InfoBar x:Name="RestoreStatus"
                                         IsOpen="False"
                                         IsClosable="False"
                                         Severity="Error"
                                         AutomationProperties.AutomationId="RestoreStatus"
                                         AutomationProperties.LiveSetting="Assertive" />
                                <toolkit:SettingsCard x:Name="RestoreMadeCard"
                                                      Visibility="Collapsed"
                                                      AutomationProperties.AutomationId="RestoreMadeCard" />
                                <toolkit:SettingsCard x:Name="RestoreHoldsCard"
                                                      Visibility="Collapsed"
                                                      AutomationProperties.AutomationId="RestoreHoldsCard" />
                                <toolkit:SettingsCard x:Name="RestoreCurrentCard"
                                                      Visibility="Collapsed"
                                                      AutomationProperties.AutomationId="RestoreCurrentCard" />
                                <toolkit:SettingsCard x:Name="RestoreActionsCard"
                                                      Visibility="Collapsed"
                                                      AutomationProperties.AutomationId="RestoreActionsCard">
                                    <StackPanel Orientation="Horizontal"
                                                Spacing="{StaticResource DesktopGuidesSpacing8}">
                                        <Button x:Name="DiscardStageButton"
                                                Content="Discard"
                                                Click="DiscardStageClicked"
                                                AutomationProperties.AutomationId="DiscardStageButton" />
                                        <Button x:Name="ReplaceLibraryButton"
                                                Content="Replace library…"
                                                Style="{ThemeResource AccentButtonStyle}"
                                                Click="ReplaceLibraryClicked"
                                                AutomationProperties.AutomationId="ReplaceLibraryButton" />
                                    </StackPanel>
                                </toolkit:SettingsCard>
                            </toolkit:SettingsExpander.Items>
                        </toolkit:SettingsExpander>
```

- [ ] **Step 2: Add staging to the partial**

Add to `ShellWindow.Backup.cs` (`using DesktopGuides.Core.Backup;` is
already there):

```csharp
    private LibraryRestorer? restorer;
    private LibraryRestoreStage? stagedBackup;

    private async void ChooseBackupClicked(object sender, RoutedEventArgs args)
    {
        if (libraryBusy || !ready || closeRequested || restorer is not LibraryRestorer active)
        {
            return;
        }
        string? path;
        try
        {
            FileOpenPicker picker = new(AppWindow.Id);
            picker.FileTypeFilter.Add(".zip");
            path = (await picker.PickSingleFileAsync())?.Path;
        }
        catch (Exception)
        {
            ShowWarningStatus(LibraryBackupMessages.PickerFailed);
            return;
        }
        if (path is null || closeRequested || libraryBusy)
        {
            return;
        }
        backupTask = StageBackupAsync(active, path);
        await backupTask;
    }

    private async Task StageBackupAsync(LibraryRestorer active, string path)
    {
        DiscardStagedBackup();
        using CancellationTokenSource cancel = new();
        backupCancel = cancel;
        SetLibraryBusy(true);
        ChooseBackupButton.IsEnabled = false;
        ExportBackupButton.IsEnabled = false;
        RestoreStatus.IsOpen = false;
        RestoreSettingsExpander.IsExpanded = true;
        RestoreCancelButton.IsEnabled = true;
        ShowRestoreProgress(new LibraryRestoreProgress(LibraryRestorePhase.Copying, 0, 0));
        RestoreCancelButton.Focus(FocusState.Programmatic);
        try
        {
            await PauseForTestAsync("Backup", cancel.Token);
            Progress<LibraryRestoreProgress> progress = new(report =>
            {
                if (ReferenceEquals(backupCancel, cancel) && !closeRequested)
                {
                    ShowRestoreProgress(report);
                }
            });
            LibraryRestoreStage stage = await active.StageAsync(path, progress, cancel.Token);
            if (closeRequested)
            {
                active.DiscardStage(stage);
                return;
            }
            stagedBackup = stage;
            await ShowStagedBackupAsync(stage);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            if (!closeRequested)
            {
                ShowRestoreStatus(LibraryBackupMessages.RestoreCanceled, InfoBarSeverity.Informational);
            }
        }
        catch (LibraryRestoreException error)
        {
            if (!closeRequested)
            {
                ShowRestoreStatus(
                    LibraryBackupMessages.RestoreFailed(error.Issue, error.Titles, error.BytesNeeded),
                    InfoBarSeverity.Error);
            }
        }
        catch (Exception)
        {
            if (!closeRequested)
            {
                ShowRestoreStatus(
                    LibraryBackupMessages.RestoreFailed(LibraryRestoreIssue.SourceUnavailable, [], null),
                    InfoBarSeverity.Error);
            }
        }
        finally
        {
            backupCancel = null;
            RestoreProgressCard.Visibility = Visibility.Collapsed;
            if (!closeRequested)
            {
                SetLibraryBusy(false);
                ChooseBackupButton.IsEnabled = ready;
                ExportBackupButton.IsEnabled = ready;
                (stagedBackup is null ? (Control)ChooseBackupButton : ReplaceLibraryButton)
                    .Focus(FocusState.Programmatic);
            }
        }
    }

    private void ShowRestoreProgress(LibraryRestoreProgress report)
    {
        string label = LibraryBackupMessages.RestorePhase(report.Phase);
        RestoreProgressCard.Header = label;
        RestoreProgressCard.Visibility = Visibility.Visible;
        RestoreProgress.IsIndeterminate = report.BytesTotal <= 0;
        RestoreProgress.Value = report.BytesTotal > 0 ? 100.0 * report.BytesDone / report.BytesTotal : 0;
        AutomationProperties.SetName(RestoreProgress, label);
    }

    private void ShowRestoreStatus(string message, InfoBarSeverity severity)
    {
        RestoreStatus.Message = message;
        RestoreStatus.Severity = severity;
        AutomationProperties.SetName(RestoreStatus, message);
        RestoreStatus.IsOpen = true;
        AnnounceStatus(message);
    }

    private async Task ShowStagedBackupAsync(LibraryRestoreStage stage)
    {
        (int games, int guides) = await CurrentCountsAsync();
        ShowDetail(RestoreMadeCard,
            LibraryBackupMessages.BackupMade(stage.CreatedUtc.ToLocalTime().DateTime, stage.AppVersion));
        ShowDetail(RestoreHoldsCard, LibraryBackupMessages.BackupHolds(stage.Games, stage.Guides, stage.Bytes));
        ShowDetail(RestoreCurrentCard, LibraryBackupMessages.LibraryHas(games, guides));
        RestoreActionsCard.Visibility = Visibility.Visible;
        DiscardStageButton.IsEnabled = true;
        ReplaceLibraryButton.IsEnabled = true;
        AnnounceStatus("Backup checked. Review it, then replace your library or discard the backup.");
    }

    private static void ShowDetail(SettingsCard card, string text)
    {
        card.Header = text;
        AutomationProperties.SetName(card, text);
        card.Visibility = Visibility.Visible;
    }

    private async Task<(int Games, int Guides)> CurrentCountsAsync()
    {
        if (repository is not SqliteLibraryRepository library)
        {
            return (0, 0);
        }
        try
        {
            IReadOnlyList<LibraryGameSummary> games = await library.ListGameSummariesAsync();
            return (games.Count, games.Sum(game => game.GuideCount));
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    private void RestoreCancelClicked(object sender, RoutedEventArgs args)
    {
        RestoreCancelButton.IsEnabled = false;
        backupCancel?.Cancel();
    }

    private void DiscardStageClicked(object sender, RoutedEventArgs args)
    {
        if (libraryBusy)
        {
            return;
        }
        DiscardStagedBackup();
        ChooseBackupButton.Focus(FocusState.Programmatic);
        AnnounceStatus("Backup discarded.");
    }

    // Deletes the staged copy and hides its details.
    private void DiscardStagedBackup()
    {
        if (stagedBackup is LibraryRestoreStage stage && restorer is LibraryRestorer active)
        {
            active.DiscardStage(stage);
        }
        stagedBackup = null;
        RestoreMadeCard.Visibility = Visibility.Collapsed;
        RestoreHoldsCard.Visibility = Visibility.Collapsed;
        RestoreCurrentCard.Visibility = Visibility.Collapsed;
        RestoreActionsCard.Visibility = Visibility.Collapsed;
    }
```

Add a placeholder `ReplaceLibraryClicked` so the XAML builds; Task 13
replaces it:

```csharp
    private void ReplaceLibraryClicked(object sender, RoutedEventArgs args)
    {
    }
```

Add `using CommunityToolkit.WinUI.Controls;` for `SettingsCard`.

- [ ] **Step 3: Create the restorer and discard on close**

In `InitializeCoreAsync`, next to `exporter = CreateExporter(...)`, add
`restorer = new LibraryRestorer(paths);`, and next to
`ExportBackupButton.IsEnabled = true;` add `ChooseBackupButton.IsEnabled = true;`
(and the matching `false` at the top of the `try`).

In `CloseWhenIdleAsync`, after the `Task.WhenAll` wait and before
disposing the repository, add:

```csharp
                    // A stage the user didn't replace with; startup would delete it anyway.
                    if (stagedBackup is LibraryRestoreStage stage && restorer is LibraryRestorer active)
                    {
                        active.DiscardStage(stage);
                    }
```

- [ ] **Step 4: Build**

Run: the Production build.
Expected: `Build succeeded`, 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Production
git commit -m "feat(shell): check a backup in Settings before restoring it"
```

### Task 13: Replace: confirmation, the in-session swap and startup recovery

TDD exception, as Task 12: verified by the Task 15 smokes.

**Files:**
- Create: `src/DesktopGuides.Production/ReplaceLibraryDialog.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.Backup.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (`InitializeCoreAsync`)

**Interfaces:**
- Consumes: `LibraryRestorer.ReplaceAsync`, `LibraryRestoreRecovery` (Tasks 9–10);
  Task 6's `ReplaceTitle`, `ReplaceBody`, `Restored`, `RestoreKept`.
- Produces: `InitializeCoreAsync(bool verifyRestore = false)`; AutomationId
  `ReplaceLibraryDialog`; statuses "Library restored: …",
  `RestoreKept`, and `SwapFailed`'s message.

- [ ] **Step 1: Add the dialog**

```csharp
// src/DesktopGuides.Production/ReplaceLibraryDialog.cs
using DesktopGuides.Core.Backup;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

/// <summary>Confirms replacing the library. Cancel is the default, so Enter and Escape both cancel.</summary>
internal static class ReplaceLibraryDialog
{
    public static ContentDialog Create(int currentGames, int currentGuides, LibraryRestoreStage stage, XamlRoot root)
    {
        TextBlock message = new()
        {
            Text = LibraryBackupMessages.ReplaceBody(currentGames, currentGuides, stage.Games, stage.Guides),
            Style = (Style)Application.Current.Resources["DesktopGuidesBodyStyle"],
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(message, "ReplaceLibraryMessage");
        ContentDialog dialog = new()
        {
            Title = LibraryBackupMessages.ReplaceTitle,
            Content = message,
            PrimaryButtonText = "Replace",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root,
        };
        AutomationProperties.SetAutomationId(dialog, "ReplaceLibraryDialog");
        return dialog;
    }
}
```

- [ ] **Step 2: Replace the placeholder handler with the swap**

In `ShellWindow.Backup.cs`, replace `ReplaceLibraryClicked` and add the
fields and methods:

```csharp
    // Set by a swap for the next initialization to report.
    private string? pendingRestoreError;
    private LibraryRestoreStage? pendingRestore;

    private async void ReplaceLibraryClicked(object sender, RoutedEventArgs args)
    {
        if (libraryBusy || !ready || closeRequested ||
            stagedBackup is not LibraryRestoreStage stage || restorer is not LibraryRestorer active)
        {
            return;
        }
        (int games, int guides) = await CurrentCountsAsync();
        ContentDialog dialog = ReplaceLibraryDialog.Create(games, guides, stage, Navigation.XamlRoot);
        DialogSurface.Apply(dialog, EffectiveMaterial, DialogTheme);
        activeRemoveDialog = dialog;
        ContentDialogResult result;
        try
        {
            result = await dialog.ShowAsync();
        }
        finally
        {
            activeRemoveDialog = null;
        }
        if (result != ContentDialogResult.Primary || closeRequested || libraryBusy)
        {
            if (!closeRequested)
            {
                ReplaceLibraryButton.Focus(FocusState.Programmatic);
            }
            return;
        }
        backupTask = ReplaceLibraryAsync(active, stage);
        await backupTask;
    }

    // Closes everything that uses the library, swaps it, and opens the
    // restored one through startup, which confirms it or rolls it back.
    private async Task ReplaceLibraryAsync(LibraryRestorer active, LibraryRestoreStage stage)
    {
        SetLibraryBusy(true);
        foreach (Control control in new Control[]
                 { ReplaceLibraryButton, DiscardStageButton, ChooseBackupButton, ExportBackupButton })
        {
            control.IsEnabled = false;
        }
        ShowRouteProgress("Restoring library…");
        try
        {
            // Queued work finishes first; ready = false then stops new work.
            await RunNavigationAsync(() => Task.CompletedTask);
            ready = false;
            CancelReaderLoad();
            refreshCancel?.Cancel();
            try
            {
                await refreshTask;
            }
            catch (Exception)
            {
                // A cancelled refresh; nothing to report during a restore.
            }
            await CloseReaderSessionAsync();
            await DisposeProgressTrackingAsync();
            if (repository is not null)
            {
                await repository.DisposeAsync();
                repository = null;
            }
            stagedBackup = null;
            DiscardStagedDetails();
            navigator.ResetToLibrary();
            try
            {
                await active.ReplaceAsync(stage, CancellationToken.None);
                pendingRestore = stage;
            }
            catch (LibraryRestoreException error)
            {
                pendingRestoreError = LibraryBackupMessages.RestoreFailed(error.Issue, error.Titles, error.BytesNeeded);
                active.DiscardStage(stage);
            }
            initializationTask = InitializeCoreAsync(verifyRestore: pendingRestore is not null);
            await initializationTask;
        }
        finally
        {
            pendingRestore = null;
            pendingRestoreError = null;
            if (!closeRequested)
            {
                SetLibraryBusy(false);
            }
        }
    }

    // Hides the staged details without touching a stage being swapped in.
    private void DiscardStagedDetails()
    {
        RestoreMadeCard.Visibility = Visibility.Collapsed;
        RestoreHoldsCard.Visibility = Visibility.Collapsed;
        RestoreCurrentCard.Visibility = Visibility.Collapsed;
        RestoreActionsCard.Visibility = Visibility.Collapsed;
        RestoreStatus.IsOpen = false;
    }
```

Make `DiscardStagedBackup` call `DiscardStagedDetails()` instead of its
four `Visibility` lines.

- [ ] **Step 3: Run recovery in `InitializeCoreAsync`**

Change the signature to `private async Task InitializeCoreAsync(bool verifyRestore = false)`.
Replace the block from `repository = new SqliteLibraryRepository(paths);`
through `await repository.InitializeAsync();` with:

```csharp
            // T20.2: finish or undo a restore before anything opens the library.
            RestoreRecoveryOutcome recovery = await Task.Run(
                () => LibraryRestoreRecovery.Run(paths, verifyRestore));
            repository = new SqliteLibraryRepository(paths);
            artwork = new ManagedArtworkStore(paths);
            try
            {
                await repository.InitializeAsync();
            }
            catch (Exception) when (recovery == RestoreRecoveryOutcome.PendingVerify)
            {
                // The restored library didn't open: put the previous one back.
                await repository.DisposeAsync();
                await Task.Run(() => LibraryRestoreRecovery.RollBack(paths));
                recovery = RestoreRecoveryOutcome.RolledBack;
                repository = new SqliteLibraryRepository(paths);
                await repository.InitializeAsync();
            }
            if (recovery == RestoreRecoveryOutcome.PendingVerify)
            {
                await Task.Run(() => LibraryRestoreRecovery.Complete(paths));
            }
```

After `await WarnIfRuntimeMissingAsync(sweptRoot);`, add:

```csharp
            if (pendingRestoreError is string swapError)
            {
                ShowErrorStatus(swapError);
            }
            else if (recovery == RestoreRecoveryOutcome.RolledBack)
            {
                ShowWarningStatus(LibraryBackupMessages.RestoreKept);
            }
            else if (recovery == RestoreRecoveryOutcome.PendingVerify && pendingRestore is { } restored)
            {
                ShowStatus(LibraryBackupMessages.Restored(restored.Games, restored.Guides),
                    InfoBarSeverity.Success, true, false);
            }
```

`LibraryRestoreRecovery` throws `LibraryOpenException(RestoreIncomplete)`
when it can't proceed, which the existing catch shows on the Library
unavailable page; **Try again** re-runs `InitializeCoreAsync()` with
`verifyRestore: false`.

- [ ] **Step 4: Build**

Run: the Production build.
Expected: `Build succeeded`, 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Production
git commit -m "feat(shell): replace the library with a checked backup"
```

### Task 14: Seed commands for the restore smokes

TDD exception: test tooling. Each command is checked by its first use in
Task 15.

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs`

**Interfaces:**
- Consumes: public `LibraryExporter`, `LibraryRestorer`,
  `LibraryArchiveManifest`.
- Produces (the app must be closed for each):
  - `export-backup <dataRoot> <zip>`: exports the library; prints the path.
  - `damage-backup <zip> <out> truncate|missing-artwork`: `truncate`
    keeps the first half of the bytes; `missing-artwork` drops every
    `library/artwork/` entry and rewrites the manifest to match, so only
    the reference check can catch it.
  - `interrupt-restore <dataRoot> <zip>`: stages and swaps the backup in,
    leaving the marker, as a crash right after the swap would.
  - `describe-library <dataRoot>`: prints
    `{"games":n,"guides":n,"marker":bool,"stages":n,"parked":n}`.

- [ ] **Step 1: Add the commands**

Before the final `seed` dispatch in `Program.cs`:

```csharp
if (args.Length == 3 && args[0] == "export-backup")
{
    ManagedPathResolver exportPaths = new(args[1]);
    await using SqliteLibraryRepository exportRepository = new(exportPaths);
    await exportRepository.InitializeAsync();
    LibraryExportResult exported = await new LibraryExporter(exportRepository, exportPaths,
            new LibraryExportOptions("1.0.0.0", "seed", []))
        .ExportAsync(args[2], true, null, CancellationToken.None);
    Console.WriteLine(exported.Path);
    return 0;
}

if (args.Length == 4 && args[0] == "damage-backup" && args[3] is "truncate" or "missing-artwork")
{
    if (args[3] == "truncate")
    {
        byte[] whole = File.ReadAllBytes(args[1]);
        File.WriteAllBytes(args[2], whole[..(whole.Length / 2)]);
        return 0;
    }
    List<(string Name, byte[] Bytes)> kept = [];
    LibraryArchiveManifest original;
    using (ZipArchive source = ZipFile.OpenRead(args[1]))
    {
        using (Stream stream = source.Entries[0].Open())
        using (MemoryStream json = new())
        {
            stream.CopyTo(json);
            original = LibraryArchiveManifest.Parse(json.ToArray());
        }
        foreach (ZipArchiveEntry entry in source.Entries.Skip(1))
        {
            if (entry.FullName.StartsWith("library/artwork/", StringComparison.Ordinal)) continue;
            using Stream stream = entry.Open();
            using MemoryStream bytes = new();
            stream.CopyTo(bytes);
            kept.Add((entry.FullName, bytes.ToArray()));
        }
    }
    if (kept.Count == original.Entries.Count)
    {
        throw new InvalidOperationException("The backup has no artwork to drop.");
    }
    LibraryArchiveManifest edited = original with
    {
        Entries = kept.Select(file => new LibraryArchiveEntry(
            file.Name, file.Bytes.Length, Convert.ToHexStringLower(SHA256.HashData(file.Bytes)))).ToArray()
    };
    File.Delete(args[2]);
    using (ZipArchive output = ZipFile.Open(args[2], ZipArchiveMode.Create))
    {
        foreach ((string name, byte[] bytes) in kept.Prepend((LibraryArchiveManifest.EntryName, LibraryArchiveManifest.Write(edited))))
        {
            using Stream stream = output.CreateEntry(name).Open();
            stream.Write(bytes);
        }
    }
    return 0;
}

if (args.Length == 3 && args[0] == "interrupt-restore")
{
    ManagedPathResolver restorePaths = new(args[1]);
    LibraryRestorer interrupted = new(restorePaths);
    LibraryRestoreStage stage = await interrupted.StageAsync(args[2], null, CancellationToken.None);
    await interrupted.ReplaceAsync(stage, CancellationToken.None);
    Console.WriteLine(stage.StageId.ToString("N"));
    return 0;
}

if (args.Length == 2 && args[0] == "describe-library")
{
    ManagedPathResolver describedPaths = new(args[1]);
    long Count(string sql)
    {
        if (!File.Exists(describedPaths.DatabasePath)) return 0;
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = describedPaths.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }
    string staging = Path.Combine(args[1], LibraryRestorer.StagingFolderName);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        games = Count("SELECT COUNT(*) FROM Games"),
        guides = Count("SELECT COUNT(*) FROM Guides"),
        marker = File.Exists(Path.Combine(args[1], "restore.marker")),
        stages = Directory.Exists(staging) ? Directory.EnumerateDirectories(staging).Count() : 0,
        parked = Directory.Exists(describedPaths.RecoveryRoot)
            ? Directory.EnumerateDirectories(describedPaths.RecoveryRoot, "restore-*").Count()
            : 0
    }));
    return 0;
}
```

If `seed-import`'s library has no artwork, `damage-backup … missing-artwork`
throws; Task 15 then seeds `seed-linked-game` into that library before
exporting, which stores provider artwork for one game.

- [ ] **Step 2: Build**

Run: the seed build.
Expected: `Build succeeded`, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add tools/p1/DesktopGuides.ShellSeed/Program.cs
git commit -m "test(seed): build, damage and interrupt library backups"
```

### Task 15: Installed restore smokes

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1`
- Modify: `tools/p1/windows_shell_install.ps1` (`Run-BackupScenarios`)

**Interfaces:**
- Consumes: Tasks 12–14 (AutomationIds, statuses, seed commands); Task 4's
  picker helpers, `-BackupPath` and the `Backup` test gate.
- Produces: smoke modes `restore-clean`, `restore-replace`,
  `restore-corrupt`, `restore-missing-artwork`, `restore-cancel`,
  `restore-interrupted`.

- [ ] **Step 1: Add the modes (RED against Task 12 alone)**

In `windows_shell_ui_smoke.ps1`, add the six modes to the `ValidateSet`,
add beside `Choose-SavePath`:

```powershell
    function Choose-OpenPath([string] $path) {
        $picker = Wait-FilePicker
        [DesktopGuidesForegroundProbe]::SetText((Find-InPicker $picker '1148' 'Edit'), $path)
        Send-PickerCommand $picker 1
        Wait-PickerClosed $picker
    }
```

and before the final `else` of the mode chain:

```powershell
    elseif ($Mode -like 'restore-*') {
        # T20.2: restore from Settings through the real Open dialog.
        if ($Mode -eq 'restore-interrupted') {
            [void](Wait-Status "The backup couldn't be opened, so your previous library was kept." -AllowHidden -Seconds 30)
            [void](Wait-Name 'LibraryHeading' 'Library')
            $report.restoreKeptScreenshot = Save-WindowScreenshot 'restore-kept'
            $report.phases += 'restore-interrupted-kept'
        }
        else {
            if (-not $BackupPath) { throw "$Mode needs -BackupPath." }
            Select-Element 'Settings'
            [void](Wait-Status 'Settings ready.')
            [void](Show-SettingsCard 'RestoreSettingsExpander')
            Invoke-Element (Wait-EnabledById 'ChooseBackupButton')
            Choose-OpenPath $BackupPath
            if ($Mode -eq 'restore-cancel') {
                # The install script holds staging at its Backup test gate.
                Invoke-Element (Wait-EnabledById 'RestoreCancelButton')
                [void](Wait-Name 'RestoreStatus' 'Restore canceled.')
                Wait-FocusedId 'ChooseBackupButton'
                $report.phases += 'restore-cancel'
            }
            elseif ($Mode -eq 'restore-corrupt') {
                [void](Wait-Name 'RestoreStatus' "This file isn't a Desktop Guides backup, or it's damaged.")
                Assert-Absent 'ReplaceLibraryButton'
                Wait-FocusedId 'ChooseBackupButton'
                $report.restoreCorruptScreenshot = Save-WindowScreenshot 'restore-corrupt'
                $report.phases += 'restore-corrupt-refused'
            }
            elseif ($Mode -eq 'restore-missing-artwork') {
                [void](Wait-Name 'RestoreStatus' 'This backup is missing files for: Seeded Linked Game.')
                Assert-Absent 'ReplaceLibraryButton'
                $report.phases += 'restore-missing-artwork-refused'
            }
            else {
                Wait-FocusedId 'ReplaceLibraryButton'
                $holds = (Wait-VisibleById 'RestoreHoldsCard').Current.Name
                if ($holds -notlike 'Backup holds: * game*, * guide*, *') {
                    throw "Unexpected backup summary '$holds'."
                }
                [void](Wait-VisibleById 'RestoreMadeCard')
                [void](Wait-VisibleById 'RestoreCurrentCard')
                $report.restoreStagedScreenshot = Save-WindowScreenshot 'restore-staged'
                $report.phases += 'restore-staged'
                if ($Mode -eq 'restore-replace') {
                    Invoke-Element (Wait-EnabledById 'ReplaceLibraryButton')
                    [void](Wait-VisibleById 'ReplaceLibraryDialog')
                    $report.restoreConfirmScreenshot = Save-WindowScreenshot 'restore-confirm'
                    Invoke-Element (Wait-EnabledById 'CloseButton')
                    [void](Wait-HiddenById 'ReplaceLibraryDialog')
                    Wait-FocusedId 'ReplaceLibraryButton'
                    $report.phases += 'restore-confirm-cancel'
                }
                Invoke-Element (Wait-EnabledById 'ReplaceLibraryButton')
                [void](Wait-VisibleById 'ReplaceLibraryDialog')
                Invoke-Element (Wait-EnabledById 'PrimaryButton')
                [void](Wait-Status 'Library restored: ' -Prefix -Seconds 60)
                [void](Wait-Name 'LibraryHeading' 'Library')
                [void](Wait-GameRow 'Seeded Linked Game')
                $report.restoredScreenshot = Save-WindowScreenshot 'restore-done'
                $report.phases += 'restore-done'
                if ($Mode -eq 'restore-clean') {
                    # The restored guides are listed and one opens.
                    Select-Element 'Import Test Game'
                    [void](Wait-Status 'Game ready.')
                    Wait-GuideRowCount 2
                    Open-GuideFromGame (Get-GuideRowNames)[0]
                    [void](Wait-Status 'Guide ready.' -Seconds 30)
                    $report.phases += 'restore-clean-guide-opens'
                }
            }
        }
    }
```

In `windows_shell_install.ps1`, at the end of `Run-BackupScenarios`'s
`try` block, after the `export-protected` check:

```powershell
        # T20.2 PR c: restore. The source backup holds the reminder's guides
        # plus a linked game with artwork.
        Invoke-ShellSeed @('seed-linked-game', $dataRoot) | Out-Null
        $source = Join-Path $backupRoot 'restore-source.zip'
        Invoke-ShellSeed @('export-backup', $dataRoot, $source) | Out-Null
        $sourceState = Invoke-ShellSeed @('describe-library', $dataRoot) | ConvertFrom-Json
        $truncated = Join-Path $backupRoot 'truncated.zip'
        $missingArtwork = Join-Path $backupRoot 'missing-artwork.zip'
        Invoke-ShellSeed @('damage-backup', $source, $truncated, 'truncate') | Out-Null
        Invoke-ShellSeed @('damage-backup', $source, $missingArtwork, 'missing-artwork') | Out-Null

        function Assert-Restored([string] $what) {
            $state = Invoke-ShellSeed @('describe-library', $dataRoot) | ConvertFrom-Json
            if ($state.games -ne $sourceState.games -or $state.guides -ne $sourceState.guides -or
                $state.marker -or $state.stages -ne 0 -or $state.parked -ne 0) {
                throw "$what left '$($state | ConvertTo-Json -Compress)'; expected the source library and no restore folders."
            }
        }

        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        Start-InstalledShell
        $report.restoreClean = Run-ShellSmoke 'restore-clean' -BackupPath $source
        Close-InstalledShell
        Assert-Restored 'A clean restore'

        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        Invoke-ShellSeed @('seed', $dataRoot) | Out-Null
        Start-InstalledShell
        $report.restoreReplace = Run-ShellSmoke 'restore-replace' -BackupPath $source
        Close-InstalledShell
        Assert-Restored 'Replacing a populated library'

        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        Invoke-ShellSeed @('seed', $dataRoot) | Out-Null
        foreach ($case in @(
                @{ Mode = 'restore-corrupt'; Path = $truncated },
                @{ Mode = 'restore-missing-artwork'; Path = $missingArtwork },
                @{ Mode = 'restore-cancel'; Path = $source })) {
            $before = Invoke-ShellSeed @('describe-library', $dataRoot)
            Start-InstalledShell
            $gate = $null
            if ($case.Mode -eq 'restore-cancel') {
                $prefix = "Local\DesktopGuides.Preview.Backup.$($report.launchedProcessId)"
                $gate = @(
                    [System.Threading.EventWaitHandle]::new(
                        $false, [System.Threading.EventResetMode]::AutoReset, "$prefix.Reached"),
                    [System.Threading.EventWaitHandle]::new(
                        $false, [System.Threading.EventResetMode]::AutoReset, "$prefix.Continue"))
            }
            try {
                $report[$case.Mode] = Run-ShellSmoke $case.Mode -BackupPath $case.Path
            }
            finally {
                if ($gate) {
                    $gate[1].Set() | Out-Null
                    $gate | ForEach-Object { $_.Dispose() }
                }
            }
            Close-InstalledShell
            $after = Invoke-ShellSeed @('describe-library', $dataRoot)
            if ($after -ne $before) {
                throw "$($case.Mode) changed the library: '$before' became '$after'."
            }
        }

        $prior = Invoke-ShellSeed @('describe-library', $dataRoot)
        Invoke-ShellSeed @('interrupt-restore', $dataRoot, $source) | Out-Null
        Start-InstalledShell
        $report.restoreInterrupted = Run-ShellSmoke 'restore-interrupted'
        Close-InstalledShell
        $after = Invoke-ShellSeed @('describe-library', $dataRoot)
        if ($after -ne $prior) {
            throw "An interrupted restore didn't put the previous library back: '$prior' became '$after'."
        }
```

`describe-library` reports `stages` and `parked`, so each comparison also
shows that no stage or parked library was left behind.

- [ ] **Step 2: Run the group to verify it fails before Task 13**

Check out Task 12's commit with these harness changes applied, push to a
scratch branch, and run `shell-scope=backup`.
Expected: FAIL in `restore-clean` at `Wait-Status 'Library restored: '`,
because Replace does nothing yet. Delete the scratch branch.

- [ ] **Step 3: Run the group and the full CI to verify they pass**

Push the branch at Task 15's commit and run `shell-scope=backup`, then
the full PR run.
Expected: the group passes with the PR a phases plus `restore-staged`,
`restore-confirm-cancel`, `restore-done` (twice), `restore-clean-guide-opens`, `restore-corrupt-refused`,
`restore-missing-artwork-refused`, `restore-cancel` and
`restore-interrupted-kept`; every `describe-library` comparison holds.
The full run passes every job.

- [ ] **Step 4: Commit**

```bash
git add tools/p1
git commit -m "test(shell): installed restore smokes"
```

### Task 16: PR c documentation, the technical design's stale layout, and PR

**Files:**
- Modify: `docs/p1/t20-2-export-restore-design.md` (status, verification record)
- Modify: `docs/p1-technical-design.md` (section 2 live layout; section 9 T20.2)
- Modify: `docs/p1/implementation-plan.md`, `docs/progress.md`, `docs/work-breakdown.md` (T20.2)
- Create: `docs/p1/evidence/t20-2-export-restore/restore-staged.png`,
  `restore-confirm.png`, `restore-done.png`, `restore-kept.png`

- [ ] **Step 1: Correct the technical design**

In section 2, replace the T04.4-era text (the `GameMetadataLinks` table,
the `AddGameMetadata` journal and the `games/<game-id>` roots) with what
the code does: provider metadata in `Games` columns (`ProviderName`,
`ProviderGameId`, `MetadataJson`, `MetadataRetrievedUtcMs`) and artwork
at `library/artwork/<game>/<sha256>.<ext>` referenced by
`Games.ArtworkRelativePath`. Add `restore.marker` and `.restore-staging/`
beside `library/` in the layout list. In section 9's T20.2 bullet, note
the marker's phase (ruling 1) and that unverified copies go under
`.restore-staging` (ruling 2).

- [ ] **Step 2: Record the verification**

Add `### PR c: restore UI and the in-session swap` to the design's
verification record: host counts, the `backup` dispatch and full PR run
IDs with any reruns and their causes, each `describe-library`
comparison, the screenshots, rulings 6–8, 10 and 12, and what wasn't
run (high contrast, 200% scaling and Narrator are T16.2's). Set the design
status to "Status: PRs a and b merged in #<a> and #<b>; PR c in review."

- [ ] **Step 3: Update the status pages**

Update the T20.2 paragraph and row in `implementation-plan.md`, the T20.2
row and the open list in `progress.md`, and the T20.2 bullet in
`work-breakdown.md` ("Implemented in PRs #<a>, #<b> and #<c>; see the design").

- [ ] **Step 4: Check, commit and open PR c**

Run: `git diff --check`
Expected: no output.

```bash
git add docs
git commit -m "docs(p1): record T20.2 PR c verification"
```

Push and open the PR. The body names T20.2 PR c, the prerequisites (PRs
a and b: merged), the outcome (restore from Settings with a checked
summary, native Replace confirmation, in-session swap, startup rollback),
and embeds the `restore-staged`, `restore-confirm`, `restore-done` and
`restore-kept` screenshots. It ends with the Claude Code line.

---

## Traceability

| Requirement | Covered by |
|---|---|
| TR20.1: staging validates checksums, paths, guide and artwork references before the live library changes | Tasks 7–8 cells; Task 15 `restore-corrupt`, `restore-missing-artwork` |
| TR20.2: no credentials, transient WebView2 data or unrelated files | T20.1's exclusion tests; Task 2 protected roots; Task 4 `describe-backup` `credentials: false` |
| Export outside app data, explained in Settings and the reminder | Tasks 1–4 |
| Cancel/Replace after a count summary; no merge | Tasks 12–13, 15 |
| Marker, swap and rollback on failure or interrupted startup | Tasks 9–10, 13, 15 `restore-interrupted` |
| Clean, populated, cancel, corrupt, traversal, oversized, forced swap failure | Tasks 7–10 and 15 |

## PR outcome

### PR a

- **Target task:** T20.2, PR a.
- **Prerequisites:** T14.4 (#54), T15.1 (#74–#76), T15.4 (#62), T20.1 (#59), all merged.
- **Outcome:** Settings exports the library to a `.zip` outside app data
  with progress and Cancel, and the first import reminds once that
  uninstalling removes the library. Screenshots: the Export card and the
  reminder.

### PR b

- **Target task:** T20.2, PR b.
- **Prerequisites:** PR a merged.
- **Outcome:** a backup can be staged and fully checked, swapped in behind
  a phased marker, and confirmed or rolled back from disk state. No UI, so
  no screenshot.

### PR c

- **Target task:** T20.2, PR c.
- **Prerequisites:** PRs a and b merged.
- **Outcome:** Settings restores a checked backup after a native
  confirmation, in the running app; an interrupted restore puts the
  previous library back on the next start. Screenshots: the staged
  summary, the confirmation, the restored library and the kept-library
  status.
