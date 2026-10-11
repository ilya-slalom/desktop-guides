# T17.1 Release Identity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Desktop Guides releases install and upgrade in place under one
permanent identity, `IlyaLissoboi.DesktopGuides`, signed in CI with a
self-signed certificate and published as a draft GitHub pre-release from a
version tag, with a portable build alongside. The Preview development lane
is unchanged.

**Architecture:** A `PackageLane` build property picks the Preview or
Public manifest pair, and a Core `AppLaneNames` record supplies every
lane-dependent name (instance key, named events, pipe, window title). A
`release.yml` workflow, gated on the tagged commit's green `windows-ci`
push run, stamps the version, builds, verifies, signs with the certificate
from GitHub secrets, runs an installed upgrade gate and creates the draft
release. A `public-upgrade` job in `windows-ci` proves identity continuity
on every main build with a throwaway certificate.

**Tech Stack:** .NET 10, WinUI 3 single-project MSIX, MSBuild, PowerShell
5.1 and 7, Python 3.11 (package verifier), SignTool, GitHub Actions and the
`gh` CLI.

**Spec:** `docs/p1/t17-1-release-identity-design.md`

**Target:** T17.1, in two PRs. **Prerequisite:** T11.1, merged; the
27 September groundwork (verifier, public-mode signer, release procedure),
merged.

| PR | Branch | Tasks |
|---|---|---|
| a: two lanes | `feat/p1-t17-1-release-identity` | 1–4 |
| b: release | `feat/p1-t17-1-release` (from `main` after PR a merges) | 5–11 |

## Global Constraints

- Public identity: Name `IlyaLissoboi.DesktopGuides`, Publisher
  `CN=359962D5-3801-484E-BE5B-39D0D498BEA4`, PublisherDisplayName
  `Ilya Lissoboi`, DisplayName `Desktop Guides`. The signing certificate's
  Subject is exactly that Publisher.
- The Preview lane's identity (`DesktopGuides.Preview`,
  `CN=DesktopGuides Development`, `0.1.0.0`), names, data and smokes don't
  change. Every Preview name stays byte-identical.
- Public versions are `X.Y.Z.0` with a nonzero major, from tag `vX.Y.Z`,
  strictly higher than the previous published release, with the same Name,
  Publisher and architecture.
- Only x64 is released.
- No private key material in the repository, logs or artifacts. Only the
  public `.cer` and the thumbprint are committed.
- Timestamp URL: `http://timestamp.digicert.com`.
- Secrets: `RELEASE_SIGNING_PFX` (base64 PFX) and `RELEASE_SIGNING_PASSWORD`.
- PowerShell scripts stay ASCII-only.

## Rulings against the spec

Tasks 4 and 11 record these in the design's verification record.

