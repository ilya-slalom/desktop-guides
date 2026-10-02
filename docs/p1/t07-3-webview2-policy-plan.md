# T07.3 WebView2 manifest responder and deny rules — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An imported static HTML guide opens in the production Reader in a
WebView2 that serves only that guide's approved files, makes no network
request of its own, and leaves the app only through an explicit **Open in
browser** action.

**Architecture:** Schema v4 stores each HTML guide's preview manifest as
`GuideAssets` rows in the publication transaction. Pure Core policies
(`GuideWebOrigin`, `HtmlRequestPolicy`, `HtmlNavigationPolicy`) decide every
request and navigation. Infrastructure re-reads and re-hashes the managed
file for each served row. A minimal Production `HtmlReaderSession` wires the
policies to WebView2, and a Reader InfoBar carries external links. An
installed online/offline canary run proves zero guide-originated traffic.

**Tech Stack:** .NET 10, C#, xUnit, Microsoft.Data.Sqlite, WinUI 3 with
WebView2 (transitive through WindowsAppSDK), PowerShell UI Automation smoke,
Python 3 canary.

**Spec:** [t07-3-webview2-policy-design.md](t07-3-webview2-policy-design.md)

## Global Constraints

- Treat imported HTML and asset paths as untrusted; serve files only through
  a row's `RelativePath` via `ManagedPathResolver`, never the request path.
- Keep originals untouched and guides readable offline.
- Origin: `https://g<GuideId:N>.guide.invalid`.
- CSP, verbatim (one line in the header):
  `default-src 'none'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; font-src 'none'; script-src 'none'; frame-src 'none'; form-action 'none'; connect-src 'none'; object-src 'none'; base-uri 'none'`
- Every response: `X-Content-Type-Options: nosniff`, `Cache-Control: no-store`.
  Denied: 403, empty body.
- Content types: `.html`/`.htm` `text/html`, `.css` `text/css`, `.png`
  `image/png`, `.jpg`/`.jpeg` `image/jpeg`, `.gif` `image/gif`, `.webp`
  `image/webp`; anything else denied. No `charset` parameter.
- Copy, verbatim:
  - "Web page guides need the Microsoft Edge WebView2 Runtime."
  - "Re-import this guide to read it."
  - "This guide's files have changed. Re-import it to read it."
  - Bar title "This link leaves Desktop Guides"; buttons **Open in browser**
    and **Dismiss**.
- AutomationIds: `ReaderExternalLinkBar`, `ReaderExternalLinkUrl`,
  `ReaderExternalLinkOpen`, `ReaderExternalLinkDismiss`.
- Gates: `Local\DesktopGuides.Preview.HtmlDiagnostics.<pid>` and
  `Local\DesktopGuides.Preview.ExternalLaunch.<pid>`.
- Diagnostics never contain external URLs or guide text.
- PowerShell stays ASCII-only. UI tests assert only what app code controls.
- No local `dotnet`: RED/GREEN is observed by pushing and reading the CI
  `core-tests` job (`gh run list --branch feat/p1-t07-3-webview2-policy`,
  then `gh run view <id> --log-failed`). The installed gate is
  `production-shell-ui`.
- The canary is loopback-only; add no firewall rules.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Query strings on asset references** (`style.css?ver=5.8`, common in
   saved WordPress pages). The scanner strips the query when it builds the
   manifest, so the browser's `/style.css?ver=5.8` request must still serve
   the row. Pinned in Task 2 (`QueryIsIgnoredForLookup`). This deviates from
   the spec's "no query" rule; see Ruling R1.
2. **Percent-named companion folders** (`100% Completion Guide_files`)
   requested as `%25%20`, a lone `%`, or `%2525`. The first two serve and
   the double-encoded form denies. Pinned in Tasks 1 and 2.
3. **Legacy-encoded HTML and CSS** (Windows-1252 meta charset or a BOM). No
   charset in `Content-Type`, so the document's own declaration wins. Pinned
   in Task 2 (`ContentTypesCarryNoCharset`).
4. **Non-user-initiated external navigation** (`meta refresh`, server-style
   redirects) must not pop the bar. Pinned in Task 2 (`Classify` tables),
   and in Task 6 by the canary page's `meta refresh` to a loopback URL.
5. **Reference casing differing from on-disk casing** (`IMAGES/Map.png` for
   `images/map.png` on NTFS). The stored `RequestPath` must keep the
   reference's casing, because that is what the browser requests. Pinned in
   Task 1 (`RequestPathKeepsTheReferenceCasing`).

## Rulings (planning decisions that refine the spec)

- **R1. Queries are ignored for lookup, not denied.** The scanner already
  drops `?…` and `#…` when it records a reference, so denying queries would
  break realistic guides. A query never changes which file is served.
- **R2. No "no `%` after decoding" rule.** A single decode of `%252e` gives
  `%2e`, which matches no row, so the rule adds nothing and would deny real
  folders named with `%`.
- **R3. Portable cache root** is `%LOCALAPPDATA%\DesktopGuides\Cache`
  (`AppCacheRoot`); packaged uses `ApplicationData.Current.LocalCacheFolder`.
- **R4. The seeded "Web Page Guide"** in `seed-txt-reader` stays rowless,
  standing in for a pre-v4 import. Its phase becomes `html-no-manifest`. This
  replaces the spec's "ShellSeed writes rows for every HTML guide".
- **R5. Canary guides are seeded through the real `GuideImportPublisher`**
  (`seed-html-reader`), so their rows come from the real scanner.
- **R6. Serving uses `ManagedPathResolver.ResolveExistingGuideFile`.** The
  P0 `HtmlAssetPolicy` stays unchanged.
- **R7. `GuideAssets` adds CHECK constraints** on `Kind` and `length(Sha256)`.
- **R8. `GetGuideAssetsAsync` lives only on `SqliteLibraryRepository`.**
  `ILibraryRepository` is unchanged.
- **R9. External navigation must be user-initiated.** A non-user-initiated
  navigation or new window to a website is denied silently.
- **R10. External URLs with user info are denied** (no
  `https://example.com@evil.example` in the bar).
- **R11. `IsGuideHost` also matches the bare P0 host `guide.invalid`.**
- **R12. `FileMissing` is a separate deny reason** from `HashMismatch`.
- **R13. Fragments are ignored by `Decide`.**
- **R14. `System.Uri` normalization is accepted.** A raw `/images/../guide.html`
  collapses to `/guide.html` before `Decide` sees it, the same result a
  browser computes. `%2e%2e` and `%5c` stay encoded until the single decode
  and are then rejected.
- **R15. One smoke mode `html-reader`** covers canary A, canary B and the
  external-link checks, reported as separate phases.
- **R16. Diagnostics files are named `html-session-<pid>-<n>.json`.** The
  spec's `html-session-<n>.json` would collide across the online and offline
  launches, whose counters both start at 1.
- **R17. "This link couldn't be opened."** is new copy, shown as a warning
  status when the system launcher returns false or throws. The spec doesn't
  say what happens then.
- **R18. Load failures inside `OpenAsync`** (runtime missing, entry not
  served) surface as `HtmlGuideLoadException(HtmlGuideLoadError)` so the
  shell maps them through the same `HtmlGuideLoadMessages.For` copy.
