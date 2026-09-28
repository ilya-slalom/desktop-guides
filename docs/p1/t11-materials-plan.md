# T11.4 window materials implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the shell backdrop run seamlessly across the whole window in
light and dark themes, like Windows Settings. Add a persisted Window background
setting (Mica, Acrylic, Solid), style dialogs to match it, and defer
high-contrast and colorful-icon work to recorded later tasks.

**Architecture:** The `NavigationView` content layer and its border become
transparent, so the window backdrop shows behind the pane and route content.
Explicit content panels, such as the reader surface and cards, stay opaque. A
small `WindowMaterials` helper maps the stored `WindowMaterial` to a
`SystemBackdrop`: `MicaBackdrop`, a custom `ThinAcrylicBackdrop` built on
`DesktopAcrylicController`, or no backdrop plus an opaque canvas. The backdrop
uses the default configuration, so it follows the app theme and turns solid
when the window is inactive. The setting is stored as a new key in the existing
key/value `Settings` table and written through a new atomic
`UpdateSettingsAsync`.

**Tech Stack:** .NET 10, WinUI 3 / Windows App SDK 2.5.1, Community Toolkit
`SettingsControls` 8.2.251219, SQLite (Microsoft.Data.Sqlite), xUnit, and
PowerShell UI Automation harnesses run on the Windows 11 x64 host.

**Spec:** User feedback of 28 September 2026 and the decisions recorded in the
same session: seamless card pattern plus a Mica/Acrylic/Solid setting (default
Mica), a styled `ContentDialog`, storage in SQLite `AppSettings`, and an
inactive window that always follows Windows. Base design:
[t11-design-language-plan.md](t11-design-language-plan.md).

## Global Constraints

- Windows App SDK stays at `2.5.1`; no new NuGet packages.
- Keep Windows 10-compatible fallback behavior. If a material is unsupported,
  render Solid and tell the user once through a warning `InfoBar`.
- The default material is `Mica`. A missing stored key means `Mica`.
- No schema migration. `LibrarySchema.CurrentVersion` stays `2`; T04.4 keeps
  version 3.
- Invalid stored values throw `InvalidDataException`, as the Theme key does.
- Copy is sentence case: setting header `Window background`, description
  `Choose how much of your desktop shows behind the app.`, options `Mica`,
  `Acrylic`, `Solid`.
- The reader content panel, cards, and dialogs stay opaque enough to read over
  any wallpaper. Only the shell chrome and route backgrounds show the backdrop.
- Automated appearance runs must not change high-contrast state. High-contrast
  checks move to T16.2.
- Installed UI runs over SSH use an interactive scheduled task and the backup
  and cleanup rules in [e2e-testing.md](e2e-testing.md).
- No code is built or tested on the Mac; all `dotnet` and harness commands run
  on the host (`ssh -o BatchMode=yes pcsx2-win`).

## Review Focus

1. Opening a guide right after changing the material must not revert the
   material; the resume write used to read, modify, and write the whole
   `AppSettings` (Task 1 concurrency test, Task 2 persistence run).
2. On Windows 10, Mica is unsupported and must fall back to a legible Solid
   canvas with a warning (Task 3 `WindowMaterials.Resolve`). With transparency
   effects off, the controllers stay supported and draw their own solid
   fallback color, which must still be legible (manual check in Final
   verification; Windows 10 itself remains untested).
3. Changing the Windows theme while the app is open must keep the backdrop and
   canvas in the matching theme (Task 2 runs light and dark separately; manual
   check during host verification).
4. Titles and body text that sit directly on Acrylic over a busy wallpaper
   must stay readable (Task 2 captures screenshots on the host's photo
   wallpaper; reviewed by eye).
5. A database written by the current release, with no `WindowMaterial` key, or
   with an unknown value must load or fail predictably (Task 1 tests).

---

## File map

| File | Responsibility |
| --- | --- |
| `src/DesktopGuides.Core/Library/LibraryModels.cs` | `WindowMaterial` enum and the extended `AppSettings` record |
| `src/DesktopGuides.Core/Library/ILibraryRepository.cs` | New atomic `UpdateSettingsAsync` |
| `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs` | Reads and writes the `WindowMaterial` key; shared settings read and write helpers |
| `src/DesktopGuides.Production/Materials/ThinAcrylicBackdrop.cs` (new) | `SystemBackdrop` that hosts a Thin `DesktopAcrylicController` |
| `src/DesktopGuides.Production/Materials/WindowMaterials.cs` (new) | Support check, fallback, and backdrop creation |
| `src/DesktopGuides.Production/Materials/DialogSurface.cs` (new) | Applies the matching style to a `ContentDialog` |
| `src/DesktopGuides.Production/ShellWindow.xaml(.cs)` | Transparent content layer, solid canvas, Settings card, applying and saving the material |
| `src/DesktopGuides.Production/Styles/DesignTokens.xaml`, `Styles/Controls.xaml` | Reading-surface brush and acrylic dialog style |
| `src/DesktopGuides.Production/GameEditorDialog.xaml.cs`, `ReaderToolbar.xaml.cs` | Dialogs use `DialogSurface` |
| `tools/p1/DesktopGuides.ReaderToolbarSmoke/*` | Links the new files the toolbar depends on |
| `tools/p1/DesktopGuides.ShellSeed/Program.cs` | New `set-material` mode |
| `tools/p1/windows_shell_screenshot_stats.ps1` (new) + `test_windows_shell_screenshot_stats.ps1` (new) | Screenshot luminance and region statistics |
| `tools/p1/windows_shell_ui_smoke.ps1`, `windows_shell_install.ps1` | `material` smoke mode; the high-contrast pass is removed |
| `.github/workflows/windows-ci.yml` | Runs the new harness test |
| Docs listed in Task 5 | Design record, task tables, deferrals, and evidence |

### Task 1: Store the window material

**Files:**
- Modify: `src/DesktopGuides.Core/Library/LibraryModels.cs`
- Modify: `src/DesktopGuides.Core/Library/ILibraryRepository.cs`
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs:278-350`
- Test: `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `public enum WindowMaterial { Mica, Acrylic, Solid }` in `DesktopGuides.Core.Library`
  - `public sealed record AppSettings(ThemePreference Theme, Guid? LastActiveGuideId, WindowMaterial WindowMaterial = WindowMaterial.Mica)`
  - `Task<AppSettings> ILibraryRepository.UpdateSettingsAsync(Func<AppSettings, AppSettings> update, CancellationToken token = default)`. It returns the saved settings. The read and the write share one transaction under the write gate.

- [ ] **Step 1: Write the failing tests**

Add these to `SqliteLibraryRepositoryTests`:

```csharp
[Fact]
public async Task PersistsWindowMaterialAcrossReopen()
{
    using TestLibrary directory = new();
    await using (SqliteLibraryRepository repository = new(directory.Paths))
    {
        await repository.InitializeAsync();
        Assert.Equal(WindowMaterial.Mica, (await repository.GetSettingsAsync()).WindowMaterial);
        await repository.SaveSettingsAsync(
            new AppSettings(ThemePreference.System, null, WindowMaterial.Acrylic));
    }

    await using SqliteLibraryRepository reopened = new(directory.Paths);
    await reopened.InitializeAsync();
    Assert.Equal(
        new AppSettings(ThemePreference.System, null, WindowMaterial.Acrylic),
        await reopened.GetSettingsAsync());
}

[Theory]
[InlineData("Glass")]
[InlineData("acrylic")]
[InlineData("1")]
public async Task RejectsInvalidStoredWindowMaterial(string value)
{
    using TestLibrary directory = new();
    await using SqliteLibraryRepository repository = new(directory.Paths);
    await repository.InitializeAsync();
    using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
    using (SqliteCommand command = connection.CreateCommand())
    {
        command.CommandText = "INSERT INTO Settings (Key, Value) VALUES ('WindowMaterial', $value)";
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
        () => repository.GetSettingsAsync());
    Assert.Equal("Stored window material is invalid.", error.Message);
}

[Fact]
public async Task RejectsUndefinedWindowMaterialOnSave()
{
    using TestLibrary directory = new();
    await using SqliteLibraryRepository repository = new(directory.Paths);
    await repository.InitializeAsync();
    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.SaveSettingsAsync(
        new AppSettings(ThemePreference.System, null, (WindowMaterial)42)));
}

[Fact]
public async Task ConcurrentSettingsUpdatesKeepBothChanges()
{
    using TestLibrary directory = new();
    Guid guide = Guid.NewGuid();
    await using SqliteLibraryRepository repository = new(directory.Paths);
    await repository.InitializeAsync();

    Task<AppSettings>[] updates = Enumerable.Range(0, 20)
        .Select(i => i % 2 == 0
            ? repository.UpdateSettingsAsync(s => s with { WindowMaterial = WindowMaterial.Solid })
            : repository.UpdateSettingsAsync(s => s with { LastActiveGuideId = guide }))
        .ToArray();
    await Task.WhenAll(updates);

    Assert.Equal(
        new AppSettings(ThemePreference.System, guide, WindowMaterial.Solid),
        await repository.GetSettingsAsync());
}

[Fact]
public async Task UpdateSettingsRejectsUndefinedValuesWithoutWriting()
{
    using TestLibrary directory = new();
    await using SqliteLibraryRepository repository = new(directory.Paths);
    await repository.InitializeAsync();
    await repository.SaveSettingsAsync(
        new AppSettings(ThemePreference.Dark, null, WindowMaterial.Acrylic));

    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
        repository.UpdateSettingsAsync(s => s with { WindowMaterial = (WindowMaterial)42 }));

    Assert.Equal(
        new AppSettings(ThemePreference.Dark, null, WindowMaterial.Acrylic),
        await repository.GetSettingsAsync());
}

[Fact]
public async Task IgnoresUnknownSettingsKeysFromOtherVersions()
{
    using TestLibrary directory = new();
    await using SqliteLibraryRepository repository = new(directory.Paths);
    await repository.InitializeAsync();
    using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
    using (SqliteCommand command = connection.CreateCommand())
    {
        command.CommandText = "INSERT INTO Settings (Key, Value) VALUES ('FutureSetting', 'x')";
        command.ExecuteNonQuery();
    }

    Assert.Equal(new AppSettings(ThemePreference.System, null), await repository.GetSettingsAsync());
}
```

The existing tests that build `new AppSettings(ThemePreference.Dark, second)`
keep compiling because `WindowMaterial` defaults to `Mica`. Leave them as they
are.

- [ ] **Step 2: Stage the tests on the host and confirm they fail**

```bash
RUN=t11-materials-20260928
git archive --format=tar HEAD | ssh -o BatchMode=yes pcsx2-win "powershell -NoProfile -Command \"New-Item -ItemType Directory -Force E:\\work\\desktop-guides\\$RUN | Out-Null\"; tar -x -C E:/work/desktop-guides/$RUN -f -"
# Copy modified and new untracked files over the snapshot:
git ls-files -m -o --exclude-standard | tar -cf - -T - | ssh -o BatchMode=yes pcsx2-win "tar -x -C E:/work/desktop-guides/$RUN -f -"
ssh -o BatchMode=yes pcsx2-win "cd /d E:\\work\\desktop-guides\\$RUN && dotnet test tests\\DesktopGuides.Infrastructure.Tests --filter FullyQualifiedName~SqliteLibraryRepositoryTests"
```

Expected: the build fails because `WindowMaterial` and `UpdateSettingsAsync`
do not exist.

- [ ] **Step 3: Add the model and interface**

In `LibraryModels.cs`, next to `ThemePreference`:

```csharp
public enum WindowMaterial
{
    Mica,
    Acrylic,
    Solid
}

public sealed record AppSettings(
    ThemePreference Theme,
    Guid? LastActiveGuideId,
    WindowMaterial WindowMaterial = WindowMaterial.Mica);
```

Keep whatever modifiers the current `AppSettings` declaration uses.

In `ILibraryRepository.cs`, after `SaveSettingsAsync`:

```csharp
Task<AppSettings> UpdateSettingsAsync(
    Func<AppSettings, AppSettings> update,
    CancellationToken token = default);
```

- [ ] **Step 4: Implement the repository changes**

Split the bodies of `GetSettingsAsync` and `SaveSettingsAsync` into two
private static helpers. The public methods and `UpdateSettingsAsync` call
them:

```csharp
public Task<AppSettings> GetSettingsAsync(CancellationToken token = default) =>
    ReadAsync(() =>
    {
        using SqliteConnection connection = OpenConnection();
        return ReadSettings(connection, null);
    }, token);

public Task SaveSettingsAsync(AppSettings settings, CancellationToken token = default)
{
    ValidateSettings(settings);
    return WriteAsync(() =>
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();
        WriteSettings(connection, transaction, settings);
        transaction.Commit();
    }, token);
}

public Task<AppSettings> UpdateSettingsAsync(
    Func<AppSettings, AppSettings> update,
    CancellationToken token = default)
{
    ArgumentNullException.ThrowIfNull(update);
    return WriteAsync(() =>
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();
        AppSettings updated = update(ReadSettings(connection, transaction));
        ValidateSettings(updated);
        WriteSettings(connection, transaction, updated);
        transaction.Commit();
        return updated;
    }, token);
}

private static void ValidateSettings(AppSettings settings)
{
    ArgumentNullException.ThrowIfNull(settings);
    if (!Enum.IsDefined(settings.Theme) || !Enum.IsDefined(settings.WindowMaterial))
    {
        throw new ArgumentOutOfRangeException(nameof(settings));
    }
}
```

`ReadSettings(SqliteConnection connection, SqliteTransaction? transaction)`
holds the current read loop with `command.Transaction = transaction`, plus
this branch:

```csharp
else if (key == "WindowMaterial")
{
    if (!Enum.TryParse(value, out material) ||
        !Enum.IsDefined(material) ||
        material.ToString() != value)
    {
        throw new InvalidDataException("Stored window material is invalid.");
    }
}
```

It starts with `WindowMaterial material = WindowMaterial.Mica;` and returns
`new AppSettings(theme, lastGuide, material)`.

`WriteSettings(SqliteConnection connection, SqliteTransaction transaction, AppSettings settings)`
holds the current Theme and LastActiveGuideId commands, plus a third upsert:

```csharp
using (SqliteCommand material = connection.CreateCommand())
{
    material.Transaction = transaction;
    material.CommandText = """
        INSERT INTO Settings (Key, Value) VALUES ('WindowMaterial', $value)
        ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
        """;
    material.Parameters.AddWithValue("$value", settings.WindowMaterial.ToString());
    material.ExecuteNonQuery();
}
```