1. **Lane names take three forms.** The code uses `Local\<prefix>.<name>`,
   `Local\<prefix>.<name>.<pid>` and the pipe name `<prefix>.<name>.<pid>`
   (no `Local\`). `AppLaneNames` exposes `LocalEvent(name)`,
   `LocalEvent(name, pid)` and `PipeName(name, pid)` instead of the spec's
   single `TestGate(name, pid)`.
2. **The release's CI gate allows docs-only changes.** `windows-ci` skips
   docs-only pushes, so a tag on a docs-only commit has no run of its own.
   The gate accepts the tagged commit if it, or its nearest ancestor with a
   successful `windows-ci` push run, differs from it only in docs paths
   (`docs/**` and `*.md`).
3. **The upgrade gate checks the library by content, not raw database
   bytes.** The app may checkpoint or reconcile on startup. The gate
   compares the seed tool's `describe-library` output and the SHA-256 of
   every file under `library/content` before and after the upgraded app has
   run.
4. **The upgrade gate checks the upgraded app launches by its window**, not
   "Library ready.". It launches through `windows_shell_launch.ps1` in an
   interactive scheduled task, as the installed smokes do, and requires the
   process's main window title to be "Desktop Guides" within 60 s. Reading
   the in-app status needs the UI Automation smoke harness, which is
   Preview-only.
5. **The release workflow has a dry run.** `workflow_dispatch` with
   `dry-run: true` builds, signs and uploads the files as a workflow
   artifact without creating a release, so it can be checked before the
   first tag.
6. **The v1 install is launched once before seeding**, so Windows creates
   the package's `LocalState` the normal way.
7. **A dry run skips the CI gate.** It publishes nothing, so it may build
   any commit (including the PR branch before merge). A tag release still
   requires `main` and a green `windows-ci` run.

## Review Focus

1. **A tag on a commit whose `windows-ci` run failed or never ran.** No
   release may be built. Test: `test_release_ci_gate.ps1` cases for a
   failed run, no run with code changes, and no run with docs-only changes
   (Task 6).
2. **A tag that isn't higher than the published release, or a second tag
   on the same version.** Signing must refuse. Test: the verifier's
   `--previous-package` path, exercised by the `public-upgrade` job's v2
   signing and by `test_release_version.ps1`'s rejection cases (Tasks 5, 8).
3. **The secret holds a different certificate than the pinned one** (rotated
   or pasted wrongly). Signing must refuse before anything is signed. Test:
   `test_release_certificate_pin.ps1` (Task 10).
4. **The upgrade gate runs where the public app is already installed** (for
   example someone's own machine). It must refuse and change nothing. Test:
   the gate's preflight check in `test_windows_package_upgrade.ps1` (Task 8).
5. **A Preview name drifts during the lane refactor.** Every installed smoke
   depends on them. Test: `AppLaneNames` Preview cases pin each literal
   (Task 1), and the full `windows-ci` run passes on PR a (Task 3).

## Host commands

The Mac has no dotnet, so builds and tests run on `pcsx2-win`:

```bash
s(){ ssh -o BatchMode=yes -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t17-1) { Remove-Item -Recurse -Force E:\work\desktop-guides\t17-1 }; New-Item -ItemType Directory E:\work\desktop-guides\t17-1 | Out-Null"'
  COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --exclude=.claude --exclude=.git --exclude=.superpowers -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t17-1'
}
```

- **Core tests:** `stage && s 'cd /d E:\work\desktop-guides\t17-1 && dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release'`
- **Production build (Preview):** `stage && s 'cd /d E:\work\desktop-guides\t17-1 && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false'`
- **Production build (Public):** the same with `-p:PackageLane=Public`.
- **PowerShell test:** `stage && s 'cd /d E:\work\desktop-guides\t17-1 && pwsh -NoProfile -File tools\p1\<test>.ps1'`
- **CI:** `gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref <branch>`;
  watch with `gh run watch <id> --repo ilya-slalom/desktop-guides --exit-status`.

---

# PR a: two lanes in one project

### Task 1: Lane names in Core

**Files:**
- Create: `src/DesktopGuides.Core/Packaging/AppLaneNames.cs`
- Test: `tests/DesktopGuides.Core.Tests/AppLaneNamesTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record AppLaneNames(string Prefix, string WindowTitle)`
  - `public static AppLaneNames Preview { get; }`, `public static AppLaneNames Public { get; }`, `public static AppLaneNames For(bool publicLane)`
  - `string InstanceKey` → `<Prefix>.Main`
  - `string LocalEvent(string name)` → `Local\<Prefix>.<name>`
  - `string LocalEvent(string name, int processId)` → `Local\<Prefix>.<name>.<pid>`
  - `string PipeName(string name, int processId)` → `<Prefix>.<name>.<pid>`
  - `public const string PortableInstanceKey = "DesktopGuides.Portable.Main"`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/DesktopGuides.Core.Tests/AppLaneNamesTests.cs
using DesktopGuides.Core.Packaging;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class AppLaneNamesTests
{
    // Every installed smoke opens these exact names, so they must not drift.
    [Fact]
    public void PreviewNamesAreUnchanged()
    {
        AppLaneNames lane = AppLaneNames.Preview;

        Assert.Equal("DesktopGuides.Preview", lane.Prefix);
        Assert.Equal("Desktop Guides Preview", lane.WindowTitle);
        Assert.Equal("DesktopGuides.Preview.Main", lane.InstanceKey);
        Assert.Equal(@"Local\DesktopGuides.Preview.RedirectedActivation", lane.LocalEvent("RedirectedActivation"));
        Assert.Equal(@"Local\DesktopGuides.Preview.Closing.42", lane.LocalEvent("Closing", 42));
        Assert.Equal("DesktopGuides.Preview.Activation.42", lane.PipeName("Activation", 42));
    }

    [Fact]
    public void PublicNamesUseTheReleasePrefix()
    {
        AppLaneNames lane = AppLaneNames.Public;

        Assert.Equal("DesktopGuides", lane.Prefix);
        Assert.Equal("Desktop Guides", lane.WindowTitle);
        Assert.Equal("DesktopGuides.Main", lane.InstanceKey);
        Assert.Equal(@"Local\DesktopGuides.TextLoad.7", lane.LocalEvent("TextLoad", 7));
        Assert.Equal("DesktopGuides.Activation.7", lane.PipeName("Activation", 7));
    }

    [Theory]
    [InlineData(false, "DesktopGuides.Preview")]
    [InlineData(true, "DesktopGuides")]
    public void ForPicksTheLane(bool publicLane, string prefix) =>
        Assert.Equal(prefix, AppLaneNames.For(publicLane).Prefix);

    [Fact]
    public void ThePortableKeyIsShared() =>
        Assert.Equal("DesktopGuides.Portable.Main", AppLaneNames.PortableInstanceKey);

    [Theory]
    [InlineData("")]
    [InlineData("Has.Dot")]
    [InlineData("Has Space")]
    [InlineData(@"Back\Slash")]
    public void ANameMustBeOneSegment(string name)
    {
        Assert.Throws<ArgumentException>(() => AppLaneNames.Preview.LocalEvent(name));
        Assert.Throws<ArgumentException>(() => AppLaneNames.Preview.PipeName(name, 1));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: Core tests with `--filter "FullyQualifiedName~AppLaneNamesTests"`.
Expected: the build fails: `AppLaneNames` doesn't exist.

- [ ] **Step 3: Write the implementation**

```csharp
// src/DesktopGuides.Core/Packaging/AppLaneNames.cs
using System.Globalization;
using System.Text.RegularExpressions;

namespace DesktopGuides.Core.Packaging;

/// <summary>
/// The names that depend on the package lane: the single-instance key, the
/// named events the shell and the installed tests share, the activation pipe
/// and the window title. Preview keeps the development names; Public is the
/// released app, so both can be installed side by side.
/// </summary>
public sealed partial record AppLaneNames(string Prefix, string WindowTitle)
{
    public const string PortableInstanceKey = "DesktopGuides.Portable.Main";

    public static AppLaneNames Preview { get; } = new("DesktopGuides.Preview", "Desktop Guides Preview");

    public static AppLaneNames Public { get; } = new("DesktopGuides", "Desktop Guides");

    public static AppLaneNames For(bool publicLane) => publicLane ? Public : Preview;

    public string InstanceKey => $"{Prefix}.Main";

    public string LocalEvent(string name) => $@"Local\{Prefix}.{Segment(name)}";

    public string LocalEvent(string name, int processId) =>
        string.Create(CultureInfo.InvariantCulture, $@"Local\{Prefix}.{Segment(name)}.{processId}");

    public string PipeName(string name, int processId) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}.{Segment(name)}.{processId}");

    [GeneratedRegex("^[A-Za-z0-9]+$")]
    private static partial Regex SegmentPattern();

    private static string Segment(string name) =>
        SegmentPattern().IsMatch(name)
            ? name
            : throw new ArgumentException("A lane name is one alphanumeric segment.", nameof(name));
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: the Step 2 command, then the whole Core suite.
Expected: PASS, 9 new tests; the suite passes.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Core/Packaging tests/DesktopGuides.Core.Tests/AppLaneNamesTests.cs
git commit -m "feat(core): name lane-dependent identities in one place"
```

### Task 2: The Public lane in the Production project

TDD note: the lane switch is build configuration. It is verified by
building both lanes and checking each packed manifest with the existing
verifier, plus a source scan for leftover literals. Task 1's tests pin the
names themselves.

**Files:**
- Create: `src/DesktopGuides.Production/Package.Public.appxmanifest`
- Create: `src/DesktopGuides.Production/app.public.manifest`
- Create: `src/DesktopGuides.Production/AppLane.cs`
- Modify: `src/DesktopGuides.Production/DesktopGuides.Production.csproj`
- Modify: `src/DesktopGuides.Production/Program.cs` (lines 13-29, 140-144)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (lines 103, 1642, 1677)
- Modify: `src/DesktopGuides.Production/TextReaderView.xaml.cs:234`,
  `TextReaderSession.cs:30`, `ShellWindow.Progress.cs:36,216`,
  `HtmlReaderSession.cs:86,87,113,120`, `PdfReaderSession.cs:113`,
  `ExternalLinkLaunchers.cs:14`

**Interfaces:**
- Consumes: Task 1's `AppLaneNames`.
- Produces: `-p:PackageLane=Public` builds an MSIX with the public identity
  at version `1.0.0.0`; `AppLane.Current` (`AppLaneNames`).

- [ ] **Step 1: Add the public manifests**

Copy `Package.appxmanifest` to `Package.Public.appxmanifest` and change
exactly these attributes:

```xml
  <Identity Name="IlyaLissoboi.DesktopGuides"
            Publisher="CN=359962D5-3801-484E-BE5B-39D0D498BEA4"
            Version="1.0.0.0" />
  <Properties>
    <DisplayName>Desktop Guides</DisplayName>
    <PublisherDisplayName>Ilya Lissoboi</PublisherDisplayName>
```

and, in `uap:VisualElements`, `DisplayName="Desktop Guides"`. Everything
else (capabilities, assets, dependencies, extensions) stays identical.

Copy `app.manifest` to `app.public.manifest` with
`<assemblyIdentity version="1.0.0.0" name="DesktopGuides" />`.

- [ ] **Step 2: Select the lane in the project**

In `DesktopGuides.Production.csproj`, after the first `PropertyGroup`:

```xml
  <!-- T17.1: Preview is the development lane; Public is the released
       identity. Only the manifests and the lane names differ. -->
  <PropertyGroup>
    <PackageLane Condition="'$(PackageLane)' == ''">Preview</PackageLane>
  </PropertyGroup>
  <PropertyGroup Condition="'$(PackageLane)' == 'Public'">
    <ApplicationManifest>app.public.manifest</ApplicationManifest>
    <DefineConstants>$(DefineConstants);PUBLIC_LANE</DefineConstants>
  </PropertyGroup>
  <ItemGroup Condition="'$(PackageLane)' == 'Public'">
    <AppxManifest Remove="Package.appxmanifest" />
    <AppxManifest Include="Package.Public.appxmanifest" />
  </ItemGroup>
  <ItemGroup Condition="'$(PackageLane)' != 'Public'">
    <AppxManifest Remove="Package.Public.appxmanifest" />
    <None Remove="Package.Public.appxmanifest" />
    <None Remove="app.public.manifest" />
  </ItemGroup>
  <Target Name="RequireKnownPackageLane" BeforeTargets="BeforeBuild">
    <Error Condition="'$(PackageLane)' != 'Preview' and '$(PackageLane)' != 'Public'"
           Text="PackageLane must be Preview or Public, got '$(PackageLane)'." />
  </Target>
```

If the single-project MSIX tooling ignores the `AppxManifest` item swap (the
packed manifest stays Preview in Step 6), set the manifest through the
property it reads instead (`AppxManifest` item metadata or
`AppxPackageManifest`, whichever `Microsoft.WindowsAppSDK`'s targets use;
search the restored package's `build` folder for `AppxManifest`), and note
which in the report.

- [ ] **Step 3: Add `AppLane`**

```csharp
// src/DesktopGuides.Production/AppLane.cs
using DesktopGuides.Core.Packaging;

namespace DesktopGuides.Production;

// The lane this build was packaged for (T17.1). Preview is the default.
internal static class AppLane
{
#if PUBLIC_LANE
    public static AppLaneNames Current => AppLaneNames.Public;
#else
    public static AppLaneNames Current => AppLaneNames.Preview;
#endif
}
```

- [ ] **Step 4: Replace the literals**

In `Program.cs`:

```csharp
    // The MSIX and portable builds keep separate libraries, so each is its own
    // single instance.
    private static readonly string InstanceKey = AppDataRoot.HasPackageIdentity()
        ? AppLane.Current.InstanceKey
        : AppLaneNames.PortableInstanceKey;
    private static readonly string ActivationProbeName =
        AppLane.Current.LocalEvent("RedirectedActivation");
    private static readonly string RedirectSelectedProbeName =
        AppLane.Current.LocalEvent("RedirectSelected");
    private static readonly string RedirectContinueProbeName =
        AppLane.Current.LocalEvent("RedirectContinue");
    private static readonly string ActivationQueuedProbeName =
        AppLane.Current.LocalEvent("ActivationQueued");
    private static readonly string ActivationContinueProbeName =
        AppLane.Current.LocalEvent("ActivationContinue");
    private static readonly string AcceptanceReceivedProbeName =
        AppLane.Current.LocalEvent("AcceptanceReceived");
    private static readonly string AcceptanceContinueProbeName =
        AppLane.Current.LocalEvent("AcceptanceContinue");
```

and

```csharp
    private static string ClosingSignalName(int processId) =>
        AppLane.Current.LocalEvent("Closing", processId);

    private static string ActivationPipeName(int processId) =>
        AppLane.Current.PipeName("Activation", processId);
```

Add `using DesktopGuides.Core.Packaging;`. If any of the former `const`
names is used where a constant is required (a `switch` case or attribute),
the build reports it; convert that use to an `if`.

Every other site uses `AppLane.Current.LocalEvent(<name>, Environment.ProcessId)`
with the name that was between `DesktopGuides.Preview.` and `.{Environment.ProcessId}`:

| File:line | Name |
|---|---|
| `ExternalLinkLaunchers.cs:14` | `ExternalLaunch` |
| `HtmlReaderSession.cs:86` | `HtmlPosition` |
| `HtmlReaderSession.cs:87` | `HtmlAssetDelay` |
| `HtmlReaderSession.cs:113` | `HtmlDiagnostics` |
| `HtmlReaderSession.cs:120` | `WebView2Missing` |
| `PdfReaderSession.cs:113` | `PdfDiagnostics` |
| `TextReaderSession.cs:30` | `TextDiagnostics` |
| `TextReaderView.xaml.cs:234` | `TextRemeasure` |
| `ShellWindow.Progress.cs:36` | `ProgressDiagnostics` |
| `ShellWindow.Progress.cs:216` | `ProgressOverride` |
| `ShellWindow.xaml.cs:1642` | `ReaderLoad` |
| `ShellWindow.xaml.cs:1677` | the `gate` parameter |

For example `HtmlReaderSession.cs:86` becomes
`positionForTest = TestGate.IsOpen(AppLane.Current.LocalEvent("HtmlPosition", Environment.ProcessId));`.
`ShellWindow.xaml.cs:103` becomes `Title = AppLane.Current.WindowTitle;`.

- [ ] **Step 5: Scan for leftovers**

Run: `grep -rn "DesktopGuides\.Preview\|Desktop Guides Preview" src/DesktopGuides.Production --include=*.cs`
Expected: no output.

- [ ] **Step 6: Build and verify both lanes**

Run the Preview build, then on the host:
`python tools\p1\verify_production_package.py <the x64 msix> --name DesktopGuides.Preview --publisher "CN=DesktopGuides Development" --version 0.1.0.0 --architecture x64`
Expected: build with 0 warnings; the verifier prints the Preview identity.

Delete `src\DesktopGuides.Production\AppPackages` and `bin`/`obj`, run the
Public build, then:
`python tools\p1\verify_production_package.py <the x64 msix> --name IlyaLissoboi.DesktopGuides --publisher "CN=359962D5-3801-484E-BE5B-39D0D498BEA4" --version 1.0.0.0 --architecture x64`
Expected: build with 0 warnings; the verifier prints the public identity.

Build with `-p:PackageLane=Store`.
Expected: the build fails with "PackageLane must be Preview or Public, got 'Store'."

Run the Core suite.
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/DesktopGuides.Production
git commit -m "feat(shell): build the public identity as its own lane"
```

### Task 3: CI builds and verifies the Public lane

**Files:**
- Modify: `.github/workflows/windows-ci.yml` (`production-packages`, after
  "Build unsigned production package")

**Interfaces:**
- Consumes: Task 2's `-p:PackageLane=Public`.
- Produces: every `production-packages (x64)` run proves the public
  identity packs and verifies. Nothing downstream consumes its output.

- [ ] **Step 1: Add the step**

Insert after the "Build unsigned production package" step (the steps are a
YAML anchor shared with `dev-production-packages`, so this runs there too):

```yaml
      - name: Build and verify the public lane
        if: matrix.architecture == 'x64'
        shell: pwsh
        run: |
          # T17.1: the released identity must pack and verify on every build.
          # Its own output folder keeps it apart from the Preview package.
          dotnet build src/DesktopGuides.Production/DesktopGuides.Production.csproj `
            -c Release --no-restore `
            -p:Platform=x64 `
            -p:PackageLane=Public `
            -p:AppxPackageDir=$PWD/artifacts/public-lane/ `
            -p:GenerateAppxPackageOnBuild=true `
            -p:AppxPackageSigningEnabled=false
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          $packages = @(Get-ChildItem artifacts/public-lane -Recurse `
            -Filter 'DesktopGuides.Production_*_x64.msix')
          if ($packages.Count -ne 1) {
            throw "Expected one public x64 MSIX, found $($packages.Count)."
          }
          python tools/p1/verify_production_package.py $packages[0].FullName `
            --name IlyaLissoboi.DesktopGuides `
            --publisher 'CN=359962D5-3801-484E-BE5B-39D0D498BEA4' `
            --version 1.0.0.0 `
            --architecture x64
          exit $LASTEXITCODE
```

If `AppxPackageDir` doesn't redirect the package (the step finds no MSIX),
find it under `src/DesktopGuides.Production/AppPackages` by its
`1.0.0.0` version folder instead, and note it in the report. The Public
build reuses the same restore: the lane changes no package references.

- [ ] **Step 2: Run CI**

Commit, push the branch and dispatch a full `windows-ci` run.
Expected: every job passes, including `production-packages (x64)` with the
new step, and all four `production-shell-ui` shards (they still install
the Preview package, so they show Task 2 kept every Preview name).

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/windows-ci.yml
git commit -m "ci: build and verify the public package lane"
```

(Commit before pushing in Step 2; this step lists the commit.)

### Task 4: PR a documentation, verification record and PR

**Files:**
- Modify: `docs/p1/t17-1-release-identity-design.md` (status, record)
- Modify: `docs/p1/release-packaging.md` (lanes table)
- Modify: `docs/p1/implementation-plan.md` (T17.1 paragraph)

- [ ] **Step 1: Record the verification**

Under `## T17.1 verification record`, replace "Not yet run." with
`### PR a: two lanes`: the Core count, the two host verifier outputs, the
`PackageLane=Store` failure, the CI run ID, rulings 1 from this plan, and
what isn't run yet (signing, release, upgrade gate: PR b).

- [ ] **Step 2: Update the docs**

- Design status: "Status: PR a (two lanes) in review; PR b planned."
- `release-packaging.md`, the Public row of the lanes table: Name
  `IlyaLissoboi.DesktopGuides`, Publisher
  `CN=359962D5-3801-484E-BE5B-39D0D498BEA4` (reserved in Partner Center),
  built with `-p:PackageLane=Public`.
- `implementation-plan.md`: in the T17.1 paragraph, say the public identity
  is reserved and PR a adds the Public lane.

- [ ] **Step 3: Check, commit and open PR a**

Run: `git diff --check`
Expected: no output.

```bash
git add docs
git commit -m "docs(p1): record T17.1 PR a verification"
```

Push and open the PR. The body names T17.1 PR a, the prerequisites (T11.1,
merged; the 27 September groundwork, merged), the outcome (a Public lane
that builds the reserved identity; Preview unchanged), and the CI run. No
UI change except the public build's title, which Preview users never see,
so no screenshot. It ends with the Claude Code line.

---

# PR b: certificate, release workflow and upgrade gate

### Task 5: Release version and manifest stamping

**Files:**
- Create: `tools/p1/release_version.ps1`, `tools/p1/stamp_package_version.ps1`
- Test: `tools/p1/test_release_version.ps1`, `tools/p1/test_stamp_package_version.ps1`
- Modify: `.github/workflows/windows-ci.yml` (`core-tests`: two steps)

**Interfaces:**
- Produces:
  - `release_version.ps1 -Tag vX.Y.Z` writes `X.Y.Z.0` to the pipeline and
    throws on anything else.
  - `stamp_package_version.ps1 -Manifest <path> -Version X.Y.Z.0` rewrites
    only the Identity `Version` attribute of the public manifest and checks
    the result.

- [ ] **Step 1: Write the failing tests**

```powershell
# tools/p1/test_release_version.ps1
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'release_version.ps1'

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

foreach ($case in @(
        @('v1.0.0', '1.0.0.0'),
        @('v12.3.45', '12.3.45.0'),
        @('v65535.65535.65535', '65535.65535.65535.0'))) {
    $version = & $script -Tag $case[0]
    Assert-True ($version -eq $case[1]) "Expected $($case[1]) for $($case[0]), got '$version'."
}

foreach ($tag in @('1.0.0', 'v0.1.0', 'v1.0', 'v1.0.0.0', 'v01.0.0', 'v1.00.0',
        'v1.0.0-rc1', 'v65536.0.0', 'v1.0.0 ', 'V1.0.0', 'v-1.0.0')) {
    $rejected = $false
    try { & $script -Tag $tag | Out-Null } catch { $rejected = $true }
    Assert-True $rejected "The tag '$tag' should be rejected."
}
Write-Output 'release_version: all cases passed.'
```

```powershell
# tools/p1/test_stamp_package_version.ps1
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'stamp_package_version.ps1'
$project = Join-Path $PSScriptRoot '..\..\src\DesktopGuides.Production'

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Rejected([scriptblock] $Action, [string] $Message) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    Assert-True $rejected $Message
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-stamp-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    $public = Join-Path $scratch 'Package.Public.appxmanifest'
    Copy-Item (Join-Path $project 'Package.Public.appxmanifest') $public
    $before = [IO.File]::ReadAllText($public)

    & $script -Manifest $public -Version '2.3.4.0'

    $after = [IO.File]::ReadAllText($public)
    [xml] $xml = $after
    Assert-True ($xml.Package.Identity.Version -eq '2.3.4.0') 'The Identity version was not stamped.'
    Assert-True ($xml.Package.Identity.Name -eq 'IlyaLissoboi.DesktopGuides') 'The Identity name changed.'
    $expected = $before -replace 'Version="1\.0\.0\.0"', 'Version="2.3.4.0"'
    Assert-True ($after -eq $expected) 'Stamping changed more than the Identity version.'

    $preview = Join-Path $scratch 'Package.appxmanifest'
    Copy-Item (Join-Path $project 'Package.appxmanifest') $preview
    Assert-Rejected { & $script -Manifest $preview -Version '2.3.4.0' } `
        'The Preview manifest must not be stamped.'
    Assert-Rejected { & $script -Manifest $public -Version '2.3.4.1' } `
        'A fourth field other than 0 must be rejected.'
    Assert-Rejected { & $script -Manifest $public -Version '0.3.4.0' } `
        'A zero major must be rejected.'
    Write-Output 'stamp_package_version: all cases passed.'
}
finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
```

The `$expected` comparison relies on `Version="1.0.0.0"` appearing only in
the Identity element of `Package.Public.appxmanifest`; check with
`grep -c 'Version="1.0.0.0"'` that it occurs once, and if the dependency
element also carries that version, compare only the Identity line instead.

- [ ] **Step 2: Run the tests to verify they fail**

Run: both PowerShell tests on the host.
Expected: FAIL: "The term '...release_version.ps1' is not recognized" (or
the stamp equivalent), because the scripts don't exist.

- [ ] **Step 3: Write the scripts**

```powershell
# tools/p1/release_version.ps1
param(
    [Parameter(Mandatory = $true)]
    [string] $Tag
)
# Turns a release tag vX.Y.Z into the package version X.Y.Z.0 (T17.1).
$ErrorActionPreference = 'Stop'
if ($Tag -cnotmatch '^v(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})$') {
    throw "Expected a release tag vX.Y.Z, got '$Tag'."
}
$parts = @([int] $Matches[1], [int] $Matches[2], [int] $Matches[3])
if ($parts[0] -lt 1) { throw "The major version must be at least 1, got '$Tag'." }
if (@($parts | Where-Object { $_ -gt 65535 }).Count -gt 0) {
    throw "Each version field must be at most 65535, got '$Tag'."
}
'{0}.{1}.{2}.0' -f $parts[0], $parts[1], $parts[2]
```

```powershell
# tools/p1/stamp_package_version.ps1
param(
    [Parameter(Mandatory = $true)]
    [string] $Manifest,

    [Parameter(Mandatory = $true)]
    [string] $Version
)
# Writes the release version into the public package manifest's Identity,
# leaving every other byte as it was (T17.1).
$ErrorActionPreference = 'Stop'
$publicName = 'IlyaLissoboi.DesktopGuides'
if ($Version -notmatch '^[1-9][0-9]{0,4}\.[0-9]{1,5}\.[0-9]{1,5}\.0$') {
    throw "Expected a public package version X.Y.Z.0, got '$Version'."
}
$text = [IO.File]::ReadAllText($Manifest)
[xml] $parsed = $text
if ($parsed.Package.Identity.Name -ne $publicName) {
    throw "Refusing to stamp a manifest that isn't the public identity: $Manifest"
}
$identity = [regex]::Match($text, '<Identity\b[^>]*>')
if (-not $identity.Success) { throw "No Identity element in $Manifest." }
$stampedIdentity = [regex]::Replace($identity.Value, 'Version="[^"]*"', "Version=""$Version""")
$stamped = $text.Substring(0, $identity.Index) + $stampedIdentity +
    $text.Substring($identity.Index + $identity.Length)
[IO.File]::WriteAllText($Manifest, $stamped, [Text.UTF8Encoding]::new($false))
[xml] $check = [IO.File]::ReadAllText($Manifest)
if ($check.Package.Identity.Version -ne $Version -or $check.Package.Identity.Name -ne $publicName) {
    throw "Stamping $Manifest did not produce version $Version."
}
```

If `Package.Public.appxmanifest` starts with a UTF-8 BOM, write with
`[Text.UTF8Encoding]::new($true)` instead, so the stamped file keeps it (the
test's byte comparison catches either mismatch).

- [ ] **Step 4: Run the tests to verify they pass**

Run: both tests on the host.
Expected: "release_version: all cases passed." and
"stamp_package_version: all cases passed."

- [ ] **Step 5: Run them in CI**

In `core-tests`, after "Check portable release packaging", add:

```yaml
      - name: Check release version parsing
        shell: pwsh
        run: ./tools/p1/test_release_version.ps1
      - name: Check public manifest stamping
        shell: pwsh
        run: ./tools/p1/test_stamp_package_version.ps1
```

- [ ] **Step 6: Commit**

```bash
git add tools/p1/release_version.ps1 tools/p1/stamp_package_version.ps1 tools/p1/test_release_version.ps1 tools/p1/test_stamp_package_version.ps1 .github/workflows/windows-ci.yml
git commit -m "build: turn release tags into stamped public package versions"
```

### Task 6: The release CI gate

**Files:**
- Create: `tools/p1/release_ci_gate.ps1`
- Test: `tools/p1/test_release_ci_gate.ps1`
- Modify: `.github/workflows/windows-ci.yml` (`core-tests`: one step)

**Interfaces:**
- Produces: `release_ci_gate.ps1 -Commit <sha> [-MainRef origin/main] [-Repository owner/name] [-RunConclusion <scriptblock>]`.
  It writes the commit whose green run it relied on and throws when:
  - the commit isn't on `MainRef`;
  - the nearest first-parent ancestor with a completed `windows-ci` push
    run (the commit itself included) didn't succeed, or none exists in the
    last 50;
  - files outside `docs/**` and `**/*.md` changed between that ancestor and
    the commit (ruling 2; these match `windows-ci`'s `paths-ignore`).
  `-RunConclusion` maps a sha to `success`, `failure` or `$null` (no
  completed push run); its default calls the GitHub API.

- [ ] **Step 1: Write the failing test**

```powershell
# tools/p1/test_release_ci_gate.ps1
$ErrorActionPreference = 'Stop'
$gate = Join-Path $PSScriptRoot 'release_ci_gate.ps1'

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Rejected([scriptblock] $Action, [string] $Message) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    Assert-True $rejected $Message
}