- **R19. The installed check proves cross-guide isolation by what is served,
  not by deny counts.** Chromium applies the CSP in the renderer before
  `WebResourceRequested` runs, so a CSP-blocked subresource (the loopback
  images, the cross-guide image) may never reach the handler, and the
  `CrossGuide`/`External` counts may be 0. The installed check asserts
  instead that A's served set is exactly A's manifest request paths
  (`guide.html`, `images/a.png`, `style.css`), that B's is exactly B's, that
  the canary log has no new lines, and that clicking "Open canary guide B"
  (a link into B's origin) shows no bar and keeps A's heading. Deny counts
  are kept as evidence only. Task 2's Core tests pin the handler's
  `CrossGuide` rule. This deviates from the spec's "the diagnostics show …
  the cross-guide image denied"; Task 7 records which layer stopped each
  reference.

## Implementation notes to confirm in CI

Record results in the design's "Implementation notes" section (Task 7):
whether a fragment link raises `NavigationStarting`; whether `rel=icon` or
prefetch loads reach `WebResourceRequested`; whether `ping` is sent for a
cancelled navigation; whether `data:` images reach `WebResourceRequested`;
whether `IsUserInitiated` is true for pointer-clicked anchor links; which
canary references reach `WebResourceRequested` at all and which the CSP
stops first (R19).

Two outcomes are real findings, not test noise:

- **`base href` is honored.** The canary page's `<base href>` points at
  loopback. The CSP's `base-uri 'none'` should make Chromium ignore it, so
  relative URLs still resolve against the guide origin. If it doesn't,
  `style.css` and `images/a.png` resolve to loopback, the served-set check
  fails and (online) the canary records the requests.
- **The canary records an `ACCEPT` line.** `preconnect` and `dns-prefetch`
  aren't governed by the CSP and can open a connection without a request.
  If one does, stop and report it; a candidate mitigation is disabling
  them through `CoreWebView2EnvironmentOptions.AdditionalBrowserArguments`,
  which needs its own ruling.

## File map

| File | Task | Responsibility |
|---|---|---|
| `src/DesktopGuides.Core/Html/GuideAsset.cs` | 1 | `GuideAssetKind`, `GuideAsset` |
| `src/DesktopGuides.Infrastructure/Storage/LibrarySchema.cs` | 1 | v4 table |
| `src/DesktopGuides.Infrastructure/Storage/ImportJournal.cs` | 1 | `NewImportedGuide.Assets` |
| `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs` | 1 | insert rows; `GetGuideAssetsAsync` |
| `src/DesktopGuides.Infrastructure/Import/GuideImportPublisher.cs` | 1 | map manifest to rows |
| `src/DesktopGuides.Core/Html/GuideWebOrigin.cs` | 2 | origin and host rules |
| `src/DesktopGuides.Core/Html/HtmlRequestPolicy.cs` | 2 | request decisions and headers |
| `src/DesktopGuides.Core/Html/HtmlNavigationPolicy.cs` | 2 | navigation classification |
| `src/DesktopGuides.Core/Html/HtmlGuideLoadMessages.cs` | 2 | errors and copy |
| `src/DesktopGuides.Core/Html/HtmlSessionDiagnostics.cs` | 2 | counted decisions, JSON |
| `src/DesktopGuides.Infrastructure/Reading/ManagedHtmlAssetReader.cs` | 3 | re-read and re-hash a row |
| `src/DesktopGuides.Infrastructure/Reading/ManagedHtmlGuideLoader.cs` | 3 | rows + entry check |
| `src/DesktopGuides.Infrastructure/Storage/AppCacheRoot.cs` | 3 | cache root |
| `src/DesktopGuides.Production/HtmlReaderSession.cs` | 4 | WebView2 session |
| `src/DesktopGuides.Production/ExternalLinkLaunchers.cs` | 4 | launch or record |
| `src/DesktopGuides.Production/TestGate.cs` | 4 | named-event test gates |
| `src/DesktopGuides.Production/ShellWindow.xaml(.cs)`, `ShellWindow.HtmlReader.cs` | 5 | bar and wiring |
| `tools/p0/http_canary.py`, `tools/p0/test_http_canary.py` | 6 | connection-counting canary |
| `tests/fixtures/p1/html-canary/{a,b}/` | 6 | canary guides |
| `tools/p1/DesktopGuides.ShellSeed/Program.cs` | 6 | `seed-html-reader` |
| `tools/p1/windows_shell_ui_smoke.ps1`, `windows_shell_install.ps1` | 6 | installed checks |
| `.github/workflows/windows-ci.yml` | 6 | canary unit test |
| docs | 7 | traceability and results |

---

### Task 1: Schema v4 `GuideAssets` and publication rows

**Files:**
- Create: `src/DesktopGuides.Core/Html/GuideAsset.cs`
- Modify: `src/DesktopGuides.Infrastructure/Storage/LibrarySchema.cs` (`CurrentVersion`, new `Version4`, `Migrations`)
- Modify: `src/DesktopGuides.Infrastructure/Storage/ImportJournal.cs:21` (`NewImportedGuide`)
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs` (`Publish` at ~862; new `GetGuideAssetsAsync` beside `GetGuideAsync` at ~343)
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideImportPublisher.cs:120-125`
- Test: `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs`, `tests/DesktopGuides.Infrastructure.Tests/Import/GuideImportPublisherTests.cs`

**Interfaces:**
- Produces (Core, namespace `DesktopGuides.Core.Html`):
  ```csharp
  public enum GuideAssetKind { EntryHtml, StyleSheet, Image }
  public sealed record GuideAsset(
      string RequestPath, string RelativePath, GuideAssetKind Kind, long ByteCount, string Sha256);
  ```
- Produces (Infrastructure): `Task<IReadOnlyList<GuideAsset>> SqliteLibraryRepository.GetGuideAssetsAsync(Guid guideId, CancellationToken token = default)`, ordered by `RequestPath` (ordinal, SQLite `BINARY`).
- Produces: `NewImportedGuide(..., int? TextCodePage, IReadOnlyList<GuideAsset>? Assets = null)`.

- [ ] **Step 1: Write the failing repository tests**

In `SqliteLibraryRepositoryTests.cs`:
- Change the three `3L` user-version asserts (lines ~61, ~254, ~697) to `4L`.
- Add, beside `CreatePopulatedVersionTwo`:

```csharp
private static void CreatePopulatedVersionThree(TestLibrary directory, Guid gameId, Guid guideId)
{
    CreatePopulatedVersionTwo(directory, gameId);
    using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = LibrarySchema.Version3;
    command.ExecuteNonQuery();
    connection.Close();
    InsertGuide(directory.Paths.DatabasePath, guideId, gameId);
}

private static void InsertAsset(string databasePath, Guid guideId, string requestPath, string kind)
{
    using SqliteConnection connection = OpenWithForeignKeys(databasePath);
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO GuideAssets (GuideId, RequestPath, RelativePath, Kind, ByteCount, Sha256)
        VALUES ($guide, $request, $request, $kind, 1, $hash)
        """;
    command.Parameters.AddWithValue("$guide", guideId.ToString("N"));
    command.Parameters.AddWithValue("$request", requestPath);
    command.Parameters.AddWithValue("$kind", kind);
    command.Parameters.AddWithValue("$hash", new string('b', 64));
    command.ExecuteNonQuery();
}
```

Note: `InsertGuide` inserts a TXT guide, which is fine for row-level tests.

- Add the tests:

```csharp
[Fact]
public async Task UpgradesPopulatedVersionThreeWithAnEmptyAssetTable()
{
    using TestLibrary directory = new();
    Guid gameId = Guid.NewGuid();
    Guid guideId = Guid.NewGuid();
    CreatePopulatedVersionThree(directory, gameId, guideId);

    await using SqliteLibraryRepository repository = new(directory.Paths);
    await repository.InitializeAsync();

    Assert.Equal(4L, ReadUserVersion(directory.Paths.DatabasePath));
    Assert.NotNull(await repository.GetGuideAsync(guideId));
    Assert.Empty(await repository.GetGuideAssetsAsync(guideId));
}

[Fact]
public async Task FailedVersionFourMigrationStaysAtVersionThree()
{
    using TestLibrary directory = new();
    CreatePopulatedVersionThree(directory, Guid.NewGuid(), Guid.NewGuid());

    await using (SqliteLibraryRepository failing = new(directory.Paths, null, version =>
    {
        if (version == 4) throw new IOException("Injected after the v4 table was created.");
    }))
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => failing.InitializeAsync());
    }

    Assert.Equal(3L, ReadUserVersion(directory.Paths.DatabasePath));
    using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
    using SqliteCommand table = connection.CreateCommand();
    table.CommandText = "SELECT count(*) FROM sqlite_master WHERE name = 'GuideAssets'";
    Assert.Equal(0L, (long)table.ExecuteScalar()!);
}

[Fact]
public async Task GuideAssetsRoundTripInOrdinalOrderAndCascadeWithTheGuide()
{
    using TestLibrary directory = new();
    await using SqliteLibraryRepository repository = new(directory.Paths);
    await repository.InitializeAsync();
    Game game = await repository.AddGameAsync("Assets", null, null);
    Guid guideId = Guid.NewGuid();
    InsertGuide(directory.Paths.DatabasePath, guideId, game.Id);
    InsertAsset(directory.Paths.DatabasePath, guideId, "styles/main.css", "StyleSheet");
    InsertAsset(directory.Paths.DatabasePath, guideId, "Images/map.png", "Image");
    InsertAsset(directory.Paths.DatabasePath, guideId, "guide.html", "EntryHtml");

    IReadOnlyList<GuideAsset> assets = await repository.GetGuideAssetsAsync(guideId);

    Assert.Equal(["Images/map.png", "guide.html", "styles/main.css"], assets.Select(a => a.RequestPath));
    Assert.Equal(
        new GuideAsset("guide.html", "guide.html", GuideAssetKind.EntryHtml, 1, new string('b', 64)),
        assets[1]);

    using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
    using (SqliteCommand delete = connection.CreateCommand())
    {
        delete.CommandText = "DELETE FROM Guides WHERE Id = $id";
        delete.Parameters.AddWithValue("$id", guideId.ToString("N"));
        delete.ExecuteNonQuery();
    }
    Assert.Empty(await repository.GetGuideAssetsAsync(guideId));
}

[Theory]
[InlineData("Other", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
[InlineData("Image", "abc")]
public async Task GuideAssetChecksRejectBadKindsAndHashes(string kind, string hash)
{
    using TestLibrary directory = new();
    await using SqliteLibraryRepository repository = new(directory.Paths);
    await repository.InitializeAsync();
    Game game = await repository.AddGameAsync("Checks", null, null);
    Guid guideId = Guid.NewGuid();
    InsertGuide(directory.Paths.DatabasePath, guideId, game.Id);

    using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO GuideAssets (GuideId, RequestPath, RelativePath, Kind, ByteCount, Sha256)
        VALUES ($guide, 'a.png', 'a.png', $kind, 1, $hash)
        """;
    command.Parameters.AddWithValue("$guide", guideId.ToString("N"));
    command.Parameters.AddWithValue("$kind", kind);
    command.Parameters.AddWithValue("$hash", hash);
    Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
}
```

Add `using DesktopGuides.Core.Html;` to the file.

- [ ] **Step 2: Write the failing publisher tests**

In `GuideImportPublisherTests.cs` (add `using DesktopGuides.Core.Html;`):

```csharp
[Fact]
public async Task PublishesTheHtmlManifestAsAssetRows()
{
    await using PublisherHarness harness = await PublisherHarness.CreateAsync();
    string entry = CopyHtmlStatic(harness, "guide.html");

    Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(entry));

    IReadOnlyList<GuideAsset> assets = await harness.Repository.GetGuideAssetsAsync(id);
    Assert.Equal(
        ["guide.html", "images/map.png", "styles/main.css", "styles/palette.css"],
        assets.Select(asset => asset.RequestPath));
    Assert.Equal(
        [GuideAssetKind.EntryHtml, GuideAssetKind.Image, GuideAssetKind.StyleSheet, GuideAssetKind.StyleSheet],
        assets.Select(asset => asset.Kind));
    foreach (GuideAsset asset in assets)
    {
        byte[] bytes = File.ReadAllBytes(harness.Paths.ResolveExistingGuideFile(id, asset.RelativePath));
        Assert.Equal(bytes.LongLength, asset.ByteCount);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), asset.Sha256);
    }
}

[Fact]
public async Task AFailureInsideTheCommitLeavesNoAssetRows()
{
    await using PublisherHarness harness = await PublisherHarness.CreateAsync();
    string entry = CopyHtmlStatic(harness, "guide.html");
    GuideImportPublisher publisher = harness.Publisher(checkpoint: point =>
    {
        if (point == ImportCheckpoint.InCommit) throw new IOException("Injected inside the commit.");
    });

    await Assert.ThrowsAnyAsync<Exception>(
        async () => await harness.PublishAsync(publisher, await harness.InspectAsync(entry)));

    Assert.Equal(0, harness.Count("GuideAssets"));
    harness.AssertNothingLeft();
}

[Theory]
[InlineData("txt-utf8.txt", "notes.txt")]
[InlineData("pdf-short.pdf", "short.pdf")]
public async Task TextAndPdfGuidesPublishNoAssetRows(string fixture, string name)
{
    await using PublisherHarness harness = await PublisherHarness.CreateAsync();
    ImportManifest manifest = await harness.InspectAsync(harness.Sources.Copy(fixture, name));

    Guid id = await harness.PublishAsync(harness.Publisher(), manifest);

    Assert.Empty(await harness.Repository.GetGuideAssetsAsync(id));
}

[Theory]
[InlineData("100% Completion Guide_files")]
[InlineData("100%25 Completion Guide_files")]
[InlineData("100%25%20Completion%20Guide_files")]
public async Task PercentNamedCompanionRowsUseTheSourceFolderName(string referencedFolder)
{
    if (!OperatingSystem.IsWindows()) return;
    await using PublisherHarness harness = await PublisherHarness.CreateAsync();
    harness.Sources.Copy("html-static/images/map.png", "100% Completion Guide_files/map.png");
    string entry = harness.Sources.Write(
        "100% Completion Guide.html", $"<p>Guide</p><img src=\"{referencedFolder}/map.png\">");

    Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(entry));

    GuideAsset image = Assert.Single(
        await harness.Repository.GetGuideAssetsAsync(id), asset => asset.Kind == GuideAssetKind.Image);
    Assert.Equal("100% Completion Guide_files/map.png", image.RequestPath);
    Assert.Equal("__desktop_guides_files/map.png", image.RelativePath);
}

[Fact]
public async Task RequestPathKeepsTheReferenceCasing()
{
    if (!OperatingSystem.IsWindows()) return;
    await using PublisherHarness harness = await PublisherHarness.CreateAsync();
    harness.Sources.Copy("html-static/images/map.png", "images/map.png");
    string entry = harness.Sources.Write("guide.html", "<p>Guide</p><img src=\"IMAGES/Map.png\">");

    Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(entry));

    GuideAsset image = Assert.Single(
        await harness.Repository.GetGuideAssetsAsync(id), asset => asset.Kind == GuideAssetKind.Image);
    Assert.Equal("IMAGES/Map.png", image.RequestPath);
}
```

`ImportTestDirectory.Write` and `Copy` return the full source path. Add
`using System.Security.Cryptography;`.

`RequestPathKeepsTheReferenceCasing` is Review Focus item 5. If it fails
because the scanner records the on-disk casing, fix `ToRequestPath` in
`RootedStaticHtmlAssetSource` to keep the decoded reference text (a real
defect). If it fails because import rejects the case mismatch as missing,
record `Ruling: case-mismatched references are reported missing at preview`
and change the assert to that warning; the reader then never meets the case.

- [ ] **Step 3: Push and confirm RED**

```bash
git add -A tests && git commit -m "test(p1): GuideAssets schema v4 and publication rows (red)" \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push -u origin feat/p1-t07-3-webview2-policy
gh run list --branch feat/p1-t07-3-webview2-policy --limit 1
gh run watch <id> --exit-status; gh run view <id> --log-failed | tail -60
```

Expected: `core-tests` fails to compile Infrastructure.Tests with `CS0246:
GuideAsset` / `CS1061: GetGuideAssetsAsync`.

- [ ] **Step 4: Implement**

`src/DesktopGuides.Core/Html/GuideAsset.cs`:

```csharp
namespace DesktopGuides.Core.Html;

public enum GuideAssetKind
{
    EntryHtml,
    StyleSheet,
    Image
}

/// <summary>
/// One approved file of an HTML guide. RequestPath is the decoded URL path
/// and only ever a lookup key; RelativePath is the managed file to open.
/// </summary>
public sealed record GuideAsset(
    string RequestPath, string RelativePath, GuideAssetKind Kind, long ByteCount, string Sha256);
```

`LibrarySchema.cs`: set `CurrentVersion = 4`, add after `Version3`:

```csharp
public const string Version4 = """
    CREATE TABLE GuideAssets (
        GuideId TEXT NOT NULL REFERENCES Guides(Id) ON DELETE CASCADE,
        RequestPath TEXT NOT NULL,
        RelativePath TEXT NOT NULL,
        Kind TEXT NOT NULL CHECK (Kind IN ('EntryHtml', 'StyleSheet', 'Image')),
        ByteCount INTEGER NOT NULL CHECK (ByteCount >= 0),
        Sha256 TEXT NOT NULL CHECK (length(Sha256) = 64),
        PRIMARY KEY (GuideId, RequestPath)
    );
    PRAGMA user_version = 4;
    """;
```

and append `(4, Version4)` to `Migrations`. Each version script sets its own
`user_version`, as `Version1`–`Version3` do.

`ImportJournal.cs:21`: append `, IReadOnlyList<GuideAsset>? Assets = null`
to `NewImportedGuide` and add `using DesktopGuides.Core.Html;`.

`SqliteLibraryRepository.Publish`, after `insert.ExecuteNonQuery();`:

```csharp
foreach (GuideAsset asset in guide.Assets ?? [])
{
    using SqliteCommand row = connection.CreateCommand();
    row.Transaction = transaction;
    row.CommandText = """
        INSERT INTO GuideAssets (GuideId, RequestPath, RelativePath, Kind, ByteCount, Sha256)
        VALUES ($id, $request, $relative, $kind, $bytes, $sha)
        """;
    row.Parameters.AddWithValue("$id", id);
    row.Parameters.AddWithValue("$request", asset.RequestPath);
    row.Parameters.AddWithValue("$relative", asset.RelativePath);
    row.Parameters.AddWithValue("$kind", asset.Kind.ToString());
    row.Parameters.AddWithValue("$bytes", asset.ByteCount);
    row.Parameters.AddWithValue("$sha", asset.Sha256);
    row.ExecuteNonQuery();
}
```

Beside `GetGuideAsync`:

```csharp
public Task<IReadOnlyList<GuideAsset>> GetGuideAssetsAsync(Guid guideId, CancellationToken token = default) =>
    ReadAsync<IReadOnlyList<GuideAsset>>(() =>
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT RequestPath, RelativePath, Kind, ByteCount, Sha256
            FROM GuideAssets WHERE GuideId = $id ORDER BY RequestPath
            """;
        command.Parameters.AddWithValue("$id", guideId.ToString("N"));
        using SqliteDataReader reader = command.ExecuteReader();
        List<GuideAsset> assets = [];
        while (reader.Read())
        {
            assets.Add(new GuideAsset(
                reader.GetString(0), reader.GetString(1),
                Enum.Parse<GuideAssetKind>(reader.GetString(2)),
                reader.GetInt64(3), reader.GetString(4)));
        }
        return assets;
    }, token);
```

`GuideImportPublisher.cs:121-124`: pass the rows as the last argument:

```csharp
new NewImportedGuide(
    operationId, guideId, gameId, title, manifest.Format, plan.PrimaryRelativePath,
    fingerprint, plan.TotalBytes, manifest.Source.FileName,
    (manifest as TxtImportManifest)?.CodePage,
    plan.Html?.Manifest.Assets.Select(ToGuideAsset).ToArray() ?? []),
```

and add:

```csharp
private static GuideAsset ToGuideAsset(StaticAsset asset) => new(
    asset.RequestRelativePath, asset.RelativePath,
    asset.Kind switch
    {
        StaticAssetKind.EntryHtml => GuideAssetKind.EntryHtml,
        StaticAssetKind.StyleSheet => GuideAssetKind.StyleSheet,
        StaticAssetKind.Image => GuideAssetKind.Image,
        _ => throw new InvalidDataException("An HTML asset has no reader kind.")
    },
    asset.ByteCount, asset.Sha256);
```

- [ ] **Step 5: Push and confirm GREEN**

```bash
git add -A src tests && git commit -m "feat(p1): store HTML guide assets in schema v4" \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push; gh run watch <id> --exit-status
```

Expected: `core-tests` passes, including every existing schema test (the
schema validator builds its expectation from `Migrations`).

### Task 2: Core origin, request and navigation policies, messages, diagnostics

**Files:**
- Create: `src/DesktopGuides.Core/Html/GuideWebOrigin.cs`, `HtmlRequestPolicy.cs`, `HtmlNavigationPolicy.cs`, `HtmlGuideLoadMessages.cs`, `HtmlSessionDiagnostics.cs`
- Test: `tests/DesktopGuides.Core.Tests/GuideWebOriginTests.cs`, `HtmlRequestPolicyTests.cs`, `HtmlNavigationPolicyTests.cs`, `HtmlGuideLoadMessagesTests.cs`, `HtmlSessionDiagnosticsTests.cs`

**Interfaces:**
- Consumes: `GuideAsset`, `GuideAssetKind` (Task 1).
- Produces (namespace `DesktopGuides.Core.Html`):
  ```csharp
  public static class GuideWebOrigin
  {
      public const string HostSuffix = ".guide.invalid";
      public static string HostFor(Guid guideId);          // Guid.Empty -> ArgumentException
      public static Uri OriginFor(Guid guideId);           // https://g<N>.guide.invalid/
      public static Uri EntryUri(Guid guideId, string requestPath);
      public static bool IsGuideHost(string host);
  }
  public enum HtmlDenyReason { Method, CrossGuide, External, OtherScheme, Malformed, NotInManifest, UnsupportedType, HashMismatch, FileMissing }
  public abstract record HtmlRequestDecision;
  public sealed record HtmlServe(GuideAsset Asset, string ContentType) : HtmlRequestDecision;
  public sealed record HtmlDeny(HtmlDenyReason Reason) : HtmlRequestDecision;
  public sealed class HtmlRequestPolicy
  {
      public const string ContentSecurityPolicy = "...";   // Global Constraints, verbatim
      public HtmlRequestPolicy(Guid guideId, IEnumerable<GuideAsset> assets);
      public Guid GuideId { get; }
      public GuideAsset Entry { get; }
      public Uri EntryUri { get; }
      public HtmlRequestDecision Decide(string method, string uri);
      public static string ServedHeaders(string contentType);
      public const string DeniedHeaders = "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff";
  }
  public enum HtmlNavigationKind { Entry, SameDocument, External, Deny }
  public sealed record HtmlNavigation(HtmlNavigationKind Kind, Uri? ExternalUri = null);
  public static class HtmlNavigationPolicy
  {
      public static HtmlNavigation Classify(string uri, Uri entry, bool entryNavigated, bool userInitiated);
  }
  public enum HtmlGuideLoadError { RuntimeMissing, NoManifest, Changed }
  public static class HtmlGuideLoadMessages { public static string For(HtmlGuideLoadError error); }
  public sealed class HtmlSessionDiagnostics
  {
      public void RecordServed(string requestPath);
      public void RecordDenied(HtmlDenyReason reason, string context);
      public string ToJson(Guid guideId);
  }
  ```

- [ ] **Step 1: Write the failing origin tests**

`GuideWebOriginTests.cs`:

```csharp
using DesktopGuides.Core.Html;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class GuideWebOriginTests
{
    private static readonly Guid Id = Guid.Parse("3f2a9c0e-4b7d-1a65-f08c-2e9d3b4a7c10");

    [Fact]
    public void HostIsTheGuideIdUnderTheInvalidSuffix()
    {
        Assert.Equal("g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid", GuideWebOrigin.HostFor(Id));
        Assert.Equal("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/", GuideWebOrigin.OriginFor(Id).AbsoluteUri);
    }

    [Fact]
    public void EveryGuideHasItsOwnOrigin() =>
        Assert.NotEqual(GuideWebOrigin.HostFor(Guid.NewGuid()), GuideWebOrigin.HostFor(Guid.NewGuid()));

    [Fact]
    public void EmptyIdHasNoOrigin() =>
        Assert.Throws<ArgumentException>(() => GuideWebOrigin.HostFor(Guid.Empty));

    [Theory]
    [InlineData("guide.html", "/guide.html")]
    [InlineData("100% Completion Guide_files/map.png", "/100%25%20Completion%20Guide_files/map.png")]
    [InlineData("a:b#c?.html", "/a%3Ab%23c%3F.html")]
    public void EntryUriEscapesEachSegment(string requestPath, string expectedPath)
    {
        Uri entry = GuideWebOrigin.EntryUri(Id, requestPath);
        Assert.Equal(GuideWebOrigin.HostFor(Id), entry.Host);
        Assert.Equal(expectedPath, entry.AbsolutePath);
        Assert.Equal("", entry.Query);
        Assert.Equal("", entry.Fragment);
    }

    [Theory]
    [InlineData("g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid", true)]
    [InlineData("G00000000000000000000000000000001.GUIDE.INVALID", true)]
    [InlineData("guide.invalid", true)]
    [InlineData("g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid.example", false)]
    [InlineData("example.com", false)]
    [InlineData("notguide.invalid", false)]
    public void RecognizesGuideHosts(string host, bool expected) =>
        Assert.Equal(expected, GuideWebOrigin.IsGuideHost(host));
}
```

- [ ] **Step 2: Write the failing request-policy tests**

`HtmlRequestPolicyTests.cs`:

```csharp
using DesktopGuides.Core.Html;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlRequestPolicyTests
{
    private static readonly Guid Id = Guid.Parse("3f2a9c0e-4b7d-1a65-f08c-2e9d3b4a7c10");
    private static readonly string Origin = "https://" + GuideWebOrigin.HostFor(Id);
    private const string Hash = "0000000000000000000000000000000000000000000000000000000000000000";

    private static readonly GuideAsset[] Assets =
    [
        new("guide.html", "guide.html", GuideAssetKind.EntryHtml, 10, Hash),
        new("styles/main.css", "styles/main.css", GuideAssetKind.StyleSheet, 10, Hash),
        new("images/map.png", "images/map.png", GuideAssetKind.Image, 10, Hash),
        new("images/photo.JPEG", "images/photo.JPEG", GuideAssetKind.Image, 10, Hash),
        new("images/anim.gif", "images/anim.gif", GuideAssetKind.Image, 10, Hash),
        new("images/pic.webp", "images/pic.webp", GuideAssetKind.Image, 10, Hash),
        new("images/vector.svg", "images/vector.svg", GuideAssetKind.Image, 10, Hash),
        new("100% Completion Guide_files/map.png", "__desktop_guides_files/map.png", GuideAssetKind.Image, 10, Hash),
    ];

    private static HtmlRequestPolicy Policy() => new(Id, Assets);

    private static HtmlDenyReason Denied(string uri, string method = "GET") =>
        Assert.IsType<HtmlDeny>(Policy().Decide(method, uri)).Reason;

    private static HtmlServe Served(string uri) =>
        Assert.IsType<HtmlServe>(Policy().Decide("GET", uri));

    [Fact]
    public void EntryUriIsTheEntryRowUnderTheGuideOrigin() =>
        Assert.Equal(Origin + "/guide.html", Policy().EntryUri.AbsoluteUri);

    [Theory]
    [InlineData("/guide.html", "guide.html", "text/html")]
    [InlineData("/styles/main.css", "styles/main.css", "text/css")]
    [InlineData("/images/map.png", "images/map.png", "image/png")]
    [InlineData("/images/photo.JPEG", "images/photo.JPEG", "image/jpeg")]
    [InlineData("/images/anim.gif", "images/anim.gif", "image/gif")]
    [InlineData("/images/pic.webp", "images/pic.webp", "image/webp")]
    public void ServesManifestRowsWithTheirContentType(string path, string relative, string type)
    {
        HtmlServe serve = Served(Origin + path);
        Assert.Equal(relative, serve.Asset.RelativePath);
        Assert.Equal(type, serve.ContentType);
    }

    [Fact]
    public void ContentTypesCarryNoCharset()
    {
        Assert.DoesNotContain("charset", Served(Origin + "/guide.html").ContentType);
        Assert.DoesNotContain("charset", Served(Origin + "/styles/main.css").ContentType);
    }

    [Theory]
    [InlineData("/100%25%20Completion%20Guide_files/map.png")]
    [InlineData("/100%%20Completion%20Guide_files/map.png")]
    public void PercentNamedCompanionFolderServesTheManagedFile(string path) =>
        Assert.Equal("__desktop_guides_files/map.png", Served(Origin + path).Asset.RelativePath);

    [Fact]
    public void DoubleEncodedCompanionFolderIsNotInTheManifest() =>
        Assert.Equal(HtmlDenyReason.NotInManifest, Denied(Origin + "/100%2525%20Completion%20Guide_files/map.png"));

    [Theory]
    [InlineData("/styles/main.css?ver=5.8")]
    [InlineData("/styles/main.css#top")]
    [InlineData("/styles/main.css?ver=5.8#top")]
    public void QueryAndFragmentAreIgnoredForLookup(string path) =>
        Assert.Equal("styles/main.css", Served(Origin + path).Asset.RequestPath);

    [Fact]
    public void HostCaseDoesNotMatterButPathCaseDoes()
    {
        Served(Origin.ToUpperInvariant().Replace("HTTPS", "https") + "/guide.html");
        Assert.Equal(HtmlDenyReason.NotInManifest, Denied(Origin + "/GUIDE.html"));
    }

    [Fact]
    public void RawDotSegmentsAreCollapsedByUriLikeABrowser() =>
        Assert.Equal("guide.html", Served(Origin + "/images/../guide.html").Asset.RequestPath);

    [Theory]
    [InlineData("HEAD")]
    [InlineData("POST")]
    [InlineData("get")]
    public void OnlyGetIsServed(string method) =>
        Assert.Equal(HtmlDenyReason.Method, Denied(Origin + "/guide.html", method));

    [Theory]
    [InlineData("http://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/guide.html")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid:8443/guide.html")]
    [InlineData("https://user@g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/guide.html")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/styles/")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/styles//main.css")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/%2e%2e/guide.html")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/images/%2e/map.png")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/images%5cmap.png")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/guide.html%00")]
    [InlineData("not a uri")]
    public void MalformedRequestsAreDenied(string uri) =>
        Assert.Equal(HtmlDenyReason.Malformed, Denied(uri));

    [Fact]
    public void DoubleEncodedTraversalIsNotInTheManifest() =>
        Assert.Equal(HtmlDenyReason.NotInManifest, Denied(Origin + "/%252e%252e/guide.html"));

    [Theory]
    [InlineData("https://g00000000000000000000000000000001.guide.invalid/guide.html")]
    [InlineData("https://guide.invalid/guide.html")]
    public void AnotherGuidesOriginIsCrossGuide(string uri) =>
        Assert.Equal(HtmlDenyReason.CrossGuide, Denied(uri));

    [Theory]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid.example/guide.html")]
    [InlineData("http://127.0.0.1:8765/canary.png")]
    [InlineData("https://example.com/a.png")]
    public void OtherHostsAreExternal(string uri) =>
        Assert.Equal(HtmlDenyReason.External, Denied(uri));

    [Theory]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("blob:https://example.com/1")]
    [InlineData("ws://127.0.0.1:8765/")]
    public void OtherSchemesAreDenied(string uri) =>
        Assert.Equal(HtmlDenyReason.OtherScheme, Denied(uri));

    [Fact]
    public void UnsupportedFileTypesAreDenied() =>
        Assert.Equal(HtmlDenyReason.UnsupportedType, Denied(Origin + "/images/vector.svg"));

    [Fact]
    public void ManifestNeedsExactlyOneEntry()
    {
        Assert.Throws<ArgumentException>(() => new HtmlRequestPolicy(Id, Assets.Skip(1)));
        Assert.Throws<ArgumentException>(() => new HtmlRequestPolicy(Id,
            [.. Assets, new GuideAsset("other.html", "other.html", GuideAssetKind.EntryHtml, 1, Hash)]));
        Assert.Throws<ArgumentException>(() => new HtmlRequestPolicy(Id, [.. Assets, Assets[1]]));
    }

    [Fact]
    public void ServedHeadersCarryTheContentSecurityPolicy()
    {
        string headers = HtmlRequestPolicy.ServedHeaders("text/css");
        Assert.Equal(
            "Content-Type: text/css\r\nX-Content-Type-Options: nosniff\r\nCache-Control: no-store\r\n" +
            "Content-Security-Policy: default-src 'none'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; " +
            "font-src 'none'; script-src 'none'; frame-src 'none'; form-action 'none'; connect-src 'none'; " +
            "object-src 'none'; base-uri 'none'",
            headers);
    }
}
```

If a CI run shows that `System.Uri` treats one of the encoded inputs
differently (for example it unescapes `%2e` and collapses the segment, so
`/%2e%2e/guide.html` serves the entry), the request is still the entry the
browser would compute; record a ruling and move that case to the served
table rather than loosening the decode checks.

- [ ] **Step 3: Write the failing navigation, message and diagnostics tests**

`HtmlNavigationPolicyTests.cs`:

```csharp
using DesktopGuides.Core.Html;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlNavigationPolicyTests
{
    private static readonly Guid Id = Guid.Parse("3f2a9c0e-4b7d-1a65-f08c-2e9d3b4a7c10");
    private static readonly Uri Entry = GuideWebOrigin.EntryUri(Id, "guide.html");

    private static HtmlNavigationKind Kind(string uri, bool navigated = true, bool user = true) =>
        HtmlNavigationPolicy.Classify(uri, Entry, navigated, user).Kind;

    [Fact]
    public void FirstEntryNavigationIsAllowedOnce()
    {
        Assert.Equal(HtmlNavigationKind.Entry, Kind(Entry.AbsoluteUri, navigated: false, user: false));
        Assert.Equal(HtmlNavigationKind.Deny, Kind(Entry.AbsoluteUri, navigated: true));
    }

    [Fact]
    public void FragmentsOfTheEntryAreSameDocumentAfterItLoads()
    {
        Assert.Equal(HtmlNavigationKind.SameDocument, Kind(Entry.AbsoluteUri + "#details"));
        Assert.Equal(HtmlNavigationKind.Deny, Kind(Entry.AbsoluteUri + "#details", navigated: false));
    }

    [Fact]
    public void UserInitiatedWebsiteLinksAreExternal()
    {
        HtmlNavigation navigation = HtmlNavigationPolicy.Classify(
            "HTTPS://Example.com/desktop-guides-canary", Entry, true, true);
        Assert.Equal(HtmlNavigationKind.External, navigation.Kind);
        Assert.Equal("https://example.com/desktop-guides-canary", navigation.ExternalUri!.AbsoluteUri);
        Assert.Equal(HtmlNavigationKind.External, Kind("http://example.org/page?x=1"));
    }

    [Theory]
    [InlineData("https://example.com/redirected")]
    [InlineData("http://127.0.0.1:8765/refresh")]
    public void NonUserInitiatedWebsiteNavigationIsDenied(string uri) =>
        Assert.Equal(HtmlNavigationKind.Deny, Kind(uri, user: false));

    [Theory]
    [InlineData("https://example.com@evil.example/")]
    [InlineData("https://g00000000000000000000000000000001.guide.invalid/guide.html")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/other.html")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/guide.html?page=2")]
    [InlineData("https://guide.invalid/guide.html")]
    [InlineData("javascript:alert(1)")]
    [InlineData("JAVASCRIPT:alert(1)")]
    [InlineData("data:text/html,hi")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("file:///C:/guide.html")]
    [InlineData("blob:https://example.com/1")]
    [InlineData("steam://run/1")]
    [InlineData("about:blank")]
    [InlineData("not a uri")]
    public void EverythingElseIsDenied(string uri) =>
        Assert.Equal(HtmlNavigationKind.Deny, Kind(uri));
}
```

`HtmlGuideLoadMessagesTests.cs`:

```csharp
using DesktopGuides.Core.Html;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlGuideLoadMessagesTests
{
    [Theory]
    [InlineData(HtmlGuideLoadError.RuntimeMissing, "Web page guides need the Microsoft Edge WebView2 Runtime.")]
    [InlineData(HtmlGuideLoadError.NoManifest, "Re-import this guide to read it.")]
    [InlineData(HtmlGuideLoadError.Changed, "This guide's files have changed. Re-import it to read it.")]
    public void EachErrorHasOneSentence(HtmlGuideLoadError error, string message) =>
        Assert.Equal(message, HtmlGuideLoadMessages.For(error));

    [Fact]
    public void UndefinedErrorsThrow() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => HtmlGuideLoadMessages.For((HtmlGuideLoadError)99));
}
```

`HtmlSessionDiagnosticsTests.cs`:

```csharp
using System.Text.Json;
using DesktopGuides.Core.Html;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlSessionDiagnosticsTests
{
    [Fact]
    public void CountsDistinctServedPathsAndDeniedReasonsByContext()
    {
        Guid id = Guid.NewGuid();
        HtmlSessionDiagnostics diagnostics = new();
        diagnostics.RecordServed("styles/main.css");
        diagnostics.RecordServed("guide.html");
        diagnostics.RecordServed("guide.html");
        diagnostics.RecordDenied(HtmlDenyReason.External, "Image");
        diagnostics.RecordDenied(HtmlDenyReason.External, "Image");
        diagnostics.RecordDenied(HtmlDenyReason.CrossGuide, "Image");

        using JsonDocument json = JsonDocument.Parse(diagnostics.ToJson(id));

        Assert.Equal(id.ToString("N"), json.RootElement.GetProperty("guideId").GetString());
        Assert.Equal(["guide.html", "styles/main.css"],
            json.RootElement.GetProperty("served").EnumerateArray().Select(e => e.GetString()));
        JsonElement[] denied = [.. json.RootElement.GetProperty("denied").EnumerateArray()];
        Assert.Equal(2, denied.Length);
        Assert.Equal(("CrossGuide", "Image", 1),
            (denied[0].GetProperty("reason").GetString(), denied[0].GetProperty("context").GetString(),
             denied[0].GetProperty("count").GetInt32()));
        Assert.Equal(("External", "Image", 2),
            (denied[1].GetProperty("reason").GetString(), denied[1].GetProperty("context").GetString(),
             denied[1].GetProperty("count").GetInt32()));
    }
}
```

- [ ] **Step 4: Push and confirm RED**

Commit `test(p1): Core HTML origin and request policies (red)`, push, watch.
Expected: `core-tests` fails to compile Core.Tests (`CS0246: GuideWebOrigin`, …).

- [ ] **Step 5: Implement**

`GuideWebOrigin.cs`:

```csharp
namespace DesktopGuides.Core.Html;

/// <summary>
/// Each guide gets its own origin, so a URL into another guide is
/// cross-guide, never external. ".invalid" never resolves.
/// </summary>
public static class GuideWebOrigin
{
    public const string HostSuffix = ".guide.invalid";
    private const string P0Host = "guide.invalid";

    public static string HostFor(Guid guideId)
    {
        if (guideId == Guid.Empty)
        {
            throw new ArgumentException("A guide origin needs a guide ID.", nameof(guideId));
        }
        return "g" + guideId.ToString("N") + HostSuffix;
    }

    public static Uri OriginFor(Guid guideId) => new("https://" + HostFor(guideId) + "/");

    public static Uri EntryUri(Guid guideId, string requestPath) =>
        new("https://" + HostFor(guideId) + "/" +
            string.Join('/', requestPath.Split('/').Select(Uri.EscapeDataString)));

    public static bool IsGuideHost(string host) =>
        host.EndsWith(HostSuffix, StringComparison.OrdinalIgnoreCase) ||
        host.Equals(P0Host, StringComparison.OrdinalIgnoreCase);
}
```

`HtmlRequestPolicy.cs`:

```csharp
namespace DesktopGuides.Core.Html;

public enum HtmlDenyReason
{
    Method, CrossGuide, External, OtherScheme, Malformed, NotInManifest, UnsupportedType, HashMismatch, FileMissing
}

public abstract record HtmlRequestDecision;
public sealed record HtmlServe(GuideAsset Asset, string ContentType) : HtmlRequestDecision;
public sealed record HtmlDeny(HtmlDenyReason Reason) : HtmlRequestDecision;

/// <summary>
/// Decides every WebView2 request for one guide. A served decision carries
/// the row; the caller opens the row's RelativePath, never the request path.
/// </summary>
public sealed class HtmlRequestPolicy
{
    public const string ContentSecurityPolicy =
        "default-src 'none'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; " +
        "font-src 'none'; script-src 'none'; frame-src 'none'; form-action 'none'; " +
        "connect-src 'none'; object-src 'none'; base-uri 'none'";

    public const string DeniedHeaders = "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff";

    private readonly Dictionary<string, GuideAsset> assets = new(StringComparer.Ordinal);
    private readonly string host;

    public HtmlRequestPolicy(Guid guideId, IEnumerable<GuideAsset> assets)
    {
        host = GuideWebOrigin.HostFor(guideId);
        foreach (GuideAsset asset in assets)
        {
            if (!this.assets.TryAdd(asset.RequestPath, asset))
            {
                throw new ArgumentException("Guide assets repeat a request path.", nameof(assets));
            }
        }
        GuideAsset[] entries = [.. this.assets.Values.Where(asset => asset.Kind == GuideAssetKind.EntryHtml)];
        if (entries.Length != 1)
        {
            throw new ArgumentException("A guide needs exactly one entry document.", nameof(assets));
        }
        GuideId = guideId;
        Entry = entries[0];
        EntryUri = GuideWebOrigin.EntryUri(guideId, Entry.RequestPath);
    }

    public Guid GuideId { get; }
    public GuideAsset Entry { get; }
    public Uri EntryUri { get; }

    public HtmlRequestDecision Decide(string method, string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? target)) return Deny(HtmlDenyReason.Malformed);
        if (target.Scheme is not ("http" or "https")) return Deny(HtmlDenyReason.OtherScheme);
        if (!string.Equals(target.Host, host, StringComparison.Ordinal))
        {
            return Deny(GuideWebOrigin.IsGuideHost(target.Host) ? HtmlDenyReason.CrossGuide : HtmlDenyReason.External);
        }
        // The query is ignored: the scanner drops it when it records a reference.
        if (target.Scheme != "https" || target.Port != 443 || target.UserInfo.Length != 0)
        {
            return Deny(HtmlDenyReason.Malformed);
        }
        if (!string.Equals(method, "GET", StringComparison.Ordinal)) return Deny(HtmlDenyReason.Method);
        string path = Uri.UnescapeDataString(target.AbsolutePath);
        if (!path.StartsWith('/')) return Deny(HtmlDenyReason.Malformed);
        string requestPath = path[1..];
        if (requestPath.Contains('\\') || requestPath.Contains('\0') ||
            requestPath.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            return Deny(HtmlDenyReason.Malformed);
        }
        if (!assets.TryGetValue(requestPath, out GuideAsset? asset)) return Deny(HtmlDenyReason.NotInManifest);
        return ContentTypeFor(asset.RelativePath) is { } type
            ? new HtmlServe(asset, type)
            : Deny(HtmlDenyReason.UnsupportedType);
    }

    public static string ServedHeaders(string contentType) =>
        $"Content-Type: {contentType}\r\nX-Content-Type-Options: nosniff\r\nCache-Control: no-store\r\n" +
        $"Content-Security-Policy: {ContentSecurityPolicy}";

    // No charset: legacy guides keep their own meta charset or BOM.
    private static string? ContentTypeFor(string relativePath) =>
        Path.GetExtension(relativePath).ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => null
        };

    private static HtmlDeny Deny(HtmlDenyReason reason) => new(reason);
}
```

`HtmlNavigationPolicy.cs`:

```csharp
namespace DesktopGuides.Core.Html;

public enum HtmlNavigationKind { Entry, SameDocument, External, Deny }

public sealed record HtmlNavigation(HtmlNavigationKind Kind, Uri? ExternalUri = null);

public static class HtmlNavigationPolicy
{
    private static readonly HtmlNavigation Denied = new(HtmlNavigationKind.Deny);

    public static HtmlNavigation Classify(string uri, Uri entry, bool entryNavigated, bool userInitiated)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? target)) return Denied;
        if (Uri.Compare(target, entry, UriComponents.SchemeAndServer | UriComponents.PathAndQuery,
                UriFormat.UriEscaped, StringComparison.Ordinal) == 0)
        {
            bool fragment = target.Fragment.Length > 0;
            if (!entryNavigated && !fragment) return new(HtmlNavigationKind.Entry);
            if (entryNavigated && fragment) return new(HtmlNavigationKind.SameDocument);
            return Denied;
        }
        // Only a person's click may raise the bar; redirects and refreshes can't.
        if (target.Scheme is "http" or "https" && userInitiated &&
            target.UserInfo.Length == 0 && !GuideWebOrigin.IsGuideHost(target.Host))
        {
            return new(HtmlNavigationKind.External, target);
        }
        return Denied;
    }
}
```

`HtmlGuideLoadMessages.cs`:

```csharp
namespace DesktopGuides.Core.Html;

public enum HtmlGuideLoadError { RuntimeMissing, NoManifest, Changed }

public static class HtmlGuideLoadMessages
{
    public static string For(HtmlGuideLoadError error) => error switch
    {
        HtmlGuideLoadError.RuntimeMissing => "Web page guides need the Microsoft Edge WebView2 Runtime.",
        HtmlGuideLoadError.NoManifest => "Re-import this guide to read it.",
        HtmlGuideLoadError.Changed => "This guide's files have changed. Re-import it to read it.",
        _ => throw new ArgumentOutOfRangeException(nameof(error))
    };
}
```

`HtmlSessionDiagnostics.cs`:

```csharp
using System.Text.Json;

namespace DesktopGuides.Core.Html;

/// <summary>
/// Test-only counts of one session's decisions. Holds request paths and
/// reasons, never external URLs or guide text.
/// </summary>
public sealed class HtmlSessionDiagnostics
{
    private readonly object gate = new();
    private readonly SortedSet<string> served = new(StringComparer.Ordinal);
    private readonly Dictionary<(HtmlDenyReason Reason, string Context), int> denied = [];

    public void RecordServed(string requestPath)
    {
        lock (gate) served.Add(requestPath);
    }

    public void RecordDenied(HtmlDenyReason reason, string context)
    {
        lock (gate) denied[(reason, context)] = denied.GetValueOrDefault((reason, context)) + 1;
    }

    public string ToJson(Guid guideId)
    {
        lock (gate)
        {
            return JsonSerializer.Serialize(new
            {
                guideId = guideId.ToString("N"),
                served = served.ToArray(),
                denied = denied
                    .OrderBy(pair => pair.Key.Reason.ToString(), StringComparer.Ordinal)
                    .ThenBy(pair => pair.Key.Context, StringComparer.Ordinal)
                    .Select(pair => new { reason = pair.Key.Reason.ToString(), context = pair.Key.Context, count = pair.Value })
                    .ToArray()
            });
        }
    }
}
```

- [ ] **Step 6: Push and confirm GREEN**

Commit `feat(p1): Core HTML origin, request and navigation policies`, push,
watch. Expected: `core-tests` passes. Any `System.Uri` surprise in the
encoded cases is handled as Step 2 describes, with a ledger ruling.

### Task 3: Managed asset reader, HTML guide loader and cache root

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Reading/ManagedHtmlAssetReader.cs`
- Create: `src/DesktopGuides.Infrastructure/Reading/ManagedHtmlGuideLoader.cs`
- Create: `src/DesktopGuides.Infrastructure/Storage/AppCacheRoot.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Reading/ManagedHtmlAssetReaderTests.cs`, `Reading/ManagedHtmlGuideLoaderTests.cs`, `AppCacheRootTests.cs`

**Interfaces:**
- Consumes: `GuideAsset`, `GetGuideAssetsAsync` (Task 1); `HtmlRequestPolicy`, `HtmlGuideLoadError` (Task 2).
- Produces (namespace `DesktopGuides.Infrastructure.Reading`):
  ```csharp
  public enum HtmlAssetReadStatus { Served, Missing, Changed }
  public sealed record HtmlAssetRead(HtmlAssetReadStatus Status, byte[]? Bytes = null);
  public sealed class ManagedHtmlAssetReader(ManagedPathResolver paths, Guid guideId)
  { public HtmlAssetRead Read(GuideAsset asset); }   // synchronous; callers use Task.Run
  public abstract record HtmlGuideLoad;
  public sealed record HtmlGuideLoaded(HtmlRequestPolicy Policy, ManagedHtmlAssetReader Reader, string EntryFilePath) : HtmlGuideLoad;
  public sealed record HtmlGuideLoadFailed(HtmlGuideLoadError Error) : HtmlGuideLoad;
  public sealed class ManagedHtmlGuideLoader(SqliteLibraryRepository repository, ManagedPathResolver paths)
  { public Task<HtmlGuideLoad> LoadAsync(Guide guide, CancellationToken token); }
  ```
- Produces (namespace `DesktopGuides.Infrastructure.Storage`):
  ```csharp
  public static class AppCacheRoot
  {
      public const string PortableFolderName = "Cache";
      public static string Resolve(bool packaged, Func<string> packagedCacheFolder, string localAppData);
  }
  ```

- [ ] **Step 1: Write the failing reader tests**

`ManagedHtmlAssetReaderTests.cs`:

```csharp
using DesktopGuides.Core.Html;
using DesktopGuides.Infrastructure.Reading;
using DesktopGuides.Infrastructure.Tests.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Reading;

public sealed class ManagedHtmlAssetReaderTests
{
    private static async Task<(Guid Id, IReadOnlyList<GuideAsset> Assets)> PublishAsync(PublisherHarness harness)
    {
        foreach (string file in new[] { "images/map.png", "styles/main.css", "styles/palette.css" })
        {
            harness.Sources.Copy("html-static/" + file, file);
        }
        string entry = harness.Sources.Copy("html-static/guide.html", "guide.html");
        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(entry));
        return (id, await harness.Repository.GetGuideAssetsAsync(id));
    }

    private static string Managed(PublisherHarness harness, Guid id, GuideAsset asset) =>
        harness.Paths.ResolveExistingGuideFile(id, asset.RelativePath);

    [Fact]
    public async Task ServesEveryPublishedRowWithItsManagedBytes()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guid id, IReadOnlyList<GuideAsset> assets) = await PublishAsync(harness);
        ManagedHtmlAssetReader reader = new(harness.Paths, id);

        foreach (GuideAsset asset in assets)
        {
            HtmlAssetRead read = reader.Read(asset);
            Assert.Equal(HtmlAssetReadStatus.Served, read.Status);
            Assert.Equal(File.ReadAllBytes(Managed(harness, id, asset)), read.Bytes);
        }
    }

    [Fact]
    public async Task SameLengthEditIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guid id, IReadOnlyList<GuideAsset> assets) = await PublishAsync(harness);
        GuideAsset css = assets.First(asset => asset.Kind == GuideAssetKind.StyleSheet);
        byte[] bytes = File.ReadAllBytes(Managed(harness, id, css));
        bytes[0] ^= 0x20;
        File.WriteAllBytes(Managed(harness, id, css), bytes);

        Assert.Equal(HtmlAssetReadStatus.Changed, new ManagedHtmlAssetReader(harness.Paths, id).Read(css).Status);
    }

    [Fact]
    public async Task LengthChangeIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guid id, IReadOnlyList<GuideAsset> assets) = await PublishAsync(harness);
        GuideAsset css = assets.First(asset => asset.Kind == GuideAssetKind.StyleSheet);
        File.AppendAllText(Managed(harness, id, css), " ");

        Assert.Equal(HtmlAssetReadStatus.Changed, new ManagedHtmlAssetReader(harness.Paths, id).Read(css).Status);
    }

    [Fact]
    public async Task DeletedFileIsMissing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guid id, IReadOnlyList<GuideAsset> assets) = await PublishAsync(harness);
        GuideAsset image = assets.First(asset => asset.Kind == GuideAssetKind.Image);
        File.Delete(Managed(harness, id, image));

        Assert.Equal(HtmlAssetReadStatus.Missing, new ManagedHtmlAssetReader(harness.Paths, id).Read(image).Status);
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("")]
    public async Task RowsThatEscapeTheGuideRootAreMissing(string relativePath)
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guid id, IReadOnlyList<GuideAsset> assets) = await PublishAsync(harness);
        GuideAsset escaping = assets[0] with { RelativePath = relativePath };

        Assert.Equal(HtmlAssetReadStatus.Missing, new ManagedHtmlAssetReader(harness.Paths, id).Read(escaping).Status);
    }

    [Fact]
    public async Task ReadingLeavesTheManagedCopyUntouched()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guid id, IReadOnlyList<GuideAsset> assets) = await PublishAsync(harness);
        IReadOnlyList<FileFingerprint> before = FileFingerprint.Of(harness.Paths.GetGuideRoot(id));

        foreach (GuideAsset asset in assets) new ManagedHtmlAssetReader(harness.Paths, id).Read(asset);

        Assert.Equal(before, FileFingerprint.Of(harness.Paths.GetGuideRoot(id)));
    }
}
```

(`FileFingerprint.Of` accepts a directory, as `GuideImportPublisherTests`
uses it on `Sources.Root`.)

- [ ] **Step 2: Write the failing loader and cache-root tests**

`ManagedHtmlGuideLoaderTests.cs`:

```csharp
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Reading;
using DesktopGuides.Infrastructure.Tests.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Reading;

