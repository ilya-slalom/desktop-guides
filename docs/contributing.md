# Contributing

This page is for anyone changing Desktop Guides, people and coding agents
alike. It covers the toolchain, building, tests, CI and the conventions a
change has to follow. Read [architecture.md](architecture.md) before changing
code. The installed end-to-end harness has its own page,
[testing.md](testing.md).

## Repository layout

- `src/DesktopGuides.Core`: portable library code (`net10.0`).
- `src/DesktopGuides.Infrastructure`: storage, import, providers and online
  sources (`net10.0`).
- `src/DesktopGuides.Production`: the WinUI 3 app that ships, as an MSIX and
  as a portable exe (`net10.0-windows10.0.19041.0`).
- `src/DesktopGuides.App`: the original reader probe. CI still builds and
  runs it as a regression lane.
- `tests/DesktopGuides.Core.Tests`, `tests/DesktopGuides.Infrastructure.Tests`:
  the unit tests (`net10.0`).
- `tests/fixtures`: guide, provider and online fixtures.
- `tools/p0`: fixture generators and the probe's UI scripts.
- `tools/p1`: the installed test harness, release scripts and their tests.

## Prerequisites

### Windows

You need Windows to build the app, package it and run the UI tests.

- .NET SDK `10.0.401`. [`global.json`](../global.json) pins it with
  `rollForward: disable`, so no other SDK builds the repo.
- The Windows SDK. The harness signs test packages with its `signtool.exe`.
- The Windows App Runtime 2.5 (x64), at least `2.5.1`. The MSIX is
  framework-dependent and won't install without it. The NuGet package
  `Microsoft.WindowsAppSDK` is `2.5.1`.
- The Evergreen Microsoft Edge WebView2 Runtime. HTML guides need it; TXT and
  PDF work without it.
- Python 3.11 for the fixture scripts. CI uses 3.11.
- PowerShell. CI uses `pwsh`; the harness also runs under Windows
  PowerShell 5.1.

The app targets Windows 10 build 19041 and later, on x64 and ARM64. Only the
targets in [release/tested-matrix.md](release/tested-matrix.md) have been
tested.

### macOS

On macOS you can build and test Core and Infrastructure, which target
`net10.0`. That is faster than a CI round trip. Install SDK `10.0.401` and
Python 3. The WinUI projects, packaging and every UI test need Windows.

## Fixtures

Some test fixtures are generated, not checked in. Generate them before any
test run or build that uses them:

```sh
python3 tools/p0/make_fixtures.py
python3 tools/p0/verify_fixtures.py
```

- `make_fixtures.py` writes the self-authored TXT, PDF and image fixtures
  under `tests/fixtures/p0`, and its `manifest.json` with each file's hash.
  The large ones (the long TXT and PDF guides, the outline and line PDFs,
  the maps) go to `tests/fixtures/p0/generated/`, which is gitignored.
- `verify_fixtures.py` checks every file against the manifest.
- Unit tests and the installed harness read these files. Without
  `generated/`, the PDF, maps and long-text checks fail.
- CI regenerates them and fails if a tracked fixture or the manifest
  changed. When you change the generator, commit the new manifest and
  tracked files.

## Restore

Every project has a `packages.lock.json`, and package versions are central
in [`Directory.Packages.props`](../Directory.Packages.props). Restore in
locked mode, as CI does:

```powershell
dotnet restore tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj --locked-mode
dotnet restore tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj --locked-mode
dotnet restore src\DesktopGuides.Production\DesktopGuides.Production.csproj --locked-mode -p:Platform=x64
```

A locked restore fails if a lock file doesn't match the project. When you
change a dependency, restore without `--locked-mode` and commit the updated
lock files with the change.

## Build

### The production app (MSIX)

The usual check before a PR is a clean Release x64 build with warnings as
errors:

```powershell
dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj `
  -c Release -p:Platform=x64 -nologo -warnaserror
```

To produce the unsigned MSIX, as CI does (CI doesn't pass `-warnaserror`):

```powershell
dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj `
  -c Release --no-restore -p:Platform=x64 `
  -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false
```

The MSIX lands under `src\DesktopGuides.Production\AppPackages\`. Use
`-p:Platform=ARM64` for ARM64. The installed harness signs a copy with a
temporary certificate; see [testing.md](testing.md).

### The portable exe

The portable build is one self-contained `DesktopGuides.Production.exe` that
bundles .NET and the Windows App SDK. It is trimmed, compressed and
ReadyToRun. Start from an empty `artifacts\portable` folder, because publish
doesn't delete files an earlier build left there.

```powershell
dotnet restore src\DesktopGuides.Production\DesktopGuides.Production.csproj `
  --locked-mode -p:Platform=x64 -p:Portable=true
dotnet publish src\DesktopGuides.Production\DesktopGuides.Production.csproj `
  -c Release --no-restore -p:Platform=x64 -p:Portable=true
