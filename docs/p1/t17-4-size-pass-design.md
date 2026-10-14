# T17.4 distribution size pass design

Status: PR a merged in #106 (merge commit `1c01e75`). PR b1 (trim-safe
code and the warning gate) is implemented on `feat/p1-t17-4-trim`; PR b2
(the trimmed, compressed exe) follows.
Prerequisites T17.1 and T17.2 are merged (T17.2's last piece in #104).

## Intent

Make both builds smaller on the final feature set without changing what the
app does, and record what each release weighs and how fast it starts.

Traces: T17.4 in [implementation-plan.md](implementation-plan.md) (M6) and
[work-breakdown.md](../work-breakdown.md) T17.4. The starting point is the
[portable size spike](results.md#portable-size-spike--8-october-2026)
(8 October 2026):

| Variant | Exe | Extraction | Cold / warm launch | Result |
| --- | --- | --- | --- | --- |
| `80d2647` | 244.0 MB | 551 files, 233.3 MB | 2.2 s / 0.5 s | passes |
| V1: components excluded | 181.8 MB | 485 files, 171.2 MB | 1.5 s / 0.5 s | passes |
| V2c: V1 trimmed and compressed | 41.7 MB | 355 files, 84.0 MB | 3.0 s / 0.65 s | crashes |

## PR a: exclude the unused components, and measure

### The change

`Microsoft.WindowsAppSDK` 2.5.1 stays a direct reference: the Community
Toolkit packages depend on the metapackage, and referencing only the used
components made NuGet resolve the Toolkit's metapackage at 1.6, which
collides with the 2.x components.

The Production project adds direct references with `ExcludeAssets="all"`
to the components the app doesn't use, at the versions the lock file
resolves today, with central versions in `Directory.Packages.props`:

| Package | Version |
| --- | --- |
| `Microsoft.WindowsAppSDK.AI` | 2.5.5 |
| `Microsoft.WindowsAppSDK.ML` | 2.1.94 |
| `Microsoft.WindowsAppSDK.Search` | 2.5.5 |
| `Microsoft.WindowsAppSDK.Widgets` | 2.0.5 |
| `Microsoft.WindowsAppSDK.DWrite` | 2.1.0 |
| `Microsoft.Windows.AI.MachineLearning` | 2.1.74 |

The lock files are regenerated. Both builds get the change. The app keeps
Foundation (file pickers, app lifecycle), WinUI and WebView2, and loads
only the system `DWrite.dll`, so text rendering doesn't change.

### The component check (written first)

CI's build verification gains a check that fails before the change and
passes after it:

- **Portable output:** the published folder contains none of the excluded
  components' files: `DWriteCore.dll`, `onnxruntime.dll`, `DirectML.dll`,
  and the `Microsoft.Windows.AI.*`, ML, Search and Widgets assemblies and
  WinMDs. The exact list is taken from the 551 → 485 file difference
  between a default and a V1 publish.
- **MSIX manifest:** the package still declares the
  `Microsoft.WindowsAppRuntime.2` dependency at 2.5.1.0 or later.

### Measurement

A new script, `tools/p1/measure_distribution.ps1`, measures a release's
builds. `release.yml` runs it after building the portable release and
writes its result into the release manifest as a `measurements` block:

- **Sizes:** the MSIX and portable exe in bytes, and the extraction's file
  count and total bytes.
- **Cold launch:** from starting the portable exe, with its
  `%TEMP%\.net` extraction folder removed first, until UI Automation finds
  the opened, empty library view (`LibraryEmpty`). The installed smoke
  waits for the same element, so the app doesn't change. (The navigation
  item `LibraryNavigation` shows from the first frame, before the library
  opens, so it doesn't mark readiness; see the PR a verification record.)
- **Warm launch:** the same, on a following launch.
- **Runs:** the median of 3 runs for each launch time.

Launches go through the interactive scheduled task
(`start_installed_task.ps1`), as installed runs do, and use a scratch
portable data folder that the script removes afterwards, with the
extraction folder.

Launch times are recorded for the portable build only. The MSIX has no
extraction, and trimming doesn't apply to it, so the size pass doesn't
change how it starts. PR a records times without a limit.

### Verification

- The component check fails before the change and passes after it.
- All 10 installed shards pass on both lanes.
- A results.md entry records the measured sizes and times next to the
  spike's `80d2647` and V1 rows. Runner launch times are noisier than the
  host's, so the first CI figures are a new baseline, not a comparison
  with the spike.

## PR b: a trim-safe, trimmed portable build

Only the portable build is trimmed.

### 1. The warning gate (written first)

The portable publish sets `EnableTrimAnalyzer` and
`ILLinkTreatWarningsAsErrors`, and `WarningsAsErrors` adds the trim
analysis codes (IL2xxx, IL3xxx, including `IL2026` and `IL2104`) and
`CsWinRT1028` and `CsWinRT1030`. CI's portable build must then fail on the
20 known warnings, which shows the gate catches them.

If an assembly the project doesn't own (`WinRT.Runtime`, the Windows SDK
projection) can't be made warning-free, it goes on a short allow-list.
Each entry silences one warning code for one assembly, with its reason.

### 2. The warning fixes

- **CsWinRT1028 (4):** `GameSearchItem`, `GuideRowItem`, `LibraryGameItem`
  and `ProviderServices` become `partial`.
- **CsWinRT1030 (9):** the Production project sets `AllowUnsafeBlocks` so
  C#/WinRT can generate the generic-interface code.
- **IL2026 (7):** the `System.Text.Json` calls in `HtmlPositionScripts`,
  `ExternalLinkLaunchers`, `HtmlReaderSession` and `ShellWindow.Progress`
  move to a source-generated `JsonSerializerContext`. Unit tests written
  first pin each payload's current JSON, so the messages sent to WebView2
  don't change.
- **IL2104, AngleSharp:** a review lists where AngleSharp and AngleSharp.Css
  use reflection and whether the import and sanitise path reaches it. If
  it can't be shown trim-safe, AngleSharp is kept whole as a
  `TrimmerRootAssembly`, and the size cost is recorded.
- **Referenced projects:** anything the Core and Infrastructure projects
  raise is fixed the same way.

### 3. The trimmed crash

The spike's trimmed builds crashed with a stowed exception (`0xc000027b`)
in `Microsoft.UI.Xaml.dll`. The likely cause is XAML failing to find types
trimming removed, which the `partial` fixes may cure. Before trimming
ships, the root cause is found with systematic debugging: the stowed
exception and its inner error from a crash dump and the event log on the
host, and then, if it persists, `TrimmerRootAssembly` one assembly at a
time. The cause is recorded here.

### 4. The trimmed, compressed portable build

The portable publish sets `PublishTrimmed`, `TrimMode=partial` and
`EnableCompressionInSingleFile`. `TrimMode=full` is out of scope.

It ships only if all of these hold:

- all 10 installed shards pass on both lanes;
- the PR a component check and measurements run on the trimmed build;
- on pcsx2-win, the spike's machine, the median cold launch is **2.5 s or
  less**, with warm launch recorded. CI timings are too noisy to gate on.

If cold launch is over the limit, ReadyToRun is tried next, trading some
size for start time. The spike's trimmed launch times came from crashing
builds, so the baseline is PR a's measurement, not the spike's.

If the trimmed build still doesn't pass, the portable build ships
untrimmed as in PR a. The warning fixes and the gate still merge, so later
trimming starts clean. The outcome goes in results.md, and the T17.4 plan
row is revised with the user, since it calls for a trimmed build.

### Verification

- The gate's red run, then its green run.
- The crash's root cause.
- A size and time table: trimmed against PR a against the spike.
- The host launch figures.

## PR a verification record

- **The check failed first.** Before the exclusion, on the host:
  - the portable bundle list (716 entries, exe 246.0 MB) failed, naming
    `DWriteCore.dll`, `onnxruntime.dll`, `DirectML.dll`, the Workloads,
    Search and Widgets DLLs and the AI projections;
  - the Preview MSIX (70.3 MB) failed, naming `DirectML.dll`,
    `onnxruntime.dll`, `Microsoft.ML.OnnxRuntime.dll` and the AI, Search,
    Widgets and MachineLearning projection DLLs.
- **Then it passed.** After it, both builds passed: "none of 60 file
  names". Both locked restores passed, and the portable publish had no
  warnings. The exe was 183.8 MB with 650 bundle entries, and the MSIX
  52.6 MB.
- **The lock files:** the Production lock file changed only the six
  packages' entries, with no resolved version moving. The P0 app and the
  toolbar smoke lock files changed only those packages' `type`, to
  `CentralTransitive`.
- **CI dispatch**
  [38492789165](https://github.com/ilya-slalom/desktop-guides/actions/runs/38492789165)
  (`core` on the portable lane): the component check passed on both MSIX
  lanes, the Public MSIX and the bundle list, and `portable-measure`
  passed. It also found the two stale lock files (NU1004).
- **Release dry run**
  [38494268276](https://github.com/ilya-slalom/desktop-guides/actions/runs/38494268276)
  (tag v1.0.1) passed, and its manifest recorded `measurements`.
- **After the final review's fix,** whose ready signal is `LibraryEmpty`
  (ruling 6): dry run 38495508903 failed, because the readiness closure got
  null UI Automation values (`GetNewClosure` keeps only a function's
  locals). After that fix, dispatch
  [38496516651](https://github.com/ilya-slalom/desktop-guides/actions/runs/38496516651)
  passed every job it ran, including `packages`, `reader-toolbar-ui` and
  `portable-measure`. Dry run
  [38496519722](https://github.com/ilya-slalom/desktop-guides/actions/runs/38496519722)
  passed, with `readyWhen` naming `LibraryEmpty`.
- **Rulings:**
  1. The check reads the bundle's input list (`FilesToBundle`), because a
     single-file publish folder holds only the exe.
  2. The forbidden names are the six packages' own `.dll` and `.winmd`
     files.
  3. The MSIX payload is checked as well as its runtime dependency.
  4. Measurement has its own CI job, `portable-measure`.
  5. Central versions for the six packages also changed the P0 app's and
     the toolbar smoke's lock files. They were regenerated rather than the
     versions moved into the Production project.
  6. Launches are ready at `LibraryEmpty`, not `LibraryNavigation`. The
     final review found that the navigation item exists before the library
     opens, so a failed open would still have counted as ready. A launch
     that shows `LibraryUnavailable` fails the run, and one that never
     reaches the empty library times out. The first measurements (the
     dispatch and dry run above) used the navigation item and are
     superseded.

Sizes and times are in [results.md](results.md#t174-pr-a-components-excluded--14-october-2026).

## PR b1 verification record

PR b splits in two: **b1** (sections 1 and 2: trim-safe code and the
warning gate) and **b2** (sections 3 and 4: the crash and the trimmed,
compressed exe). See the [b1 plan](t17-4-trim-plan.md).

- **The gate reads the publish log.** MSBuild can't suppress IL2104 for
  one assembly, so `tools/p1/assert_trim_warnings.ps1` checks a trimmed
  publish's log against `tools/p1/trim-warning-allowlist.txt`. It accepts
  only IL2104 entries for assemblies the project doesn't own, each with a
  reason, and it fails on stale entries and on a log without trimming. CI
  runs it in `portable-trim-analysis`.
- **The gate failed first.** On the host's trimmed publish of the base
  commit, it listed:
  - CsWinRT1028 in 4 files;
  - CsWinRT1030 in 7 files;
  - IL2026 in 13 files (5 Core, 4 Infrastructure, 4 Production);
  - IL2104 for AngleSharp, `Microsoft.Windows.SDK.NET` and `WinRT.Runtime`.
- **C#/WinRT:** after four `partial` types and `AllowUnsafeBlocks`, no
  CsWinRT warning is left, and the MSIX build has 0 warnings.
- **JSON:**
  - **Pinned first:** exact-output tests pinned each Core and
    Infrastructure payload, and passed on the old code (5 and 6 tests).
  - **Then red:** with reflection JSON switched off in both test
    projects, 19 Core and 385 Infrastructure tests failed. The
    Infrastructure count is high because imports, removals and restores
    all write a file-operation manifest.
  - **Then green:** after the source-generated contexts, Core passed
    1172/1172 and Infrastructure 790/790.
  - **Production:** its four files are checked by the gate and by the
    installed groups that read their payloads.
- **The AngleSharp review:**
  - **Where it's used:** only `StaticHtmlDependencyScanner`, through
    `HtmlParser` and `CssParser`.
  - **Its reflection:** the warnings come from
    `DocumentExtensions.CreateElement<TElement>` and
    `ObjectExtensions.ToDictionary`. The app calls neither.
  - **Trimmability:** neither AngleSharp nor AngleSharp.Css declares
    `IsTrimmable`, in any target framework, so `TrimMode=partial` keeps
    both whole.
  - **The decision:** its IL2104 is allowed. PR b2 needs no root for it
    while the build stays at `TrimMode=partial`.
- **The allow-list:** `IL2104` for AngleSharp, `Microsoft.Windows.SDK.NET`
  and `WinRT.Runtime`.
- **CI dispatch
  [38505896730](https://github.com/ilya-slalom/desktop-guides/actions/runs/38505896730)**
  (`html` on both lanes): every job passed. `core-tests` ran the gate's
  tests under pwsh 7, `portable-trim-analysis` reported "Trim warnings:
  none new; 3 allowed.", and the `html` shards passed on both lanes.
- **Rulings:**
  1. The gate reads the publish log; see the first bullet.
  2. PR b splits into b1 and b2.
  3. Production's JSON is checked by the gate and the installed shards,
     because that project has no unit-test project.
  4. Reflection JSON is off in the Core and Infrastructure test projects.
  5. The gate resolves its default paths in its body. Windows PowerShell
     5.1 leaves `$PSScriptRoot` empty in parameter defaults under `-File`,
     so the first version crashed on its first real run. A test now runs
     it that way.
  6. Two test-side JSON calls needed the explicit reflection path: one
     test's options, and a `JsonArray.Add<T>` call.

## Out of scope

- Trimming the MSIX.
- `TrimMode=full` and Native AOT.
- A launch-time gate in CI.