If `ValidateSettings` throws inside `WriteAsync`, the transaction is disposed
without a commit, so nothing is written. The
`UpdateSettingsRejectsUndefinedValuesWithoutWriting` test pins this. Check
that `WriteAsync<T>` (line 592) rethrows the original exception rather than
wrapping it. Keep the `SaveSettingsAsync` validation outside `WriteAsync` so
it keeps its current synchronous throw.

- [ ] **Step 5: Run the repository tests and the full suites on the host**

Re-sync the changed files as in Step 2, then:

```bash
ssh -o BatchMode=yes pcsx2-win "cd /d E:\\work\\desktop-guides\\$RUN && dotnet test tests\\DesktopGuides.Core.Tests && dotnet test tests\\DesktopGuides.Infrastructure.Tests"
```

Expected: all pass. The previous counts were 73 Core and 90 Infrastructure;
Infrastructure is now 90 + 8 = 98, because the theory adds three cases.

- [ ] **Step 6: Commit**

```bash
git add src/DesktopGuides.Core/Library src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs
git commit -m "feat(storage): persist window material with atomic settings updates"
```

### Task 2: Material harness, written before the UI

This task drops the high-contrast pass and adds checks that fail against the
current UI. They fail because `WindowMaterialSelector` does not exist yet and
because the Solid strip check catches the current content-layer border.

**Files:**
- Create: `tools/p1/windows_shell_screenshot_stats.ps1`
- Create: `tools/p1/test_windows_shell_screenshot_stats.ps1`
- Modify: `tools/p1/windows_shell_install.ps1` (lines 30, 688-731, 733-756, 758-826, 890-898)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (parameters, helpers near line 590, new mode block after line 821)
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs`
- Modify: `.github/workflows/windows-ci.yml`
- Delete: `tools/p1/windows_shell_appearance_probe.cs` (git history keeps it for T16.2)

**Interfaces:**
- Consumes: `ILibraryRepository.UpdateSettingsAsync` and `WindowMaterial` from Task 1.
- Produces, used by Task 3's UI contract:
  - AutomationId `WindowMaterialSelector`: a `ComboBox` whose items are named `Mica`, `Acrylic`, `Solid`
  - AutomationId `WindowMaterialSettingsCard`, named `Window background. Choose how much of your desktop shows behind the app.`
  - Status text after a change: `Window background set to <Material>.`
  - Status text when a material is unsupported: `<Material> isn't available on this device. Using Solid.` (warning, stays until dismissed)
  - `Get-ScreenshotLuminance([string] $Path)` and `Get-ScreenshotRegionStats([string] $Path, [int] $X, [int] $Y, [int] $Width, [int] $Height)`. The second returns `[ordered]@{ meanR; meanG; meanB; maxChannelRange }`, where `maxChannelRange` is the largest max-minus-min across R, G, and B.

- [ ] **Step 1: Write the screenshot-stats test**

`tools/p1/test_windows_shell_screenshot_stats.ps1`:

```powershell
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot 'windows_shell_screenshot_stats.ps1')

$root = Join-Path $env:TEMP "DesktopGuides-Stats-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $path = Join-Path $root 'split.png'
    $bitmap = [System.Drawing.Bitmap]::new(40, 10)
    try {
        for ($x = 0; $x -lt 40; $x++) {
            for ($y = 0; $y -lt 10; $y++) {
                $color = if ($x -lt 20) { [System.Drawing.Color]::FromArgb(243, 243, 243) }
                    else { [System.Drawing.Color]::FromArgb(249, 249, 249) }
                $bitmap.SetPixel($x, $y, $color)
            }
        }
        $bitmap.SetPixel(20, 5, [System.Drawing.Color]::FromArgb(229, 229, 229))
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bitmap.Dispose() }

    $uniform = Get-ScreenshotRegionStats $path 0 0 20 10
    if ($uniform.maxChannelRange -ne 0 -or $uniform.meanR -ne 243) {
        throw "Uniform region stats were wrong: $($uniform | ConvertTo-Json -Compress)"
    }
    $split = Get-ScreenshotRegionStats $path 10 0 20 10
    if ($split.maxChannelRange -ne 20) {
        throw "Split region range was $($split.maxChannelRange), expected 20."
    }
    $outside = $false
    try { [void](Get-ScreenshotRegionStats $path 30 0 20 10) } catch { $outside = $true }
    if (-not $outside) { throw 'A region outside the image did not fail closed.' }
    if ((Get-ScreenshotLuminance $path) -lt 243) {
        throw 'Luminance of a light image was too low.'
    }
    'Screenshot stats checks passed.'
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
```

- [ ] **Step 2: Run it on the host and confirm it fails**

```bash
ssh -o BatchMode=yes pcsx2-win "cd /d E:\\work\\desktop-guides\\$RUN\\tools\\p1 && powershell -NoProfile -ExecutionPolicy Bypass -File test_windows_shell_screenshot_stats.ps1"
```

Expected: FAIL, because `windows_shell_screenshot_stats.ps1` is missing.

- [ ] **Step 3: Create `windows_shell_screenshot_stats.ps1`**

Move `Get-ScreenshotLuminance` out of `windows_shell_install.ps1`
(lines 733-756), unchanged, and add:

```powershell
function Get-ScreenshotRegionStats(
    [string] $Path, [int] $X, [int] $Y, [int] $Width, [int] $Height) {
    Add-Type -AssemblyName System.Drawing
    $bitmap = [System.Drawing.Bitmap]::new($Path)
    try {
        if ($X -lt 0 -or $Y -lt 0 -or $Width -lt 1 -or $Height -lt 1 -or
            $X + $Width -gt $bitmap.Width -or $Y + $Height -gt $bitmap.Height) {
            throw "Region $X,$Y ${Width}x$Height is outside $Path."
        }
        $min = @(255, 255, 255); $max = @(0, 0, 0); $sum = @(0.0, 0.0, 0.0)
        for ($py = $Y; $py -lt $Y + $Height; $py++) {
            for ($px = $X; $px -lt $X + $Width; $px++) {
                $c = $bitmap.GetPixel($px, $py)
                $values = @($c.R, $c.G, $c.B)
                for ($i = 0; $i -lt 3; $i++) {
                    $sum[$i] += $values[$i]
                    if ($values[$i] -lt $min[$i]) { $min[$i] = $values[$i] }
                    if ($values[$i] -gt $max[$i]) { $max[$i] = $values[$i] }
                }
            }
        }
        $count = $Width * $Height
        return [ordered]@{
            meanR = [Math]::Round($sum[0] / $count, 2)
            meanG = [Math]::Round($sum[1] / $count, 2)
            meanB = [Math]::Round($sum[2] / $count, 2)
            maxChannelRange = [int](@(0, 1, 2 | ForEach-Object { $max[$_] - $min[$_] }) |
                Measure-Object -Maximum).Maximum
        }
    }
    finally { $bitmap.Dispose() }
}
```

In `windows_shell_install.ps1`, dot-source it next to the other helpers
(line 26-29) and delete the moved function.

- [ ] **Step 4: Run the stats test and the existing harness tests; expect PASS**