$repo = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-gate-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $repo | Out-Null
function Git([string[]] $Arguments) {
    $output = & git -C $repo @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed." }
    $output
}
function Commit([string] $Path, [string] $Text) {
    $file = Join-Path $repo $Path
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $file) | Out-Null
    Set-Content -LiteralPath $file -Value $Text
    Git @('add', '-A') | Out-Null
    Git @('-c', 'user.name=t', '-c', 'user.email=t@t', 'commit', '-q', '-m', $Path) | Out-Null
    (Git @('rev-parse', 'HEAD')).Trim()
}
try {
    Git @('init', '-q', '-b', 'main') | Out-Null
    $code = Commit 'src/a.cs' 'one'
    $docs = Commit 'docs/notes.md' 'two'
    $readme = Commit 'README.md' 'three'
    $later = Commit 'src/a.cs' 'four'
    Git @('checkout', '-q', '-b', 'side') | Out-Null
    $side = Commit 'src/b.cs' 'side'
    Git @('checkout', '-q', 'main') | Out-Null

    $green = { param($sha) if ($sha -eq $code) { 'success' } else { $null } }
    $red = { param($sha) if ($sha -eq $code) { 'failure' } else { $null } }

    $relied = & $gate -Commit $code -MainRef main -RunConclusion $green
    Assert-True ($relied -eq $code) 'A commit with its own green run should pass.'
    $relied = & $gate -Commit $readme -MainRef main -RunConclusion $green
    Assert-True ($relied -eq $code) 'Docs-only commits after a green run should pass.'

    Assert-Rejected { & $gate -Commit $later -MainRef main -RunConclusion $green } `
        'A code change without its own green run must be rejected.'
    Assert-Rejected { & $gate -Commit $docs -MainRef main -RunConclusion $red } `
        'A failed run must be rejected even for a docs-only tag.'
    Assert-Rejected { & $gate -Commit $side -MainRef main -RunConclusion { param($sha) 'success' } } `
        'A commit that is not on main must be rejected.'
    Assert-Rejected { & $gate -Commit $docs -MainRef main -RunConclusion { param($sha) $null } } `
        'No completed run at all must be rejected.'
    Write-Output 'release_ci_gate: all cases passed.'
}
finally {
    Remove-Item -LiteralPath $repo -Recurse -Force
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: the test on the host.
Expected: FAIL: the script `release_ci_gate.ps1` is not recognized.

- [ ] **Step 3: Write the gate**

```powershell
# tools/p1/release_ci_gate.ps1
param(
    [Parameter(Mandatory = $true)]
    [string] $Commit,

    [string] $MainRef = 'origin/main',

    [string] $Repository = $env:GITHUB_REPOSITORY,

    # Maps a commit sha to 'success', 'failure' or $null (no completed push run).
    [scriptblock] $RunConclusion
)
# T17.1: a release is built only from main, on code that windows-ci passed.
# windows-ci skips docs-only pushes, so the nearest ancestor with a completed
# push run counts when everything after it is docs.
$ErrorActionPreference = 'Stop'
if (-not $RunConclusion) {
    if (-not $Repository) { throw 'Pass -Repository or set GITHUB_REPOSITORY.' }
    $RunConclusion = {
        param($sha)
        $json = gh api "repos/$Repository/actions/workflows/windows-ci.yml/runs?head_sha=$sha&event=push&status=completed" `
            --jq '[.workflow_runs[] | {conclusion, created_at}] | sort_by(.created_at) | last | .conclusion'
        if ($LASTEXITCODE -ne 0) { throw "Couldn't read windows-ci runs for $sha." }
        $value = "$json".Trim()
        if ($value -eq '' -or $value -eq 'null') { $null } else { $value }
    }
}