public sealed class ManagedHtmlGuideLoaderTests
{
    private static async Task<Guide> PublishAsync(PublisherHarness harness)
    {
        foreach (string file in new[] { "images/map.png", "styles/main.css", "styles/palette.css" })
        {
            harness.Sources.Copy("html-static/" + file, file);
        }
        string entry = harness.Sources.Copy("html-static/guide.html", "guide.html");
        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(entry));
        return (await harness.Repository.GetGuideAsync(id))!;
    }

    private static Task<HtmlGuideLoad> LoadAsync(PublisherHarness harness, Guide guide) =>
        new ManagedHtmlGuideLoader(harness.Repository, harness.Paths).LoadAsync(guide, CancellationToken.None);

    private static async Task<HtmlGuideLoadError> FailedAsync(PublisherHarness harness, Guide guide) =>
        Assert.IsType<HtmlGuideLoadFailed>(await LoadAsync(harness, guide)).Error;

    [Fact]
    public async Task LoadsAPublishedGuideAtItsOwnOrigin()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);

        HtmlGuideLoaded loaded = Assert.IsType<HtmlGuideLoaded>(await LoadAsync(harness, guide));

        Assert.Equal(GuideWebOrigin.EntryUri(guide.Id, "guide.html"), loaded.Policy.EntryUri);
        Assert.IsType<HtmlServe>(loaded.Policy.Decide("GET", loaded.Policy.EntryUri.AbsoluteUri));
        Assert.Equal(harness.Paths.ResolveExistingGuideFile(guide.Id, "guide.html"), loaded.EntryFilePath);
    }

    [Fact]
    public async Task GuideWithoutRowsNeedsReimport()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);
        harness.Execute("DELETE FROM GuideAssets WHERE GuideId = $id", guide.Id);

        Assert.Equal(HtmlGuideLoadError.NoManifest, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task ChangedEntryIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);
        File.AppendAllText(harness.Paths.ResolveExistingGuideFile(guide.Id, "guide.html"), "<p>edit</p>");

        Assert.Equal(HtmlGuideLoadError.Changed, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task MissingEntryIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);
        File.Delete(harness.Paths.ResolveExistingGuideFile(guide.Id, "guide.html"));

        Assert.Equal(HtmlGuideLoadError.Changed, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task EntryRowThatIsNotThePrimaryFileIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);

        Assert.Equal(HtmlGuideLoadError.Changed,
            await FailedAsync(harness, guide with { PrimaryRelativePath = "styles/main.css" }));
    }
}
```

`AppCacheRootTests.cs` (mirrors `AppDataRootTests`):

```csharp
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class AppCacheRootTests : IDisposable
{
    private readonly string localAppData = Directory.CreateTempSubdirectory("dg-localappdata-").FullName;

    [Fact]
    public void PackagedAppUsesThePackageCacheFolder()
    {
        string packaged = Path.Combine(localAppData, "Packages", "DesktopGuides.Preview_x", "LocalCache");

        Assert.Equal(packaged, AppCacheRoot.Resolve(packaged: true, () => packaged, localAppData));
        Assert.False(Directory.Exists(Path.Combine(localAppData, AppDataRoot.PortableFolderName)));
    }

    [Fact]
    public void PortableAppCreatesCacheUnderItsDataFolder()
    {
        string root = AppCacheRoot.Resolve(
            packaged: false, () => throw new InvalidOperationException("No package identity."), localAppData);

        Assert.Equal(Path.Combine(localAppData, "DesktopGuides", "Cache"), root);
        Assert.True(Directory.Exists(root));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative\\folder")]
    public void PortableAppRejectsAMissingOrRelativeLocalAppData(string value) =>
        Assert.Throws<InvalidOperationException>(() => AppCacheRoot.Resolve(packaged: false, () => "", value));

    public void Dispose() => Directory.Delete(localAppData, recursive: true);
}
```

- [ ] **Step 3: Push and confirm RED**

Commit `test(p1): managed HTML asset reader, loader and cache root (red)`,
push, watch. Expected: Infrastructure.Tests fails to compile
(`CS0246: ManagedHtmlAssetReader`, `ManagedHtmlGuideLoader`, `AppCacheRoot`).

- [ ] **Step 4: Implement**

`ManagedHtmlAssetReader.cs`:

```csharp
using System.Security.Cryptography;
using DesktopGuides.Core.Html;
using DesktopGuides.Infrastructure.Storage;

namespace DesktopGuides.Infrastructure.Reading;

public enum HtmlAssetReadStatus { Served, Missing, Changed }

public sealed record HtmlAssetRead(HtmlAssetReadStatus Status, byte[]? Bytes = null);

/// <summary>
/// Opens a row's managed file, never the request path, and returns its
/// bytes only when they still match the imported length and hash.
/// </summary>
public sealed class ManagedHtmlAssetReader(ManagedPathResolver paths, Guid guideId)
{
    private static readonly HtmlAssetRead Missing = new(HtmlAssetReadStatus.Missing);
    private static readonly HtmlAssetRead Changed = new(HtmlAssetReadStatus.Changed);

    public HtmlAssetRead Read(GuideAsset asset)
    {
        try
        {
            string path = paths.ResolveExistingGuideFile(guideId, asset.RelativePath);
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length != asset.ByteCount || asset.ByteCount > Array.MaxLength) return Changed;
            byte[] bytes = new byte[asset.ByteCount];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) return Changed;
            byte[] expected = Convert.FromHexString(asset.Sha256);
            return CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), expected)
                ? new HtmlAssetRead(HtmlAssetReadStatus.Served, bytes)
                : Changed;
        }
        catch (EndOfStreamException)
        {
            return Changed;
        }
        catch (FormatException)
        {
            return Changed;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                          UnauthorizedAccessException or ArgumentException)
        {
            return Missing;
        }
    }
}
```

`ManagedHtmlGuideLoader.cs`:

```csharp
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;