tools\p1\package_portable_release.ps1 -Commit <full hash>
```

- The exe goes to
  `artifacts\portable\bin\DesktopGuides.Production\x64\Release\net10.0-windows10.0.19041.0\win-x64\publish\`,
  apart from the MSIX build's `bin` and `obj` folders.
- `package_portable_release.ps1` refuses a publish folder that holds anything
  besides the exe, and never replaces an existing zip. It writes
  `DesktopGuides-portable-x64-<short hash>.zip`, a `.zip.sha256` and a `.json`
  manifest to `artifacts\portable\release\`.
- On first launch each build unpacks its bundle to
  `%TEMP%\.net\DesktopGuides.Production\<bundle id>\`. The Windows App SDK
  needs every file on disk. Deleting that folder while the app is closed is
  safe.

### Build switches

| Switch | Default | Effect |
| --- | --- | --- |
| `-p:Platform=` | none | `x64` or `ARM64`. Required for the WinUI projects. |
| `-p:PackageLane=` | `Preview` | `Public` builds the released identity, `IlyaLissoboi.DesktopGuides`. It also compiles out the test-only fixture paths. |
| `-p:Portable=true` | off | The unpackaged, self-contained single-file build. Its output goes under `artifacts\portable`. |
| `-p:IncludeGameFaqs=false` | on | Leaves the GameFAQs source out. Its pasted URLs are refused like any other site. |
| `-p:IncludeNeoseeker=false` | on | The same for Neoseeker. |
| `-p:IncludeSteam=false` | on | The same for Steam. |
| `-p:IncludeVgMaps=false` | on | The same for VGMaps. |

### The trim-warning gate

The portable publish must not add trim or C#/WinRT warnings.
`tools/p1/assert_trim_warnings.ps1` reads a normal-verbosity publish log. It
fails on any warning that `tools/p1/trim-warning-allowlist.txt` doesn't name,
and on any allow-list entry that no warning needs. Only assembly-level
`IL2104` entries for assemblies the project doesn't own are allowed. CI runs
it like this:

```powershell
dotnet publish src\DesktopGuides.Production\DesktopGuides.Production.csproj `
  -c Release --no-restore -p:Platform=x64 -p:Portable=true `
  "-flp:logfile=artifacts\portable-publish.log;verbosity=normal"