$sha = (git rev-parse --verify "$Commit^{commit}").Trim()
if ($LASTEXITCODE -ne 0) { throw "Unknown commit '$Commit'." }
git merge-base --is-ancestor $sha $MainRef
if ($LASTEXITCODE -ne 0) { throw "Commit $sha is not on $MainRef." }

$candidates = @(git rev-list --first-parent --max-count=50 $sha)
foreach ($candidate in $candidates) {
    $conclusion = & $RunConclusion $candidate
    if ($null -eq $conclusion) { continue }
    if ($conclusion -ne 'success') {
        throw "windows-ci's push run for $candidate concluded '$conclusion'."
    }
    $changed = @(git diff --name-only $candidate $sha | Where-Object { $_ })
    $code = @($changed | Where-Object { $_ -notlike 'docs/*' -and $_ -notlike '*.md' })
    if ($code.Count -gt 0) {
        throw "Files changed since the last green run ($candidate) need their own run: $($code -join ', ')"
    }
    return $candidate
}
throw "No completed windows-ci push run among the last $($candidates.Count) commits before $sha."
```

- [ ] **Step 4: Run the test to verify it passes**

Run: the test on the host.
Expected: "release_ci_gate: all cases passed."

- [ ] **Step 5: Run it in CI and commit**

In `core-tests`, after "Check public manifest stamping", add:

```yaml
      - name: Check the release CI gate
        shell: pwsh
        run: ./tools/p1/test_release_ci_gate.ps1
```

```bash
git add tools/p1/release_ci_gate.ps1 tools/p1/test_release_ci_gate.ps1 .github/workflows/windows-ci.yml
git commit -m "build: build releases only from code windows-ci passed"
```

### Task 7: Name the portable release by version

**Files:**
- Modify: `tools/p1/package_portable_release.ps1`
- Test: `tools/p1/test_package_portable_release.ps1`

**Interfaces:**
- Produces: `package_portable_release.ps1 -Commit <sha> [-Version X.Y.Z]`.
  With `-Version` the files are `DesktopGuides-portable-x64-X.Y.Z.{zip,zip.sha256,json}`
  and the manifest has `"version": "X.Y.Z"`; without it, names and manifest
  are exactly as today.

- [ ] **Step 1: Write the failing test cases**

In `test_package_portable_release.ps1`, before the final
`Write-Output 'Portable release packaging checks passed.'`, add:

```powershell
    # T17.1: a release names the zip by its version.
    $versioned = Join-Path $scratch 'versioned'
    & $packager -PublishDirectory $publish -OutputDirectory $versioned -Commit $commit -Version '1.2.3' | Out-Null
    $versionedName = 'DesktopGuides-portable-x64-1.2.3'
    $versionedOutputs = Get-Outputs $versioned | Sort-Object
    Assert-True (($versionedOutputs -join ',') -eq "$versionedName.json,$versionedName.zip,$versionedName.zip.sha256") `
        "Unexpected versioned release files: $($versionedOutputs -join ', ')"
    $versionedManifest = Get-Content -LiteralPath (Join-Path $versioned "$versionedName.json") -Raw | ConvertFrom-Json
    Assert-True ($versionedManifest.version -eq '1.2.3') 'The manifest should record the version.'
    Assert-True ($versionedManifest.commit -eq $commit) 'The versioned manifest should keep the commit.'
    Assert-True ($null -eq $manifest.version) 'An unversioned manifest should have no version.'

    foreach ($badVersion in @('1.2', '1.2.3.0', 'v1.2.3', '0.1.0', '1.2.x')) {
        Assert-Rejected {
            & $packager -PublishDirectory $publish -OutputDirectory (Join-Path $scratch "v-$badVersion") `
                -Commit $commit -Version $badVersion
        } "The version '$badVersion' should be rejected."
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: the test on the host.
Expected: FAIL: "A parameter cannot be found that matches parameter name 'Version'."

- [ ] **Step 3: Implement**

In `package_portable_release.ps1`, add the parameter after `$Commit`:

```powershell
    # Release version X.Y.Z. Names the files by version instead of commit.
    [string] $Version,
```

after the commit check:

```powershell
if ($Version -and $Version -notmatch '^[1-9][0-9]*\.[0-9]+\.[0-9]+$') {
    throw "Expected a release version X.Y.Z, got '$Version'."
}
```

replace the `$name =` line with:

```powershell
$suffix = if ($Version) { $Version } else { $Commit.Substring(0, 7) }
$name = "DesktopGuides-portable-x64-$suffix"
```

and after `$manifest = [ordered]@{ ... }`:

```powershell
if ($Version) { $manifest.Insert(1, 'version', $Version) }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: the test on the host.
Expected: "Portable release packaging checks passed."

- [ ] **Step 5: Commit**

```bash
git add tools/p1/package_portable_release.ps1 tools/p1/test_package_portable_release.ps1
git commit -m "build: name the portable release by its version"
```

### Task 8: The installed upgrade gate

**Files:**
- Create: `tools/p1/windows_package_upgrade.ps1`
- Test: `tools/p1/test_windows_package_upgrade.ps1`
- Modify: `.github/workflows/windows-ci.yml` (`core-tests` step; new
  `public-upgrade` job)

**Interfaces:**
- Consumes: Task 5's stamp script; Task 2's Public lane; the existing
  `sign_production_package.ps1`, `windows_shell_launch.ps1` and the seed
  tool's `seed` and `describe-library` commands.
- Produces: `windows_package_upgrade.ps1 -OldPackage <msix> -NewPackage <msix> -Certificate <cer> -SeedTool <dll> -ResultPath <json> [-PreflightOnly] [-InstalledCheck <scriptblock>]`,
  which writes `{family, oldVersion, newVersion, libraryBefore, libraryAfter, libraryPreserved, upgradedAppLaunched}`
  and throws on any failed check. Task 10's release workflow calls it.

- [ ] **Step 1: Write the failing preflight test**

```powershell
# tools/p1/test_windows_package_upgrade.ps1
$ErrorActionPreference = 'Stop'
$gate = Join-Path $PSScriptRoot 'windows_package_upgrade.ps1'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Rejected([scriptblock] $Action, [string] $Message) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    Assert-True $rejected $Message
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-upgrade-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $scratch | Out-Null

# A stand-in package: a zip holding only AppxManifest.xml with the given identity.
function New-Package([string] $File, [string] $Name, [string] $Publisher, [string] $Version) {
    $path = Join-Path $scratch $File
    $zip = [IO.Compression.ZipFile]::Open($path, 'Create')
    try {
        $entry = $zip.CreateEntry('AppxManifest.xml')
        $writer = [IO.StreamWriter]::new($entry.Open())
        $writer.Write("<Package xmlns=""http://schemas.microsoft.com/appx/manifest/foundation/windows10""><Identity Name=""$Name"" Publisher=""$Publisher"" Version=""$Version"" ProcessorArchitecture=""x64"" /></Package>")
        $writer.Dispose()
    }
    finally { $zip.Dispose() }
    $path
}

try {
    $public = 'IlyaLissoboi.DesktopGuides'
    $publisher = 'CN=359962D5-3801-484E-BE5B-39D0D498BEA4'
    $v1 = New-Package 'v1.msix' $public $publisher '1.0.0.0'
    $v2 = New-Package 'v2.msix' $public $publisher '1.0.1.0'
    $otherPublisher = New-Package 'other.msix' $public 'CN=Someone Else' '1.0.1.0'
    $preview = New-Package 'preview.msix' 'DesktopGuides.Preview' 'CN=DesktopGuides Development' '1.0.1.0'
    $cer = Join-Path $scratch 'release.cer'
    Set-Content -LiteralPath $cer -Value 'x'
    $notInstalled = { $false }
    $common = @{ Certificate = $cer; SeedTool = 'seed.dll'; ResultPath = (Join-Path $scratch 'r.json'); PreflightOnly = $true }

    & $gate -OldPackage $v1 -NewPackage $v2 -InstalledCheck $notInstalled @common
    Assert-Rejected { & $gate -OldPackage $v1 -NewPackage $v2 -InstalledCheck { $true } @common } `
        'The gate must refuse when the public app is already installed.'
    Assert-Rejected { & $gate -OldPackage $v2 -NewPackage $v1 -InstalledCheck $notInstalled @common } `
        'A lower new version must be rejected.'
    Assert-Rejected { & $gate -OldPackage $v1 -NewPackage $v1 -InstalledCheck $notInstalled @common } `
        'The same version must be rejected.'
    Assert-Rejected { & $gate -OldPackage $v1 -NewPackage $otherPublisher -InstalledCheck $notInstalled @common } `
        'A different Publisher must be rejected.'
    Assert-Rejected { & $gate -OldPackage $preview -NewPackage $v2 -InstalledCheck $notInstalled @common } `
        'A non-public package must be rejected.'
    Write-Output 'windows_package_upgrade preflight: all cases passed.'
}
finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: the test on the host.
Expected: FAIL: the script `windows_package_upgrade.ps1` is not recognized.

- [ ] **Step 3: Write the gate**

```powershell
# tools/p1/windows_package_upgrade.ps1
param(
    [Parameter(Mandatory = $true)] [string] $OldPackage,
    [Parameter(Mandatory = $true)] [string] $NewPackage,
    # Public certificate both packages are signed with.
    [Parameter(Mandatory = $true)] [string] $Certificate,
    # Built DesktopGuides.ShellSeed.dll.
    [Parameter(Mandatory = $true)] [string] $SeedTool,
    [Parameter(Mandatory = $true)] [string] $ResultPath,
    # Checks the packages and the machine, then stops before installing.
    [switch] $PreflightOnly,
    # True when the public app is installed; replaceable for tests.
    [scriptblock] $InstalledCheck
)
# T17.1: installs the old public package, seeds a library, installs the new
# package over it without uninstalling, and proves the identity and the
# library survive and the upgraded app opens. Refuses to run where the
# public app is already installed, and removes everything it added.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$publicName = 'IlyaLissoboi.DesktopGuides'
$publicPublisher = 'CN=359962D5-3801-484E-BE5B-39D0D498BEA4'
$windowTitle = 'Desktop Guides'
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (-not $InstalledCheck) {
    $InstalledCheck = { @(Get-AppxPackage -Name $publicName).Count -gt 0 }
}

function Read-Identity([string] $Path) {
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $zip.GetEntry('AppxManifest.xml')
        if (-not $entry) { throw "$Path has no AppxManifest.xml." }
        $reader = [IO.StreamReader]::new($entry.Open())
        [xml] $xml = $reader.ReadToEnd()
        $reader.Dispose()
    }
    finally { $zip.Dispose() }
    $identity = $xml.Package.Identity
    [pscustomobject]@{
        Name = $identity.Name
        Publisher = $identity.Publisher
        Version = [Version] $identity.Version
        Architecture = $identity.ProcessorArchitecture
    }
}

