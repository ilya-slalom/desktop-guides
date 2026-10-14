# T17.4 PR b2 (Trimmed, Compressed Portable Exe) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The portable release ships trimmed and compressed, passing
every scenario group and starting in a 2.5 s median cold launch on the
host. If it can't, it ships as before, and that outcome is recorded.

**Architecture:**
- **The build:** the Portable PropertyGroup sets `PublishTrimmed`,
  `TrimMode=partial` and `EnableCompressionInSingleFile`.
- **The gate:** it moves onto the real portable publish in
  `portable-packages` and in `release.yml`, so the separate
  `portable-trim-analysis` job is removed.
- **The host check:** on pcsx2-win, the existing measurement script times
  the untrimmed and trimmed builds side by side. The user's portable data
  folder is renamed aside first and restored afterwards, checked against a
  before-listing.

**Tech Stack:** MSBuild/.NET single-file publishing, ILLink, PowerShell 5.1
and 7, GitHub Actions.

**Spec:** `docs/p1/t17-4-size-pass-design.md`, PR b sections 3 and 4.

**Target:** T17.4 PR b2, on `feat/p1-t17-4-trimmed-exe`.
**Prerequisites:** T17.4 PR a (#106) and PR b1 (#107), both merged.

## Global Constraints

- **Build settings:** `TrimMode=partial`. `TrimMode=full` and Native AOT
  are out of scope.
- **Ship condition:** every installed portable shard passes; the gate
  reports no new warning; and the host median cold launch is 2500 ms or
  less.
- **The fallback order:** if cold launch is over the limit, add
  ReadyToRun. If it is still over, ship untrimmed, keeping the gate, and
  record the outcome. The T17.4 plan row is then revised with the user.
- **The user's data on pcsx2-win** (the user chose "rename aside,
  restore"):
  - rename `%LOCALAPPDATA%\DesktopGuides` to a timestamped name, and list
    its files (path, size, write time) on the host only;
  - read nothing else, and copy nothing off the host;
  - restore it afterwards, and confirm the listing matches;
  - if `DesktopGuides.Production` is running, stop and ask instead.
- **Shell rules:** PowerShell stays ASCII-only and runs under 5.1 and 7.
- **No `cd` in local shell commands.**

## The root cause (from the b2 spike)

The spike's throwaway branches are deleted. Their runs:
- **38511158128:** main after #107, trimmed: every portable group passed.
- **38513401525 (A):** the code before #107, trimmed: it crashed.
- **38513404216 (B):** A plus #107's C#/WinRT commit.

Two causes:

1. **The crash** (stowed `0xc000027b`). XAML reads list items through
   `IList.GetAt`, and C#/WinRT then builds a COM wrapper for each item at
   runtime. For WinRT-exposed types that weren't `partial`,
   `WinRT.TypeExtensions.GetAbiToProjectionVftblPtr` threw
   `NullReferenceException`, because trimming had removed the helper it
   looks up. A crashed; B didn't. This is the path that CsWinRT1028 and
   CsWinRT1030 warn about.
2. **The functional failures.** `PublishTrimmed` turns reflection-based
   JSON off by default. In B, every reflection JSON call threw "Reflection-based
   serialization has been disabled". Guide removal failed with
   `GuideRemovalException`, and a reader scenario timed out. After #107's
   source-generated JSON, every group passes.

The spike's trimmed exe was 96.7 MB, against 183.9 MB untrimmed. It
extracted 437 files (86.2 MB). Its runner cold and warm launches were
3.9 s and 1.5 s.

## Review Focus

1. **A trimmed member that is used only at runtime,** such as XAML
   reflection, a converter or a resource, and only on a path the shards
   don't reach. Pinned by every portable shard, and by the gate's
   allow-list review. Residual risk is stated in the PR.
2. **The release build differs from CI's,** as Public lane against
   Preview. Pinned by the release dry run in Task 2, which runs the gate
   and the measurement on the Public build.
3. **The user's data folder isn't restored exactly.** Pinned by Task 3's
   before and after listing comparison, which stops on any difference.
4. **Compression slows cold launch.** Pinned by Task 3, which measures the
   untrimmed build and the trimmed, compressed build in the same session.
5. **The gate passes on a log that didn't trim,** for example if a
   property override drops `PublishTrimmed`. Pinned by the gate's
   "shows no trimming" check, and its three allow-list entries, which go
   stale without IL2104 lines.

## Host commands

```bash
s(){ ssh -o BatchMode=yes -o ConnectTimeout=15 -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t17-4c) { Remove-Item -Recurse -Force E:\work\desktop-guides\t17-4c }; New-Item -ItemType Directory E:\work\desktop-guides\t17-4c | Out-Null"'
  COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --exclude=.claude --exclude=.git --exclude=.superpowers --exclude='*/bin' --exclude='*/obj' --exclude=artifacts -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t17-4c'
}
```

---

### Task 1: Ship the portable build trimmed and compressed, with the gate on the real publish

**Files:**
- Modify: `src/DesktopGuides.Production/DesktopGuides.Production.csproj`
  (the Portable PropertyGroup)
- Modify: `.github/workflows/windows-ci.yml` (`portable-packages`' publish
  step; remove the `portable-trim-analysis` job)
- Modify: `.github/workflows/release.yml` ("Build the portable release")

This is a configuration change, so TDD's red step comes from CI: the
installed shards and the gate are its tests. The skip is stated here.

- [ ] **Step 1: Trim and compress the portable build**

In the Portable PropertyGroup, after `IncludeAllContentForSelfExtract`:

```xml
    <!-- T17.4: trimmed and compressed. TrimMode=partial trims only
         assemblies marked trimmable; the app, AngleSharp and the Toolkit
         stay whole. tools/p1/assert_trim_warnings.ps1 gates the warnings. -->
    <PublishTrimmed>true</PublishTrimmed>
    <TrimMode>partial</TrimMode>
    <CsWinRTAotWarningLevel>2</CsWinRTAotWarningLevel>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
```

- [ ] **Step 2: Gate the real publish in CI, and drop the analysis job**

In `portable-packages`' "Build and zip the portable exe", the publish
gains a file logger, and the gate runs after the component check:

```powershell
          $publish = "$PWD/artifacts/portable-ci-publish"
          dotnet publish src/DesktopGuides.Production/DesktopGuides.Production.csproj `
            -c Release --no-restore -p:Platform=x64 -p:Portable=true `
            -p:PublishDir=$publish/ -p:BundleFileListPath=$PWD/artifacts/portable-bundle-files.txt `
            "-flp:logfile=$PWD/artifacts/portable-publish.log;verbosity=normal"
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          ./tools/p1/assert_excluded_components.ps1 -FileList artifacts/portable-bundle-files.txt
          ./tools/p1/assert_trim_warnings.ps1 -Log artifacts/portable-publish.log
          ./tools/p1/package_portable_release.ps1 -Commit $env:GITHUB_SHA `
            -PublishDirectory $publish -OutputDirectory artifacts/portable-ci
```

Delete the whole `portable-trim-analysis` job, including its comment. Its
check now runs on the shipped exe.

- [ ] **Step 3: Gate the release publish**

In `release.yml` "Build the portable release", add
`"-flp:logfile=$PWD/artifacts/portable-release-publish.log;verbosity=normal"`
to the `dotnet publish` call. In "Check the release builds for excluded
components", add a last line:

```powershell
          ./tools/p1/assert_trim_warnings.ps1 -Log artifacts/portable-release-publish.log
```

and rename the step "Check the release builds for excluded components and
trim warnings".

- [ ] **Step 4: Check the build and the gate on the host**

```bash
stage
s 'cd /d E:\work\desktop-guides\t17-4c && dotnet restore src\DesktopGuides.Production\DesktopGuides.Production.csproj --locked-mode -p:Platform=x64 -p:Portable=true -nologo && dotnet publish src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release --no-restore -p:Platform=x64 -p:Portable=true -p:PublishDir=E:\work\desktop-guides\t17-4c\artifacts\trimmed\ -p:BundleFileListPath=E:\work\desktop-guides\t17-4c\artifacts\bundle-files.txt -nologo "-flp:logfile=E:\work\desktop-guides\t17-4c\artifacts\portable-publish.log;verbosity=normal"' | tail -2
s 'cd /d E:\work\desktop-guides\t17-4c && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\assert_trim_warnings.ps1 -Log artifacts\portable-publish.log & powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\assert_excluded_components.ps1 -FileList artifacts\bundle-files.txt & dir artifacts\trimmed'
```

Expected:
- the locked restore and the publish succeed;
- `Trim warnings: none new; 3 allowed.`;
- `Excluded components: none ...`;
- `artifacts\trimmed` holds only `DesktopGuides.Production.exe`, of
  about 40-50 MB (the spike's V2c was 41.7 MB).

Then check that the MSIX build is unaffected, because it doesn't set
`Portable`:

```bash
s 'cd /d E:\work\desktop-guides\t17-4c && dotnet restore src\DesktopGuides.Production\DesktopGuides.Production.csproj --locked-mode -p:Platform=x64 -nologo && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release --no-restore -p:Platform=x64 -nologo' | grep -E "Warning\(s\)|Error\(s\)"
```

Expected: `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 5: Commit, push, and run every portable shard**

```bash
git add src/DesktopGuides.Production/DesktopGuides.Production.csproj .github/workflows/windows-ci.yml .github/workflows/release.yml
git commit -m "build(p1): ship the portable exe trimmed and compressed, gated on its own publish"
git push -u origin feat/p1-t17-4-trimmed-exe
gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p1-t17-4-trimmed-exe -f shell-lane=portable
```

Expected:
- `portable-packages` passes, and its log shows `Trim warnings: none new; 3 allowed.`;
- all five portable shards pass;
- `portable-measure` prints an exe of about 40-50 MB.

Record the run ID, the exe size, the extraction size and the runner
times. If a shard fails, use superpowers:systematic-debugging. Read its
evidence, and compare it with the same shard's result on main, before
changing anything.

---

### Task 2: A release dry run of the trimmed build

**Files:** none (verification only).

- [ ] **Step 1: Dispatch and check the manifest**

```bash
gh workflow run release.yml --repo ilya-slalom/desktop-guides --ref feat/p1-t17-4-trimmed-exe -f tag=v1.0.1
```

Once it finishes:

```bash
gh run download <run-id> --repo ilya-slalom/desktop-guides -n release-1.0.1.0 -D /tmp/t17-4-release
python3 -c "import json,sys; m = json.load(open(sys.argv[1], encoding='utf-8-sig')); p = m['measurements']['portable']; print(m['portable']['bytes'], p['executableBytes'], p['extraction'], p['coldLaunch'], p['warmLaunch'])" /tmp/t17-4-release/release/release-manifest.json
rm -rf /tmp/t17-4-release
```

Expected:
- the run passes, including "Check the release builds for excluded
  components and trim warnings";
- the manifest shows a portable zip and exe of about 40-50 MB, and cold
  and warm medians.

Record the run ID and the figures. The artifact holds a signed package
and is public for a day; delete only the local copy.

---

### Task 3: The host launch check

**Files:**
- Create (throwaway, never committed): `/tmp/t17-4-aside.ps1`

- [ ] **Step 1: Build the two exes on the host**

```bash
s 'cd /d E:\work\desktop-guides\t17-4c && dotnet publish src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release --no-restore -p:Platform=x64 -p:Portable=true -p:PublishTrimmed=false -p:EnableCompressionInSingleFile=false -p:PublishDir=E:\work\desktop-guides\t17-4c\artifacts\untrimmed\ -nologo' | tail -1
s 'dir E:\work\desktop-guides\t17-4c\artifacts\untrimmed E:\work\desktop-guides\t17-4c\artifacts\trimmed'
```

Expected: each folder holds only `DesktopGuides.Production.exe`. The
untrimmed exe is about 184 MB, and the trimmed one is Task 1's size.

- [ ] **Step 2: Put the user's data folder aside**

`/tmp/t17-4-aside.ps1`:

```powershell
param([ValidateSet('aside', 'restore')] [string] $Mode)
$ErrorActionPreference = 'Stop'
$data = Join-Path $env:LOCALAPPDATA 'DesktopGuides'
$state = 'E:\work\t17-4-aside.json'
function Get-Listing([string] $Root) {
    @(Get-ChildItem -LiteralPath $Root -Recurse -Force -File | ForEach-Object {
        '{0}|{1}|{2}' -f $_.FullName.Substring($Root.Length), $_.Length, $_.LastWriteTimeUtc.Ticks
    } | Sort-Object)
}
if ($Mode -eq 'aside') {
    if (Get-Process -Name 'DesktopGuides.Production' -ErrorAction SilentlyContinue) {
        throw 'DesktopGuides.Production is running; nothing was moved.'
    }
    if (Test-Path -LiteralPath $state) { throw "$state exists: an earlier run wasn't restored." }
    if (-not (Test-Path -LiteralPath $data)) { throw 'No portable data folder to set aside.' }
    $name = 'DesktopGuides.t17-4-aside-' + (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
    $listing = Get-Listing $data
    Rename-Item -LiteralPath $data -NewName $name
    @{ name = $name; listing = $listing } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $state -Encoding UTF8
    "Set aside: $($listing.Count) files as $name."
}
else {
    $saved = Get-Content -LiteralPath $state -Raw | ConvertFrom-Json
    $aside = Join-Path $env:LOCALAPPDATA $saved.name
    if (Test-Path -LiteralPath $data) { throw "$data exists; the measurement left its folder. Nothing was restored." }
    Rename-Item -LiteralPath $aside -NewName 'DesktopGuides'
    $now = Get-Listing $data
    $diff = @(Compare-Object @($saved.listing) $now)
    if ($diff.Count -gt 0) { throw "Restored, but $($diff.Count) listing lines differ; investigate before anything else." }
    Remove-Item -LiteralPath $state
    "Restored: $($now.Count) files match the listing."
}
```

```bash
scp -q -o LogLevel=ERROR /tmp/t17-4-aside.ps1 pcsx2-win:E:/work/t17-4-aside.ps1
s 'powershell -NoProfile -ExecutionPolicy Bypass -File E:\work\t17-4-aside.ps1 -Mode aside'
```

Expected: `Set aside: <N> files as DesktopGuides.t17-4-aside-<time>.`
Any throw: stop, and tell the user what it said.

- [ ] **Step 3: Measure both exes, alternating, three times each**

```bash
for round in 1 2 3; do for build in untrimmed trimmed; do
  s "cd /d E:\work\desktop-guides\t17-4c && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\start_installed_task.ps1 -PortableExecutable artifacts\\$build\DesktopGuides.Production.exe -ResultDirectory artifacts\measure-$build-$round -Installer tools\p1\measure_distribution.ps1 -TimeoutMinutes 10" | grep -E "Measured|start_installed_task" ;
done; done
```

Expected: six "Measured ..." lines. Each run's own medians come from 3
launches.

Always run Step 4 next, even after a failure.

- [ ] **Step 4: Restore the user's data folder**

```bash
s 'powershell -NoProfile -ExecutionPolicy Bypass -File E:\work\t17-4-aside.ps1 -Mode restore'
s 'del E:\work\t17-4-aside.ps1'
```

Expected: `Restored: <N> files match the listing.`, with the same N as
Step 2. Any throw: stop, and tell the user exactly what it said.

- [ ] **Step 5: Decide**

The figure is the median of the three runs' cold medians, for each build.
- **Trimmed at or under 2500 ms:** ship it. Ledger the figures.
- **Trimmed over 2500 ms:** add
  `<PublishReadyToRun>true</PublishReadyToRun>` to the Portable group,
  rebuild the trimmed exe, and repeat Steps 2-5 for it alone.
  - At or under 2500 ms: keep ReadyToRun. Rerun Task 1 Step 5's dispatch
    and Task 2, and record the larger size.
  - Still over: revert to the untrimmed build (remove `PublishTrimmed`,
    `TrimMode` and `EnableCompressionInSingleFile`, and keep the gate on a
    trimmed analysis publish as in b1). Record the outcome, and ask the
    user to revise the T17.4 plan row.

---

### Task 4: Records, the full PR run and the PR

**Files:**
- Modify: `docs/p1/t17-4-size-pass-design.md` (Status; a "PR b2
  verification record" holding this plan's root cause and figures)
- Modify: `docs/p1/results.md` (a "T17.4 PR b2" section after the PR a
  section)
- Modify: `docs/p1/release-packaging.md` (the portable release is trimmed
  and compressed)
- Modify: `docs/p1/implementation-plan.md` (status) and `docs/progress.md`
  (status)

- [ ] **Step 1: Write the records**

- **The design:**
  - Status: "PR a (#106) and PR b1 (#107) merged; PR b2 implemented on
    `feat/p1-t17-4-trimmed-exe`."
  - A "PR b2 verification record": the root cause section above, the
    Task 1-3 run IDs and figures, the decision taken in Task 3 Step 5, and
    every ruling.
- **`results.md`:** a "T17.4 PR b2: trimmed and compressed —
  <date>" section with this table, filled from Tasks 1-3:

```markdown
| Build | Exe | Extraction | Cold / warm launch | Measured on |
| --- | --- | --- | --- | --- |
| Untrimmed (PR a) | <MB> | <files>, <MB> | <cold> s / <warm> s | host, this session |
| Trimmed and compressed | <MB> | <files>, <MB> | <cold> s / <warm> s | host, this session |
| Trimmed and compressed (Public) | <MB> | <files>, <MB> | <cold> s / <warm> s | release dry run <id> |
```

  No `<...>` may stay in the committed file.
- **`release-packaging.md`:** after the sentence about the measurements
  block, add "The portable exe is trimmed (`TrimMode=partial`) and
  compressed; `tools/p1/assert_trim_warnings.ps1` fails its publish on any
  trim or C#/WinRT warning that `tools/p1/trim-warning-allowlist.txt`
  doesn't name."
- **`implementation-plan.md`:** "T17.4 (size pass) has PR a merged in #106
  and PR b1 in review, with PR b2 to follow" becomes "T17.4 (size pass)
  has PR a (#106) and PR b1 (#107) merged and PR b2 in review".
- **`progress.md`:** "PR a merged in #106, PR b in progress" becomes "PR a
  (#106) and PR b1 (#107) merged, PR b2 in review".

- [ ] **Step 2: Commit, push, open the PR, and wait for its run**

```bash
git add docs
git commit -m "docs(p1): record T17.4 PR b2: the trimmed crash's root cause, sizes and the host launch check"
git push
```

Title: "T17.4 PR b2: ship the portable exe trimmed and compressed". The
body names:
- the task, the prerequisites and the outcome;
- the root cause;
- the size and time table;
- the host check, with the data-folder handling and its matching listing;
- every ruling.

There are no UI changes, so no screenshot.

Expected: every job passes, including all 10 installed shards on both
lanes. Record the run in the PR description, then stop for the user's
merge decision.

## Traceability

| Spec item (PR b, sections 3 and 4) | Task |
| --- | --- |
| Root-cause the trimmed crash before trimming ships | Root cause section (spike), recorded in 4 |
| `PublishTrimmed`, `TrimMode=partial`, `EnableCompressionInSingleFile` | 1 |
| All shards on both lanes; component check and measurements on the trimmed build | 1, 2, 4 |
| 2.5 s median host cold launch, warm recorded | 3 |
| ReadyToRun if over the limit; fallback to untrimmed | 3 |
| Verification record: crash cause, size and time table, host figures | 4 |