tools\p1\assert_trim_warnings.ps1 -Log artifacts\portable-publish.log
```

`tools/p1/assert_excluded_components.ps1` also checks that unused Windows App
SDK components stay out of the MSIX and the portable bundle.

## Unit tests

On Windows:

```powershell
dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release --no-restore
dotnet test tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj -c Release --no-restore
```

Add `--filter "<expression>"` to run a subset.

On macOS, after the same locked restore of the two test projects:

```sh
dotnet test tests/DesktopGuides.Core.Tests/DesktopGuides.Core.Tests.csproj --no-restore
mkdir -p /private/tmp/dg
TMPDIR=/private/tmp/dg/ dotnet test tests/DesktopGuides.Infrastructure.Tests/DesktopGuides.Infrastructure.Tests.csproj --no-restore
```

- `TMPDIR` must not cross a symbolic link. The default `/var/folders/...`
  goes through `/var -> /private/var`, which the managed-path checks reject.
  The short path also keeps local socket names under the macOS length limit.
- Tests that need Windows (the static HTML boundary, file locks, package
  identity) return early elsewhere. A green macOS run doesn't replace the
  Windows run.

The PowerShell harness has its own tests, `tools/p1/test_*.ps1`, which CI
runs in `core-tests`. Run the one for a script you change.
`test_windows_scripts_parse.ps1` fails on any `tools/p1` script that doesn't
parse, or whose if-chain has a branch after its `else`.

## CI

[`.github/workflows/windows-ci.yml`](../.github/workflows/windows-ci.yml) runs
on every pull request and on pushes to `main`.

- Pushes and PRs that change only `docs/**` or Markdown files skip CI, so a
  docs-only PR has no check run.
- A new push to a PR cancels that PR's older run. Dispatches and pushes to
  `main` are never cancelled.

### Jobs

| Job | What it does |
| --- | --- |
| `core-tests` | Fixtures, locked restore, Core and Infrastructure tests, every `tools/p1/test_*.ps1`, the loopback-canary Python tests. The package jobs depend on it. |
| `packages` | The probe app's unsigned MSIX, x64 and ARM64. |
| `production-packages` | The unsigned Preview MSIX, x64 and ARM64, with identity and excluded components checked. On x64 it also builds the Public lane and checks the fixture-provider code is absent from it. |
| `portable-packages` | The portable exe, with the excluded-component and trim-warning gates, zipped. |
| `portable-measure` | The portable exe's size, extraction and launch times. |
| `public-upgrade` | Two Public builds signed with a throwaway certificate must upgrade in place. Pushes and full dispatches only. |
| `production-shell-ui` | The installed harness, 6 shards × 2 lanes; see [testing.md](testing.md#ci-shards). |
| `reader-toolbar-ui` | The reader toolbar's installed test package, with timeout-cleanup and overlapping-install checks. |
| `native-arm64-core`, `native-arm64-ui` | The unit tests, then the probe package's UI suite, natively on ARM64. |

### Manual dispatch

Dispatch the workflow for faster iteration on a branch:

```sh
gh workflow run windows-ci.yml --ref <branch> -f shell-scope=html -f dev-fast=true
```

| Input | Values | Effect |
| --- | --- | --- |
| `shell-scope` | `all` or one group name | Runs one scenario group as a single shard. |
| `shell-lane` | `both`, `msix`, `portable` | Narrows the installed shards to one lane. |
| `shell-pass` | comma-separated wildcards | With `shell-scope=pdf` only, runs the matching PDF passes, for example `pdf-zoom-*,pdf-locked-light`. |
| `dev-fast` | boolean | Builds only the x64 production package and portable exe next to `core-tests`, then runs the shell shards. Skips the probe, toolbar, ARM64 and upgrade jobs. |
| `verify-test-gate` | boolean | Adds a failing Core test to show that packaging is skipped. Overrides `dev-fast`. |

A dispatch run is not PR evidence. Push and pull-request runs always use
the full job graph and every scenario group.

## Working conventions

### Test first

Write a failing test, see it fail, then make it pass. Pick the cheapest
layer that can see the behavior: a Core or Infrastructure test where
possible, an installed mode for what only the running app shows. For
installed tests, assert what the app's code controls; see
[testing.md](testing.md#what-ui-tests-assert). A purely mechanical change
(config, dependencies, moves) can skip this; say so in the PR.

### Branches

Branch from `main` and name the branch `<type>/<item>-<slug>`:

- `feat/` for features, `fix/` for defects, `docs/` for documentation,
  `test/` for test-only changes, `ci/` for workflow changes.
- `<item>` is the roadmap item or issue number, for example
  `feat/p3-s22-vgmaps` or `fix/94-wal-anchor`.

### Commits

Commit subjects follow `type(scope): summary`, for example
`feat(p3): VGMaps in Find online` or `fix(p3): Steam review fixes`.

- Types: `feat`, `fix`, `test`, `docs`, `refactor`, `ci`.
- The scope names the roadmap item or area. It may be left out, as in
  `docs: …` or `test: …`.
- The summary is a plain lowercase phrase with no trailing period.
- A failing test can land as its own `test(...)` commit before the `feat`
  or `fix` that makes it pass.

### Pull requests

Every PR description:

- names the target item in [roadmap.md](roadmap.md);
- lists its prerequisites and their status;
- states the intended outcome;
- for UI changes, includes a screenshot of the changed interface.

Link the CI artifacts that show the installed checks. Remove a roadmap item
once its work is finished. Keep [architecture.md](architecture.md) and this
page current when the design or the workflow changes.

### Review

- Review first for functional defects and regressions.
- Look at test-harness details and small edge cases when they affect those
  outcomes.
- For an HTML or PDF finding, ask whether a guide from a major public
  game-guide website could realistically trigger it through a supported
  import path. Check representative guides when you can. Set priority from
  likely frequency and user impact. State your uncertainty instead of
  treating a contrived fixture as evidence of prevalence.

## Product invariants

Every change keeps these. [architecture.md](architecture.md) explains how the
code enforces them.

- Imported guides stay readable offline. The app reads its managed copies,
  never the originals.
- Import never changes the original files.
- Imported HTML and its asset paths are untrusted.
- Each guide keeps a stable locator for its place.
- Completion is an explicit state the user sets. It is never inferred from
  the estimated reading percentage or from reaching the end.
- TXT, HTML (WebView2), PDF and image maps are separate reader adapters
  behind a common interface.
- Online sources follow [sources.md](sources.md). No new source or fetch
  path goes in without its terms review. Live sites are checked by hand,
  never by automation.
- Support for a Windows version or architecture is claimed only after reader
  behavior and packaging are validated on it, and recorded in
  [release/tested-matrix.md](release/tested-matrix.md).

## App icons

The icon masters are in `docs/assets/`. After changing them, regenerate the
app's logos and `AppIcon.ico`. The script needs Pillow.

```sh
python3 tools/p1/generate_app_icon_assets.py src/DesktopGuides.Production/Assets
```

Releases are covered in [releasing.md](releasing.md).