namespace DesktopGuides.Infrastructure.Reading;

public abstract record HtmlGuideLoad;
public sealed record HtmlGuideLoaded(HtmlRequestPolicy Policy, ManagedHtmlAssetReader Reader, string EntryFilePath) : HtmlGuideLoad;
public sealed record HtmlGuideLoadFailed(HtmlGuideLoadError Error) : HtmlGuideLoad;

public sealed class ManagedHtmlGuideLoader(SqliteLibraryRepository repository, ManagedPathResolver paths)
{
    public async Task<HtmlGuideLoad> LoadAsync(Guide guide, CancellationToken token)
    {
        IReadOnlyList<GuideAsset> assets = await repository.GetGuideAssetsAsync(guide.Id, token);
        if (assets.Count == 0) return new HtmlGuideLoadFailed(HtmlGuideLoadError.NoManifest);
        GuideAsset[] entries = [.. assets.Where(asset => asset.Kind == GuideAssetKind.EntryHtml)];
        if (entries.Length != 1 ||
            !string.Equals(entries[0].RelativePath, guide.PrimaryRelativePath, StringComparison.Ordinal))
        {
            return new HtmlGuideLoadFailed(HtmlGuideLoadError.Changed);
        }
        ManagedHtmlAssetReader reader = new(paths, guide.Id);
        HtmlAssetRead entry = await Task.Run(() => reader.Read(entries[0]), token);
        if (entry.Status != HtmlAssetReadStatus.Served) return new HtmlGuideLoadFailed(HtmlGuideLoadError.Changed);
        string entryFile;
        try
        {
            entryFile = paths.ResolveExistingGuideFile(guide.Id, entries[0].RelativePath);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                          UnauthorizedAccessException or ArgumentException)
        {
            return new HtmlGuideLoadFailed(HtmlGuideLoadError.Changed);
        }
        return new HtmlGuideLoaded(new HtmlRequestPolicy(guide.Id, assets), reader, entryFile);
    }
}
```

`AppCacheRoot.cs`:

```csharp
namespace DesktopGuides.Infrastructure.Storage;