$old = Read-Identity $OldPackage
$new = Read-Identity $NewPackage
foreach ($package in @($old, $new)) {
    if ($package.Name -ne $publicName -or $package.Publisher -ne $publicPublisher) {
        throw "Expected the public identity, got $($package.Name) / $($package.Publisher)."
    }
}
if ($new.Architecture -ne $old.Architecture) { throw 'The packages target different architectures.' }
if ($new.Version -le $old.Version) { throw "The new version $($new.Version) isn't higher than $($old.Version)." }
if (-not (Test-Path -LiteralPath $Certificate -PathType Leaf)) { throw "Certificate not found: $Certificate" }
if (& $InstalledCheck) { throw "$publicName is already installed here; the gate runs only on a machine without it." }
if ($PreflightOnly) { return }

$runId = [Guid]::NewGuid().ToString('N')
$taskName = "DesktopGuides-T17-1-Upgrade-$runId"
$launchResult = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-upgrade-launch-$runId.json"
$trusted = $null
$report = [ordered]@{
    oldVersion = "$($old.Version)"
    newVersion = "$($new.Version)"
    libraryPreserved = $false
    upgradedAppLaunched = $false
}

function Invoke-Seed([string[]] $Arguments) {
    $output = @(dotnet $SeedTool @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "ShellSeed $($Arguments[0]) failed: $($output | Select-Object -Last 3)" }
    $output | Select-Object -Last 1
}