```bash
ssh -o BatchMode=yes pcsx2-win "cd /d E:\\work\\desktop-guides\\$RUN\\tools\\p1 && for %t in (test_*.ps1) do powershell -NoProfile -ExecutionPolicy Bypass -File %t || exit /b 1"
```

Add `test_windows_shell_screenshot_stats.ps1` to the PowerShell harness step
in `.github/workflows/windows-ci.yml`, next to
`test_windows_shell_smoke_result.ps1`.

- [ ] **Step 5: Remove the high-contrast pass**

In `windows_shell_install.ps1`:
- Delete the `Add-Type ... windows_shell_appearance_probe.cs` line (30).
- Delete `Get-HighContrastPreference`, `Set-HighContrastPreference`, and
  `Restore-HighContrastPreference` (688-731).
- In `Run-DesignLanguageScenarios`, delete `$originalHighContrast`, the
  initial high-contrast guard, the `Set-HighContrastPreference` block with
  `designLanguageHighContrast`, and the high-contrast restore in `finally`.
  Keep the system, light, and dark passes, the luminance check, and
  `Restore-AppThemePreference`.
- Record the deferral in the report with `$report.highContrast = 'deferred-to-T16.2'`.

Delete `tools/p1/windows_shell_appearance_probe.cs`. Run
`git grep -n "HighContrast\|appearance_probe" tools .github` and expect matches
only in `docs/` history notes.

- [ ] **Step 6: Add the ShellSeed `set-material` mode**

In `tools/p1/DesktopGuides.ShellSeed/Program.cs`, accept
`set-material <app-data-root> <Mica|Acrylic|Solid>` as a three-argument mode,
checked before the existing two-argument validation:

```csharp
if (args.Length == 3 && args[0] == "set-material")
{
    if (!Enum.TryParse(args[2], false, out WindowMaterial material) ||
        !Enum.IsDefined(material) || material.ToString() != args[2])
    {
        Console.Error.WriteLine("Material must be Mica, Acrylic, or Solid.");
        return 2;
    }
    await using SqliteLibraryRepository materialRepository =
        new(new ManagedPathResolver(args[1]));
    await materialRepository.InitializeAsync();
    await materialRepository.UpdateSettingsAsync(
        settings => settings with { WindowMaterial = material });
    return 0;
}
```

Match the file's existing return and error conventions if they differ from
`return 2`. Check them before editing.

- [ ] **Step 7: Add the smoke `material` mode**

In `windows_shell_ui_smoke.ps1`:
- Add `'material'` to the `-Mode` `ValidateSet`.
- Add `[ValidateSet('Mica', 'Acrylic', 'Solid')] [string] $ExpectedMaterial = 'Mica'`.
- Add `[ValidateSet('', 'Mica', 'Acrylic', 'Solid')] [string] $SwitchToMaterial = ''`.
- Dot-source `windows_shell_screenshot_stats.ps1`.

Add these helpers next to `Save-WindowScreenshot`:

```powershell
function Get-ComboSelection([string] $id) {
    $combo = Wait-VisibleById $id
    $selection = $combo.GetCurrentPattern(
        [System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    if ($selection.Length -ne 1) { throw "$id has $($selection.Length) selected items." }
    return $selection[0].Current.Name
}

function Select-ComboItem([string] $id, [string] $name) {
    $combo = Wait-VisibleById $id
    $expand = $combo.GetCurrentPattern(
        [System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    $item = $null
    $deadline = (Get-Date).AddSeconds(5)
    do {
        $item = $combo.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::NameProperty, $name))
        if (-not $item) { Start-Sleep -Milliseconds 100 }
    } while (-not $item -and (Get-Date) -lt $deadline)
    if (-not $item) { throw "$id has no item named $name." }
    $item.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    if ($expand.Current.ExpandCollapseState -ne
            [System.Windows.Automation.ExpandCollapseState]::Collapsed) {
        $expand.Collapse()
    }
}

function Assert-ShellForeground {
    if ([DesktopGuidesForegroundProbe]::GetForegroundWindow() -ne
            $process.MainWindowHandle) {
        throw 'The shell was not the foreground window; its backdrop would be inactive.'
    }
}

function Get-EmptyStrip([string] $screenshot) {
    $window = $root.Current.BoundingRectangle
    $content = (Wait-VisibleById 'ShellContent').Current.BoundingRectangle
    $boundary = [int]($content.Left - $window.Left)
    $y = [int]($window.Height - 24)
    return Get-ScreenshotRegionStats $screenshot ($boundary - 40) $y 80 8
}
```

If `Wait-VisibleById` returns an element that the `$root` search can't
expand, look up the ComboBox popup items from `$root` instead: WinUI hosts the
dropdown in a popup. Confirm which one works on the host in Step 8.

The mode block (add after the `design-language` block):

```powershell
elseif ($Mode -eq 'material') {
    $designGame = 'The Legend of Zelda: Tears of the Kingdom'
    $designGuide = 'Complete Story Walkthrough'
    Resize-ShellWindow 1500 720
    Select-Element 'Settings'
    [void](Wait-Name 'WindowMaterialSettingsCard' (
        'Window background. Choose how much of your desktop shows behind the app.'))
    $selected = Get-ComboSelection 'WindowMaterialSelector'
    if ($selected -ne $ExpectedMaterial) {
        throw "Expected $ExpectedMaterial window background, found $selected."
    }
    $report.phases += "material-$selected-restored"

    if ($SwitchToMaterial) {
        Select-ComboItem 'WindowMaterialSelector' $SwitchToMaterial
        [void](Wait-Status "Window background set to $SwitchToMaterial.")
        $selected = $SwitchToMaterial
        $report.phases += "material-switched-$selected"
    }
    [void](Wait-HiddenById 'ShellStatus')
    Assert-ShellForeground
    $report.settingsScreenshot = Save-WindowScreenshot "material-$selected-settings"

    Select-Element 'Library'
    [void](Wait-GameRow $designGame)
    Start-Sleep -Milliseconds 400
    Assert-ShellForeground
    $report.libraryScreenshot = Save-WindowScreenshot "material-$selected-library"
    $report.libraryStrip = Get-EmptyStrip $report.libraryScreenshot

    Press-Enter (Wait-GameRow $designGame)
    [void](Wait-Name 'GameHeading' $designGame)
    Invoke-Element (Wait-EnabledById 'EditGameButton')
    [void](Wait-VisibleById 'GameTitleInput')
    Assert-ShellForeground
    $report.dialogScreenshot = Save-WindowScreenshot "material-$selected-edit-game"
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Wait-EditorClosed

    Press-Enter (Wait-GuideRow $designGuide)
    [void](Wait-Name 'ReaderHeading' $designGuide)
    [void](Wait-Status 'Guide details ready.')
    [void](Wait-HiddenById 'ShellStatus')
    Assert-ShellForeground
    $report.readerScreenshot = Save-WindowScreenshot "material-$selected-reader"
    $report.material = $selected
    $report.phases += "material-$selected-captured"
}
```

Opening the guide writes `LastActiveGuideId`. A later relaunch that expects
the switched material therefore covers Review Focus item 1.

- [ ] **Step 8: Add `Run-MaterialScenarios` to `windows_shell_install.ps1`**