// The MSIX build keeps disposable data in the package's LocalCache. The
// portable build has no package identity, so it uses a Cache folder beside
// its library under %LOCALAPPDATA%\DesktopGuides.
public static class AppCacheRoot
{
    public const string PortableFolderName = "Cache";

    public static string Resolve(bool packaged, Func<string> packagedCacheFolder, string localAppData)
    {
        if (packaged) return packagedCacheFolder();
        string root = Path.Combine(
            AppDataRoot.Resolve(false, static () => throw new InvalidOperationException(), localAppData),
            PortableFolderName);
        Directory.CreateDirectory(root);
        return root;
    }
}
```

- [ ] **Step 5: Push and confirm GREEN**

Commit `feat(p1): read managed HTML assets by row and resolve the cache root`,
push, watch. Expected: `core-tests` passes.

---

### Task 4: Production `HtmlReaderSession`, external-link launchers and test gates

**TDD skip (stated):** these types wrap WebView2 and `Windows.System.Launcher`,
which need a WinUI window and an installed runtime; no unit-test project
references Production. Their behavior is verified by the Release build in
`production-packages` and by the installed `html-reader` smoke in Task 6, whose
diagnostics and launch-file assertions fail if any rule below is missing.
Every decision they make comes from the Core policies already pinned in Task 2.

**Files:**
- Create: `src/DesktopGuides.Production/TestGate.cs`
- Create: `src/DesktopGuides.Production/HtmlReaderSession.cs`
- Create: `src/DesktopGuides.Production/ExternalLinkLaunchers.cs`

No `PackageReference` change: `Microsoft.Web.WebView2` (1.0.3719.77) already
reaches Production through `Microsoft.WindowsAppSDK`, and CI restores with
`--locked-mode`, so adding a direct reference would also need a lock
regeneration.

**Interfaces:**
- Consumes: `HtmlRequestPolicy`, `HtmlServe`, `HtmlDeny`, `HtmlDenyReason`,
  `HtmlNavigationPolicy.Classify`, `HtmlNavigationKind`, `HtmlSessionDiagnostics`,
  `HtmlGuideLoadError`, `GuideAssetKind` (Tasks 1–2); `HtmlGuideLoaded`,
  `ManagedHtmlAssetReader`, `HtmlAssetReadStatus` (Task 3); `IReaderSession`,
  `ManagedGuideSource`, `ReaderLocation`, `HtmlPosition`, `RestoreOutcome`.
- Produces (namespace `DesktopGuides.Production`, all `internal`):
  ```csharp
  static class TestGate { public static bool IsOpen(string name); }
  sealed class HtmlGuideLoadException(HtmlGuideLoadError error) : Exception
  { public HtmlGuideLoadError Error { get; } }
  sealed class HtmlReaderSession : IReaderSession
  {
      public HtmlReaderSession(HtmlGuideLoaded loaded, string cacheRoot, HtmlSessionDiagnostics? diagnostics);
      public WebView2 View { get; }
      public event EventHandler<Uri>? ExternalLinkRequested;
      public static HtmlSessionDiagnostics? DiagnosticsForTest();   // non-null only when the gate exists
  }
  interface IExternalLinkLauncher { Task<bool> LaunchAsync(Uri uri); }
  static class ExternalLinkLaunchers { public static IExternalLinkLauncher Create(string cacheRoot); }
  ```

- [ ] **Step 1: Add `TestGate`**

`TestGate.cs`:

```csharp
namespace DesktopGuides.Production;

// Installed tests switch on optional behavior by creating a named event
// before they drive the window. Without the event the app behaves normally.
internal static class TestGate
{
    public static bool IsOpen(string name)
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(name, out EventWaitHandle? gate)) return false;
            gate.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
```

- [ ] **Step 2: Add `HtmlReaderSession`**

`HtmlReaderSession.cs`:

```csharp
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Infrastructure.Reading;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace DesktopGuides.Production;

internal sealed class HtmlGuideLoadException(HtmlGuideLoadError error)
    : Exception(HtmlGuideLoadMessages.For(error))
{
    public HtmlGuideLoadError Error { get; } = error;
}

// The Reader's session for one HTML guide. WebView2 sees only the guide's
// own origin: every request is answered from the manifest rows, every
// navigation away from the entry document is cancelled, and a click on a
// website link is handed to the shell's bar instead of being followed.
internal sealed class HtmlReaderSession : IReaderSession
{
    private static int sessionCounter;
    private readonly HtmlRequestPolicy policy;
    private readonly ManagedHtmlAssetReader reader;
    private readonly string cacheRoot;
    private readonly HtmlSessionDiagnostics? diagnostics;
    private readonly string profile;
    private CoreWebView2Environment? environment;
    private CoreWebView2? core;
    private string? contentSha256;
    private bool entryNavigated;
    private bool entryServed;
    private bool disposed;

    public HtmlReaderSession(HtmlGuideLoaded loaded, string cacheRoot, HtmlSessionDiagnostics? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentException.ThrowIfNullOrEmpty(cacheRoot);
        policy = loaded.Policy;
        reader = loaded.Reader;
        this.cacheRoot = cacheRoot;
        this.diagnostics = diagnostics;
        profile = Path.Combine(cacheRoot, "WebView2", Guid.NewGuid().ToString("N"));
        View = new WebView2();
    }

    public WebView2 View { get; }
    public GuideFormat Format => GuideFormat.Html;
    public ReaderCapabilities Capabilities => ReaderCapabilities.Scroll;

    // Capabilities don't change during an HTML session.
    public event EventHandler? CapabilitiesChanged { add { } remove { } }
    // T09.3 adds position tracking.
    public event EventHandler<LocationChangedEventArgs>? LocationChanged { add { } remove { } }
    public event EventHandler<Uri>? ExternalLinkRequested;

    public static HtmlSessionDiagnostics? DiagnosticsForTest() =>
        TestGate.IsOpen($@"Local\DesktopGuides.Preview.HtmlDiagnostics.{Environment.ProcessId}")
            ? new HtmlSessionDiagnostics()
            : null;

    public async Task OpenAsync(ManagedGuideSource source, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Guide.Id != policy.GuideId)
        {
            throw new ArgumentException("The source is a different guide.", nameof(source));
        }
        ObjectDisposedException.ThrowIf(disposed, this);
        contentSha256 = source.Guide.ContentSha256;
        try
        {
            Directory.CreateDirectory(profile);
            environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                null, profile, new CoreWebView2EnvironmentOptions());
            await View.EnsureCoreWebView2Async(environment);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new HtmlGuideLoadException(HtmlGuideLoadError.RuntimeMissing);
        }
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed, this);

        core = View.CoreWebView2;
        CoreWebView2Settings settings = core.Settings;
        settings.IsScriptEnabled = false;
        settings.IsWebMessageEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreDevToolsEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;
        core.NavigationStarting += OnNavigationStarting;
        core.FrameNavigationStarting += OnFrameNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.PermissionRequested += OnPermissionRequested;
        core.DownloadStarting += OnDownloadStarting;
        core.LaunchingExternalUriScheme += OnLaunchingExternalUriScheme;

        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            sender.NavigationCompleted -= Completed;
            completion.TrySetResult(args.IsSuccess);
        }
        core.NavigationCompleted += Completed;
        core.Navigate(policy.EntryUri.AbsoluteUri);
        bool success = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
        if (!success || !entryServed)
        {
            throw new HtmlGuideLoadException(HtmlGuideLoadError.Changed);
        }
    }

    public Task<ReaderLocation> GetLocationAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // T09.3 captures the real position; until then the entry's top.
        return Task.FromResult(new ReaderLocation(
            GuideFormat.Html, 1, contentSha256 ?? string.Empty,
            new HtmlPosition(policy.Entry.RequestPath, null, null, 0, 0), 0));
    }

    public Task<RestoreOutcome> RestoreLocationAsync(ReaderLocation location, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(location);
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new RestoreOutcome(
            RestoreKind.Unavailable, "HTML positions are restored in a later preview."));
    }

    public Task ApplyAppearanceAsync(ReaderAppearance appearance, CancellationToken token) => Task.CompletedTask;

    public Task ExecuteAsync(ReaderAction action, CancellationToken token) =>
        throw new NotSupportedException("HTML guides have no reader commands yet.");

    private async void OnWebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        CoreWebView2Deferral deferral = args.GetDeferral();
        try
        {
            string context = args.ResourceContext.ToString();
            HtmlRequestDecision decision = policy.Decide(args.Request.Method, args.Request.Uri);
            if (decision is HtmlServe serve)
            {
                HtmlAssetRead read = await Task.Run(() => reader.Read(serve.Asset));
                if (disposed || environment is null) return;
                if (read.Status == HtmlAssetReadStatus.Served)
                {
                    diagnostics?.RecordServed(serve.Asset.RequestPath);
                    if (serve.Asset.Kind == GuideAssetKind.EntryHtml) entryServed = true;
                    args.Response = environment.CreateWebResourceResponse(
                        new MemoryStream(read.Bytes!).AsRandomAccessStream(), 200, "OK",
                        HtmlRequestPolicy.ServedHeaders(serve.ContentType));
                    return;
                }
                Deny(args, read.Status == HtmlAssetReadStatus.Changed
                    ? HtmlDenyReason.HashMismatch
                    : HtmlDenyReason.FileMissing, context);
                return;
            }
            Deny(args, ((HtmlDeny)decision).Reason, context);
        }
        catch (Exception) when (disposed)
        {
            // The view closed while a request was in flight.
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void Deny(CoreWebView2WebResourceRequestedEventArgs args, HtmlDenyReason reason, string context)
    {
        if (disposed || environment is null) return;
        diagnostics?.RecordDenied(reason, context);
        args.Response = environment.CreateWebResourceResponse(
            new MemoryStream().AsRandomAccessStream(), 403, "Forbidden", HtmlRequestPolicy.DeniedHeaders);
    }

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        HtmlNavigation navigation = HtmlNavigationPolicy.Classify(
            args.Uri, policy.EntryUri, entryNavigated, args.IsUserInitiated);
        switch (navigation.Kind)
        {
            case HtmlNavigationKind.Entry:
                entryNavigated = true;
                break;
            case HtmlNavigationKind.SameDocument:
                break;
            case HtmlNavigationKind.External:
                args.Cancel = true;
                ExternalLinkRequested?.Invoke(this, navigation.ExternalUri!);
                break;
            default:
                args.Cancel = true;
                break;
        }
    }

    private static void OnFrameNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args) =>
        args.Cancel = true;

    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        HtmlNavigation navigation = HtmlNavigationPolicy.Classify(
            args.Uri, policy.EntryUri, entryNavigated: true, args.IsUserInitiated);
        if (navigation.Kind == HtmlNavigationKind.External)
        {
            ExternalLinkRequested?.Invoke(this, navigation.ExternalUri!);
        }
    }

    private static void OnPermissionRequested(CoreWebView2 sender, CoreWebView2PermissionRequestedEventArgs args) =>
        args.State = CoreWebView2PermissionState.Deny;

    private static void OnDownloadStarting(CoreWebView2 sender, CoreWebView2DownloadStartingEventArgs args) =>
        args.Cancel = true;

    // Defense in depth: NavigationStarting already cancels custom schemes.
    private static void OnLaunchingExternalUriScheme(
        CoreWebView2 sender, CoreWebView2LaunchingExternalUriSchemeEventArgs args) => args.Cancel = true;

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        ExternalLinkRequested = null;
        WriteDiagnostics();
        if (core is not null)
        {
            core.WebResourceRequested -= OnWebResourceRequested;
            core.NavigationStarting -= OnNavigationStarting;
            core.FrameNavigationStarting -= OnFrameNavigationStarting;
            core.NewWindowRequested -= OnNewWindowRequested;
            core.PermissionRequested -= OnPermissionRequested;
            core.DownloadStarting -= OnDownloadStarting;
            core.LaunchingExternalUriScheme -= OnLaunchingExternalUriScheme;
        }
        if (environment is not null)
        {
            // The profile folder is locked until the browser process exits.
            TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
            environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
            View.Close();
            await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        }
        else
        {
            View.Close();
        }
        // T09.1 sweeps profiles a crash or a slow exit leaves behind.
        try
        {
            if (Directory.Exists(profile)) Directory.Delete(profile, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void WriteDiagnostics()
    {
        if (diagnostics is null) return;
        try
        {
            string folder = Path.Combine(cacheRoot, "diagnostics");
            Directory.CreateDirectory(folder);
            int number = Interlocked.Increment(ref sessionCounter);
            File.WriteAllText(
                Path.Combine(folder, $"html-session-{Environment.ProcessId}-{number}.json"),
                diagnostics.ToJson(policy.GuideId));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Test diagnostics must never affect closing a guide.
        }
    }
}
```

- [ ] **Step 3: Add the launchers**

`ExternalLinkLaunchers.cs`:

```csharp
using System.Text.Json;

namespace DesktopGuides.Production;

internal interface IExternalLinkLauncher
{
    Task<bool> LaunchAsync(Uri uri);
}

internal static class ExternalLinkLaunchers
{
    // Installed tests record the URL instead of opening a browser on the runner.
    public static IExternalLinkLauncher Create(string cacheRoot) =>
        TestGate.IsOpen($@"Local\DesktopGuides.Preview.ExternalLaunch.{Environment.ProcessId}")
            ? new RecordingExternalLinkLauncher(Path.Combine(cacheRoot, "diagnostics", "external-launches.json"))
            : new SystemExternalLinkLauncher();
}

internal sealed class SystemExternalLinkLauncher : IExternalLinkLauncher
{
    public async Task<bool> LaunchAsync(Uri uri) => await Windows.System.Launcher.LaunchUriAsync(uri);
}

internal sealed class RecordingExternalLinkLauncher(string path) : IExternalLinkLauncher
{
    public async Task<bool> LaunchAsync(Uri uri)
    {
        List<string> launched = File.Exists(path)
            ? JsonSerializer.Deserialize<List<string>>(await File.ReadAllTextAsync(path)) ?? []
            : [];
        launched.Add(uri.AbsoluteUri);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(launched));
        return true;
    }
}
```

- [ ] **Step 4: Build in CI**

Nothing calls these types yet; Task 5 wires them. Commit
`feat(p1): HTML reader session with manifest responder and deny rules`,
push, watch. Expected: `production-packages (x64)` and `(ARM64)` build with
no new warnings; `production-shell-ui` still passes (the HTML placeholder is
unchanged). A compile error naming a WebView2 member (for example
`LaunchingExternalUriScheme` or `IsGeneralAutofillEnabled`) means the
transitive WebView2 version lacks it: check
`src/DesktopGuides.Production/packages.lock.json` and record a ruling rather
than adding a `PackageReference`.

---

### Task 5: Shell wiring and the external-link bar

**TDD skip (stated):** the shell is WinUI code with no unit-test project. The
RED for this task is the Task 6 smoke, which is written against these
AutomationIds and copy; Task 6 lands in the same PR and the
`production-shell-ui` job is the GREEN.

**Files:**
- Create: `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (ReaderPanel row 2, around line 444)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (fields at line 73–75; `InitializeCoreAsync` at line 269; `CloseReaderSessionAsync` at line 1255; the reader route at about line 1641)