function Start-PublicApp([string] $Executable) {
    Remove-Item -LiteralPath $launchResult, "$launchResult.ack" -ErrorAction SilentlyContinue
    $script = Join-Path $PSScriptRoot 'windows_shell_launch.ps1'
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -WorkingDirectory $PSScriptRoot -Argument (
        '-NoProfile -NonInteractive -Sta -WindowStyle Hidden -ExecutionPolicy Bypass ' +
        "-File ""$script"" -ExecutablePath ""$Executable"" -ResultPath ""$launchResult""")
    $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Force | Out-Null
    Start-ScheduledTask -TaskName $taskName
    $deadline = (Get-Date).AddSeconds(60)
    while (-not (Test-Path -LiteralPath $launchResult) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
    if (-not (Test-Path -LiteralPath $launchResult)) { throw 'The interactive launch wrote no result.' }
    $launch = Get-Content -LiteralPath $launchResult -Raw | ConvertFrom-Json
    if (-not $launch.success) { throw "The interactive launch failed: $($launch.error)" }
    # windows_shell_launch.ps1 waits for this before it lets the app go.
    @{ handoffToken = $launch.handoffToken } | ConvertTo-Json -Compress |
        Set-Content -LiteralPath "$launchResult.ack" -Encoding UTF8
    $process = Get-Process -Id ([int] $launch.processId)
    $deadline = (Get-Date).AddSeconds(60)
    do {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
    } while ($process.MainWindowTitle -ne $windowTitle -and -not $process.HasExited -and (Get-Date) -lt $deadline)
    if ($process.MainWindowTitle -ne $windowTitle) {
        throw "The app didn't show a '$windowTitle' window (title '$($process.MainWindowTitle)')."
    }
    [void] $process.CloseMainWindow()
    if (-not $process.WaitForExit(30000)) { throw 'The app did not close.' }
}

function Get-LibraryState([string] $DataRoot) {
    $content = Join-Path $DataRoot 'library\content'
    $files = @(Get-ChildItem -LiteralPath $content -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object FullName | ForEach-Object {
            '{0} {1}' -f $_.FullName.Substring($content.Length), (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        })
    [ordered]@{ description = (Invoke-Seed @('describe-library', $DataRoot)); content = $files }
}

try {
    $trusted = Import-Certificate -FilePath $Certificate -CertStoreLocation Cert:\LocalMachine\TrustedPeople
    Add-AppxPackage -Path $OldPackage
    $installed = Get-AppxPackage -Name $publicName
    $report.family = $installed.PackageFamilyName
    $executable = Join-Path $installed.InstallLocation 'DesktopGuides.Production.exe'
    Start-PublicApp $executable
    $dataRoot = Join-Path $env:LOCALAPPDATA "Packages\$($installed.PackageFamilyName)\LocalState"
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    Invoke-Seed @('seed', $dataRoot) | Out-Null
    $report.libraryBefore = Get-LibraryState $dataRoot

    Add-AppxPackage -Path $NewPackage
    $upgraded = Get-AppxPackage -Name $publicName
    if ($upgraded.PackageFamilyName -ne $report.family) {
        throw "The package family changed: $($report.family) -> $($upgraded.PackageFamilyName)."
    }
    if ([Version] $upgraded.Version -ne $new.Version) {
        throw "Expected version $($new.Version) after the upgrade, got $($upgraded.Version)."
    }
    Start-PublicApp (Join-Path $upgraded.InstallLocation 'DesktopGuides.Production.exe')
    $report.upgradedAppLaunched = $true
    $report.libraryAfter = Get-LibraryState $dataRoot
    $report.libraryPreserved =
        ($report.libraryBefore | ConvertTo-Json -Depth 4) -eq ($report.libraryAfter | ConvertTo-Json -Depth 4)
    if (-not $report.libraryPreserved) { throw 'The library changed across the upgrade.' }
}
finally {
    Get-AppxPackage -Name $publicName | Remove-AppxPackage -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $launchResult, "$launchResult.ack" -ErrorAction SilentlyContinue
    if ($trusted) {
        Remove-Item -LiteralPath "Cert:\LocalMachine\TrustedPeople\$($trusted.Thumbprint)" -ErrorAction SilentlyContinue
    }
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ResultPath -Encoding UTF8
}
```

Check `windows_shell_launch.ps1`'s result fields (`success`, `error`,
`processId`, `handoffToken`) and its ack format against this code before
running; adjust the gate to match the launcher, not the other way round.
`Remove-AppxPackage` also deletes the package's `LocalState`.

- [ ] **Step 4: Run the preflight test to verify it passes**

Run: the test on the host.
Expected: "windows_package_upgrade preflight: all cases passed."

- [ ] **Step 5: Add the CI test step and the `public-upgrade` job**

In `core-tests`, after "Check the release CI gate":

```yaml
      - name: Check the upgrade gate preflight
        shell: pwsh
        run: ./tools/p1/test_windows_package_upgrade.ps1
```

A new job, after `production-packages`:

```yaml
  public-upgrade:
    # T17.1: two public builds from one commit, signed with a throwaway
    # certificate whose Subject is the public Publisher, must upgrade in place.
    needs: core-tests
    if: ${{ github.event_name == 'push' || (github.event_name == 'workflow_dispatch' && !inputs.dev-fast) }}
    runs-on: windows-2025
    steps:
      - uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2
      - uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9 # v4.3.1
        with:
          dotnet-version: 10.0.401
      - uses: actions/setup-python@a26af69be951a213d495a4c3e4e4022e16d87065 # v5.6.0
        with:
          python-version: '3.11'
      - name: Restore locked dependencies
        run: |
          dotnet restore src/DesktopGuides.Production/DesktopGuides.Production.csproj --locked-mode -p:Platform=x64
          dotnet restore tools/p1/DesktopGuides.ShellSeed/DesktopGuides.ShellSeed.csproj --locked-mode
      - name: Build two public versions and the seed tool
        shell: pwsh
        run: |
          dotnet build tools/p1/DesktopGuides.ShellSeed/DesktopGuides.ShellSeed.csproj -c Release --no-restore
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          $manifest = 'src/DesktopGuides.Production/Package.Public.appxmanifest'
          foreach ($version in @('1.0.0.0', '1.0.1.0')) {
            ./tools/p1/stamp_package_version.ps1 -Manifest $manifest -Version $version
            dotnet build src/DesktopGuides.Production/DesktopGuides.Production.csproj `
              -c Release --no-restore -p:Platform=x64 -p:PackageLane=Public `
              -p:AppxPackageDir=$PWD/artifacts/public-upgrade/$version/ `
              -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          }
      - name: Sign both with a throwaway certificate and run the gate
        shell: pwsh
        run: |
          $subject = 'CN=359962D5-3801-484E-BE5B-39D0D498BEA4'
          $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
            -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy NonExportable `
            -NotAfter (Get-Date).AddDays(1)
          try {
            $cer = Join-Path $env:RUNNER_TEMP 'throwaway.cer'
            Export-Certificate -Cert $cert -FilePath $cer | Out-Null
            $signed = @{}
            $previous = $null
            foreach ($version in @('1.0.0.0', '1.0.1.0')) {
              $unsigned = @(Get-ChildItem "artifacts/public-upgrade/$version" -Recurse -Filter '*_x64.msix')[0].FullName
              $output = Join-Path $env:RUNNER_TEMP "public-$version.msix"
              $arguments = @{
                PackagePath = $unsigned; OutputPath = $output
                CertificateThumbprint = $cert.Thumbprint
                ExpectedName = 'IlyaLissoboi.DesktopGuides'; ExpectedPublisher = $subject
                ExpectedVersion = $version; ExpectedArchitecture = 'x64'
                TimestampUrl = 'http://timestamp.digicert.com'
              }
              if ($previous) { $arguments.PreviousPackagePath = $previous }
              ./tools/p1/sign_production_package.ps1 @arguments | Out-Null
              $signed[$version] = $output
              $previous = $output
            }
            $seed = @(Get-ChildItem tools/p1/DesktopGuides.ShellSeed/bin/Release -Recurse -Filter 'DesktopGuides.ShellSeed.dll')[0].FullName
            New-Item -ItemType Directory -Force artifacts/public-upgrade | Out-Null
            ./tools/p1/windows_package_upgrade.ps1 -OldPackage $signed['1.0.0.0'] -NewPackage $signed['1.0.1.0'] `
              -Certificate $cer -SeedTool $seed -ResultPath artifacts/public-upgrade/result.json
          }
          finally {
            Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($cert.Thumbprint)" -ErrorAction SilentlyContinue
          }
      - name: Upload the upgrade result
        if: always()
        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4.6.2
        with:
          name: public-upgrade
          path: artifacts/public-upgrade/result.json
          if-no-files-found: warn
```

Check the parameter names against `sign_production_package.ps1`'s
`param` block before running and use its names.

- [ ] **Step 6: Run the job**

Commit, push and dispatch `windows-ci` on the branch.
Expected: `public-upgrade` passes; `result.json` shows the same family,
`1.0.0.0` → `1.0.1.0`, `libraryPreserved: true`, `upgradedAppLaunched: true`.
Every other job still passes.

- [ ] **Step 7: Commit**

```bash
git add tools/p1/windows_package_upgrade.ps1 tools/p1/test_windows_package_upgrade.ps1 .github/workflows/windows-ci.yml
git commit -m "test: prove public packages upgrade in place with the library"
```

(Commit before pushing in Step 6.)

### Task 9: The release certificate

**Files:**
- Create: `tools/p1/new_release_certificate.ps1`
- Test: `tools/p1/test_new_release_certificate.ps1`
- Modify: `.github/workflows/windows-ci.yml` (`core-tests` step)
- Create (Step 6, after the user runs the script): `docs/p1/release/desktop-guides-release.cer`,
  `docs/p1/release/release-certificate.json`

**Interfaces:**
- Produces:
  - `new_release_certificate.ps1 -OutputDirectory <new folder> [-Subject <CN>] [-Years 5]`
    writes `desktop-guides-release.pfx`, `desktop-guides-release.pfx.base64.txt`,
    `desktop-guides-release.password.txt` and `desktop-guides-release.cer`,
    removes the certificate from `Cert:\CurrentUser\My`, and prints
    `{subject, thumbprint, notBefore, notAfter}` as JSON followed by the
    two `gh secret set` commands.
  - `docs/p1/release/release-certificate.json`:
    `{"subject": "...", "thumbprint": "<40 hex>", "notAfter": "<ISO date>"}`,
    read by Task 10.

- [ ] **Step 1: Write the failing test**

```powershell
# tools/p1/test_new_release_certificate.ps1
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'new_release_certificate.ps1'

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

$subject = "CN=DesktopGuides CertScript Test $([Guid]::NewGuid().ToString('N'))"
$out = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-cert-$([Guid]::NewGuid().ToString('N'))"
try {
    $lines = @(& $script -OutputDirectory $out -Subject $subject -Years 1)
    $info = ($lines | Where-Object { $_ -like '{*' } | Select-Object -First 1) | ConvertFrom-Json
    Assert-True ($info.subject -eq $subject) 'The certificate Subject is wrong.'
    Assert-True ($info.thumbprint -match '^[0-9A-F]{40}$') 'The thumbprint is missing.'
    foreach ($file in @('pfx', 'pfx.base64.txt', 'password.txt', 'cer')) {
        Assert-True (Test-Path -LiteralPath (Join-Path $out "desktop-guides-release.$file")) "The $file file is missing."
    }
    Assert-True (-not (Test-Path "Cert:\CurrentUser\My\$($info.thumbprint)")) `
        'The certificate must be removed from the store.'
    Assert-True (@($lines | Where-Object { $_ -like '*gh secret set RELEASE_SIGNING_PFX*' }).Count -eq 1) `
        'The PFX secret command is missing.'
    $password = [IO.File]::ReadAllText((Join-Path $out 'desktop-guides-release.password.txt'))
    Assert-True (@($lines | Where-Object { $_.Contains($password) }).Count -eq 0) 'The password was printed.'
    $pfx = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
        (Join-Path $out 'desktop-guides-release.pfx'), $password)
    Assert-True ($pfx.Subject -eq $subject -and $pfx.HasPrivateKey) 'The PFX does not open with its password.'
    $eku = $pfx.Extensions | Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] }
    Assert-True (@($eku.EnhancedKeyUsages | Where-Object Value -eq '1.3.6.1.5.5.7.3.3').Count -eq 1) `
        'The certificate is not for code signing.'
    $pfx.Dispose()
    $refused = $false
    try { & $script -OutputDirectory $out -Subject $subject | Out-Null } catch { $refused = $true }
    Assert-True $refused 'A second run into a used folder must refuse.'
    Write-Output 'new_release_certificate: all cases passed.'
}
finally {
    Remove-Item -LiteralPath $out -Recurse -Force -ErrorAction SilentlyContinue
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: the test on the host.
Expected: FAIL: the script `new_release_certificate.ps1` is not recognized.

- [ ] **Step 3: Write the script**

```powershell
# tools/p1/new_release_certificate.ps1
param(
    # A new, empty folder for the PFX, its password and the public certificate.
    [Parameter(Mandatory = $true)] [string] $OutputDirectory,
    [string] $Subject = 'CN=359962D5-3801-484E-BE5B-39D0D498BEA4',
    [ValidateRange(1, 10)] [int] $Years = 5
)
# T17.1: creates the self-signed release certificate once. The PFX and its
# password go to the GitHub secrets and the owner's password manager; the
# certificate never stays in this machine's store. Prints no secret.
$ErrorActionPreference = 'Stop'
if ((Test-Path -LiteralPath $OutputDirectory) -and
    @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -gt 0) {
    throw "Use a new, empty folder: $OutputDirectory"
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$base = Join-Path $OutputDirectory 'desktop-guides-release'
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Subject `
    -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy Exportable `
    -NotAfter (Get-Date).AddYears($Years) -CertStoreLocation Cert:\CurrentUser\My
try {
    $bytes = [byte[]]::new(32)
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($bytes)
    $rng.Dispose()
    $password = [Convert]::ToBase64String($bytes)
    $secure = ConvertTo-SecureString -String $password -AsPlainText -Force
    Export-PfxCertificate -Cert $cert -FilePath "$base.pfx" -Password $secure | Out-Null
    Export-Certificate -Cert $cert -FilePath "$base.cer" | Out-Null
    $utf8 = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText("$base.password.txt", $password, $utf8)
    [IO.File]::WriteAllText("$base.pfx.base64.txt",
        [Convert]::ToBase64String([IO.File]::ReadAllBytes("$base.pfx")), $utf8)
}
finally {
    Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($cert.Thumbprint)" -ErrorAction SilentlyContinue
}
[ordered]@{
    subject = $cert.Subject
    thumbprint = $cert.Thumbprint
    notBefore = $cert.NotBefore.ToUniversalTime().ToString('o')
    notAfter = $cert.NotAfter.ToUniversalTime().ToString('o')
} | ConvertTo-Json -Compress
"Get-Content -Raw '$base.pfx.base64.txt' | gh secret set RELEASE_SIGNING_PFX --repo ilya-slalom/desktop-guides"
"Get-Content -Raw '$base.password.txt' | gh secret set RELEASE_SIGNING_PASSWORD --repo ilya-slalom/desktop-guides"
```

- [ ] **Step 4: Run the test to verify it passes, add it to CI, commit**

Run: the test on the host.
Expected: "new_release_certificate: all cases passed."

In `core-tests`, after "Check the upgrade gate preflight":

```yaml
      - name: Check release certificate creation
        shell: pwsh
        run: ./tools/p1/test_new_release_certificate.ps1
```

```bash
git add tools/p1/new_release_certificate.ps1 tools/p1/test_new_release_certificate.ps1 .github/workflows/windows-ci.yml
git commit -m "build: create the self-signed release certificate once"
```

- [ ] **Step 5: The user creates the certificate (stop and ask)**

This step needs the repository owner. Push the branch, then ask the user to
run, in their own interactive session on pcsx2-win (not over the
controller's ssh, because the PFX and password are theirs to keep):

```powershell
cd E:\work\desktop-guides\t17-1   # or a fresh checkout of the branch
pwsh -NoProfile -File tools\p1\new_release_certificate.ps1 -OutputDirectory $HOME\desktop-guides-release-cert
```

then to:
1. save `desktop-guides-release.pfx` and the password file's content in
   their password manager;
2. run the two printed `gh secret set` commands;
3. delete the output folder except `desktop-guides-release.cer`;
4. send the controller the printed JSON line (subject, thumbprint and
   dates; none of it is secret) and the `.cer` file (or leave it at a path
   the controller can copy over ssh).

Never read, copy or print the PFX or the password.

- [ ] **Step 6: Pin the certificate**

Copy the `.cer` to `docs/p1/release/desktop-guides-release.cer` and write
`docs/p1/release/release-certificate.json` from the user's JSON:

```json
{
  "subject": "CN=359962D5-3801-484E-BE5B-39D0D498BEA4",
  "thumbprint": "<40 hex from the user>",
  "notAfter": "<notAfter from the user>"
}
```

Check: `gh secret list --repo ilya-slalom/desktop-guides` lists
`RELEASE_SIGNING_PFX` and `RELEASE_SIGNING_PASSWORD`, and the `.cer`'s
thumbprint (on the host: `(Get-PfxCertificate <cer>).Thumbprint`) equals
the pinned one.

```bash
git add docs/p1/release/desktop-guides-release.cer docs/p1/release/release-certificate.json
git commit -m "build: pin the public release certificate"
```

### Task 10: The release workflow

**Files:**
- Create: `.github/workflows/release.yml`
- Create: `tools/p1/assert_release_certificate.ps1`
- Test: `tools/p1/test_release_certificate_pin.ps1`
- Create: `docs/p1/release/release-notes-template.md`
- Modify: `.github/workflows/windows-ci.yml` (`core-tests` step)

**Interfaces:**
- Consumes: Tasks 5–9 (`release_version.ps1`, `stamp_package_version.ps1`,
  `release_ci_gate.ps1`, `package_portable_release.ps1 -Version`,
  `windows_package_upgrade.ps1`, `release-certificate.json`, the `.cer`), the
  secrets `RELEASE_SIGNING_PFX` and `RELEASE_SIGNING_PASSWORD`.
- Produces: `assert_release_certificate.ps1 -Thumbprint <x> [-PinFile <json>]`
  throws unless the thumbprint and Subject match the pin and the pinned
  certificate hasn't expired. On a `vX.Y.Z` tag the workflow creates a draft
  pre-release; a `workflow_dispatch` (input `tag`) is a dry run that uploads
  the same files as the `release-X.Y.Z` artifact and creates nothing.

- [ ] **Step 1: Write the failing pin test**

```powershell
# tools/p1/test_release_certificate_pin.ps1
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'assert_release_certificate.ps1'

function Assert-Rejected([scriptblock] $Action, [string] $Message) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (-not $rejected) { throw $Message }
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-pin-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $scratch | Out-Null
$subject = "CN=DesktopGuides Pin Test $([Guid]::NewGuid().ToString('N'))"
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
    -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddDays(1)
try {
    function Write-Pin([string] $Thumbprint, [string] $PinSubject, [datetime] $NotAfter) {
        $path = Join-Path $scratch "pin-$([Guid]::NewGuid().ToString('N')).json"
        [ordered]@{ subject = $PinSubject; thumbprint = $Thumbprint; notAfter = $NotAfter.ToUniversalTime().ToString('o') } |
            ConvertTo-Json | Set-Content -LiteralPath $path -Encoding UTF8
        $path
    }
    $good = Write-Pin $cert.Thumbprint $subject $cert.NotAfter
    & $script -Thumbprint $cert.Thumbprint -PinFile $good

    $other = Write-Pin ('0' * 40) $subject $cert.NotAfter
    Assert-Rejected { & $script -Thumbprint $cert.Thumbprint -PinFile $other } `
        'A certificate that is not the pinned one must be rejected.'
    $wrongSubject = Write-Pin $cert.Thumbprint 'CN=Someone Else' $cert.NotAfter
    Assert-Rejected { & $script -Thumbprint $cert.Thumbprint -PinFile $wrongSubject } `
        'A Subject mismatch must be rejected.'
    $expired = Write-Pin $cert.Thumbprint $subject (Get-Date).AddDays(-1)
    Assert-Rejected { & $script -Thumbprint $cert.Thumbprint -PinFile $expired } `
        'An expired pin must be rejected.'
    Assert-Rejected { & $script -Thumbprint ('A' * 40) -PinFile $good } `
        'A thumbprint missing from the store must be rejected.'
    Write-Output 'release certificate pin: all cases passed.'
}
finally {
    Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($cert.Thumbprint)" -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: the test on the host.
Expected: FAIL: the script `assert_release_certificate.ps1` is not recognized.

- [ ] **Step 3: Write the pin check**

```powershell
# tools/p1/assert_release_certificate.ps1
param(
    [Parameter(Mandatory = $true)] [string] $Thumbprint,
    [string] $PinFile = (Join-Path $PSScriptRoot '..\..\docs\p1\release\release-certificate.json')
)
# T17.1: the release job signs only with the pinned certificate, so a wrong
# or rotated secret fails before anything is signed.
$ErrorActionPreference = 'Stop'
$pin = Get-Content -LiteralPath $PinFile -Raw | ConvertFrom-Json
if ($Thumbprint -ne $pin.thumbprint) {
    throw "The signing certificate $Thumbprint is not the pinned release certificate $($pin.thumbprint)."
}
$cert = Get-Item -LiteralPath "Cert:\CurrentUser\My\$Thumbprint" -ErrorAction SilentlyContinue
if (-not $cert) { throw "Certificate $Thumbprint is not in Cert:\CurrentUser\My." }
if ($cert.Subject -ne $pin.subject) { throw "The certificate Subject '$($cert.Subject)' is not '$($pin.subject)'." }
if ([datetime] $pin.notAfter -lt (Get-Date).ToUniversalTime()) {
    throw "The pinned release certificate expired on $($pin.notAfter); create and pin a new one."
}
```

- [ ] **Step 4: Run it to verify it passes; add it to CI**

Run: the test on the host.
Expected: "release certificate pin: all cases passed."

In `core-tests`, after "Check release certificate creation":

```yaml
      - name: Check the release certificate pin
        shell: pwsh
        run: ./tools/p1/test_release_certificate_pin.ps1
```

- [ ] **Step 5: Write the release notes template**

```markdown
<!-- docs/p1/release/release-notes-template.md: {{VERSION}}, {{COMMIT}},
     {{MSIX}}, {{PORTABLE}}, {{THUMBPRINT}} and {{RUNTIME}} are filled in by
     the release workflow. -->
# Desktop Guides {{VERSION}}

A pre-release for testing. Built from {{COMMIT}}.

## Install

1. Install the prerequisites, if you don't have them:
   - [Windows App Runtime {{RUNTIME}} or later](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)
   - [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (for web page guides; most Windows 11 PCs already have it)
2. Trust the release certificate once. In an administrator PowerShell, in
   the folder where you downloaded `desktop-guides-release.cer`:

   ```powershell
   Import-Certificate -FilePath .\desktop-guides-release.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```

   The certificate's thumbprint is `{{THUMBPRINT}}`.
3. Open `{{MSIX}}` and choose **Install**. Later releases upgrade it in
   place and keep your library.

## Portable build

`{{PORTABLE}}` holds one `DesktopGuides.Production.exe` that runs without
installing. It keeps its library in `%LOCALAPPDATA%\DesktopGuides`,
separate from the installed app's. It needs the WebView2 Runtime for web
page guides.

## Limits

- x64 only.
- Tested on Windows 11. Clean-machine and Windows 10 support aren't claimed yet.
- Back up your library from **Settings → Back up library** before testing a
  pre-release.

Check each download against its `.sha256` file and `release-manifest.json`.
```

- [ ] **Step 6: Write the workflow**

```yaml
# .github/workflows/release.yml
name: Release

on:
  push:
    tags: ['v*.*.*']
  workflow_dispatch:
    inputs:
      tag:
        description: Version tag to build as a dry run (vX.Y.Z); nothing is published
        required: true
        type: string

permissions:
  contents: read

concurrency:
  group: release
  cancel-in-progress: false

jobs:
  gate:
    runs-on: ubuntu-latest
    outputs:
      tag: ${{ steps.resolve.outputs.tag }}
      version: ${{ steps.resolve.outputs.version }}
      commit: ${{ steps.resolve.outputs.commit }}
    steps:
      - uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2
        with:
          fetch-depth: 0
      - id: resolve
        name: Resolve the version and check CI
        shell: pwsh
        env:
          GH_TOKEN: ${{ github.token }}
          INPUT_TAG: ${{ inputs.tag }}
        run: |
          $tag = if ($env:GITHUB_EVENT_NAME -eq 'push') { $env:GITHUB_REF_NAME } else { $env:INPUT_TAG }
          $version = ./tools/p1/release_version.ps1 -Tag $tag
          $commit = $env:GITHUB_SHA
          # A dry run may build any commit; a tag release needs main and green CI.
          if ($env:GITHUB_EVENT_NAME -eq 'push') {
            ./tools/p1/release_ci_gate.ps1 -Commit $commit -MainRef origin/main -Repository $env:GITHUB_REPOSITORY
          }
          "tag=$tag" >> $env:GITHUB_OUTPUT
          "version=$version" >> $env:GITHUB_OUTPUT
          "commit=$commit" >> $env:GITHUB_OUTPUT

  build:
    needs: gate
    runs-on: windows-2025
    env:
      VERSION: ${{ needs.gate.outputs.version }}
      TAG: ${{ needs.gate.outputs.tag }}
      COMMIT: ${{ needs.gate.outputs.commit }}
      GH_TOKEN: ${{ github.token }}
    steps:
      - uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2
        with:
          ref: ${{ needs.gate.outputs.commit }}
      - uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9 # v4.3.1
        with:
          dotnet-version: 10.0.401
      - uses: actions/setup-python@a26af69be951a213d495a4c3e4e4022e16d87065 # v5.6.0
        with:
          python-version: '3.11'
      - name: Restore locked dependencies
        run: |
          dotnet restore src/DesktopGuides.Production/DesktopGuides.Production.csproj --locked-mode -p:Platform=x64
          dotnet restore src/DesktopGuides.Production/DesktopGuides.Production.csproj --locked-mode -p:Platform=x64 -p:Portable=true
          dotnet restore tools/p1/DesktopGuides.ShellSeed/DesktopGuides.ShellSeed.csproj --locked-mode
      - name: Build and verify the public package
        id: package
        shell: pwsh
        run: |
          $ErrorActionPreference = 'Stop'
          $packageVersion = "$env:VERSION"
          ./tools/p1/stamp_package_version.ps1 -Manifest src/DesktopGuides.Production/Package.Public.appxmanifest -Version $packageVersion
          dotnet build src/DesktopGuides.Production/DesktopGuides.Production.csproj `
            -c Release --no-restore -p:Platform=x64 -p:PackageLane=Public `
            -p:AppxPackageDir=$PWD/artifacts/release-build/ `
            -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          $unsigned = @(Get-ChildItem artifacts/release-build -Recurse -Filter '*_x64.msix')
          if ($unsigned.Count -ne 1) { throw "Expected one public x64 MSIX, found $($unsigned.Count)." }
          New-Item -ItemType Directory -Force artifacts/previous, artifacts/release | Out-Null
          $previous = ''
          $published = gh release list --repo $env:GITHUB_REPOSITORY --exclude-drafts --limit 1 --json tagName --jq '.[0].tagName'
          if ($published) {
            gh release download $published --repo $env:GITHUB_REPOSITORY --pattern '*.msix' --dir artifacts/previous
            $previous = @(Get-ChildItem artifacts/previous -Filter '*.msix')[0].FullName
          }
          $verifyArguments = @($unsigned[0].FullName, '--name', 'IlyaLissoboi.DesktopGuides',
            '--publisher', 'CN=359962D5-3801-484E-BE5B-39D0D498BEA4', '--version', $packageVersion, '--architecture', 'x64')
          if ($previous) { $verifyArguments += @('--previous-package', $previous) }
          $identity = python tools/p1/verify_production_package.py @verifyArguments | ConvertFrom-Json
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          "unsigned=$($unsigned[0].FullName)" >> $env:GITHUB_OUTPUT
          "previous=$previous" >> $env:GITHUB_OUTPUT
          "runtime=$($identity.windowsAppRuntimeMinimum)" >> $env:GITHUB_OUTPUT
      - name: Sign the package
        id: sign
        shell: pwsh
        env:
          RELEASE_SIGNING_PFX: ${{ secrets.RELEASE_SIGNING_PFX }}
          RELEASE_SIGNING_PASSWORD: ${{ secrets.RELEASE_SIGNING_PASSWORD }}
        run: |
          $ErrorActionPreference = 'Stop'
          if (-not $env:RELEASE_SIGNING_PFX -or -not $env:RELEASE_SIGNING_PASSWORD) { throw 'The release signing secrets are not set.' }
          $pfx = Join-Path $env:RUNNER_TEMP 'release.pfx'
          [IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:RELEASE_SIGNING_PFX))
          try {
            $password = ConvertTo-SecureString -String $env:RELEASE_SIGNING_PASSWORD -AsPlainText -Force
            $cert = Import-PfxCertificate -FilePath $pfx -CertStoreLocation Cert:\CurrentUser\My -Password $password
          }
          finally {
            Remove-Item -LiteralPath $pfx -Force -ErrorAction SilentlyContinue
          }
          "thumbprint=$($cert.Thumbprint)" >> $env:GITHUB_OUTPUT
          ./tools/p1/assert_release_certificate.ps1 -Thumbprint $cert.Thumbprint
          $msix = "artifacts/release/DesktopGuides-$env:VERSION-x64.msix"
          $arguments = @{
            PackagePath = '${{ steps.package.outputs.unsigned }}'; OutputPath = $msix
            CertificateThumbprint = $cert.Thumbprint
            ExpectedName = 'IlyaLissoboi.DesktopGuides'
            ExpectedPublisher = 'CN=359962D5-3801-484E-BE5B-39D0D498BEA4'
            ExpectedVersion = "$env:VERSION"; ExpectedArchitecture = 'x64'
            TimestampUrl = 'http://timestamp.digicert.com'
          }
          if ('${{ steps.package.outputs.previous }}') { $arguments.PreviousPackagePath = '${{ steps.package.outputs.previous }}' }
          ./tools/p1/sign_production_package.ps1 @arguments | Set-Content artifacts/release/signing.json -Encoding utf8
      - name: Remove the signing certificate
        if: always()
        shell: pwsh
        run: |
          if ('${{ steps.sign.outputs.thumbprint }}') {
            Remove-Item -LiteralPath 'Cert:\CurrentUser\My\${{ steps.sign.outputs.thumbprint }}' -ErrorAction SilentlyContinue
          }
      - name: Upgrade from the previous release
        if: steps.package.outputs.previous != ''
        shell: pwsh
        run: |
          dotnet build tools/p1/DesktopGuides.ShellSeed/DesktopGuides.ShellSeed.csproj -c Release --no-restore
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          $seed = @(Get-ChildItem tools/p1/DesktopGuides.ShellSeed/bin/Release -Recurse -Filter 'DesktopGuides.ShellSeed.dll')[0].FullName
          ./tools/p1/windows_package_upgrade.ps1 -OldPackage '${{ steps.package.outputs.previous }}' `
            -NewPackage "artifacts/release/DesktopGuides-$env:VERSION-x64.msix" `
            -Certificate docs/p1/release/desktop-guides-release.cer `
            -SeedTool $seed -ResultPath artifacts/release/upgrade.json
      - name: Build the portable release
        shell: pwsh
        run: |
          dotnet publish src/DesktopGuides.Production/DesktopGuides.Production.csproj `
            -c Release --no-restore -p:Platform=x64 -p:Portable=true
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          ./tools/p1/package_portable_release.ps1 -Commit $env:COMMIT -Version ($env:VERSION -replace '\.0$', '') `
            -OutputDirectory artifacts/release
      - name: Write the release manifest and notes
        shell: pwsh
        run: |
          $ErrorActionPreference = 'Stop'
          $short = $env:VERSION -replace '\.0$', ''
          $msix = Get-Item "artifacts/release/DesktopGuides-$env:VERSION-x64.msix"
          $msixHash = (Get-FileHash $msix.FullName -Algorithm SHA256).Hash
          [IO.File]::WriteAllText("$($msix.FullName).sha256", "$msixHash  $($msix.Name)`n", [Text.UTF8Encoding]::new($false))
          $portable = Get-Item "artifacts/release/DesktopGuides-portable-x64-$short.zip"
          $signing = Get-Content artifacts/release/signing.json -Raw | ConvertFrom-Json
          $pin = Get-Content docs/p1/release/release-certificate.json -Raw | ConvertFrom-Json
          $windowsAppSdk = ([xml](Get-Content Directory.Packages.props -Raw)).Project.ItemGroup.PackageVersion |
            Where-Object Include -eq 'Microsoft.WindowsAppSDK' | ForEach-Object Version
          [ordered]@{
            version = $short; packageVersion = $env:VERSION; tag = $env:TAG; commit = $env:COMMIT
            identity = [ordered]@{ name = 'IlyaLissoboi.DesktopGuides'; publisher = 'CN=359962D5-3801-484E-BE5B-39D0D498BEA4'; architecture = 'x64' }
            msix = [ordered]@{ file = $msix.Name; bytes = $msix.Length; sha256 = $msixHash }
            portable = [ordered]@{ file = $portable.Name; bytes = $portable.Length; sha256 = (Get-FileHash $portable.FullName -Algorithm SHA256).Hash }
            signer = [ordered]@{ thumbprint = $signing.signerThumbprint; notAfter = $pin.notAfter; timestamped = $signing.timestamped }
            windowsAppRuntimeMinimum = '${{ steps.package.outputs.runtime }}'
            windowsAppSdk = $windowsAppSdk
            prerequisites = [ordered]@{
              windowsAppRuntime = 'https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads'
              webView2 = 'https://developer.microsoft.com/microsoft-edge/webview2/'
            }
            previousRelease = '${{ steps.package.outputs.previous }}' -ne ''
          } | ConvertTo-Json -Depth 4 | Set-Content artifacts/release/release-manifest.json -Encoding utf8
          Copy-Item docs/p1/release/desktop-guides-release.cer artifacts/release/
          $notes = Get-Content docs/p1/release/release-notes-template.md -Raw
          $notes = $notes -replace '(?s)^<!--.*?-->\s*', ''
          foreach ($pair in @(
              @('{{VERSION}}', $short), @('{{COMMIT}}', $env:COMMIT), @('{{MSIX}}', $msix.Name),
              @('{{PORTABLE}}', $portable.Name), @('{{THUMBPRINT}}', $signing.signerThumbprint),
              @('{{RUNTIME}}', '${{ steps.package.outputs.runtime }}'))) {
            $notes = $notes.Replace($pair[0], $pair[1])
          }
          if ($notes -match '\{\{') { throw 'The release notes still have an unfilled placeholder.' }
          Set-Content artifacts/release-notes.md $notes -Encoding utf8
          Remove-Item artifacts/release/signing.json
      - uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4.6.2
        with:
          name: release-${{ needs.gate.outputs.version }}
          path: |
            artifacts/release/*
            artifacts/release-notes.md
          if-no-files-found: error

  publish:
    needs: [gate, build]
    if: github.event_name == 'push'
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - uses: actions/download-artifact@d3f86a106a0bac45b974a628896c90dbdf5c8093 # v4.3.0
        with:
          name: release-${{ needs.gate.outputs.version }}
          path: release
      - name: Create the draft pre-release
        env:
          GH_TOKEN: ${{ github.token }}
          TAG: ${{ needs.gate.outputs.tag }}
        run: |
          set -euo pipefail
          if gh release view "$TAG" --repo "$GITHUB_REPOSITORY" >/dev/null 2>&1; then
            echo "A release for $TAG already exists." >&2; exit 1
          fi
          version="${TAG#v}"
          gh release create "$TAG" --repo "$GITHUB_REPOSITORY" --draft --prerelease \
            --title "Desktop Guides $version" --notes-file release/release-notes.md \
            release/release/*
```

Check the action SHAs against the ones `windows-ci.yml` already pins and
reuse them; `actions/download-artifact` isn't used there yet, so pin its
current v4 release SHA. Check how `upload-artifact` lays out two paths
(the download may nest them as `release/release/*` and
`release/release-notes.md`, or flatten them) with the dry run in Step 7, and
fix the `publish` paths to match.

- [ ] **Step 7: Dry-run the workflow**

Commit and push the branch, then:
`gh workflow run release.yml --repo ilya-slalom/desktop-guides --ref feat/p1-t17-1-release -f tag=v1.0.0`.

A dry run skips the CI gate (ruling 7), so it works from the branch.

Expected: `gate` and `build` pass, no `publish` job runs, and the
`release-1.0.0.0` artifact holds `DesktopGuides-1.0.0.0-x64.msix` and its
`.sha256`, `desktop-guides-release.cer`, `DesktopGuides-portable-x64-1.0.0.zip`
with its `.sha256` and `.json`, `release-manifest.json` (signer thumbprint
equals the pin, `timestamped: true`) and `release-notes.md` with no `{{`.
Download it and check the MSIX signature on the host:
`signtool verify /pa /tw <msix>`.

- [ ] **Step 8: Commit**

```bash
git add .github/workflows/release.yml tools/p1/assert_release_certificate.ps1 tools/p1/test_release_certificate_pin.ps1 docs/p1/release/release-notes-template.md .github/workflows/windows-ci.yml
git commit -m "build: release signed public packages from version tags"
```

(Commit before pushing in Step 7.)

### Task 11: PR b documentation, T17.5, verification record and PR

**Files:**
- Modify: `docs/p1/release-packaging.md`
- Modify: `docs/p1/t17-1-release-identity-design.md` (status, record)
- Modify: `docs/p1/implementation-plan.md` (T17.1 paragraph and row; T17.5 row; count)
- Modify: `docs/work-breakdown.md` (S17: T17.1 bullet; new T17.5 bullet)
- Modify: `docs/progress.md` (T17.1 row; P1 summary count 55 → 56 tasks)

- [ ] **Step 1: Update the release procedure**

In `release-packaging.md`:
- Status line: T17.1 public identity and signing are implemented.
- The lanes table's Public row (from Task 4) plus the pinned thumbprint and
  `notAfter` from `release-certificate.json`.
- Replace the manual "Windows 11 x64 candidate" signing steps with the
  release workflow: tag `vX.Y.Z` on a green `main` commit, the dry run
  (`gh workflow run release.yml -f tag=vX.Y.Z`), the draft pre-release, and
  the manual publish.
- A "Release certificate" section: how it was created
  (`new_release_certificate.ps1`), where the PFX and password live (the
  two secrets and the owner's password manager), the pin file, and
  rotation: create a new certificate with the same Subject, update both
  secrets and the pin, and tell users to trust the new `.cer`.
- The user install steps (as in the release notes template).
- The upgrade gate: the `public-upgrade` CI job and the release job's
  previous-release upgrade.

- [ ] **Step 2: Add T17.5 and update the counts**

- `work-breakdown.md`, after T17.3:
  "- **T17.5** Submit to the Microsoft Store with the reserved identity
  (`IlyaLissoboi.DesktopGuides`) once feature development is finished: pass
  Store certification, write the listing, and confirm the Store-signed
  package upgrades sideloaded installs in place. Decide whether sideloaded
  releases continue."
- `implementation-plan.md`: a T17.5 row (prerequisites: T17.3 and every P1
  feature task; TR: TR17.1, TR17.2) and the status line's task count 56.
- `progress.md`: the P1 summary says "of 56 tasks" and lists T17.5 under
  Open.

- [ ] **Step 3: Record the verification**

Add `### PR b: release` to the design's verification record: the
PowerShell test results, the `public-upgrade` run ID and its `result.json`
fields, the release dry-run run ID and the artifact's file list, the host
`signtool verify /pa /tw` output, the pinned thumbprint, rulings 2–7, and
what isn't run (a real tag release, which needs the user's go-ahead after
merge; ARM64; clean-machine). Set the design status to "Status: PR a merged
in #<a>; PR b in review."

- [ ] **Step 4: Check, commit and open PR b**

Run: `git diff --check`
Expected: no output.

```bash
git add docs
git commit -m "docs(p1): record T17.1 PR b verification and add T17.5"
```

Push and open the PR. The body names T17.1 PR b, the prerequisites (PR a
merged; the release certificate created and its secrets set by the owner),
the outcome (tag-driven, signed, draft pre-releases with a portable build;
an upgrade gate on every main build), the run IDs, and ends with the Claude
Code line. No UI change, so no screenshot.

- [ ] **Step 5: After merge (stop and ask)**

Pushing the first tag publishes a draft release on GitHub, which is
outward-facing. Ask the user before running
`git tag -a v1.0.0 -m "Desktop Guides 1.0.0" <main sha> && git push origin v1.0.0`,
then report the draft release URL for them to review and publish.

---

## Traceability

| Requirement | Covered by |
|---|---|
| TR17.2: one package identity across upgrades, tested version increase | Tasks 2, 5, 8, 10 |
| Public identity reserved for the Store and used for sideloaded builds | Tasks 2, 9 |
| Signing secret-backed, private key never in source or logs | Tasks 9, 10 |
| Release output includes the portable build | Tasks 7, 10 |
| Preview lane unchanged | Tasks 1, 2, 3 |
| Store certification deferred | Task 11 (T17.5) |

## PR outcome

### PR a

- **Target task:** T17.1, PR a.
- **Prerequisites:** T11.1 and the 27 September groundwork, merged.
- **Outcome:** `-p:PackageLane=Public` builds the reserved identity; every
  lane-dependent name comes from `AppLaneNames`; Preview is unchanged and CI
  verifies both lanes.

### PR b

- **Target task:** T17.1, PR b.
- **Prerequisites:** PR a merged; the release certificate created and its
  secrets set.
- **Outcome:** a `vX.Y.Z` tag on a green `main` commit produces a signed,
  timestamped MSIX, the portable zip, checksums and a manifest as a draft
  pre-release; an installed upgrade gate runs on every main build and on
  each release.