Add a `-ExpectedMaterial` and `-SwitchToMaterial` pass-through to
`Run-ShellSmoke`, appended to `$arguments` only when set. Then add:

```powershell
function Set-StoredMaterial([string] $material) {
    dotnet run --project $seedProject -c Release --no-restore -- `
        set-material $dataRoot $material
    if ($LASTEXITCODE -ne 0) { throw "Could not store the $material window background." }
}

function Run-MaterialScenarios {
    $originalTheme = Get-AppThemePreference
    $report.materials = [ordered]@{}
    try {
        foreach ($light in @($true, $false)) {
            $themeName = if ($light) { 'light' } else { 'dark' }
            Set-AppThemePreference $light
            foreach ($material in @('Solid', 'Acrylic', 'Mica')) {
                Set-StoredMaterial $material
                Start-InstalledShell
                $report.materials["$themeName-$material"] = Run-ShellSmoke `
                    'material' -ResultName "material-$themeName-$material" `
                    -ExpectedMaterial $material
                Close-InstalledShell
            }
            $solid = $report.materials["$themeName-Solid"].libraryStrip
            if ($solid.maxChannelRange -gt 2) {
                throw "The $themeName Solid window shows a pane/content seam: " +
                    "range $($solid.maxChannelRange)."
            }
            $acrylic = $report.materials["$themeName-Acrylic"].libraryStrip
            $difference = [Math]::Max([Math]::Abs($acrylic.meanR - $solid.meanR),
                [Math]::Max([Math]::Abs($acrylic.meanG - $solid.meanG),
                    [Math]::Abs($acrylic.meanB - $solid.meanB)))
            $report.materials["$themeName-acrylicDifference"] = $difference
            if ($difference -le $acrylicThreshold) {
                throw "The $themeName Acrylic backdrop matched Solid " +
                    "(difference $difference); the backdrop is not visible."
            }
        }

        Set-AppThemePreference $true
        Set-StoredMaterial 'Mica'
        Start-InstalledShell
        $report.materialSwitch = Run-ShellSmoke 'material' `
            -ResultName 'material-switch' -ExpectedMaterial 'Mica' `
            -SwitchToMaterial 'Acrylic'
        Close-InstalledShell
        Start-InstalledShell
        $report.materialPersisted = Run-ShellSmoke 'material' `
            -ResultName 'material-persisted' -ExpectedMaterial 'Acrylic'
        Close-InstalledShell
    }
    finally {
        Set-StoredMaterial 'Mica'
        Restore-AppThemePreference $originalTheme
        $report.restoredAppTheme = Get-AppThemePreference
    }
}
```

Set `$acrylicThreshold = 4` at the top of the function as the starting value.
Calibrate it on the first run: record both themes' differences in the result
and in the plan's verification record. Set the final threshold to roughly half
the smaller observed difference, but never below 3. If dark Acrylic shows a
difference under 3 even with Thin acrylic, stop and apply
superpowers:systematic-debugging. That is the reported dark-theme defect, and
it is not a threshold to tune away.

Call it in the `-DesignOnly` branch right after `Run-DesignLanguageScenarios`.
Also call it in the full regression at the point where the design-language
scenarios already run. Find that with
`grep -n Run-DesignLanguageScenarios tools/p1/windows_shell_install.ps1`.

- [ ] **Step 9: Confirm the new checks fail against the current UI**

Build and install the current package on the host, then run the design-only
pass through the interactive scheduled task. Follow
`docs/p1/e2e-testing.md`, including its data backup:

```bash
ssh -o BatchMode=yes pcsx2-win "cd /d E:\\work\\desktop-guides\\$RUN && powershell -NoProfile -ExecutionPolicy Bypass -File tools\\p1\\windows_shell_install.ps1 -PackagePath <built msix> -ResultDirectory E:\\work\\desktop-guides\\$RUN\\results-red -DesignOnly"
```

Expected: the design-language passes succeed without touching high contrast.
`Run-MaterialScenarios` then fails at `WindowMaterialSettingsCard`, or the
seed fails because the running build ignores the new key. Record the failure
text.

- [ ] **Step 10: Commit**

```bash
git add tools/p1 .github/workflows/windows-ci.yml
git commit -m "test(shell): add window material harness and defer high contrast"
```

### Task 3: Seamless shell and the Window background setting

**Design direction.** The window works like a field notebook left open on the
desk. The desktop can show through the margins, but never through the page.
Backdrop materials cover the title bar, navigation pane, and route background
as one continuous plane with no panel edge between pane and content. Anything
a person reads for longer than a heading sits on an opaque or card surface:
the reader page, cards, and dialogs. `CardBackgroundFillColorDefaultBrush` is
only about 5% white in dark theme, so the reader page gets its own opaque
brush instead of reusing the card brush. Nothing else changes; there are no
new colors, gradients, or motion. The one visible change is the missing seam.

**Files:**
- Create: `src/DesktopGuides.Production/Materials/ThinAcrylicBackdrop.cs`
- Create: `src/DesktopGuides.Production/Materials/WindowMaterials.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml:7-60, 279-281, 305-322`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (constructor, `InitializeCoreAsync` lines 125-145, resume save lines 632-650)
- Modify: `src/DesktopGuides.Production/Styles/DesignTokens.xaml`

**Interfaces:**
- Consumes: `WindowMaterial`, `AppSettings.WindowMaterial`, `UpdateSettingsAsync` (Task 1); the AutomationIds and status strings in Task 2's Produces list.
- Produces:
  - `internal static class WindowMaterials` with `WindowMaterial Resolve(WindowMaterial requested)` and `SystemBackdrop? CreateBackdrop(WindowMaterial effective)`
  - `internal sealed class ThinAcrylicBackdrop : SystemBackdrop`
  - `ShellWindow.EffectiveMaterial` (`internal WindowMaterial`, private setter), read by Task 4
  - Theme resource `DesktopGuidesReadingSurfaceBrush`

This task makes Task 2's red run green. There is no unit test project for the
Production assembly, so the failing test is Task 2's installed harness.

- [ ] **Step 1: Add the backdrop classes**

`Materials/ThinAcrylicBackdrop.cs`:

```csharp
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DesktopGuides.Production.Materials;

// DesktopAcrylicBackdrop has no Kind, so Thin acrylic needs its own controller.
// The default configuration follows the app theme and turns solid while the
// window is inactive.
internal sealed class ThinAcrylicBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? controller;

    protected override void OnTargetConnected(
        ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };
        controller.SetSystemBackdropConfiguration(
            GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot));
        controller.AddSystemBackdropTarget(connectedTarget);
    }

    protected override void OnTargetDisconnected(
        ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        controller?.Dispose();
        controller = null;
    }
}
```

`Materials/WindowMaterials.cs`:

```csharp
using DesktopGuides.Core.Library;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml.Media;

namespace DesktopGuides.Production.Materials;

internal static class WindowMaterials
{
    public static WindowMaterial Resolve(WindowMaterial requested) => requested switch
    {
        WindowMaterial.Mica when MicaController.IsSupported() => WindowMaterial.Mica,
        WindowMaterial.Acrylic when DesktopAcrylicController.IsSupported() =>
            WindowMaterial.Acrylic,
        _ => WindowMaterial.Solid
    };

    public static SystemBackdrop? CreateBackdrop(WindowMaterial effective) => effective switch
    {
        WindowMaterial.Mica => new MicaBackdrop { Kind = MicaKind.Base },
        WindowMaterial.Acrylic => new ThinAcrylicBackdrop(),
        _ => null
    };
}
```

Do not set tint or luminosity. Doing so turns off the controller's automatic
light and dark switching, which is the likely cause of the reported dark-theme
defect. Keep the defaults and let Task 2's dark Acrylic check confirm them.

- [ ] **Step 2: Make the content layer seamless and add the solid canvas**

In `ShellWindow.xaml`:
- Delete the `<Window.SystemBackdrop>` block (lines 7-9). Code sets the backdrop.
- Give the root `Grid` `x:Name="ShellRoot"` and add, as its first child,
  spanning both rows:

```xml
<Border x:Name="SolidCanvas"
        Grid.RowSpan="2"
        Background="{ThemeResource DesktopGuidesCanvasBrush}"
        Visibility="Collapsed"
        AutomationProperties.AccessibilityView="Raw" />
```

- Inside `<NavigationView ...>`, before `<NavigationView.MenuItems>`:

```xml
<NavigationView.Resources>
    <ResourceDictionary>
        <ResourceDictionary.ThemeDictionaries>
            <ResourceDictionary x:Key="Default">
                <SolidColorBrush x:Key="NavigationViewContentBackground" Color="Transparent" />
                <SolidColorBrush x:Key="NavigationViewContentGridBorderBrush" Color="Transparent" />
            </ResourceDictionary>
            <ResourceDictionary x:Key="HighContrast">
                <StaticResource x:Key="NavigationViewContentBackground" ResourceKey="SystemColorWindowColorBrush" />
                <StaticResource x:Key="NavigationViewContentGridBorderBrush" ResourceKey="SystemColorWindowTextColorBrush" />
            </ResourceDictionary>
        </ResourceDictionary.ThemeDictionaries>
    </ResourceDictionary>
</NavigationView.Resources>
```

`Default` covers Light and Dark. High contrast keeps an explicit window color
and border because backdrops are off there; T16.2 verifies it.

- Change the reader `Border` (line 279) to use the opaque reading surface:

```xml
<Border Grid.Row="3"
        Style="{StaticResource DesktopGuidesElevatedSurfaceStyle}"
        Background="{ThemeResource DesktopGuidesReadingSurfaceBrush}">
```

In `Styles/DesignTokens.xaml`, add `DesktopGuidesReadingSurfaceBrush` to each
theme dictionary next to `DesktopGuidesElevatedSurfaceBrush`:
- Light and Dark: `<StaticResource x:Key="DesktopGuidesReadingSurfaceBrush" ResourceKey="SolidBackgroundFillColorQuarternaryBrush" />`
- HighContrast: `<SolidColorBrush x:Key="DesktopGuidesReadingSurfaceBrush" Color="{ThemeResource SystemColorWindowColor}" />`

Follow the form the neighboring HighContrast entries use.

- [ ] **Step 3: Add the Window background card**

Replace the Settings `ScrollViewer` content (lines 308-322) with a
`StackPanel Spacing="{StaticResource DesktopGuidesSpacing4}"` that holds the
existing `LibraryStorageSettingsCard` unchanged, followed by:

```xml
<toolkit:SettingsCard
    x:Name="WindowMaterialSettingsCard"
    Header="Window background"
    Description="Choose how much of your desktop shows behind the app."
    HorizontalAlignment="Stretch"
    AutomationProperties.AutomationId="WindowMaterialSettingsCard"
    AutomationProperties.Name="Window background. Choose how much of your desktop shows behind the app.">
    <toolkit:SettingsCard.HeaderIcon>
        <FontIcon Glyph="&#xE790;" AutomationProperties.AccessibilityView="Raw" />
    </toolkit:SettingsCard.HeaderIcon>
    <ComboBox x:Name="WindowMaterialSelector"
              MinWidth="160"
              SelectionChanged="WindowMaterialSelectionChanged"
              AutomationProperties.AutomationId="WindowMaterialSelector"
              AutomationProperties.Name="Window background">
        <ComboBoxItem Content="Mica" Tag="Mica" />
        <ComboBoxItem Content="Acrylic" Tag="Acrylic" />
        <ComboBoxItem Content="Solid" Tag="Solid" />
    </ComboBox>
</toolkit:SettingsCard>
```

The item order must match the `WindowMaterial` enum order (Mica, Acrylic,
Solid), because the code selects items by `(int)material`.

If `DesktopGuidesSpacing4` has a different key, use the key defined in
`DesignTokens.xaml`. The ComboBox is disabled until the library is ready:
set `IsEnabled="False"` here and enable it in Step 4.

- [ ] **Step 4: Apply, save, and restore the material in code**

In `ShellWindow.xaml.cs`, add `using DesktopGuides.Production.Materials;` and
these members:

```csharp
private bool applyingMaterialSelection;
internal WindowMaterial EffectiveMaterial { get; private set; } = WindowMaterial.Solid;

private void ApplyWindowMaterial(WindowMaterial requested)
{
    EffectiveMaterial = WindowMaterials.Resolve(requested);
    SystemBackdrop = WindowMaterials.CreateBackdrop(EffectiveMaterial);
    SolidCanvas.Visibility = EffectiveMaterial == WindowMaterial.Solid
        ? Visibility.Visible
        : Visibility.Collapsed;
    ReaderActions.DialogMaterial = EffectiveMaterial;
    applyingMaterialSelection = true;
    WindowMaterialSelector.SelectedIndex = (int)requested;
    applyingMaterialSelection = false;
    if (EffectiveMaterial != requested)
    {
        ShowWarningStatus($"{requested} isn't available on this device. Using Solid.");
    }
}

private async void WindowMaterialSelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (applyingMaterialSelection ||
        repository is null ||
        WindowMaterialSelector.SelectedItem is not ComboBoxItem { Tag: string tag } ||
        !Enum.TryParse(tag, out WindowMaterial requested))
    {
        return;
    }
    WindowMaterial previous = e.RemovedItems.FirstOrDefault() is ComboBoxItem { Tag: string old } &&
        Enum.TryParse(old, out WindowMaterial parsed) ? parsed : WindowMaterial.Mica;
    ApplyWindowMaterial(requested);
    try
    {
        await repository.UpdateSettingsAsync(s => s with { WindowMaterial = requested });
        if (EffectiveMaterial == requested)
        {
            ShowTransientStatus($"Window background set to {requested}.");
        }
    }
    catch (Exception error)
    {
        ApplyWindowMaterial(previous);
        ShowErrorStatus($"Could not save the window background: {error.Message}");
    }
}
```

The selector shows the requested material even when it falls back to Solid,
so the saved choice returns once the device supports it again. The warning
explains the difference.

The `ReaderActions.DialogMaterial` line needs Task 4. Until Task 4 lands,
comment it out; do not stub the property here.

In the constructor, after `SetTitleBar(AppTitleBar);`, call
`ApplyWindowMaterial(WindowMaterial.Mica);`. This keeps today's startup look
before the library loads.

In `InitializeCoreAsync`, after `await repository.InitializeAsync();`:

```csharp
try
{
    ApplyWindowMaterial((await repository.GetSettingsAsync()).WindowMaterial);
}
catch (InvalidDataException)
{
    ShowWarningStatus("Could not read the window background setting. Using Mica.");
}
WindowMaterialSelector.IsEnabled = true;
```

A corrupt settings row must not block the library. Other settings reads keep
their current behavior.

- [ ] **Step 5: Make the Resume write atomic**

In the open-guide path (around line 646), replace

```csharp
await library.SaveSettingsAsync(settings with { LastActiveGuideId = guide.Id });
```

with

```csharp
await library.UpdateSettingsAsync(s => s with { LastActiveGuideId = guide.Id });
```

If `settings` has no other use, keep the call as
`await library.GetSettingsAsync();`. That line still checks stored settings
before navigation and keeps the await point the `queue-guide` harness modes
use. Search for any other `SaveSettingsAsync(` callers in `src/` and convert
them the same way.

- [ ] **Step 6: Build both architectures and run the installed harness**

```bash
ssh -o BatchMode=yes pcsx2-win "cd /d E:\\work\\desktop-guides\\$RUN && dotnet build src\\DesktopGuides.Production -c Release -p:Platform=x64 && dotnet build src\\DesktopGuides.Production -c Release -p:Platform=ARM64"
```

Use the package-build commands in `docs/p1/e2e-testing.md` for the MSIX. Then
run the design-only install from Task 2 Step 9 into `results-green`.

Expected: all design-language passes and all six material passes succeed.
Solid strips have a range of 2 or less in both themes, and Acrylic differs
from Solid in both themes. Open the dark Acrylic library screenshot and check
by eye that the wallpaper shows through the pane and route background while
the reader page stays opaque.

- [ ] **Step 7: Commit**

```bash
git add src/DesktopGuides.Production
git commit -m "feat(ui): seamless window materials with a Window background setting"
```

### Task 4: Dialogs match the window material

Modal dialogs stay `ContentDialog`. In Acrylic mode they use in-app acrylic,
which blurs the app's own content behind the dialog. In Mica and Solid modes
they keep the default solid dialog, as Windows Settings does. Owned modal
windows were considered and rejected: they add HWND ownership code and a
second backdrop without a workflow that needs one.

**Files:**
- Create: `src/DesktopGuides.Production/Styles/Dialogs.xaml`
- Create: `src/DesktopGuides.Production/Materials/DialogSurface.cs`
- Modify: `src/DesktopGuides.Production/App.xaml` (merge `Dialogs.xaml`)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs:440, 503` (GameEditorDialog creation)
- Modify: `src/DesktopGuides.Production/ReaderToolbar.xaml.cs:117-135` (`PromptAsync`)
- Modify: `tools/p1/DesktopGuides.ReaderToolbarSmoke/DesktopGuides.ReaderToolbarSmoke.csproj`, `tools/p1/DesktopGuides.ReaderToolbarSmoke/App.xaml`

**Interfaces:**
- Consumes: `ShellWindow.EffectiveMaterial` (Task 3), `WindowMaterial` (Task 1).
- Produces: `internal static class DialogSurface { static void Apply(ContentDialog dialog, WindowMaterial material); }`, `ReaderToolbar.DialogMaterial` (`internal WindowMaterial`, default `Mica`), and resource key `DesktopGuidesAcrylicDialogStyle`.

- [ ] **Step 1: Add the failing dialog check to the harness**

In the `material` smoke mode (Task 2 Step 7), after the edit-game screenshot
and before `{ESC}`, sample the dialog. With the dialog open, the root's
descendants include `GameEditorDialog`:

```powershell
$dialog = (Wait-VisibleById 'GameEditorDialog').Current.BoundingRectangle
$window = $root.Current.BoundingRectangle
$report.dialogStrip = Get-ScreenshotRegionStats $report.dialogScreenshot `
    ([int]($dialog.Left - $window.Left + 8)) ([int]($dialog.Bottom - $window.Top - 12)) 48 4
```

This samples the command area near the bottom-left, below the buttons' row
baseline. If it overlaps a button on the host, move it up into the content
padding and record the offset. In `Run-MaterialScenarios`, after the Acrylic
check, add:

```powershell
$solidDialog = $report.materials["$themeName-Solid"].dialogStrip
$acrylicDialog = $report.materials["$themeName-Acrylic"].dialogStrip
$dialogDifference = [Math]::Max([Math]::Abs($acrylicDialog.meanR - $solidDialog.meanR),
    [Math]::Max([Math]::Abs($acrylicDialog.meanG - $solidDialog.meanG),
        [Math]::Abs($acrylicDialog.meanB - $solidDialog.meanB)))
$report.materials["$themeName-dialogDifference"] = $dialogDifference
if ($dialogDifference -le 2) {
    throw "The $themeName Acrylic dialog matched the Solid dialog."
}
```

Run the design-only install. Expected: FAIL with `Acrylic dialog matched the
Solid dialog` in both themes.

- [ ] **Step 2: Add the style and helper**

`Styles/Dialogs.xaml`:

```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Style x:Key="DesktopGuidesAcrylicDialogStyle"
           TargetType="ContentDialog"
           BasedOn="{StaticResource DefaultContentDialogStyle}">
        <Setter Property="Background" Value="{ThemeResource AcrylicInAppFillColorDefaultBrush}" />
    </Style>
</ResourceDictionary>
```

Merge it in `App.xaml` after `Controls.xaml`.

`Materials/DialogSurface.cs`:

```csharp
using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production.Materials;

internal static class DialogSurface
{
    public static void Apply(ContentDialog dialog, WindowMaterial material) =>
        dialog.Style = (Style)Application.Current.Resources[
            material == WindowMaterial.Acrylic
                ? "DesktopGuidesAcrylicDialogStyle"
                : "DefaultContentDialogStyle"];
}
```

- [ ] **Step 3: Apply it to both dialogs**

In `ShellWindow.xaml.cs`, at both `GameEditorDialog` creation sites, after
the object initializer:

```csharp
DialogSurface.Apply(editor, EffectiveMaterial);
```

Uncomment the `ReaderActions.DialogMaterial = EffectiveMaterial;` line from
Task 3 Step 4.

In `ReaderToolbar.xaml.cs`, add
`internal WindowMaterial DialogMaterial { get; set; } = WindowMaterial.Mica;`
plus the `using` lines for `DesktopGuides.Core.Library` and
`DesktopGuides.Production.Materials`. In `PromptAsync`, call
`DialogSurface.Apply(dialog, DialogMaterial);` after constructing `dialog`.

- [ ] **Step 4: Link the new files into the toolbar smoke**

In `DesktopGuides.ReaderToolbarSmoke.csproj`, next to the existing links:

```xml
<Page Include="../../../src/DesktopGuides.Production/Styles/Dialogs.xaml"
      Link="Styles/Dialogs.xaml" />
<Compile Include="../../../src/DesktopGuides.Production/Materials/DialogSurface.cs"
         Link="Materials/DialogSurface.cs" />
```

In its `App.xaml`, merge `<ResourceDictionary Source="ms-appx:///Styles/Dialogs.xaml" />`
after `DesignTokens.xaml`.

- [ ] **Step 5: Check the command area, then run the harnesses**

Build, install, and run the design-only pass again. Expected: PASS, with the
dialog difference above 2 in both themes. Open both Acrylic edit-game
screenshots. If the button row is still a solid band, the template paints it
with a separate resource. Find the key in the pinned WinUI `generic.xaml` in
the host NuGet cache (`microsoft.windowsappsdk.winui\2.3.9`, search
`ContentDialog` for `CommandSpace`). Override it in the style's
`Style.Resources` as Transparent, then rebuild. Record the key in the
verification record.

Then run the reader toolbar installed smoke
(`tools/p1/windows_reader_toolbar_install.ps1`, per `e2e-testing.md`) to
confirm the page-jump dialog still opens, accepts input, and restores focus.

- [ ] **Step 6: Commit**

```bash
git add src/DesktopGuides.Production tools/p1
git commit -m "feat(ui): match dialogs to the window material"
```

### Task 5: Record the design, deferrals, and T11.5

No tests: this task only edits docs. Every claim in it must match the
`results-green` evidence from Tasks 3 and 4.

**Files:**
- Modify: `docs/p1/t11-design-language-plan.md` (Color and surfaces, Implementation sequence, Verification record)
- Modify: `docs/p1-technical-design.md:288, 364`, `docs/initial-design.md:89`
- Modify: `docs/p1/implementation-plan.md:7, 605, 609, 614, 664, 679` plus a new T11.5 row after 614
- Modify: `docs/work-breakdown.md` (S11 task list near 338, T16.2 near 463)
- Modify: `docs/p1/e2e-testing.md:32-39, 181`
- Modify: `docs/p1/results.md`, `docs/progress.md:14-15`
- Create/replace: `docs/p1/evidence/t11-design-language/windows-11-x64-result.json` and screenshots

- [ ] **Step 1: Update the design record**

In `t11-design-language-plan.md` under "Color and surfaces", replace the
sentences from "The long-lived main window uses a Mica system backdrop" to
"light-dismiss surfaces." with:

> The window backdrop runs unbroken behind the title bar, navigation pane, and
> route background; the `NavigationView` content layer and its border are
> transparent. A Settings choice selects Mica (default), Thin Acrylic, or
> Solid. Unsupported materials fall back to Solid with a warning. The backdrop
> follows the app theme and turns solid while the window is inactive. Reading
> surfaces, cards, and dialogs stay opaque or card-filled so text never sits
> directly on the wallpaper. In Acrylic mode, `ContentDialog` uses in-app
> acrylic; in Mica and Solid it keeps the default solid dialog.

Add `Reading surface | SolidBackgroundFillColorQuarternaryBrush` to the role
table. In "Installed visual check", replace "system light, dark, and high
contrast" with "system, light, and dark, and in each window material; high
contrast moves to T16.2". Append a paragraph to the verification record with
the new counts, the calibrated Acrylic and dialog thresholds, and the observed
differences.

In `p1-technical-design.md` (288 and 364) and `initial-design.md` (89), change
"Acrylic is reserved for transient…" to say Acrylic is also an optional
full-window material chosen in Settings, and transient flyouts keep their
default acrylic.

- [ ] **Step 2: Update task tables and add T11.5**

In `implementation-plan.md`:
- Line 7: "53 tasks" becomes "54 tasks". Search for any other "53" task counts,
  including `docs/progress.md:15`, and update them.
- Lines 605 and 614: add "seamless window backdrop with a Mica, Acrylic, or
  Solid setting and matching dialogs", and remove "high-contrast" from the
  checks. Add "High contrast moves to T16.2."
- Add a T11.5 row after 614:

  `| T11.5 | T11.4, T14.4 | Optional colorful icons: a Settings "Colorful icons" toggle swaps navigation and route icons for Fluent UI System Icons `*_color` SVGs (MIT) packaged as assets and shown through `ImageIcon`. Monochrome Segoe Fluent icons stay the default and are always used in high contrast. Icon set, license notice, asset size, theme and high-contrast screenshots, and UIA names pass. WinUI Gallery has no colored icon set, so this uses the external library. | TR11.3 |`

- In the design-adoption table near 608, add a `T11.5 colorful icons` row:
  prerequisites `T14.4`; runs before T16.2 so the audit sees the final icons.
- Lines 609 and 679 (T16.2): add T11.5 to the prerequisites. Add: "Run the
  high-contrast pass deferred from T11.4. Before enabling high contrast, save
  the active `.theme` path and wallpaper, and restore them exactly afterward.
  Alternatively, run in a disposable Windows profile or VM."

In `work-breakdown.md`, add T11.5 under S11 with the same outcome in that
file's bullet style, and add the high-contrast restore rule to T16.2.

- [ ] **Step 3: Update the E2E procedure**

In `e2e-testing.md`, lines 32-39: drop the high-contrast pass and its
watchdog note from `-DesignOnly`. Describe the material passes instead: light
and dark × Solid, Acrylic, Mica, plus switch-and-relaunch. Say that the
harness puts the stored material back to Mica and restores the app theme.
Line 181: replace "system/light/dark/high-contrast" with
"system/light/dark and each window material", and add the seam, backdrop, and
dialog checks. Add a note that high contrast runs only in T16.2 because
Windows rewrites the active theme to `Custom.theme` and the harness cannot
restore it.

- [ ] **Step 4: Replace the evidence**

Copy the sanitized `results-green` report and these screenshots into
`docs/p1/evidence/t11-design-language/`: library wide and narrow (light and
dark), and the library, reader, and edit-game screenshots for each material in
light and dark. Strip usernames, the SID, and absolute user paths, as the
current JSON does. Remove the high-contrast screenshots, whose claims no
longer apply. Git history keeps them.

- [ ] **Step 5: Update the status rows**

In `docs/p1/results.md`, add the material and dialog results and the
high-contrast deferral to the T11.4 section. In `docs/progress.md` line 14,
replace "in system, light, dark, and high contrast" with "in system, light,
and dark themes and all three window materials", update the test counts, and
add "High contrast moves to T16.2; colorful icons are T11.5."

- [ ] **Step 6: Commit**

```bash
git add docs
git commit -m "docs(p1): record window materials, T11.5, and high-contrast deferral"
```

## Final verification

Run on the host from a fresh snapshot of the branch head (`git archive HEAD`):

- [ ] Locked restores: `dotnet restore --locked-mode` for every project the CI
      workflow restores.
- [ ] `dotnet test` Core (73) and Infrastructure (98). Record the actual
      counts.
- [ ] All `tools/p1/test_*.ps1` harness tests, including the new stats test.
- [ ] x64 and ARM64 production package builds.
- [ ] `windows_shell_install.ps1 -DesignOnly`, then the full regression,
      through the interactive scheduled task with the `e2e-testing.md` backup
      and cleanup. Confirm the high-contrast flags, the `.theme` path, and the
      app theme are unchanged afterward.
- [ ] Reader toolbar installed smoke.
- [ ] Manual: with the app open in Acrylic, switch Windows between light and
      dark and confirm the backdrop and canvas follow. Then open Edit game and
      switch again (Review Focus 3). Record the result in the verification
      record.
- [ ] Manual: record `EnableTransparency`, set it to 0, and relaunch in Mica and
      then Acrylic. Confirm the text stays legible on the fallback color, then
      restore the recorded value and confirm it reads back (Review Focus 2).
- [ ] Update the PR #13 description with the target task (T11.4),
      prerequisites (T11.1 and T11.3 merged), the outcome, and the new
      screenshots.