**Interfaces:**
- Consumes: `ManagedHtmlGuideLoader`, `HtmlGuideLoad*`, `AppCacheRoot` (Task 3);
  `HtmlReaderSession`, `HtmlGuideLoadException`, `ExternalLinkLaunchers` (Task 4);
  `HtmlGuideLoadMessages.For` (Task 2).
- Produces: AutomationIds `ReaderExternalLinkBar`, `ReaderExternalLinkUrl`,
  `ReaderExternalLinkOpen`, `ReaderExternalLinkDismiss` and the status
  "This link couldn't be opened." (R17), consumed by Task 6.

- [ ] **Step 1: Add the bar to the Reader**

In `ShellWindow.xaml`, replace

```xml
                <local:ReaderToolbar x:Name="ReaderActions"
                                     Grid.Row="2" />
```

with

```xml
                <StackPanel Grid.Row="2">
                    <local:ReaderToolbar x:Name="ReaderActions" />
                    <!-- Collapsed while closed, so the Reader's layout is unchanged without a link. -->
                    <InfoBar x:Name="ReaderExternalLinkBar"
                             Margin="0,12,0,0"
                             Visibility="Collapsed"
                             IsOpen="False"
                             IsClosable="True"
                             Severity="Informational"
                             Title="This link leaves Desktop Guides"
                             Closed="ReaderExternalLinkBarClosed"
                             AutomationProperties.AutomationId="ReaderExternalLinkBar">
                        <InfoBar.Content>
                            <StackPanel Spacing="{StaticResource DesktopGuidesSpacing12}"
                                        Margin="0,0,0,12">
                                <TextBlock x:Name="ReaderExternalLinkUrl"
                                           TextWrapping="Wrap"
                                           IsTextSelectionEnabled="True"
                                           AutomationProperties.AutomationId="ReaderExternalLinkUrl" />
                                <StackPanel Orientation="Horizontal"
                                            Spacing="{StaticResource DesktopGuidesSpacing8}">
                                    <Button x:Name="ReaderExternalLinkOpen"
                                            Content="Open in browser"
                                            Style="{StaticResource AccentButtonStyle}"
                                            Click="ReaderExternalLinkOpenClicked"
                                            AutomationProperties.AutomationId="ReaderExternalLinkOpen" />
                                    <Button x:Name="ReaderExternalLinkDismiss"
                                            Content="Dismiss"
                                            Click="ReaderExternalLinkDismissClicked"
                                            AutomationProperties.AutomationId="ReaderExternalLinkDismiss" />
                                </StackPanel>
                            </StackPanel>
                        </InfoBar.Content>
                    </InfoBar>
                </StackPanel>
```

- [ ] **Step 2: Add the fields and initialize them**

In `ShellWindow.xaml.cs`, after `private ManagedTextGuideLoader? textLoader;`:

```csharp
    private ManagedHtmlGuideLoader? htmlLoader;
    private string? cacheRoot;
```

In `InitializeCoreAsync`, after `textLoader = new ManagedTextGuideLoader(paths);`:

```csharp
            htmlLoader = new ManagedHtmlGuideLoader(repository, paths);
            cacheRoot = AppCacheRoot.Resolve(
                AppDataRoot.HasPackageIdentity(),
                () => ApplicationData.Current.LocalCacheFolder.Path,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
```

- [ ] **Step 3: Close the bar with the Reader**

At the start of `CloseReaderSessionAsync`, before `readerLoad?.Cancel();`:

```csharp
        HideExternalLinkBar();
```

- [ ] **Step 4: Route HTML guides to the session**

In the reader route, replace

```csharp
                    if (guide.Format != GuideFormat.Txt)
                    {
                        ShowTransientStatus("Guide ready.");
                        break;
                    }
```

with

```csharp
                    if (guide.Format == GuideFormat.Html)
                    {
                        if (!await OpenHtmlGuideAsync(guide, generation))
                        {
                            return false;
                        }
                        break;
                    }
                    // PDF keeps the placeholder until T10.
                    if (guide.Format != GuideFormat.Txt)
                    {
                        ShowTransientStatus("Guide ready.");
                        break;
                    }
```

- [ ] **Step 5: Add the partial**

`ShellWindow.HtmlReader.cs`:

```csharp
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Infrastructure.Reading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    private Uri? pendingExternalLink;

    // Returns false when a newer render took over, like the TXT path.
    private async Task<bool> OpenHtmlGuideAsync(Guide guide, int generation)
    {
        ShowReaderSurface(placeholder: false);
        readerLoad = new CancellationTokenSource();
        CancellationToken token = readerLoad.Token;
        HtmlGuideLoad load;
        try
        {
            load = await htmlLoader!.LoadAsync(guide, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        if (generation != renderGeneration)
        {
            return false;
        }
        if (load is HtmlGuideLoadFailed failed)
        {
            ShowHtmlLoadError(failed.Error);
            return true;
        }
        HtmlGuideLoaded loaded = (HtmlGuideLoaded)load;
        HtmlReaderSession session = new(loaded, cacheRoot!, HtmlReaderSession.DiagnosticsForTest());
        // The next render disposes it if this one is cancelled.
        readerSession = session;
        session.ExternalLinkRequested += OnExternalLinkRequested;
        ShowReaderSurface(placeholder: false, view: session.View);
        try
        {
            await session.OpenAsync(new ManagedGuideSource(guide, loaded.EntryFilePath), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        catch (HtmlGuideLoadException error)
        {
            if (generation != renderGeneration)
            {
                return false;
            }
            readerSession = null;
            ShowReaderSurface(placeholder: false);
            await session.DisposeAsync();
            ShowHtmlLoadError(error.Error);
            return true;
        }
        if (generation != renderGeneration)
        {
            return false;
        }
        ReaderActions.SetSession(session);
        ShowTransientStatus("Guide ready.");
        return true;
    }

    private void ShowHtmlLoadError(HtmlGuideLoadError error)
    {
        string message = HtmlGuideLoadMessages.For(error);
        ShowReaderSurface(placeholder: false, error: message);
        ShowWarningStatus(message);
    }

    // A newer link replaces the URL in an open bar.
    private void OnExternalLinkRequested(object? sender, Uri uri)
    {
        if (!ReferenceEquals(sender, readerSession))
        {
            return;
        }
        pendingExternalLink = uri;
        ReaderExternalLinkUrl.Text = uri.AbsoluteUri;
        ReaderExternalLinkBar.Visibility = Visibility.Visible;
        ReaderExternalLinkBar.IsOpen = true;
    }

    private void HideExternalLinkBar()
    {
        pendingExternalLink = null;
        ReaderExternalLinkBar.IsOpen = false;
        ReaderExternalLinkBar.Visibility = Visibility.Collapsed;
        ReaderExternalLinkUrl.Text = string.Empty;
    }

    private async void ReaderExternalLinkOpenClicked(object sender, RoutedEventArgs args)
    {
        Uri? uri = pendingExternalLink;
        HideExternalLinkBar();
        if (uri is null || cacheRoot is null)
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

    private void ReaderExternalLinkDismissClicked(object sender, RoutedEventArgs args) => HideExternalLinkBar();

    // The InfoBar's own close button behaves like Dismiss.
    private void ReaderExternalLinkBarClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (pendingExternalLink is not null)
        {
            HideExternalLinkBar();
        }
    }
}
```

`HideExternalLinkBar` sets `IsOpen = false` itself, which raises `Closed`
again; the `pendingExternalLink` check stops the re-entry.

- [ ] **Step 6: Build in CI**

Commit `feat(p1): open HTML guides in the Reader with an external-link bar`.
Push together with Task 6 (the smoke that exercises it). Expected:
`production-packages` builds with no new warnings; the TXT phases in
`production-shell-ui` (including `txt-layout` and the resize checks) stay
green, which shows the collapsed bar leaves the Reader's layout unchanged.

---

### Task 6: Canary, canary guides, seed and installed checks

**TDD:** the canary is test-first and runs locally (`python3`). The
Infrastructure fixture test and the installed smoke are **stated skips of
an observed RED**: no local `dotnet`, and the pre-Task-5 behavior (the HTML
placeholder) is already pinned green by `main`'s `html-placeholder` phase,
so a separate RED run would cost a full `production-shell-ui` run to show
only what that phase shows. These checks are the GREEN for Task 5.

**Files:**
- Modify: `tools/p0/http_canary.py`
- Create: `tools/p0/test_http_canary.py`
- Create: `tests/fixtures/p1/html-canary/a/guide.html`, `a/style.css`, `a/images/a.png`
- Create: `tests/fixtures/p1/html-canary/b/guide.html`, `b/style.css`, `b/images/b.png`
- Create: `tests/DesktopGuides.Infrastructure.Tests/Import/HtmlCanaryFixtureTests.cs`
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (new block before the generic usage check at line 421; usage string at line 427)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (ValidateSet lines 3–15; TXT block at line 1189; `html-placeholder` phase at lines 1519–1527)
- Modify: `tools/p1/windows_shell_install.ps1` (smoke timeouts at lines 707–709; new functions after `Run-TxtReaderScenarios` at line 860; main sequence at about line 1501)
- Modify: `.github/workflows/windows-ci.yml` (core-tests check steps at about line 103; production-shell evidence upload at lines 324–325)

**Interfaces:**
- Consumes: `GuideWebOrigin.OriginFor(Guid)` (Task 2); `GetGuideAssetsAsync`,
  `GuideAsset.RequestPath` (Task 1); `GuideImportValidator.InspectAsync`,
  `ImportReady`, `GuideImportPublisher.PublishAsync` (existing); the
  AutomationIds `ReaderExternalLinkBar`, `ReaderExternalLinkUrl`,
  `ReaderExternalLinkOpen`, `ReaderExternalLinkDismiss`, `ReaderLoadError`
  and the copy "Re-import this guide to read it." (Task 5); diagnostics JSON
  `{ guideId, served[], denied[{reason, context, count}] }` written to
  `<cacheRoot>\diagnostics\html-session-<pid>-<n>.json` and
  `<cacheRoot>\diagnostics\external-launches.json` (a JSON array of URLs)
  (Tasks 2 and 4); gates `Local\DesktopGuides.Preview.HtmlDiagnostics.<pid>`
  and `Local\DesktopGuides.Preview.ExternalLaunch.<pid>` (Task 4).
- Produces: `http_canary.make_server(log: Path, port: int) -> CanaryServer`;
  `ShellSeed seed-html-reader <app-data-root> <fixtures-root>` printing
  `{"guideA":"<N>","guideB":"<N>"}`; smoke mode `html-reader` with phases
  `html-canary-a`, `html-external-links`, `html-canary-b`; TXT phase
  `html-no-manifest`.

The packaged shell is full trust, not an AppContainer, so its WebView2
processes can reach loopback: a guide-originated request would reach the
canary. The canary binds `127.0.0.1` only, so no firewall rule is needed.

- [ ] **Step 1: Write the failing canary tests**

Create `tools/p0/test_http_canary.py`:

```python
"""Tests for the connection-counting loopback canary."""

from __future__ import annotations

import http.client
import socket
import tempfile
import threading
import time
import unittest
from pathlib import Path

import http_canary


class CanaryTests(unittest.TestCase):
    def setUp(self) -> None:
        self.folder = tempfile.TemporaryDirectory()
        self.log = Path(self.folder.name) / "logs" / "canary.log"
        self.server = http_canary.make_server(self.log, 0)
        self.port = self.server.server_address[1]
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()

    def tearDown(self) -> None:
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(5)
        self.folder.cleanup()

    def lines(self, count: int) -> list[str]:
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            if self.log.exists():
                found = self.log.read_text(encoding="utf-8").splitlines()
                if len(found) >= count:
                    return found
            time.sleep(0.05)
        self.fail(f"expected at least {count} log lines")

    def request(self, method: str, path: str) -> tuple[int, str | None]:
        connection = http.client.HTTPConnection("127.0.0.1", self.port, timeout=5)
        try:
            connection.request(method, path)
            response = connection.getresponse()
            return response.status, response.getheader("Location")
        finally:
            connection.close()

    def test_creates_the_log_folder(self) -> None:
        self.assertTrue(self.log.parent.is_dir())

    def test_connection_without_a_request_is_recorded(self) -> None:
        socket.create_connection(("127.0.0.1", self.port), timeout=5).close()
        self.assertEqual(self.lines(1), ["ACCEPT"])

    def test_request_is_recorded_after_its_connection(self) -> None:
        self.assertEqual(self.request("GET", "/x?y=1"), (204, None))
        self.assertEqual(self.lines(2), ["ACCEPT", "GET /x?y=1"])

    def test_health_records_only_the_connection(self) -> None:
        self.assertEqual(self.request("GET", "/health")[0], 204)
        time.sleep(0.2)
        self.assertEqual(self.lines(1), ["ACCEPT"])

    def test_other_methods_are_recorded(self) -> None:
        for method in ("POST", "HEAD", "OPTIONS", "PUT"):
            self.assertEqual(self.request(method, "/m")[0], 204)
        found = self.lines(8)
        for method in ("POST", "HEAD", "OPTIONS", "PUT"):
            self.assertIn(f"{method} /m", found)

    def test_redirect_points_off_host(self) -> None:
        self.assertEqual(
            self.request("GET", "/redirect"),
            (302, "https://example.net/desktop-guides-canary-redirect"))
        self.assertEqual(self.lines(2), ["ACCEPT", "GET /redirect"])


if __name__ == "__main__":
    unittest.main()
```

- [ ] **Step 2: Run them and watch them fail**

Run: `python3 -m unittest discover -s tools/p0 -p "test_*.py" -v`
Expected: every test errors with
`AttributeError: module 'http_canary' has no attribute 'make_server'`.

Commit `test(p0): canary records accepted connections`.

- [ ] **Step 3: Implement the canary**

Replace `tools/p0/http_canary.py`:

```python
"""Loopback-only canary that records every accepted connection and request.

Guide fixtures point their references at it. Any line in the log, including
a bare ACCEPT from a preconnect, is a guide-originated connection.
"""

from __future__ import annotations

import argparse
import socket
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

REDIRECT_TARGET = "https://example.net/desktop-guides-canary-redirect"


class CanaryServer(ThreadingHTTPServer):
    daemon_threads = True

    def __init__(self, log: Path, port: int) -> None:
        self.log = log
        self.lock = threading.Lock()
        super().__init__(("127.0.0.1", port), CanaryHandler)

    def record(self, line: str) -> None:
        with self.lock, self.log.open("a", encoding="utf-8") as log:
            log.write(line + "\n")

    def get_request(self) -> tuple[socket.socket, object]:
        request = super().get_request()
        self.record("ACCEPT")
        return request


class CanaryHandler(BaseHTTPRequestHandler):
    server: CanaryServer

    def do_GET(self) -> None:
        self.handle_request()

    def do_POST(self) -> None:
        self.handle_request()

    def do_HEAD(self) -> None:
        self.handle_request()

    def do_OPTIONS(self) -> None:
        self.handle_request()

    def do_PUT(self) -> None:
        self.handle_request()

    def handle_request(self) -> None:
        if self.path != "/health":
            self.server.record(f"{self.command} {self.path}")
        if self.path == "/redirect":
            self.send_response(302)
            self.send_header("Location", REDIRECT_TARGET)
        else:
            self.send_response(204)
        self.end_headers()

    def log_message(self, format: str, *values: object) -> None:
        pass


def make_server(log: Path, port: int) -> CanaryServer:
    log.parent.mkdir(parents=True, exist_ok=True)
    return CanaryServer(log, port)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--log", required=True, type=Path)
    parser.add_argument("--port", default=8765, type=int)
    args = parser.parse_args()

    server = make_server(args.log, args.port)
    print(f"listening on 127.0.0.1:{server.server_address[1]}", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `python3 -m unittest discover -s tools/p0 -p "test_*.py" -v`
Expected: `Ran 6 tests` … `OK`.

Add the CI step to the core-tests job, after "Check test-owned scheduled
task cleanup" (that job already sets up Python 3.11):

```yaml
      - name: Check loopback canary
        shell: pwsh
        run: python -m unittest discover -s tools/p0 -p "test_*.py" -v
```

Commit `feat(p0): connection-counting loopback canary`.

- [ ] **Step 5: Add the canary guides**

The PNGs are byte copies of the P0 static image:

```bash
mkdir -p tests/fixtures/p1/html-canary/a/images tests/fixtures/p1/html-canary/b/images
cp tests/fixtures/p0/html-static/images/map.png tests/fixtures/p1/html-canary/a/images/a.png
cp tests/fixtures/p0/html-static/images/map.png tests/fixtures/p1/html-canary/b/images/b.png
```

Create `tests/fixtures/p1/html-canary/a/guide.html`. `__GUIDE_B_ORIGIN__` is
replaced at seed time with B's origin without the trailing slash. The links
sit at the top so they are on screen for pointer clicks; the spacer pushes
`#details` below the fold so the fragment link has to scroll.

```html
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Canary guide A</title>
<meta http-equiv="refresh" content="1; url=http://127.0.0.1:8765/redirect">
<base href="http://127.0.0.1:8765/base/">
<link rel="stylesheet" href="style.css">
<link rel="stylesheet" href="http://127.0.0.1:8765/link-stylesheet">
<link rel="icon" href="http://127.0.0.1:8765/link-icon">
<link rel="preconnect" href="http://127.0.0.1:8765">
<link rel="dns-prefetch" href="http://127.0.0.1:8765">
<link rel="prefetch" href="http://127.0.0.1:8765/link-prefetch">
<link rel="preload" href="http://127.0.0.1:8765/link-preload" as="image">
</head>
<body>
<h1>Canary guide A loaded</h1>
<p><a href="https://example.com/desktop-guides-canary" ping="http://127.0.0.1:8765/ping">External canary link</a></p>
<p><a href="https://example.org/desktop-guides-canary-blank" target="_blank">New window canary link</a></p>
<p><a href="__GUIDE_B_ORIGIN__/guide.html">Open canary guide B</a></p>
<p><a href="#details">Jump to details</a></p>
<p class="nested set">Styled by the canary style sheet</p>
<p><img src="images/a.png" srcset="images/a.png 1x, http://127.0.0.1:8765/img-srcset 2x" alt="Local canary image" width="64" height="64"></p>
<p><img src="http://127.0.0.1:8765/img-src" alt="" width="1" height="1"></p>
<picture><source srcset="http://127.0.0.1:8765/picture-source"><img src="images/a.png" alt="" width="1" height="1"></picture>
<p><input type="image" src="http://127.0.0.1:8765/input-image" alt="" width="1" height="1"></p>
<video poster="http://127.0.0.1:8765/video-poster" width="1" height="1"></video>
<table background="http://127.0.0.1:8765/table-background"><tr><td>Background attribute</td></tr></table>
<div style="background-image: url(http://127.0.0.1:8765/inline-style)">Inline style</div>
<object data="http://127.0.0.1:8765/object" width="1" height="1"></object>
<embed src="http://127.0.0.1:8765/embed" width="1" height="1">
<iframe src="http://127.0.0.1:8765/iframe" width="1" height="1"></iframe>
<p><img src="__GUIDE_B_ORIGIN__/images/b.png" alt="" width="1" height="1"></p>
<div style="height: 3000px"></div>
<section id="details"><h2>Canary details</h2><p>The fragment link scrolls here.</p></section>
</body>
</html>
```

Create `tests/fixtures/p1/html-canary/a/style.css`:

```css
@import url(http://127.0.0.1:8765/css-import);
body { font-family: sans-serif; margin: 24px; }
h1 { padding-right: 72px; background: url(images/a.png) no-repeat right center; }
.nested { background: linear-gradient(#ffffff, #eeeeee), url("http://127.0.0.1:8765/css-nested-url"); }
.set { list-style-image: image-set(url(http://127.0.0.1:8765/css-image-set-1x) 1x, url(http://127.0.0.1:8765/css-image-set-2x) 2x); }
```

Create `tests/fixtures/p1/html-canary/b/guide.html`:

```html
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Canary guide B</title>
<link rel="stylesheet" href="style.css">
<link rel="icon" href="http://127.0.0.1:8765/b-link-icon">
</head>
<body>
<h1>Canary guide B loaded</h1>
<p><img src="images/b.png" alt="Local canary image B" width="64" height="64"></p>
<p><img src="http://127.0.0.1:8765/b-img-src" alt="" width="1" height="1"></p>
<div style="background-image: url(http://127.0.0.1:8765/b-inline-style)">Inline style B</div>
</body>
</html>
```

Create `tests/fixtures/p1/html-canary/b/style.css`:

```css
@import url(http://127.0.0.1:8765/b-css-import);
body { font-family: sans-serif; margin: 24px; }
h1 { padding-right: 72px; background: url(images/b.png) no-repeat right center; }
```

`tests/fixtures/p1/**` is already `-text` in `.gitattributes`, so the bytes
the scanner hashes don't change on checkout.

- [ ] **Step 6: Pin what the canary guides publish**

Create `tests/DesktopGuides.Infrastructure.Tests/Import/HtmlCanaryFixtureTests.cs`.
It runs the real preview and publisher, so a fixture edit that adds a local
file, or a scanner change that starts following `<base href>`, fails here
before the installed run.

```csharp
using System.Text;
using DesktopGuides.Core.Html;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Import;

public sealed class HtmlCanaryFixtureTests
{
    private static readonly string CanaryRoot =
        Path.Combine(Path.GetDirectoryName(P0Fixtures.Root)!, "p1", "html-canary");

    [Fact]
    public async Task CanaryGuidesPublishOnlyTheirOwnLocalFiles()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guid b = await harness.PublishAsync(
            harness.Publisher(), await harness.InspectAsync(CopyCanary(harness, "b", null)));
        Guid a = await harness.PublishAsync(
            harness.Publisher(), await harness.InspectAsync(CopyCanary(harness, "a", b)));

        Assert.Equal(new[] { "guide.html", "images/a.png", "style.css" }, await RequestPathsAsync(harness, a));
        Assert.Equal(new[] { "guide.html", "images/b.png", "style.css" }, await RequestPathsAsync(harness, b));
        string entry = File.ReadAllText(harness.Paths.ResolveExistingGuideFile(a, "guide.html"));
        Assert.Contains(GuideWebOrigin.OriginFor(b).Host, entry, StringComparison.Ordinal);
        Assert.DoesNotContain("__GUIDE_B_ORIGIN__", entry, StringComparison.Ordinal);
    }

    private static async Task<string[]> RequestPathsAsync(PublisherHarness harness, Guid guideId) =>
        (await harness.Repository.GetGuideAssetsAsync(guideId)).Select(asset => asset.RequestPath).ToArray();

    private static string CopyCanary(PublisherHarness harness, string guide, Guid? otherGuide)
    {
        string source = Path.Combine(CanaryRoot, guide);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file).Replace(Path.DirectorySeparatorChar, '/');
            byte[] bytes = File.ReadAllBytes(file);
            if (relative == "guide.html" && otherGuide is Guid other)
            {
                bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace(
                    "__GUIDE_B_ORIGIN__", GuideWebOrigin.OriginFor(other).AbsoluteUri.TrimEnd('/'),
                    StringComparison.Ordinal));
            }
            harness.Sources.Write($"{guide}/{relative}", bytes);
        }
        return Path.Combine(harness.Sources.Root, guide, "guide.html");
    }
}
```

Commit `test(p1): canary HTML guides publish only their own files`. It is
observed in CI with Step 9's push. Expected: passes. If `InspectAsync`
throws, a canary reference is one the preview rejects rather than warns
about: report which one, and replace it with an equivalent the preview
accepts only with a ledger ruling, because the installed run then no longer
covers that reference.

- [ ] **Step 7: Seed the canary guides through the real import (R5)**

In `tools/p1/DesktopGuides.ShellSeed/Program.cs`, add the usings:

```csharp
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Import;
using DesktopGuides.Infrastructure.Import;
```

Add this block after the `seed-txt-reader` block and before the generic
`if (args.Length != 2 || ...)` usage check:

```csharp
if (args.Length == 3 && args[0] == "seed-html-reader")
{
    ManagedPathResolver htmlPaths = new(args[1]);
    await using SqliteLibraryRepository htmlRepository = new(htmlPaths);
    await htmlRepository.InitializeAsync();
    if ((await htmlRepository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The HTML reader seed needs an empty library.");
    }
    string canaries = Path.Combine(Path.GetFullPath(args[2]), "p1", "html-canary");
    Game htmlGame = await htmlRepository.AddGameAsync("Web Reader Game", null, null);
    GuideImportValidator validator = new();
    GuideImportPublisher publisher = new(htmlRepository, htmlPaths);
    string staging = Path.Combine(Path.GetTempPath(), "desktop-guides-html-seed-" + Guid.NewGuid().ToString("N"));
    try
    {
        // Copies one canary guide, points its cross-guide references at the
        // other guide's origin, and imports it as a user would.
        async Task<Guid> PublishCanaryAsync(string guide, string title, Guid? otherGuide)
        {
            string source = Path.Combine(canaries, guide);
            string target = Path.Combine(staging, guide);
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string copy = Path.Combine(target, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(file, copy);
            }
            string entry = Path.Combine(target, "guide.html");
            if (otherGuide is Guid other)
            {
                File.WriteAllText(entry, File.ReadAllText(entry).Replace(
                    "__GUIDE_B_ORIGIN__", GuideWebOrigin.OriginFor(other).AbsoluteUri.TrimEnd('/'),
                    StringComparison.Ordinal));
            }
            ImportInspection inspection = await validator.InspectAsync(entry, CancellationToken.None);
            if (inspection is not ImportReady ready)
            {
                throw new InvalidOperationException($"The {guide} canary guide failed the import preview: {inspection}.");
            }
            return await publisher.PublishAsync(
                ready.Manifest, htmlGame.Id, title, false, null, CancellationToken.None);
        }

        Guid guideB = await PublishCanaryAsync("b", "Canary Guide B", null);
        Guid guideA = await PublishCanaryAsync("a", "Canary Guide A", guideB);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            guideA = guideA.ToString("N"),
            guideB = guideB.ToString("N"),
        }));
    }
    finally
    {
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
    }
    return 0;
}
```

In the usage string, change
`"or seed-txt-reader <app-data-root> <fixtures-root> " +` to
`"or seed-txt-reader|seed-html-reader <app-data-root> <fixtures-root> " +`.

If a new using makes an existing name ambiguous, alias the one type that
collides rather than dropping the using.

- [ ] **Step 8: Add the `html-reader` smoke and the `html-no-manifest` phase**

In `tools/p1/windows_shell_ui_smoke.ps1`:

1. Add `'html-reader'` after `'txt-load-released'` in the `-Mode`
   `ValidateSet`.
2. Change the TXT block's condition and game name:

```powershell
    elseif ($Mode -in @('txt-reader', 'txt-load-paused', 'txt-back-during-load',
        'txt-load-released', 'html-reader')) {
        $textGame = if ($Mode -eq 'html-reader') { 'Web Reader Game' } else { 'Text Reader Game' }
```

3. Replace the `html-placeholder` phase in the `txt-reader` body (R4):

```powershell
            # A Web Page Guide with no saved asset rows (imported before
            # schema v4) asks to be re-imported.
            Back-ToTextGame
            Open-TextGuide 'Web Page Guide'
            [void](Wait-Status 'Re-import this guide to read it.')
            [void](Wait-Name 'ReaderLoadError' 'Re-import this guide to read it.')
            Assert-Absent 'ReaderTextLines'
            Assert-Absent 'ReaderPlaceholder'
            Assert-NoReaderCommands 'Web Page Guide'
            $report.phases += 'html-no-manifest'
```

4. Add this branch immediately before the final `else {` (the `txt-reader`
   body) of the TXT block's inner `if`/`elseif` chain. Links are clicked
   with real pointer input, because R9 only raises user-initiated
   navigation to the bar.

```powershell
        elseif ($Mode -eq 'html-reader') {
            [void](Wait-Name 'LibraryHeading' 'Library')
            Select-Element $textGame
            [void](Wait-Name 'GameHeading' $textGame)
            [void](Wait-Status 'Game ready.')

            # WebView2 content joins the window's UI Automation tree once the
            # page has rendered.
            function Wait-PageName([string] $name) {
                $deadline = (Get-Date).AddSeconds(15)
                do {
                    $element = Find-ByName $name
                    if ($element) { return $element }
                    Start-Sleep -Milliseconds 250
                } while ((Get-Date) -lt $deadline)
                throw "The guide page did not show '$name'."
            }

            function Assert-NoExternalLinkBar([string] $after) {
                $bar = Find-ById 'ReaderExternalLinkBar'
                if ($bar -and -not $bar.Current.IsOffscreen) {
                    throw "The external-link bar opened after $after."
                }
            }

            # html-canary-a: the guide renders at its own origin with no
            # commands and no load error.
            Open-TextGuide 'Canary Guide A'
            [void](Wait-Status 'Guide ready.')
            [void](Wait-PageName 'Canary guide A loaded')
            Assert-NoReaderCommands 'Canary Guide A'
            Assert-Absent 'ReaderLoadError'
            Assert-Absent 'ReaderPlaceholder'
            # The meta refresh fires after one second. It isn't
            # user-initiated, so it is cancelled without the bar (R9).
            Start-Sleep -Seconds 2
            Assert-NoExternalLinkBar 'the meta refresh'
            [void](Wait-PageName 'Canary guide A loaded')
            $report.phases += 'html-canary-a'

            # html-external-links: another guide's origin is denied
            # silently; website links go to the bar; a fragment scrolls.
            Click-Element (Wait-PageName 'Open canary guide B')
            Start-Sleep -Seconds 1
            Assert-NoExternalLinkBar 'a link into another guide'
            [void](Wait-PageName 'Canary guide A loaded')
            if (Find-ByName 'Canary guide B loaded') {
                throw 'A link into another guide loaded that guide.'
            }

            Click-Element (Wait-PageName 'External canary link')
            [void](Wait-VisibleById 'ReaderExternalLinkBar')
            [void](Wait-Name 'ReaderExternalLinkUrl' 'https://example.com/desktop-guides-canary')
            $report.htmlExternalLinkScreenshot = Save-WindowScreenshot 'html-external-link'
            Invoke-Element (Find-ById 'ReaderExternalLinkOpen')
            Wait-HiddenById 'ReaderExternalLinkBar'
            [void](Wait-PageName 'Canary guide A loaded')

            Click-Element (Wait-PageName 'New window canary link')
            [void](Wait-VisibleById 'ReaderExternalLinkBar')
            [void](Wait-Name 'ReaderExternalLinkUrl' 'https://example.org/desktop-guides-canary-blank')
            Invoke-Element (Find-ById 'ReaderExternalLinkDismiss')
            Wait-HiddenById 'ReaderExternalLinkBar'

            Click-Element (Wait-PageName 'Jump to details')
            $details = Wait-PageName 'Canary details'
            $deadline = (Get-Date).AddSeconds(5)
            while ($details.Current.IsOffscreen -and (Get-Date) -lt $deadline) {
                Start-Sleep -Milliseconds 250
            }
            if ($details.Current.IsOffscreen) {
                throw 'The fragment link did not scroll to its section.'
            }
            Assert-NoExternalLinkBar 'a fragment link'
            [void](Wait-PageName 'Canary guide A loaded')
            $report.phases += 'html-external-links'

            # html-canary-b: the second guide renders at its own origin.
            Back-ToTextGame
            Open-TextGuide 'Canary Guide B'
            [void](Wait-Status 'Guide ready.')
            [void](Wait-PageName 'Canary guide B loaded')
            Assert-NoReaderCommands 'Canary Guide B'
            Assert-Absent 'ReaderLoadError'
            Assert-NoExternalLinkBar 'opening Canary Guide B'
            $report.phases += 'html-canary-b'

            # Back closes the session, which writes its diagnostics.
            Back-ToTextGame
        }
```

If Chromium's UIA reports `IsOffscreen` wrongly for page content (the
details check fails while the screenshot shows the section), record it in
the design's Implementation notes and drop only that `IsOffscreen` wait,
with a ledger ruling.

- [ ] **Step 9: Run the canary passes from the install script**

In `tools/p1/windows_shell_install.ps1`, give `html-*` modes the 120 s
timeout:

```powershell
    $timeoutSeconds = if ($mode -like 'provider-*') { 240 }
        elseif ($mode -like 'catalog*' -or $mode -like 'import-*' -or $mode -like 'game-actions*' -or $mode -like 'html-*') { 120 }
        else { 60 }
```

Add these functions after `Assert-TxtBackDuringLoad`:

```powershell
function Get-HtmlCacheRoot {
    # Matches AppCacheRoot: portable uses %LOCALAPPDATA%\DesktopGuides\Cache,
    # packaged uses the package's LocalCache beside LocalState.
    if ($portable) { return Join-Path $dataRoot 'Cache' }
    return Join-Path (Split-Path -Parent $dataRoot) 'LocalCache'
}

function Test-HtmlCanaryListening {
    try {
        Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:8765/health' -TimeoutSec 2 | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Start-HtmlCanary([string] $logPath) {
    $script = (Resolve-Path (Join-Path $PSScriptRoot '..\p0\http_canary.py')).Path
    $python = (Get-Command python -ErrorAction Stop).Source
    $process = Start-Process -FilePath $python -PassThru -WindowStyle Hidden -ArgumentList @(
        ('"' + $script + '"'), '--log', ('"' + $logPath + '"'), '--port', '8765')
    $deadline = (Get-Date).AddSeconds(15)
    do {
        if ($process.HasExited) {
            throw "The loopback canary exited with code $($process.ExitCode)."
        }
        if (Test-HtmlCanaryListening) { return $process }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    Stop-HtmlCanary $process
    throw 'The loopback canary did not answer its health check.'
}

function Stop-HtmlCanary($process) {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(5000) | Out-Null
    }
}

function Get-HtmlCanaryLines([string] $logPath) {
    if (-not (Test-Path -LiteralPath $logPath)) { return @() }
    return @(Get-Content -LiteralPath $logPath)
}

function Invoke-HtmlReaderPass([string] $resultName) {
    Start-InstalledShell
    $processId = $report.launchedProcessId
    $diagnosticsGate = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        "Local\DesktopGuides.Preview.HtmlDiagnostics.$processId")
    $launchGate = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        "Local\DesktopGuides.Preview.ExternalLaunch.$processId")
    try {
        $report.htmlReader[$resultName] = Run-ShellSmoke 'html-reader' -ResultName $resultName
        Close-InstalledShell
    }
    finally {
        $launchGate.Dispose()
        $diagnosticsGate.Dispose()
    }
}

function Assert-HtmlReaderPass([string] $pass, [string] $cacheRoot, $ids, [string] $logPath, [int] $baseline) {
    # R19: isolation is shown by exactly what each guide served. Deny counts
    # are kept as evidence only, because the CSP can stop a reference before
    # the request handler sees it.
    $diagnostics = Join-Path $cacheRoot 'diagnostics'
    $expected = @{
        $ids.guideA = 'guide.html,images/a.png,style.css'
        $ids.guideB = 'guide.html,images/b.png,style.css'
    }
    $files = @(Get-ChildItem -LiteralPath $diagnostics -Filter 'html-session-*.json' -ErrorAction SilentlyContinue)
    if ($files.Count -ne 2) {
        throw "The $pass pass wrote $($files.Count) HTML session diagnostics; expected 2."
    }
    $sessions = @()
    foreach ($file in $files) {
        $session = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        if (-not $expected.ContainsKey($session.guideId)) {
            throw "The $pass pass wrote diagnostics for an unexpected or repeated guide $($session.guideId)."
        }
        $served = @($session.served) -join ','
        if ($served -cne $expected[$session.guideId]) {
            throw "Guide $($session.guideId) served '$served' in the $pass pass; expected '$($expected[$session.guideId])'."
        }
        $expected.Remove($session.guideId)
        $sessions += $session
    }

    $launchesPath = Join-Path $diagnostics 'external-launches.json'
    if (-not (Test-Path -LiteralPath $launchesPath)) {
        throw "The $pass pass recorded no external launch."
    }
    # Two statements, so Windows PowerShell doesn't wrap the parsed array.
    $launches = Get-Content -LiteralPath $launchesPath -Raw | ConvertFrom-Json
    $launches = @($launches)
    if ($launches.Count -ne 1 -or $launches[0] -cne 'https://example.com/desktop-guides-canary') {
        throw "The $pass pass launched '$($launches -join ', ')'; expected only https://example.com/desktop-guides-canary."
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

function Run-HtmlReaderScenarios {
    # Each canary guide opens once with the loopback canary listening (light)
    # and once with it stopped (dark): TR07.1-TR07.3.
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    $ids = Invoke-ShellSeed @('seed-html-reader', $dataRoot, $fixtureRoot) | ConvertFrom-Json
    $cacheRoot = Get-HtmlCacheRoot
    $diagnostics = Join-Path $cacheRoot 'diagnostics'
    $logPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath(
        (Join-Path $ResultDirectory 'html-canary.log'))
    Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
    $report.htmlReader = [ordered]@{ guideA = $ids.guideA; guideB = $ids.guideB }
    $originalTheme = Get-AppThemePreference
    $canary = $null
    try {
        $canary = Start-HtmlCanary $logPath
        $baseline = @(Get-HtmlCanaryLines $logPath).Count
        Set-AppThemePreference $true
        Invoke-HtmlReaderPass 'html-reader-online'
        $report.htmlReader.online = Assert-HtmlReaderPass 'online' $cacheRoot $ids $logPath $baseline

        Stop-HtmlCanary $canary
        $canary = $null
        if (Test-HtmlCanaryListening) {
            throw 'The loopback canary still answered after it was stopped.'
        }
        Remove-Item -LiteralPath $diagnostics -Recurse -Force
        $baseline = @(Get-HtmlCanaryLines $logPath).Count
        Set-AppThemePreference $false
        Invoke-HtmlReaderPass 'html-reader-offline'
        $report.htmlReader.offline = Assert-HtmlReaderPass 'offline' $cacheRoot $ids $logPath $baseline
    }
    finally {
        Stop-HtmlCanary $canary
        Restore-AppThemePreference $originalTheme
    }
}
```

In the main sequence, run it after the TXT scenarios on a wiped library:

```powershell
    Run-TxtReaderScenarios

    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    Run-HtmlReaderScenarios

    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    Run-ImportScenarios
```

The script stays ASCII-only. The gates exist before the smoke starts and
are disposed only after the shell closes, so a click can never reach the
system launcher on the runner.

In `.github/workflows/windows-ci.yml`, add the canary log to the
production-shell evidence upload:

```yaml
            artifacts/production-shell/installed/*.json
            artifacts/production-shell/installed/*.png
            artifacts/production-shell/installed/*.log
```

- [ ] **Step 10: Push Tasks 5 and 6 and confirm GREEN**

Commit `test(p1): installed HTML canary passes and external-link checks`
(seed, smoke, install script, workflow). Push, then watch:
`gh run list --branch feat/p1-t07-3-webview2-policy --limit 1` and, on a
failure, `gh run view <id> --log-failed`.

Expected:
- `core-tests` passes, including "Check loopback canary" (6 tests) and
  `CanaryGuidesPublishOnlyTheirOwnLocalFiles`.
- `production-shell-ui` passes `txt-reader` light and dark with the
  `html-no-manifest` phase, and `html-reader-online` and
  `html-reader-offline` with phases `html-canary-a`, `html-external-links`
  and `html-canary-b`.
- The uploaded evidence has both `html-reader-*.html-external-link.png`
  screenshots (light and dark) and an `html-canary.log` whose only lines are
  the health-check `ACCEPT`s from before each baseline.
- The install report's `htmlReader.online.sessions[*].denied` lists the
  denials that reached the handler; keep them for Task 7.

A served-set mismatch that includes a loopback-relative path, or any new
canary line, is a real finding (see "Implementation notes to confirm in
CI"): stop and diagnose it with `superpowers:systematic-debugging` rather
than loosening the check.

---

### Task 7: Traceability and results docs

**TDD skip (stated):** documentation only. Verification is a re-read
against the CI evidence from Task 6 and a link check.

**Files:**
- Modify: `docs/p1/t07-3-webview2-policy-design.md` (Status; Implementation notes)
- Modify: `docs/p1/implementation-plan.md` (T08.3 paragraph at line 790; new T07.3 paragraph after it)
- Modify: `docs/work-breakdown.md` (T07.3 at lines 246–247)
- Modify: `docs/p1-technical-design.md` (only where engine behavior differs: lines 442–446 and 603–612)

**Interfaces:**
- Consumes: the Task 6 CI run ID, its install report (`htmlReader.*`), the
  ledger's `Ruling:` lines, and the PR number once the draft PR exists.

- [ ] **Step 1: Fill in the design's results**

In `docs/p1/t07-3-webview2-policy-design.md`, set Status to "implemented in
PR #<n>; CI run <id> passed `production-shell-ui`". Under "Implementation
notes", replace each open question with what CI showed:
- whether a fragment link raised `NavigationStarting`;
- whether `rel=icon`, `prefetch` and `preload` reached
  `WebResourceRequested`, and whether `ping` was sent;
- whether pointer-clicked anchors reported `IsUserInitiated`;
- for each loopback and cross-guide reference in canary A, the layer that
  stopped it: the CSP (no handler entry), the handler (a `denied` entry,
  with its reason and context), or navigation (`NavigationStarting` or
  `NewWindowRequested`);
- that `base href` was ignored under `base-uri 'none'`.

Add a "Planning rulings" list with R1–R19 from the plan, one line each,
and every `Ruling:` line from the execution ledger. Call out the two that
change spec wording: R4 (the rowless Web Page Guide) and R19 (isolation
shown by served sets, not deny counts).

- [ ] **Step 2: Update the implementation plan**

In `docs/p1/implementation-plan.md`, replace the stale first sentence of the
T08.3 paragraph:

```markdown
T08.3 was merged through PR #31 on 2 October 2026, merge commit `3157a19`,
with follow-ups in PR #33, merge commit `e447597`; see the
[design and verification record](t08-3-txt-position-design.md).
```

Then add after that paragraph:

```markdown
T07.3 is in review in PR #<n>; see the
[design and verification record](t07-3-webview2-policy-design.md). Static
HTML guides open in the Reader in a WebView2 at a per-guide
`https://g<id>.guide.invalid` origin that serves only the import's saved
`GuideAssets` rows, re-hashed on every request. Scripts, frames,
permissions, downloads and navigation away from the entry are denied; a
website link is shown in a Reader bar and opens only through **Open in
browser**. CI run <id> passed the installed `html-reader` canary with the
loopback canary listening and stopped, with zero recorded connections.
```

- [ ] **Step 3: Update the work breakdown and technical design**

In `docs/work-breakdown.md`, append to the T07.3 bullet:
`Implemented in PR #<n>; see p1/t07-3-webview2-policy-design.md.`

In `docs/p1-technical-design.md`, change text only where CI showed engine
behavior that differs from the starting approach, for example that some
references are stopped by the CSP before `WebResourceRequested`. Add a
pointer to the T07.3 design's Implementation notes. If nothing differs,
leave the file unchanged and say so in the PR description.

- [ ] **Step 4: Check and commit**

Run: `grep -n "<n>\|<id>" docs/p1/t07-3-webview2-policy-design.md docs/p1/implementation-plan.md docs/work-breakdown.md`
Expected: no output (every placeholder replaced with the real PR number
and run ID).

Commit `docs(p1): T07.3 results, rulings and traceability`, push.
Expected: CI passes unchanged.
