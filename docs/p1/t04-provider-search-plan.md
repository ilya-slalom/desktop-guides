# T04.4 provider-backed search-first Add game implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a game by searching IGDB, then save it as a local game with a
stable ID, provider link, bounded metadata snapshot and cached cover
artwork that stay readable offline. Manual add remains the fallback.

**Architecture:** Core holds the snapshot model, the normalizer, the
provider interfaces and `ProviderGameImporter`; none of it does I/O.
Infrastructure (`net10.0`) holds the HTTP clients behind one `ProviderHttp`
policy, the managed artwork validator and store, the credential blob
format, and schema v3. Production (WinUI) holds DPAPI protection, the
Settings card, `AddGameDialog`, and the Game route metadata view.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK 2.5.1),
Microsoft.Data.Sqlite 10.0.12, System.Text.Json, xunit 2.9.3,
CommunityToolkit.WinUI.Controls.MetadataControl 8.2.251219, and PowerShell
UI Automation harnesses on `pcsx2-win`.

**Spec:** [t04-provider-search-design.md](t04-provider-search-design.md)

## Global Constraints

- Provider ID `"igdb"` only. `ExternalId` is a canonical positive integer
  string of 1–20 characters.
- Search returns at most 20 results. Typing alone sends no request; only
  Enter or Search does.
- Query: trimmed, 1–100 characters, with `\` and `"` escaped.
- Snapshot limits: summary ≤ 4,000 characters; each list ≤ 16 items of
  ≤ 80 characters; `MetadataJson` ≤ 65,536 characters; `SchemaVersion = 1`.
- HTTP: HTTPS only, a fixed host allow-list, no cross-host redirect, 15 s
  per request, JSON ≤ 1 MB, images ≤ 5 MB, one request at a time.
- HTTP 429: retry once if `Retry-After` ≤ 2 s; otherwise `RateLimited`.
- Artwork: PNG, JPEG or WebP, ≤ 4096 px on each side. Stored at
  `artwork/{gameId:N}/{sha256}.{ext}` under the library root and never
  modified after it is written.
- Platform is taken from IGDB only when the record lists exactly one
  platform; otherwise it is left empty.
- Refresh never changes Title, Platform or Notes.
- Credentials live only in the protected blob at `LocalState\providers.bin`.
  They never reach SQLite, logs, exception text, evidence or the repo.
- The live credential files on `pcsx2-win` are
  `E:\work\igdb_credentials.txt` and `E:\work\steamgriddb_credentials.txt`.
  Never print their values.
- Attribution copy: "Metadata from IGDB" and "Artwork from SteamGridDB".
- UI tests assert what app code controls, not WinUI rendering.
- Every commit ends with
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Show each
  commit message in chat for approval before committing.

## Rulings carried into this plan

- **R1 (staging path):** Staged artwork goes in `library/.artwork-staging/`,
  not `library/.staging/artwork/`. `FileOperationReconciler.CountReviewOrphans`
  counts every entry under `.staging/` as an orphan that needs review.
  Task 1 updates the spec.
- **R2 (offline E2E):** Blocking the app's network for a true offline run
  needs a per-package outbound firewall rule, which changes host
  settings. Task 12 stops and asks the user before adding it. If the user
  declines, the E2E records the seeded linked game rendering with no
  credentials saved and no provider request, and Core/Infrastructure tests
  carry the rest.

## Review Focus

1. **A non-ASCII or punctuation-heavy query** ("Pokémon: Let's Go",
   `"Quote" \ Slash`) is escaped and sent correctly. Pinned in Task 7 by
   `SearchBodyEscapesQuotesAndBackslashesAndKeepsUnicode`.
2. **A game deleted after it was linked**, then added again from search:
   the unique index must not block the new add, and the sweep must remove
   the old artwork. Pinned in Task 5 by `SweepRemovesArtworkOfDeletedGame`
   and in Task 4 by `LinkIsFreeAgainAfterGameRowIsDeleted`.
3. **An IGDB record with no cover, no Steam ID and a title SteamGridDB
   does not know:** the game saves with no artwork and shows the notice.
   Pinned in Task 9 by `AddWithNoArtworkCandidateSavesGameWithNotice`.
4. **Refresh returns identical bytes:** the existing file is kept, not
   deleted and rewritten. Pinned in Task 9 by
   `RefreshWithIdenticalImageKeepsCurrentFile`.
5. **A stored `MetadataJson` that is corrupt or from a future
   `SchemaVersion`:** the game still lists and opens, with no metadata
   panel. Pinned in Task 4 by `CorruptMetadataJsonLoadsGameWithoutMetadata`.

## File structure

Create:

| Path | Responsibility |
| --- | --- |
| `src/DesktopGuides.Core/Providers/GameMetadataSnapshot.cs` | Snapshot, `ProviderGameLink`, `ArtworkHints`, `ProviderGameRecord`, `ProviderSearchResult`, `GameTypeTag` |
| `src/DesktopGuides.Core/Providers/GameMetadataNormalizer.cs` | Limits, control-character stripping, removing duplicates, type-tag mapping |
| `src/DesktopGuides.Core/Providers/ProviderContracts.cs` | `IGameMetadataProvider`, `IArtworkSource`, `ArtworkCandidate`, `IProviderCredentialStore`, `IgdbCredentials`, `ProviderCredentials`, `IArtworkStore`, `StoredArtwork` |
| `src/DesktopGuides.Core/Providers/ProviderException.cs` | `ProviderErrorKind` and `ProviderException` |
| `src/DesktopGuides.Core/Providers/FallbackArtworkSource.cs` | Tries its sources in order |
| `src/DesktopGuides.Core/Providers/ProviderGameImporter.cs` | Add and refresh orchestration |
| `src/DesktopGuides.Infrastructure/Storage/GameMetadataJson.cs` | Serializes and parses the snapshot JSON |
| `src/DesktopGuides.Infrastructure/Providers/ProviderHttp.cs` | Shared HTTP policy |
| `src/DesktopGuides.Infrastructure/Providers/TwitchTokenSource.cs` | Client-credentials token cache |
| `src/DesktopGuides.Infrastructure/Providers/IgdbClient.cs` | `IGameMetadataProvider` over IGDB v4 |
| `src/DesktopGuides.Infrastructure/Providers/SteamGridDbArtworkSource.cs` | SteamGridDB grid lookup |
| `src/DesktopGuides.Infrastructure/Providers/IgdbCoverArtworkSource.cs` | IGDB cover URL |
| `src/DesktopGuides.Infrastructure/Providers/ProviderCredentialBlob.cs` | Credential JSON format |
| `src/DesktopGuides.Infrastructure/Artwork/ArtworkValidator.cs` | Managed PNG, JPEG and WebP header parser |
| `src/DesktopGuides.Infrastructure/Artwork/ManagedArtworkStore.cs` | Stage, validate, commit, delete, sweep |
| `src/DesktopGuides.Production/Providers/WindowsProviderCredentialStore.cs` | DPAPI-protected blob |
| `src/DesktopGuides.Production/AddGameDialog.xaml(.cs)` | Search-first dialog |
| `src/DesktopGuides.Production/ProviderSettingsCard.xaml(.cs)` | Game data providers card |
| `tests/DesktopGuides.Core.Tests/Providers/*.cs` | Normalizer, fallback, importer tests |
| `tests/DesktopGuides.Infrastructure.Tests/Providers/*.cs` | HTTP, token, IGDB, SteamGridDB, blob tests plus `Fixtures/` |
| `tests/DesktopGuides.Infrastructure.Tests/Artwork/*.cs` | Validator and store tests |

Modify: `LibraryModels.cs`, `ILibraryRepository.cs`, `ILibraryPaths.cs`,
`ManagedPathResolver.cs`, `LibrarySchema.cs`, `SqliteLibraryRepository.cs`,
`SqliteLibraryRepositoryTests.cs`, `ShellWindow.xaml(.cs)`,
`Directory.Packages.props`, the Production `.csproj`,
`tools/p1/DesktopGuides.ShellSeed/Program.cs`,
`tools/p1/windows_shell_install.ps1`, `tools/p1/windows_shell_ui_smoke.ps1`,
`docs/p1/e2e-testing.md`, `docs/p1/implementation-plan.md`, and the spec.

Test commands used throughout:

```bash
dotnet test tests/DesktopGuides.Core.Tests/DesktopGuides.Core.Tests.csproj -c Release
dotnet test tests/DesktopGuides.Infrastructure.Tests/DesktopGuides.Infrastructure.Tests.csproj -c Release
```

Production builds and harness runs happen on `pcsx2-win` (see Task 11).

---

### Task 1: Terms, hosts and spec rulings

No behaviour to test; this is a documentation task, and TDD is skipped
for that reason.

**Files:**
- Modify: `docs/p1/t04-provider-search-design.md` (Provider decision,
  Components, Artwork crash safety)

**Interfaces:**
- Produces: the recorded host allow-list that Task 6 copies verbatim,
  and the attribution wording that Task 11 uses.

- [ ] **Step 1: Read the terms pages.** Fetch
  `https://www.igdb.com/api` (the IGDB terms linked from it),
  `https://legal.twitch.com/legal/developer-agreement/` and
  `https://www.steamgriddb.com/terms`. Note the attribution each requires
  and any ban on caching or storing data.
- [ ] **Step 2: Confirm the SteamGridDB image hosts.** Using the key from
  `E:\work\steamgriddb_credentials.txt` on `pcsx2-win`, run one grid query
  and print **only the host name** of each `url` and `thumb`:

```powershell
$key = ((Get-Content E:\work\steamgriddb_credentials.txt) -split ':\s*', 2)[1].Trim()
$r = Invoke-RestMethod -Uri 'https://www.steamgriddb.com/api/v2/grids/steam/620?dimensions=600x900' `
    -Headers @{ Authorization = "Bearer $key" }
$r.data | ForEach-Object { ([Uri]$_.url).Host; ([Uri]$_.thumb).Host } | Sort-Object -Unique
Remove-Variable key
```

  Expected: one or more host names (probably `cdn2.steamgriddb.com`). The
  key is never printed.
- [ ] **Step 3: Update the spec.** In "Provider decision", add a
  "Terms checked 2026-09-29" line with each page's attribution
  requirement. In "Components", replace "plus the SteamGridDB image CDN
  hosts. Task 1 records those CDN hosts…" with the confirmed host list.
  Replace both mentions of `.staging/artwork/` with
  `library/.artwork-staging/` and add R1's reason in one sentence. If the
  terms forbid storing snapshots or artwork offline, **stop and report to
  the user** before any later task.
- [ ] **Step 4: Commit** (after the user approves the message).

```bash
git add docs/p1/t04-provider-search-design.md
git commit -m "docs(p1): record provider terms, image hosts and artwork staging path"
```

### Task 2: Core snapshot model and normalizer

**Files:**
- Create: `src/DesktopGuides.Core/Providers/GameMetadataSnapshot.cs`,
  `src/DesktopGuides.Core/Providers/GameMetadataNormalizer.cs`,
  `src/DesktopGuides.Core/Providers/ProviderException.cs`
- Modify: `src/DesktopGuides.Core/Library/LibraryModels.cs` (`Game`)
- Test: `tests/DesktopGuides.Core.Tests/Providers/GameMetadataNormalizerTests.cs`

**Interfaces:**
- Produces:
  - `enum GameTypeTag { MainGame, Remaster, Remake, Port, Edition, Expansion, Bundle, Other }`
  - `record GameMetadataSnapshot(int SchemaVersion, string? Summary, DateOnly? FirstReleaseDate, IReadOnlyList<string> Genres, IReadOnlyList<string> Developers, IReadOnlyList<string> Publishers, IReadOnlyList<string> Platforms, string? ProviderUrl, GameTypeTag Type, string? ArtworkSource = null)` with `const int CurrentSchemaVersion = 1`. `ArtworkSource` is `"SteamGridDB"`, `"IGDB"` or null, and drives the artwork attribution.
  - `record ProviderGameLink(string Provider, string ExternalId, DateTimeOffset RetrievedUtc)` with `const string Igdb = "igdb"`
  - `record ArtworkHints(string Title, string? SteamAppId, string? IgdbCoverImageId)`
  - `record ProviderGameRecord(string ExternalId, string Title, GameMetadataSnapshot Snapshot, ArtworkHints Hints)`
  - `record ProviderSearchResult(string ExternalId, string Title, int? ReleaseYear, IReadOnlyList<string> Platforms, GameTypeTag Type, string? ThumbnailUrl)`
  - `enum ProviderErrorKind { NotConfigured, InvalidCredentials, Unavailable, Timeout, RateLimited, MalformedData }`
  - `sealed class ProviderException(ProviderErrorKind kind, string message, Exception? inner = null) : Exception`
  - `static class GameMetadataNormalizer` with `string NormalizeExternalId(string? raw)`, `string NormalizeTitle(string? raw)`, `string? NormalizeText(string? raw, int max)`, `IReadOnlyList<string> NormalizeList(IEnumerable<string?>? raw)`, `GameTypeTag MapType(string? igdbType, bool hasVersionParent)`, and `string? NormalizeProviderUrl(string? raw)`. Invalid IDs and titles throw `ProviderException(MalformedData)`.
  - `Game` gains the trailing parameters `ProviderGameLink? Link = null, GameMetadataSnapshot? Metadata = null, string? ArtworkRelativePath = null`.

- [ ] **Step 1: Write the failing tests**

```csharp
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Core.Tests.Providers;

public sealed class GameMetadataNormalizerTests
{
    [Theory]
    [InlineData("1942", "1942")]
    [InlineData(" 7 ", "7")]
    public void AcceptsCanonicalPositiveIds(string raw, string expected) =>
        Assert.Equal(expected, GameMetadataNormalizer.NormalizeExternalId(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("007")]
    [InlineData("12a")]
    [InlineData("123456789012345678901")]
    public void RejectsInvalidIds(string? raw)
    {
        ProviderException error = Assert.Throws<ProviderException>(
            () => GameMetadataNormalizer.NormalizeExternalId(raw));
        Assert.Equal(ProviderErrorKind.MalformedData, error.Kind);
    }

    [Fact]
    public void TitleStripsControlCharactersAndIsCappedAt160()
    {
        string title = GameMetadataNormalizer.NormalizeTitle("Half\u0000-Life\u202E " + new string('x', 300));
        Assert.StartsWith("Half-Life ", title);
        Assert.Equal(160, title.Length);
        Assert.Throws<ProviderException>(() => GameMetadataNormalizer.NormalizeTitle(" \u0007 "));
    }

    [Fact]
    public void TextKeepsNewlinesAndTruncatesToLimit()
    {
        string? text = GameMetadataNormalizer.NormalizeText("a\r\nb\u0001" + new string('c', 5000), 4000);
        Assert.StartsWith("a\nb", text);
        Assert.Equal(4000, text!.Length);
        Assert.Null(GameMetadataNormalizer.NormalizeText("  ", 4000));
    }

    [Fact]
    public void ListsDropBlanksAndDuplicatesAndCapCountAndLength()
    {
        IEnumerable<string?> raw = new[] { "PC", "pc", " ", null, new string('p', 90) }
            .Concat(Enumerable.Range(0, 30).Select(i => $"P{i}"));
        IReadOnlyList<string> list = GameMetadataNormalizer.NormalizeList(raw);
        Assert.Equal(16, list.Count);
        Assert.Equal("PC", list[0]);
        Assert.Equal(80, list[1].Length);
        Assert.DoesNotContain("pc", list);
    }

    [Theory]
    [InlineData("Main Game", false, GameTypeTag.MainGame)]
    [InlineData("Remaster", false, GameTypeTag.Remaster)]
    [InlineData("Remake", false, GameTypeTag.Remake)]
    [InlineData("Port", false, GameTypeTag.Port)]
    [InlineData("Expanded Game", false, GameTypeTag.Edition)]
    [InlineData("Main Game", true, GameTypeTag.Edition)]
    [InlineData("Expansion", false, GameTypeTag.Expansion)]
    [InlineData("Standalone Expansion", false, GameTypeTag.Expansion)]
    [InlineData("DLC", false, GameTypeTag.Expansion)]
    [InlineData("Bundle", false, GameTypeTag.Bundle)]
    [InlineData("Pack / Addon", false, GameTypeTag.Bundle)]
    [InlineData(null, false, GameTypeTag.Other)]
    [InlineData("Mod", false, GameTypeTag.Other)]
    public void MapsIgdbGameTypes(string? type, bool hasParent, GameTypeTag expected) =>
        Assert.Equal(expected, GameMetadataNormalizer.MapType(type, hasParent));

    [Theory]
    [InlineData("https://www.igdb.com/games/half-life", "https://www.igdb.com/games/half-life")]
    [InlineData("http://www.igdb.com/games/half-life", null)]
    [InlineData("https://evil.example/games/x", null)]
    [InlineData("https://www.igdb.com/companies/valve", null)]
    [InlineData("javascript:alert(1)", null)]
    public void ProviderUrlOnlyAllowsIgdbGamePages(string raw, string? expected) =>
        Assert.Equal(expected, GameMetadataNormalizer.NormalizeProviderUrl(raw));

    [Fact]
    public void ManualGamesHaveNoProviderFields()
    {
        Game game = new(Guid.NewGuid(), "T", null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        Assert.Null(game.Link);
        Assert.Null(game.Metadata);
        Assert.Null(game.ArtworkRelativePath);
    }
}
```

  Add `using DesktopGuides.Core.Library;` at the top for `Game`.

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test tests/DesktopGuides.Core.Tests/DesktopGuides.Core.Tests.csproj -c Release --filter FullyQualifiedName~GameMetadataNormalizerTests`
Expected: build FAIL, `The type or namespace name 'Providers' does not exist`.

- [ ] **Step 3: Implement**

`ProviderException.cs`:

```csharp
namespace DesktopGuides.Core.Providers;

public enum ProviderErrorKind
{
    NotConfigured,
    InvalidCredentials,
    Unavailable,
    Timeout,
    RateLimited,
    MalformedData
}

public sealed class ProviderException(
    ProviderErrorKind kind, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public ProviderErrorKind Kind { get; } = kind;
}
```

`GameMetadataSnapshot.cs`:

```csharp
namespace DesktopGuides.Core.Providers;

public enum GameTypeTag { MainGame, Remaster, Remake, Port, Edition, Expansion, Bundle, Other }

public sealed record GameMetadataSnapshot(
    int SchemaVersion,
    string? Summary,
    DateOnly? FirstReleaseDate,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Developers,
    IReadOnlyList<string> Publishers,
    IReadOnlyList<string> Platforms,
    string? ProviderUrl,
    GameTypeTag Type,
    string? ArtworkSource = null)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record ProviderGameLink(string Provider, string ExternalId, DateTimeOffset RetrievedUtc)
{
    public const string Igdb = "igdb";
}

public sealed record ArtworkHints(string Title, string? SteamAppId, string? IgdbCoverImageId);

public sealed record ProviderGameRecord(
    string ExternalId, string Title, GameMetadataSnapshot Snapshot, ArtworkHints Hints);

public sealed record ProviderSearchResult(
    string ExternalId,
    string Title,
    int? ReleaseYear,
    IReadOnlyList<string> Platforms,
    GameTypeTag Type,
    string? ThumbnailUrl);
```

`GameMetadataNormalizer.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace DesktopGuides.Core.Providers;

public static class GameMetadataNormalizer
{
    public const int TitleLimit = 160;
    public const int SummaryLimit = 4000;
    public const int ListCountLimit = 16;
    public const int ListItemLimit = 80;

    public static string NormalizeExternalId(string? raw)
    {
        string value = raw?.Trim() ?? "";
        if (value.Length is < 1 or > 20 || value[0] == '0' || !value.All(char.IsAsciiDigit))
        {
            throw Malformed("The provider returned an invalid game ID.");
        }
        return value;
    }

    public static string NormalizeTitle(string? raw) =>
        NormalizeText(raw, TitleLimit, keepNewlines: false) ??
        throw Malformed("The provider returned a game without a title.");

    public static string? NormalizeText(string? raw, int max) =>
        NormalizeText(raw, max, keepNewlines: true);

    public static IReadOnlyList<string> NormalizeList(IEnumerable<string?>? raw)
    {
        List<string> items = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string? item in raw ?? [])
        {
            string? value = NormalizeText(item, ListItemLimit, keepNewlines: false);
            if (value is not null && seen.Add(value))
            {
                items.Add(value);
                if (items.Count == ListCountLimit) break;
            }
        }
        return items;
    }

    public static GameTypeTag MapType(string? igdbType, bool hasVersionParent)
    {
        if (hasVersionParent) return GameTypeTag.Edition;
        return igdbType?.Trim().ToLowerInvariant() switch
        {
            "main game" => GameTypeTag.MainGame,
            "remaster" => GameTypeTag.Remaster,
            "remake" => GameTypeTag.Remake,
            "port" => GameTypeTag.Port,
            "expanded game" => GameTypeTag.Edition,
            "expansion" or "standalone expansion" or "dlc" or "dlc addon" => GameTypeTag.Expansion,
            "bundle" or "pack / addon" or "pack" => GameTypeTag.Bundle,
            _ => GameTypeTag.Other
        };
    }

    public static string? NormalizeProviderUrl(string? raw) =>
        Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.IdnHost == "www.igdb.com" &&
        uri.IsDefaultPort &&
        uri.AbsolutePath.StartsWith("/games/", StringComparison.Ordinal)
            ? uri.AbsoluteUri
            : null;

    private static string? NormalizeText(string? raw, int max, bool keepNewlines)
    {
        if (raw is null) return null;
        StringBuilder text = new(Math.Min(raw.Length, max));
        foreach (char c in raw.Replace("\r\n", "\n"))
        {
            UnicodeCategory category = char.GetUnicodeCategory(c);
            if (c == '\n' && keepNewlines) text.Append(c);
            else if (c is '\n' or '\t') text.Append(' ');
            else if (category is not (UnicodeCategory.Control or UnicodeCategory.Format)) text.Append(c);
        }
        string value = text.ToString().Trim();
        if (value.Length > max) value = value[..max].TrimEnd();
        return value.Length == 0 ? null : value;
    }

    private static ProviderException Malformed(string message) =>
        new(ProviderErrorKind.MalformedData, message);
}
```

  Note: `TrimEnd` after truncating can leave fewer than `max`
  characters. The tests use filler with no trailing spaces, so they still
  see exactly `max`.

  In `LibraryModels.cs`, add `using DesktopGuides.Core.Providers;` and
  replace the `Game` record:

```csharp
public sealed record Game(
    Guid Id,
    string Title,
    string? Platform,
    string? Notes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    ProviderGameLink? Link = null,
    GameMetadataSnapshot? Metadata = null,
    string? ArtworkRelativePath = null);
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/DesktopGuides.Core.Tests/DesktopGuides.Core.Tests.csproj -c Release`
Expected: PASS for the whole project, 0 failed.

- [ ] **Step 5: Commit** (after message approval)

```bash
git add src/DesktopGuides.Core tests/DesktopGuides.Core.Tests
git commit -m "feat(core): add provider snapshot model and normalizer"
```

### Task 3: Schema v3 migration

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Storage/LibrarySchema.cs`,
  `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs`
  (the `VersionOneSchema` and `VersionTwoSchema` fields,
  `BuildExpectedSchema`, and the `expectedVersion` switch in
  `ValidateDatabase`)
- Test: `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs`

**Interfaces:**
- Produces: `LibrarySchema.CurrentVersion == 3`, `LibrarySchema.Version3`,
  and the `Games` columns `ProviderName`, `ProviderGameId`,
  `MetadataJson`, `MetadataRetrievedUtcMs` and `ArtworkRelativePath`,
  plus the index `UX_Games_Provider`.

- [ ] **Step 1: Write the failing tests.** In
  `SqliteLibraryRepositoryTests.cs`, change the two `user_version`
  assertions in `PersistsTwoIndependentGuideStatesAndSettingsAcrossReopen`
  and `UpgradesPopulatedVersionOneWithConsistentRecoveryCopy` from `2L` to
  `3L`. Then add these tests and the helpers:

```csharp
    [Fact]
    public async Task UpgradesPopulatedVersionTwoAndKeepsGames()
    {
        using TestLibrary directory = new();
        Guid gameId = Guid.NewGuid();
        CreatePopulatedVersionTwo(directory, gameId);

        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();

        Game game = Assert.Single(await repository.ListGamesAsync());
        Assert.Equal(gameId, game.Id);
        Assert.Null(game.Link);
        Assert.Equal(3L, ReadUserVersion(directory.Paths.DatabasePath));
        Assert.Single(Directory.GetFiles(directory.Paths.RecoveryRoot, "*.sqlite"));
    }

    [Fact]
    public async Task FailedVersionThreeMigrationStaysAtVersionTwo()
    {
        using TestLibrary directory = new();
        Guid gameId = Guid.NewGuid();
        CreatePopulatedVersionTwo(directory, gameId);

        await using (SqliteLibraryRepository failing = new(directory.Paths, null, version =>
        {
            if (version == 3) throw new IOException("Injected after v3 columns were added.");
        }))
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => failing.InitializeAsync());
        }

        Assert.Equal(2L, ReadUserVersion(directory.Paths.DatabasePath));
        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand columns = connection.CreateCommand();
        columns.CommandText = "SELECT count(*) FROM pragma_table_info('Games') WHERE name = 'ProviderName'";
        Assert.Equal(0L, (long)columns.ExecuteScalar()!);
    }

    [Fact]
    public async Task ManyUnlinkedGamesAreAllowedButADuplicateLinkIsRejected()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);

        InsertRawGame(connection, Guid.NewGuid(), null, null, null);
        InsertRawGame(connection, Guid.NewGuid(), null, null, null);
        InsertRawGame(connection, Guid.NewGuid(), "igdb", "1942", null);
        SqliteException duplicate = Assert.Throws<SqliteException>(
            () => InsertRawGame(connection, Guid.NewGuid(), "igdb", "1942", null));
        Assert.Equal(2067, duplicate.SqliteExtendedErrorCode);
    }

    [Theory]
    [InlineData("steam", "1942", null)]
    [InlineData("igdb", null, null)]
    [InlineData(null, "1942", null)]
    [InlineData("igdb", "123456789012345678901", null)]
    [InlineData(null, null, "artwork/other/a.png")]
    [InlineData(null, null, "content/a.png")]
    public async Task ProviderPairingAndArtworkPathChecksRejectBadRows(
        string? provider, string? externalId, string? artwork)
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);

        SqliteException error = Assert.Throws<SqliteException>(
            () => InsertRawGame(connection, Guid.NewGuid(), provider, externalId, artwork));
        Assert.Equal(275, error.SqliteExtendedErrorCode); // SQLITE_CONSTRAINT_CHECK
    }

    [Fact]
    public async Task ArtworkPathUnderTheGamesOwnFolderIsAccepted()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
        Guid id = Guid.NewGuid();

        InsertRawGame(connection, id, "igdb", "7", $"artwork/{id:N}/{new string('a', 64)}.png");
    }

    private static void CreatePopulatedVersionTwo(TestLibrary directory, Guid gameId)
    {
        directory.Paths.EnsureCreated();
        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL";
        command.ExecuteNonQuery();
        command.CommandText = LibrarySchema.Version1;
        command.ExecuteNonQuery();
        command.CommandText = LibrarySchema.Version2;
        command.ExecuteNonQuery();
        command.CommandText = """
            INSERT INTO Games (Id, Title, CreatedUtcMs, UpdatedUtcMs)
            VALUES ($id, 'Version two game', $now, $now)
            """;
        command.Parameters.AddWithValue("$id", gameId.ToString("N"));
        command.Parameters.AddWithValue("$now", Now.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }

    private static void InsertRawGame(
        SqliteConnection connection, Guid id, string? provider, string? externalId, string? artwork)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Games (Id, Title, CreatedUtcMs, UpdatedUtcMs,
                ProviderName, ProviderGameId, ArtworkRelativePath)
            VALUES ($id, 'Raw', 0, 0, $provider, $externalId, $artwork)
            """;
        command.Parameters.AddWithValue("$id", id.ToString("N"));
        command.Parameters.AddWithValue("$provider", (object?)provider ?? DBNull.Value);
        command.Parameters.AddWithValue("$externalId", (object?)externalId ?? DBNull.Value);
        command.Parameters.AddWithValue("$artwork", (object?)artwork ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static long ReadUserVersion(string databasePath)
    {
        using SqliteConnection connection = OpenWithForeignKeys(databasePath);
        using SqliteCommand version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        return (long)version.ExecuteScalar()!;
    }
```

  If a `ReadUserVersion`-style helper already exists in the file, reuse
  it instead of adding a second one.

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test tests/DesktopGuides.Infrastructure.Tests/DesktopGuides.Infrastructure.Tests.csproj -c Release --filter FullyQualifiedName~SqliteLibraryRepositoryTests`
Expected: FAIL. The new tests fail on the missing columns
(`table Games has no column named ProviderName`), and the two edited
tests fail with `Expected: 3, Actual: 2`.

- [ ] **Step 3: Implement.** In `LibrarySchema.cs`, set
  `CurrentVersion = 3`, add the constant below, and append `(3, Version3)`
  to `Migrations`:

```csharp
    public const string Version3 = """
        ALTER TABLE Games ADD COLUMN ProviderName TEXT
            CHECK (ProviderName IS NULL OR ProviderName = 'igdb');
        ALTER TABLE Games ADD COLUMN ProviderGameId TEXT
            CHECK ((ProviderName IS NULL) = (ProviderGameId IS NULL) AND
                   (ProviderGameId IS NULL OR length(ProviderGameId) BETWEEN 1 AND 20));
        ALTER TABLE Games ADD COLUMN MetadataJson TEXT
            CHECK (MetadataJson IS NULL OR length(MetadataJson) <= 65536);
        ALTER TABLE Games ADD COLUMN MetadataRetrievedUtcMs INTEGER
            CHECK (MetadataRetrievedUtcMs IS NULL OR MetadataRetrievedUtcMs >= 0);
        ALTER TABLE Games ADD COLUMN ArtworkRelativePath TEXT
            CHECK (ArtworkRelativePath IS NULL OR
                   ArtworkRelativePath LIKE 'artwork/' || Id || '/%');
        CREATE UNIQUE INDEX UX_Games_Provider
            ON Games(ProviderName, ProviderGameId) WHERE ProviderName IS NOT NULL;

        PRAGMA user_version = 3;
        """;
```

  In `SqliteLibraryRepository.cs`, replace the two `Lazy` fields with a
  single cache:

```csharp
    private static readonly Lazy<IReadOnlyDictionary<int, IReadOnlyList<SchemaObject>>> ExpectedSchemas =
        new(() => LibrarySchema.Migrations.ToDictionary(
            migration => migration.Version,
            migration => BuildExpectedSchema(migration.Version)));
```

  Replace the `expectedVersion` switch in `ValidateDatabase` with:

```csharp
        if (!ExpectedSchemas.Value.TryGetValue(expectedVersion, out IReadOnlyList<SchemaObject>? expectedSchema))
        {
            throw new InvalidDataException("Unsupported library schema version.");
        }
```

  Replace the body of `BuildExpectedSchema` after `reference.Open();`
  with:

```csharp
        using SqliteCommand migration = reference.CreateCommand();
        foreach ((int nextVersion, string sql) in LibrarySchema.Migrations)
        {
            if (nextVersion > version) break;
            migration.CommandText = sql;
            migration.ExecuteNonQuery();
        }
        return ReadSchema(reference, null);
```

  `ALTER TABLE ... ADD COLUMN` rewrites the stored `CREATE TABLE` text.
  The reference build runs the same statements, so the text still
  matches.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/DesktopGuides.Infrastructure.Tests/DesktopGuides.Infrastructure.Tests.csproj -c Release`
Expected: PASS, 0 failed. If the check-constraint test sees extended code
`19` rather than `275`, the SQLite build is not reporting extended codes.
In that case assert `SqliteErrorCode == 19` and message contains
`CHECK constraint failed`, and record a ruling.

- [ ] **Step 5: Commit** (after message approval)

```bash
git add src/DesktopGuides.Infrastructure/Storage tests/DesktopGuides.Infrastructure.Tests
git commit -m "feat(storage): migrate library to schema v3 with provider link columns"
```

### Task 4: Repository linked-game methods and metadata JSON

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Storage/GameMetadataJson.cs`
- Modify: `src/DesktopGuides.Core/Library/ILibraryRepository.cs`,
  `src/DesktopGuides.Core/Library/LibraryModels.cs`,
  `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs`
  (the three `SELECT Id, Title, …` statements, `ReadGame`, and the new
  methods)
- Test: `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs`,
  `tests/DesktopGuides.Infrastructure.Tests/GameMetadataJsonTests.cs`

**Interfaces:**
- Consumes (Task 2): `GameMetadataSnapshot`, `ProviderGameLink`, and
  `Game(..., Link, Metadata, ArtworkRelativePath)`.
- Produces (in `DesktopGuides.Core.Library`):
  - `record NewLinkedGame(Guid Id, string Title, string? Platform, ProviderGameLink Link, GameMetadataSnapshot Metadata, string? ArtworkRelativePath)`
  - `sealed class DuplicateProviderLinkException(Guid existingGameId) : Exception` with `Guid ExistingGameId`
  - On `ILibraryRepository`:
    - `Task<Game?> FindLinkedGameAsync(string provider, string externalId, CancellationToken token = default)`
    - `Task<Game> AddLinkedGameAsync(NewLinkedGame game, CancellationToken token = default)`, which throws `DuplicateProviderLinkException`
    - `Task<Game> UpdateGameMetadataAsync(Guid gameId, GameMetadataSnapshot metadata, DateTimeOffset retrievedUtc, string? artworkRelativePath, CancellationToken token = default)`, which throws `KeyNotFoundException` for a missing game and `InvalidOperationException` for an unlinked game
  - `internal static class GameMetadataJson` with `string Serialize(GameMetadataSnapshot)` (throws `ArgumentException` over 65,536 characters) and `GameMetadataSnapshot? TryParse(string? json)`

- [ ] **Step 1: Write the failing JSON tests** (`GameMetadataJsonTests.cs`)

```csharp
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class GameMetadataJsonTests
{
    internal static GameMetadataSnapshot Sample() => new(
        GameMetadataSnapshot.CurrentSchemaVersion, "A summary.", new DateOnly(1998, 11, 19),
        ["Shooter"], ["Valve"], ["Sierra"], ["PC (Microsoft Windows)"],
        "https://www.igdb.com/games/half-life", GameTypeTag.MainGame);

    [Fact]
    public void RoundTripsEveryField()
    {
        GameMetadataSnapshot parsed = GameMetadataJson.TryParse(GameMetadataJson.Serialize(Sample()))!;
        Assert.Equal(Sample() with { Genres = parsed.Genres, Developers = parsed.Developers,
            Publishers = parsed.Publishers, Platforms = parsed.Platforms }, parsed);
        Assert.Equal(["Shooter"], parsed.Genres);
        Assert.Equal(["PC (Microsoft Windows)"], parsed.Platforms);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"schemaVersion":2,"genres":[],"developers":[],"publishers":[],"platforms":[],"type":"MainGame"}""")]
    [InlineData("""{"schemaVersion":1,"genres":null,"developers":[],"publishers":[],"platforms":[],"type":"MainGame"}""")]
    [InlineData("""{"schemaVersion":1,"genres":[],"developers":[],"publishers":[],"platforms":[],"type":"Nope"}""")]
    public void CorruptOrFutureJsonParsesAsNull(string? json) =>
        Assert.Null(GameMetadataJson.TryParse(json));

    [Fact]
    public void OversizedSnapshotIsRejectedOnSerialize() =>
        Assert.Throws<ArgumentException>(() => GameMetadataJson.Serialize(
            Sample() with { Summary = new string('', 20000) }));
}
```

  The oversize test relies on `` being escaped to 6 characters, so
  20,000 of them exceed 65,536. The normalizer would strip that
  character, but the serializer must enforce the limit on its own.

- [ ] **Step 2: Write the failing repository tests** (append to
  `SqliteLibraryRepositoryTests`)

```csharp
    private static NewLinkedGame Linked(Guid id, string externalId = "70", string? artwork = null) => new(
        id, "Half-Life", "PC (Microsoft Windows)",
        new ProviderGameLink(ProviderGameLink.Igdb, externalId, Now),
        GameMetadataJsonTests.Sample(), artwork);

    [Fact]
    public async Task AddsFindsAndReopensALinkedGame()
    {
        using TestLibrary directory = new();
        Guid id = Guid.NewGuid();
        string artwork = $"artwork/{id:N}/{new string('b', 64)}.jpg";
        await using (SqliteLibraryRepository repository = new(directory.Paths, new FixedTimeProvider(Now)))
        {
            await repository.InitializeAsync();
            Game added = await repository.AddLinkedGameAsync(Linked(id, artwork: artwork));
            Assert.Equal(id, added.Id);
            Assert.Equal(new ProviderGameLink("igdb", "70", Now), added.Link);
        }

        await using SqliteLibraryRepository reopened = new(directory.Paths);
        await reopened.InitializeAsync();
        Game found = (await reopened.FindLinkedGameAsync("igdb", "70"))!;
        Assert.Equal(id, found.Id);
        Assert.Equal("Half-Life", found.Title);
        Assert.Equal("A summary.", found.Metadata!.Summary);
        Assert.Equal(artwork, found.ArtworkRelativePath);
        Assert.Null(await reopened.FindLinkedGameAsync("igdb", "71"));
    }

    [Fact]
    public async Task DuplicateLinkReportsTheExistingGame()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game first = await repository.AddLinkedGameAsync(Linked(Guid.NewGuid()));

        DuplicateProviderLinkException error = await Assert.ThrowsAsync<DuplicateProviderLinkException>(
            () => repository.AddLinkedGameAsync(Linked(Guid.NewGuid())));
        Assert.Equal(first.Id, error.ExistingGameId);
        Assert.Single(await repository.ListGamesAsync());
    }

    [Fact]
    public async Task LinkIsFreeAgainAfterGameRowIsDeleted()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game first = await repository.AddLinkedGameAsync(Linked(Guid.NewGuid()));
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        using (SqliteCommand delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM Games WHERE Id = $id";
            delete.Parameters.AddWithValue("$id", first.Id.ToString("N"));
            delete.ExecuteNonQuery();
        }

        Game second = await repository.AddLinkedGameAsync(Linked(Guid.NewGuid()));
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task UpdateMetadataKeepsLocalFieldsAndReplacesSnapshotAndArtwork()
    {
        using TestLibrary directory = new();
        AdjustableTimeProvider clock = new(Now);
        await using SqliteLibraryRepository repository = new(directory.Paths, clock);
        await repository.InitializeAsync();
        Guid id = Guid.NewGuid();
        await repository.AddLinkedGameAsync(Linked(id));
        await repository.UpdateGameAsync(id, "My title", "Steam Deck", "My notes");
        clock.Now = Now.AddDays(1);
        string artwork = $"artwork/{id:N}/{new string('c', 64)}.png";

        Game updated = await repository.UpdateGameMetadataAsync(
            id, GameMetadataJsonTests.Sample() with { Summary = "New." }, Now.AddDays(1), artwork);

        Assert.Equal(("My title", "Steam Deck", "My notes"), (updated.Title, updated.Platform, updated.Notes));
        Assert.Equal("New.", updated.Metadata!.Summary);
        Assert.Equal(Now.AddDays(1), updated.Link!.RetrievedUtc);
        Assert.Equal(artwork, updated.ArtworkRelativePath);
        Assert.Equal(Now.AddDays(1), updated.UpdatedUtc);
    }

    [Fact]
    public async Task UpdateMetadataRejectsMissingAndUnlinkedGames()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game manual = await repository.AddGameAsync("Manual", null, null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => repository.UpdateGameMetadataAsync(
            Guid.NewGuid(), GameMetadataJsonTests.Sample(), Now, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpdateGameMetadataAsync(
            manual.Id, GameMetadataJsonTests.Sample(), Now, null));
    }

    [Fact]
    public async Task CorruptMetadataJsonLoadsGameWithoutMetadata()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Guid id = Guid.NewGuid();
        await repository.AddLinkedGameAsync(Linked(id));
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        using (SqliteCommand corrupt = connection.CreateCommand())
        {
            corrupt.CommandText = "UPDATE Games SET MetadataJson = '{\"schemaVersion\":9}'";
            corrupt.ExecuteNonQuery();
        }

        Game game = Assert.Single(await repository.ListGamesAsync());
        Assert.Null(game.Metadata);
        Assert.NotNull(game.Link);
        Assert.Equal(id, (await repository.GetGameAsync(id))!.Id);
    }
```

  Add `using DesktopGuides.Core.Providers;` to the test file.

- [ ] **Step 3: Run the tests and watch them fail**

Run: `dotnet test tests/DesktopGuides.Infrastructure.Tests/DesktopGuides.Infrastructure.Tests.csproj -c Release`
Expected: build FAIL, `'GameMetadataJson' does not exist` and
`'ILibraryRepository' does not contain a definition for 'AddLinkedGameAsync'`.

- [ ] **Step 4: Implement the Core types.** Append to `LibraryModels.cs`:

```csharp
public sealed record NewLinkedGame(
    Guid Id,
    string Title,
    string? Platform,
    ProviderGameLink Link,
    GameMetadataSnapshot Metadata,
    string? ArtworkRelativePath);

public sealed class DuplicateProviderLinkException(Guid existingGameId)
    : Exception("This game is already in the library.")
{
    public Guid ExistingGameId { get; } = existingGameId;
}
```

  Add to `ILibraryRepository`:

```csharp
    Task<Game?> FindLinkedGameAsync(
        string provider, string externalId, CancellationToken token = default);
    Task<Game> AddLinkedGameAsync(NewLinkedGame game, CancellationToken token = default);
    Task<Game> UpdateGameMetadataAsync(
        Guid gameId, GameMetadataSnapshot metadata, DateTimeOffset retrievedUtc,
        string? artworkRelativePath, CancellationToken token = default);
```

- [ ] **Step 5: Implement `GameMetadataJson.cs`**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Infrastructure.Storage;

internal static class GameMetadataJson
{
    public const int MaxLength = 65536;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static string Serialize(GameMetadataSnapshot snapshot)
    {
        string json = JsonSerializer.Serialize(snapshot, Options);
        if (json.Length > MaxLength)
        {
            throw new ArgumentException("The metadata snapshot is too large to store.", nameof(snapshot));
        }
        return json;
    }

    public static GameMetadataSnapshot? TryParse(string? json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > MaxLength) return null;
        try
        {
            GameMetadataSnapshot? value = JsonSerializer.Deserialize<GameMetadataSnapshot>(json, Options);
            return value is { SchemaVersion: GameMetadataSnapshot.CurrentSchemaVersion } &&
                value.Genres is not null && value.Developers is not null &&
                value.Publishers is not null && value.Platforms is not null &&
                Enum.IsDefined(value.Type) && value.ArtworkSource is null or "SteamGridDB" or "IGDB"
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 6: Implement the repository changes.** Add a column-list
  constant and use it in all three game `SELECT` statements (`SELECT
  {GameColumns} FROM Games ...`, using `$$"""` raw strings or string
  concatenation):

```csharp
    private const string GameColumns = """
        Id, Title, Platform, Notes, CreatedUtcMs, UpdatedUtcMs,
        ProviderName, ProviderGameId, MetadataJson, MetadataRetrievedUtcMs, ArtworkRelativePath
        """;
```

  Replace `ReadGame`:

```csharp
    private static Game ReadGame(SqliteDataReader reader)
    {
        string? provider = NullableString(reader, 6);
        ProviderGameLink? link = provider is null
            ? null
            : new ProviderGameLink(
                provider,
                reader.GetString(7),
                FromUnixMilliseconds(reader.IsDBNull(9) ? 0 : reader.GetInt64(9)));
        return new Game(
            Guid.ParseExact(reader.GetString(0), "N"),
            reader.GetString(1),
            NullableString(reader, 2),
            NullableString(reader, 3),
            FromUnixMilliseconds(reader.GetInt64(4)),
            FromUnixMilliseconds(reader.GetInt64(5)),
            link,
            GameMetadataJson.TryParse(NullableString(reader, 8)),
            NullableString(reader, 10));
    }
```

  Add the three methods next to `UpdateGameAsync`:

```csharp
    public Task<Game?> FindLinkedGameAsync(
        string provider, string externalId, CancellationToken token = default) =>
        ReadAsync<Game?>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            return FindLinkedGame(connection, null, provider, externalId);
        }, token);

    public Task<Game> AddLinkedGameAsync(NewLinkedGame game, CancellationToken token = default)
    {
        GameDetails details = GameDetails.Create(game.Title, game.Platform, null);
        string metadataJson = GameMetadataJson.Serialize(game.Metadata);
        return WriteAsync(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            if (FindLinkedGame(connection, transaction, game.Link.Provider, game.Link.ExternalId)
                is { } existing)
            {
                throw new DuplicateProviderLinkException(existing.Id);
            }
            DateTimeOffset now = clock.GetUtcNow();
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Games (
                    Id, Title, Platform, Notes, CreatedUtcMs, UpdatedUtcMs,
                    ProviderName, ProviderGameId, MetadataJson, MetadataRetrievedUtcMs,
                    ArtworkRelativePath
                ) VALUES ($id, $title, $platform, NULL, $now, $now,
                    $provider, $externalId, $json, $retrieved, $artwork)
                """;
            command.Parameters.AddWithValue("$id", game.Id.ToString("N"));
            command.Parameters.AddWithValue("$title", details.Title);
            command.Parameters.AddWithValue("$platform", (object?)details.Platform ?? DBNull.Value);
            command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$provider", game.Link.Provider);
            command.Parameters.AddWithValue("$externalId", game.Link.ExternalId);
            command.Parameters.AddWithValue("$json", metadataJson);
            command.Parameters.AddWithValue("$retrieved", game.Link.RetrievedUtc.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$artwork", (object?)game.ArtworkRelativePath ?? DBNull.Value);
            command.ExecuteNonQuery();
            transaction.Commit();
            return new Game(game.Id, details.Title, details.Platform, null, now, now,
                game.Link, game.Metadata, game.ArtworkRelativePath);
        }, token);
    }

    public Task<Game> UpdateGameMetadataAsync(
        Guid gameId, GameMetadataSnapshot metadata, DateTimeOffset retrievedUtc,
        string? artworkRelativePath, CancellationToken token = default)
    {
        string metadataJson = GameMetadataJson.Serialize(metadata);
        return WriteAsync(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            Game current = GetGame(connection, transaction, gameId) ??
                throw new KeyNotFoundException("The game no longer exists.");
            if (current.Link is null)
            {
                throw new InvalidOperationException("Only linked games have provider metadata.");
            }
            DateTimeOffset now = clock.GetUtcNow();
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE Games SET MetadataJson = $json, MetadataRetrievedUtcMs = $retrieved,
                    ArtworkRelativePath = $artwork, UpdatedUtcMs = $now
                WHERE Id = $id
                """;
            command.Parameters.AddWithValue("$id", gameId.ToString("N"));
            command.Parameters.AddWithValue("$json", metadataJson);
            command.Parameters.AddWithValue("$retrieved", retrievedUtc.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$artwork", (object?)artworkRelativePath ?? DBNull.Value);
            command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
            command.ExecuteNonQuery();
            transaction.Commit();
            return current with
            {
                Metadata = metadata,
                Link = current.Link with { RetrievedUtc = retrievedUtc },
                ArtworkRelativePath = artworkRelativePath,
                UpdatedUtc = now
            };
        }, token);
    }

    private static Game? FindLinkedGame(
        SqliteConnection connection, SqliteTransaction? transaction, string provider, string externalId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {GameColumns} FROM Games
            WHERE ProviderName = $provider AND ProviderGameId = $externalId
            """;
        command.Parameters.AddWithValue("$provider", provider);
        command.Parameters.AddWithValue("$externalId", externalId);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadGame(reader) : null;
    }
```

  Extract the lookup in `GetGameAsync` into the helper below, and have
  `GetGameAsync` call it inside its `ReadAsync<Game?>`:

```csharp
    private static Game? GetGame(
        SqliteConnection connection, SqliteTransaction? transaction, Guid gameId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {GameColumns} FROM Games WHERE Id = $id";
        command.Parameters.AddWithValue("$id", gameId.ToString("N"));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadGame(reader) : null;
    }
```

  Also catch a unique-index race from another process. Wrap
  `command.ExecuteNonQuery()` in `AddLinkedGameAsync`:

```csharp
            try { command.ExecuteNonQuery(); }
            catch (SqliteException error) when (error.SqliteExtendedErrorCode == 2067)
            {
                transaction.Rollback();
                Guid existingId = FindLinkedGame(connection, null, game.Link.Provider, game.Link.ExternalId)?.Id
                    ?? throw new InvalidDataException("The provider link conflicted but no game holds it.", error);
                throw new DuplicateProviderLinkException(existingId);
            }
```

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/DesktopGuides.Infrastructure.Tests/DesktopGuides.Infrastructure.Tests.csproj -c Release`
Expected: PASS, 0 failed.

- [ ] **Step 8: Commit** (after message approval)

```bash
git add src/DesktopGuides.Core src/DesktopGuides.Infrastructure tests/DesktopGuides.Infrastructure.Tests
git commit -m "feat(storage): store linked games with metadata snapshots"
```

### Task 5: Artwork roots, validator, store and startup sweep

**Files:**
- Create: `src/DesktopGuides.Core/Providers/ProviderContracts.cs`
  (`IArtworkStore` and `StoredArtwork`; Tasks 6 and 8 add more to this file),
  `src/DesktopGuides.Infrastructure/Artwork/ArtworkValidator.cs`,
  `src/DesktopGuides.Infrastructure/Artwork/ManagedArtworkStore.cs`
- Modify: `src/DesktopGuides.Core/Paths/ILibraryPaths.cs`,
  `src/DesktopGuides.Infrastructure/Storage/ManagedPathResolver.cs`,
  `src/DesktopGuides.Core/Library/LibraryModels.cs`
  (`StartupReconciliationReport`), and
  `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs`
  (`Initialize`)
- Test: `tests/DesktopGuides.Infrastructure.Tests/Artwork/ArtworkValidatorTests.cs`,
  `tests/DesktopGuides.Infrastructure.Tests/Artwork/ManagedArtworkStoreTests.cs`,
  `tests/DesktopGuides.Infrastructure.Tests/Artwork/TestImages.cs`,
  `tests/DesktopGuides.Infrastructure.Tests/ManagedPathResolverTests.cs`,
  `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs`

**Interfaces:**
- Consumes (Task 4): `AddLinkedGameAsync` and `NewLinkedGame`.
- Produces:
  - `ILibraryPaths.ArtworkRoot` (`library/artwork`) and
    `ILibraryPaths.ArtworkStagingRoot` (`library/.artwork-staging`), both
    created by `EnsureCreated`
  - `record StoredArtwork(string RelativePath, string Sha256)`
  - `interface IArtworkStore` with:
    - `Task<StoredArtwork> StoreAsync(Guid gameId, byte[] content, CancellationToken token)`, which throws `InvalidDataException` for invalid images
    - `void Delete(string relativePath)` (best effort, never throws)
    - `string? ResolveFile(string relativePath)`
  - `internal static class ArtworkValidator` with `ArtworkInfo Validate(ReadOnlySpan<byte> data)`, returning `record ArtworkInfo(string Extension, int Width, int Height)` and exposing `const int MaxBytes = 5 * 1024 * 1024` and `const int MaxDimension = 4096`
  - `public sealed class ManagedArtworkStore(ILibraryPaths paths) : IArtworkStore`, plus `internal int Sweep(IReadOnlySet<string> referenced)`
  - `StartupReconciliationReport(int ResolvedOperationCount, int ReviewOrphanCount, int ArtworkReviewCount = 0)`

- [ ] **Step 1: Write the test image builders** (`Artwork/TestImages.cs`)

```csharp
using System.Buffers.Binary;

namespace DesktopGuides.Infrastructure.Tests.Artwork;

internal static class TestImages
{
    public static byte[] Png(int width, int height)
    {
        byte[] data = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }
            .CopyTo(data, 0);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), height);
        return data;
    }

    public static byte[] Jpeg(int width, int height)
    {
        List<byte> data = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
        data.AddRange(new byte[14]);
        data.AddRange([0xFF, 0xC4, 0x00, 0x04, 0x00, 0x00]); // DHT is not a frame header
        data.AddRange([0xFF, 0xC2, 0x00, 0x11, 0x08,
            (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width]);
        data.AddRange(new byte[12]);
        return [.. data];
    }

    public static byte[] WebPExtended(int width, int height)
    {
        byte[] data = Riff("VP8X", 30);
        WriteUInt24(data, 24, width - 1);
        WriteUInt24(data, 27, height - 1);
        return data;
    }

    public static byte[] WebPLossless(int width, int height)
    {
        byte[] data = Riff("VP8L", 30);
        data[20] = 0x2F;
        uint bits = (uint)(width - 1) | ((uint)(height - 1) << 14);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(21), bits);
        return data;
    }

    public static byte[] WebPLossy(int width, int height)
    {
        byte[] data = Riff("VP8 ", 30);
        data[23] = 0x9D; data[24] = 0x01; data[25] = 0x2A;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(26), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(28), (ushort)height);
        return data;
    }

    private static byte[] Riff(string chunk, int length)
    {
        byte[] data = new byte[length];
        "RIFF"u8.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), length - 8);
        "WEBP"u8.CopyTo(data.AsSpan(8));
        System.Text.Encoding.ASCII.GetBytes(chunk).CopyTo(data, 12);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), length - 20);
        return data;
    }

    private static void WriteUInt24(byte[] data, int offset, int value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
    }
}
```

- [ ] **Step 2: Write the failing validator tests**

```csharp
using DesktopGuides.Infrastructure.Artwork;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Artwork;

public sealed class ArtworkValidatorTests
{
    public static TheoryData<byte[], string, int, int> Valid => new()
    {
        { TestImages.Png(600, 900), "png", 600, 900 },
        { TestImages.Jpeg(264, 352), "jpg", 264, 352 },
        { TestImages.WebPExtended(600, 900), "webp", 600, 900 },
        { TestImages.WebPLossless(512, 768), "webp", 512, 768 },
        { TestImages.WebPLossy(300, 450), "webp", 300, 450 },
        { TestImages.Png(4096, 4096), "png", 4096, 4096 },
    };

    [Theory, MemberData(nameof(Valid))]
    public void AcceptsSupportedHeaders(byte[] data, string extension, int width, int height) =>
        Assert.Equal(new ArtworkInfo(extension, width, height), ArtworkValidator.Validate(data));

    public static TheoryData<byte[]> Invalid => new()
    {
        Array.Empty<byte>(),
        "GIF89a\u0001\u0000\u0001\u0000"u8.ToArray(),
        "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(),
        TestImages.Png(4097, 10),
        TestImages.Png(0, 10),
        TestImages.Jpeg(10, 5000),
        TestImages.WebPExtended(5000, 10),
        TestImages.Png(10, 10)[..20],
        TestImages.Jpeg(10, 10)[..12],
    };

    [Theory, MemberData(nameof(Invalid))]
    public void RejectsOtherFormatsTruncatedAndOversizedImages(byte[] data) =>
        Assert.Throws<InvalidDataException>(() => ArtworkValidator.Validate(data));

    [Fact]
    public void RejectsFilesOverFiveMegabytes()
    {
        byte[] data = new byte[ArtworkValidator.MaxBytes + 1];
        TestImages.Png(10, 10).CopyTo(data, 0);
        Assert.Throws<InvalidDataException>(() => ArtworkValidator.Validate(data));
    }
}
```

- [ ] **Step 3: Write the failing store, path and sweep tests**

`ManagedArtworkStoreTests.cs`:

```csharp
using DesktopGuides.Infrastructure.Artwork;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Artwork;

public sealed class ManagedArtworkStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "desktop-guides-art-" + Guid.NewGuid());
    private readonly ManagedPathResolver paths;
    private readonly ManagedArtworkStore store;

    public ManagedArtworkStoreTests()
    {
        paths = new ManagedPathResolver(root);
        paths.EnsureCreated();
        store = new ManagedArtworkStore(paths);
    }

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public async Task StoresUnderGameFolderByContentHashAndLeavesNoStagedFile()
    {
        Guid gameId = Guid.NewGuid();
        StoredArtwork stored = await store.StoreAsync(gameId, TestImages.Png(600, 900), default);

        Assert.Matches($"^artwork/{gameId:N}/[0-9a-f]{{64}}\\.png$", stored.RelativePath);
        Assert.EndsWith(stored.Sha256 + ".png", stored.RelativePath);
        Assert.True(File.Exists(store.ResolveFile(stored.RelativePath)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(paths.ArtworkStagingRoot));
    }

    [Fact]
    public async Task StoringIdenticalBytesTwiceReturnsTheSameFile()
    {
        Guid gameId = Guid.NewGuid();
        StoredArtwork first = await store.StoreAsync(gameId, TestImages.Jpeg(10, 10), default);
        DateTime written = File.GetLastWriteTimeUtc(store.ResolveFile(first.RelativePath)!);
        StoredArtwork second = await store.StoreAsync(gameId, TestImages.Jpeg(10, 10), default);

        Assert.Equal(first, second);
        Assert.Equal(written, File.GetLastWriteTimeUtc(store.ResolveFile(second.RelativePath)!));
    }

    [Fact]
    public async Task InvalidImageLeavesNoFile()
    {
        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.StoreAsync(Guid.NewGuid(), "not an image"u8.ToArray(), default));
        Assert.Empty(Directory.EnumerateFileSystemEntries(paths.ArtworkRoot));
        Assert.Empty(Directory.EnumerateFileSystemEntries(paths.ArtworkStagingRoot));
    }

    [Theory]
    [InlineData("../library.sqlite")]
    [InlineData("artwork/../../x.png")]
    [InlineData("content/x/y.png")]
    [InlineData("artwork/0123/abc.png")]
    public void ResolveAndDeleteIgnoreNonCanonicalPaths(string relativePath)
    {
        Assert.Null(store.ResolveFile(relativePath));
        store.Delete(relativePath);
    }

    [Fact]
    public async Task DeleteRemovesFileAndEmptyGameFolder()
    {
        Guid gameId = Guid.NewGuid();
        StoredArtwork stored = await store.StoreAsync(gameId, TestImages.Png(1, 1), default);
        store.Delete(stored.RelativePath);
        Assert.False(Directory.Exists(Path.Combine(paths.ArtworkRoot, gameId.ToString("N"))));
    }

    [Fact]
    public async Task SweepKeepsReferencedAndRemovesStagedAndUnreferencedFiles()
    {
        StoredArtwork kept = await store.StoreAsync(Guid.NewGuid(), TestImages.Png(2, 2), default);
        StoredArtwork orphan = await store.StoreAsync(Guid.NewGuid(), TestImages.Png(3, 3), default);
        File.WriteAllBytes(Path.Combine(paths.ArtworkStagingRoot, Guid.NewGuid().ToString("N") + ".tmp"), [1]);
        File.WriteAllBytes(Path.Combine(paths.ArtworkRoot, "readme.txt"), [1]);

        int review = store.Sweep(new HashSet<string> { kept.RelativePath });

        Assert.NotNull(store.ResolveFile(kept.RelativePath));
        Assert.Null(store.ResolveFile(orphan.RelativePath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(paths.ArtworkStagingRoot));
        Assert.True(File.Exists(Path.Combine(paths.ArtworkRoot, "readme.txt")));
        Assert.Equal(1, review);
    }
}
```

  In `ManagedPathResolverTests.cs`, add:

```csharp
    [Fact]
    public void ArtworkRootsSitBesideContentAndOutsideGuideStaging()
    {
        string root = Path.Combine(Path.GetTempPath(), "desktop-guides-paths-" + Guid.NewGuid());
        try
        {
            ManagedPathResolver paths = new(root);
            paths.EnsureCreated();
            Assert.Equal(Path.Combine(paths.LibraryRoot, "artwork"), paths.ArtworkRoot);
            Assert.Equal(Path.Combine(paths.LibraryRoot, ".artwork-staging"), paths.ArtworkStagingRoot);
            Assert.True(Directory.Exists(paths.ArtworkRoot));
            Assert.True(Directory.Exists(paths.ArtworkStagingRoot));
            Assert.False(paths.ArtworkStagingRoot.StartsWith(paths.StagingRoot + Path.DirectorySeparatorChar));
        }
        finally { Directory.Delete(root, true); }
    }
```

  In `SqliteLibraryRepositoryTests.cs`, add:

```csharp
    [Fact]
    public async Task SweepRemovesArtworkOfDeletedGame()
    {
        using TestLibrary directory = new();
        Guid id = Guid.NewGuid();
        StoredArtwork stored;
        await using (SqliteLibraryRepository repository = new(directory.Paths))
        {
            await repository.InitializeAsync();
            stored = await new ManagedArtworkStore(directory.Paths)
                .StoreAsync(id, Artwork.TestImages.Png(4, 4), default);
            await repository.AddLinkedGameAsync(Linked(id, artwork: stored.RelativePath));
            using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
            using SqliteCommand delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM Games";
            delete.ExecuteNonQuery();
        }

        await using SqliteLibraryRepository reopened = new(directory.Paths);
        await reopened.InitializeAsync();
        Assert.Null(new ManagedArtworkStore(directory.Paths).ResolveFile(stored.RelativePath));
        Assert.Equal(0, reopened.LastStartupReconciliation!.ReviewOrphanCount);
        Assert.Equal(0, reopened.LastStartupReconciliation.ArtworkReviewCount);
    }

    [Fact]
    public async Task StartupKeepsArtworkOfLinkedGame()
    {
        using TestLibrary directory = new();
        Guid id = Guid.NewGuid();
        StoredArtwork stored;
        await using (SqliteLibraryRepository repository = new(directory.Paths))
        {
            await repository.InitializeAsync();
            stored = await new ManagedArtworkStore(directory.Paths)
                .StoreAsync(id, Artwork.TestImages.Png(5, 5), default);
            await repository.AddLinkedGameAsync(Linked(id, artwork: stored.RelativePath));
        }

        await using SqliteLibraryRepository reopened = new(directory.Paths);
        await reopened.InitializeAsync();
        Assert.NotNull(new ManagedArtworkStore(directory.Paths).ResolveFile(stored.RelativePath));
    }
```

  Add `using DesktopGuides.Infrastructure.Artwork;` to the file.

- [ ] **Step 4: Run the tests and watch them fail**

Run: `dotnet test tests/DesktopGuides.Infrastructure.Tests/DesktopGuides.Infrastructure.Tests.csproj -c Release`
Expected: build FAIL, `The type or namespace name 'Artwork' does not exist`
and `'ManagedPathResolver' does not contain a definition for 'ArtworkRoot'`.

- [ ] **Step 5: Implement the paths and report.** Add
  `string ArtworkRoot { get; }` and `string ArtworkStagingRoot { get; }`
  to `ILibraryPaths`. In `ManagedPathResolver`, set
  `ArtworkRoot = Path.Combine(LibraryRoot, "artwork")` and
  `ArtworkStagingRoot = Path.Combine(LibraryRoot, ".artwork-staging")`,
  add both properties, and append both roots to the `EnsureCreated`
  array. Change the report to:

```csharp
public sealed record StartupReconciliationReport(
    int ResolvedOperationCount,
    int ReviewOrphanCount,
    int ArtworkReviewCount = 0);
```

- [ ] **Step 6: Implement `ProviderContracts.cs` (first part)**

```csharp
namespace DesktopGuides.Core.Providers;

public sealed record StoredArtwork(string RelativePath, string Sha256);

public interface IArtworkStore
{
    Task<StoredArtwork> StoreAsync(Guid gameId, byte[] content, CancellationToken token);
    void Delete(string relativePath);
    string? ResolveFile(string relativePath);
}
```

- [ ] **Step 7: Implement `ArtworkValidator.cs`**

```csharp
using System.Buffers.Binary;

namespace DesktopGuides.Infrastructure.Artwork;

internal sealed record ArtworkInfo(string Extension, int Width, int Height);

internal static class ArtworkValidator
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxDimension = 4096;

    public static ArtworkInfo Validate(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxBytes) throw Invalid("The cover image is larger than 5 MB.");
        ArtworkInfo info = ReadHeader(data) ?? throw Invalid("The cover image is not a PNG, JPEG or WebP file.");
        if (info.Width is < 1 or > MaxDimension || info.Height is < 1 or > MaxDimension)
        {
            throw Invalid("The cover image dimensions are out of range.");
        }
        return info;
    }

    private static ArtworkInfo? ReadHeader(ReadOnlySpan<byte> d)
    {
        if (d.Length >= 24 && d[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) &&
            d[12..16].SequenceEqual("IHDR"u8))
        {
            return new("png", BinaryPrimitives.ReadInt32BigEndian(d[16..]), BinaryPrimitives.ReadInt32BigEndian(d[20..]));
        }
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return ReadJpeg(d);
        if (d.Length >= 30 && d[..4].SequenceEqual("RIFF"u8) && d[8..12].SequenceEqual("WEBP"u8))
        {
            ReadOnlySpan<byte> chunk = d[12..16];
            if (chunk.SequenceEqual("VP8X"u8))
                return new("webp", ReadUInt24(d[24..]) + 1, ReadUInt24(d[27..]) + 1);
            if (chunk.SequenceEqual("VP8L"u8) && d[20] == 0x2F)
            {
                uint bits = BinaryPrimitives.ReadUInt32LittleEndian(d[21..]);
                return new("webp", (int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
            }
            if (chunk.SequenceEqual("VP8 "u8) && d[23] == 0x9D && d[24] == 0x01 && d[25] == 0x2A)
                return new("webp", BinaryPrimitives.ReadUInt16LittleEndian(d[26..]) & 0x3FFF,
                    BinaryPrimitives.ReadUInt16LittleEndian(d[28..]) & 0x3FFF);
        }
        return null;
    }

    private static ArtworkInfo? ReadJpeg(ReadOnlySpan<byte> d)
    {
        int position = 2;
        while (position + 9 <= d.Length)
        {
            if (d[position] != 0xFF) return null;
            byte marker = d[position + 1];
            if (marker == 0xFF) { position++; continue; }
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return new("jpg", BinaryPrimitives.ReadUInt16BigEndian(d[(position + 7)..]),
                    BinaryPrimitives.ReadUInt16BigEndian(d[(position + 5)..]));
            }
            int length = BinaryPrimitives.ReadUInt16BigEndian(d[(position + 2)..]);
            if (length < 2) return null;
            position += 2 + length;
        }
        return null;
    }

    private static int ReadUInt24(ReadOnlySpan<byte> d) => d[0] | (d[1] << 8) | (d[2] << 16);

    private static InvalidDataException Invalid(string message) => new(message);
}
```

- [ ] **Step 8: Implement `ManagedArtworkStore.cs`**

```csharp
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DesktopGuides.Core.Paths;
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Infrastructure.Artwork;

public sealed partial class ManagedArtworkStore(ILibraryPaths paths) : IArtworkStore
{
    [GeneratedRegex("^artwork/([0-9a-f]{32})/([0-9a-f]{64}\\.(?:png|jpg|webp))$")]
    private static partial Regex CanonicalPath();

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex GameFolder();

    [GeneratedRegex("^[0-9a-f]{64}\\.(?:png|jpg|webp)$")]
    private static partial Regex ArtworkFile();

    [GeneratedRegex("^[0-9a-f]{32}\\.tmp$")]
    private static partial Regex StagedFile();

    public async Task<StoredArtwork> StoreAsync(Guid gameId, byte[] content, CancellationToken token)
    {
        ArtworkInfo info = ArtworkValidator.Validate(content);
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        string relativePath = $"artwork/{gameId:N}/{sha256}.{info.Extension}";
        string target = ResolvePath(relativePath)!;
        string staged = Path.Combine(paths.ArtworkStagingRoot, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (FileStream stream = new(staged, FileMode.CreateNew, FileAccess.Write))
            {
                await stream.WriteAsync(content, token);
                stream.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            string folder = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(folder);
            if (File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException("The artwork folder is a link and was not used.");
            }
            if (!File.Exists(target)) File.Move(staged, target);
            return new StoredArtwork(relativePath, sha256);
        }
        finally
        {
            File.Delete(staged);
        }
    }

    public void Delete(string relativePath)
    {
        if (ResolvePath(relativePath) is not { } file) return;
        try
        {
            File.Delete(file);
            string folder = Path.GetDirectoryName(file)!;
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public string? ResolveFile(string relativePath) =>
        ResolvePath(relativePath) is { } file && File.Exists(file) ? file : null;

    internal int Sweep(IReadOnlySet<string> referenced)
    {
        int review = 0;
        foreach (string entry in Directory.EnumerateFileSystemEntries(paths.ArtworkStagingRoot))
        {
            if (StagedFile().IsMatch(Path.GetFileName(entry)) && File.Exists(entry)) TryDelete(entry);
            else review++;
        }
        foreach (string entry in Directory.EnumerateFileSystemEntries(paths.ArtworkRoot))
        {
            string name = Path.GetFileName(entry);
            if (!GameFolder().IsMatch(name) || !Directory.Exists(entry) ||
                File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint))
            {
                review++;
                continue;
            }
            foreach (string file in Directory.EnumerateFileSystemEntries(entry))
            {
                string fileName = Path.GetFileName(file);
                if (!ArtworkFile().IsMatch(fileName) || !File.Exists(file)) review++;
                else if (!referenced.Contains($"artwork/{name}/{fileName}")) TryDelete(file);
            }
            if (!Directory.EnumerateFileSystemEntries(entry).Any()) Directory.Delete(entry);
        }
        return review;
    }

    private string? ResolvePath(string relativePath) =>
        CanonicalPath().Match(relativePath) is { Success: true } match
            ? Path.Combine(paths.ArtworkRoot, match.Groups[1].Value, match.Groups[2].Value)
            : null;

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
```

  `File.Delete(staged)` in `finally` is a no-op after a successful move.

- [ ] **Step 9: Wire the sweep into `Initialize`**

```csharp
    private void Initialize()
    {
        LastStartupReconciliation = null;
        paths.EnsureCreated();
        using SqliteConnection connection = OpenConnection(create: true);
        MigrateOrValidate(connection);
        StartupReconciliationReport report = new FileOperationReconciler(paths).Run(connection);
        int artworkReview = new ManagedArtworkStore(paths).Sweep(ReadArtworkReferences(connection));
        LastStartupReconciliation = report with { ArtworkReviewCount = artworkReview };
    }

    private static HashSet<string> ReadArtworkReferences(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ArtworkRelativePath FROM Games WHERE ArtworkRelativePath IS NOT NULL";
        using SqliteDataReader reader = command.ExecuteReader();
        HashSet<string> references = new(StringComparer.Ordinal);
        while (reader.Read()) references.Add(reader.GetString(0));
        return references;
    }
```

  Add `using DesktopGuides.Infrastructure.Artwork;`.

- [ ] **Step 10: Run the tests**

Run: `dotnet test tests/DesktopGuides.Infrastructure.Tests/DesktopGuides.Infrastructure.Tests.csproj -c Release`
Expected: PASS, 0 failed. The existing reconciliation tests still pass,
because nothing is written under `.staging/`.

- [ ] **Step 11: Commit** (after message approval)

```bash
git add src tests/DesktopGuides.Infrastructure.Tests
git commit -m "feat(storage): add validated content-named artwork store and startup sweep"
```

### Task 6: Provider HTTP policy and Twitch token source

**Files:**
- Modify: `src/DesktopGuides.Core/Providers/ProviderContracts.cs`
  (add the credential records)
- Create: `src/DesktopGuides.Infrastructure/Providers/ProviderHttp.cs`,
  `src/DesktopGuides.Infrastructure/Providers/TwitchTokenSource.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Providers/FakeHandler.cs`,
  `tests/DesktopGuides.Infrastructure.Tests/Providers/ProviderHttpTests.cs`,
  `tests/DesktopGuides.Infrastructure.Tests/Providers/TwitchTokenSourceTests.cs`

**Interfaces:**
- Consumes (Task 2): `ProviderException` and `ProviderErrorKind`.
- Produces:
  - `record IgdbCredentials(string ClientId, string ClientSecret)` and
    `record ProviderCredentials(IgdbCredentials? Igdb, string? SteamGridDbKey)`,
    both overriding `ToString()` so that no secret is printed
  - `record ProviderResponse(HttpStatusCode StatusCode, byte[] Body)`
  - `sealed class ProviderHttp : IDisposable` with:
    - `ProviderHttp(HttpMessageHandler? handler = null)`
    - `internal ProviderHttp(HttpMessageHandler handler, TimeSpan requestTimeout, Func<TimeSpan, CancellationToken, Task> delay)`
    - `Task<ProviderResponse> SendAsync(HttpMethod method, Uri uri, Action<HttpRequestMessage> configure, int maxBytes, CancellationToken token)`
    - `Task<byte[]> GetImageAsync(Uri uri, CancellationToken token)`
    - `const int MaxJsonBytes = 1024 * 1024`
    - `static IReadOnlySet<string> AllowedHosts`
  - `sealed class TwitchTokenSource(ProviderHttp http, TimeProvider? clock = null)` with
    `Task<string> GetTokenAsync(IgdbCredentials credentials, bool forceRefresh, CancellationToken token)`

`SendAsync` passes every status code except 429 back to the caller; the
caller maps 401, 404 and similar codes. It throws `ProviderException` in
these cases:

| Condition | Kind |
| --- | --- |
| Not HTTPS, or a host outside the allow-list | `Unavailable` |
| A redirect to another host or to HTTP, or more than 3 redirects | `Unavailable` |
| An `HttpRequestException` or socket failure | `Unavailable` |
| The 15 s per-request timer fired and the caller's token was not cancelled | `Timeout` |
| A 429 with `Retry-After` ≤ 2 s: wait and retry once. A second 429, a longer or missing `Retry-After` | `RateLimited` |
| A body over `maxBytes` | `MalformedData` |

The caller's own cancellation surfaces as `OperationCanceledException`.
Exception messages are fixed text naming the service host. They never
wrap the inner exception, so no header or URL query can leak through
`ToString()`.

- [ ] **Step 1: Write the fake handler** (`Providers/FakeHandler.cs`)

```csharp
using System.Net;

namespace DesktopGuides.Infrastructure.Tests.Providers;

internal sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    public static FakeHandler Returning(params HttpResponseMessage[] responses)
    {
        Queue<HttpResponseMessage> queue = new(responses);
        return new FakeHandler((_, _) => Task.FromResult(queue.Dequeue()));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken token)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(token);
        Requests.Add((request, body));
        return await respond(request, token);
    }
}
```

- [ ] **Step 2: Write the failing HTTP policy tests**

```csharp
using System.Net;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Providers;

public sealed class ProviderHttpTests
{
    private static readonly Uri Igdb = new("https://api.igdb.com/v4/games");
    private readonly List<TimeSpan> delays = [];

    private ProviderHttp Create(FakeHandler handler, TimeSpan? timeout = null) =>
        new(handler, timeout ?? TimeSpan.FromSeconds(15), (wait, _) => { delays.Add(wait); return Task.CompletedTask; });

    private static Task<ProviderResponse> Send(ProviderHttp http, Uri uri, CancellationToken token = default) =>
        http.SendAsync(HttpMethod.Post, uri, _ => { }, ProviderHttp.MaxJsonBytes, token);

    [Theory]
    [InlineData("http://api.igdb.com/v4/games")]
    [InlineData("https://evil.example/v4/games")]
    [InlineData("https://api.igdb.com:8443/v4/games")]
    public async Task RejectsHttpOtherHostsAndPortsWithoutSending(string uri)
    {
        FakeHandler handler = FakeHandler.Returning(FakeHandler.Json("[]"));
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => Send(Create(handler), new Uri(uri)));
        Assert.Equal(ProviderErrorKind.Unavailable, error.Kind);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FollowsSameHostRedirectButRejectsCrossHostRedirect()
    {
        HttpResponseMessage same = new(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new Uri("/v4/games?x=1", UriKind.Relative) } };
        HttpResponseMessage cross = new(HttpStatusCode.Found) { Headers = { Location = new Uri("https://www.igdb.com/") } };
        FakeHandler handler = FakeHandler.Returning(same, cross);

        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => Send(Create(handler), Igdb));
        Assert.Equal(ProviderErrorKind.Unavailable, error.Kind);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("https://api.igdb.com/v4/games?x=1", handler.Requests[1].Request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task ShortRetryAfterRetriesOnceThenSucceeds()
    {
        HttpResponseMessage busy = new(HttpStatusCode.TooManyRequests) { Headers = { RetryAfter = new(TimeSpan.FromSeconds(1)) } };
        FakeHandler handler = FakeHandler.Returning(busy, FakeHandler.Json("[]"));

        ProviderResponse response = await Send(Create(handler), Igdb);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([TimeSpan.FromSeconds(1)], delays);
    }

    [Fact]
    public async Task LongOrMissingRetryAfterOrSecond429IsRateLimited()
    {
        HttpResponseMessage Busy(int? seconds) => new(HttpStatusCode.TooManyRequests)
        { Headers = { RetryAfter = seconds is null ? null : new(TimeSpan.FromSeconds(seconds.Value)) } };

        foreach (FakeHandler handler in new[]
                 { FakeHandler.Returning(Busy(3)), FakeHandler.Returning(Busy(null)), FakeHandler.Returning(Busy(1), Busy(1)) })
        {
            ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => Send(Create(handler), Igdb));
            Assert.Equal(ProviderErrorKind.RateLimited, error.Kind);
        }
    }

    [Fact]
    public async Task SlowResponseIsATimeoutButCallerCancelIsACancel()
    {
        FakeHandler slow = new(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new(); });
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(
            () => Send(Create(slow, TimeSpan.FromMilliseconds(50)), Igdb));
        Assert.Equal(ProviderErrorKind.Timeout, error.Kind);

        using CancellationTokenSource cancel = new(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Send(Create(slow, TimeSpan.FromSeconds(30)), Igdb, cancel.Token));
    }

    [Fact]
    public async Task NetworkFailureIsUnavailableWithoutInnerException()
    {
        FakeHandler down = new((_, _) => throw new HttpRequestException("Bearer secret-token leaked"));
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => Send(Create(down), Igdb));
        Assert.Equal(ProviderErrorKind.Unavailable, error.Kind);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("secret-token", error.ToString());
    }

    [Fact]
    public async Task OversizedBodyIsMalformed()
    {
        FakeHandler big = FakeHandler.Returning(FakeHandler.Json(new string('x', ProviderHttp.MaxJsonBytes + 1)));
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => Send(Create(big), Igdb));
        Assert.Equal(ProviderErrorKind.MalformedData, error.Kind);
    }

    [Fact]
    public async Task RequestsRunOneAtATime()
    {
        int active = 0, peak = 0;
        FakeHandler handler = new(async (_, _) =>
        {
            peak = Math.Max(peak, Interlocked.Increment(ref active));
            await Task.Delay(20);
            Interlocked.Decrement(ref active);
            return FakeHandler.Json("[]");
        });
        ProviderHttp http = Create(handler);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Send(http, Igdb)));
        Assert.Equal(1, peak);
    }

    [Fact]
    public void CredentialRecordsNeverPrintSecrets()
    {
        ProviderCredentials credentials = new(new IgdbCredentials("client-id", "igdb-secret"), "sgdb-key");
        Assert.DoesNotContain("igdb-secret", credentials.ToString());
        Assert.DoesNotContain("igdb-secret", credentials.Igdb!.ToString());
        Assert.DoesNotContain("sgdb-key", credentials.ToString());
    }
}
```

- [ ] **Step 3: Write the failing token tests**

```csharp
using System.Net;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Providers;

public sealed class TwitchTokenSourceTests
{
    private static readonly IgdbCredentials Credentials = new("client-id", "very-secret");

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static FakeHandler Tokens(params string[] tokens)
    {
        Queue<string> queue = new(tokens);
        return new FakeHandler((_, _) => Task.FromResult(FakeHandler.Json(
            $$"""{"access_token":"{{queue.Dequeue()}}","expires_in":3600,"token_type":"bearer"}""")));
    }

    [Fact]
    public async Task PostsClientCredentialsFormAndCachesUntilFiveMinutesBeforeExpiry()
    {
        Clock clock = new(DateTimeOffset.UnixEpoch);
        FakeHandler handler = Tokens("one", "two");
        TwitchTokenSource source = new(new ProviderHttp(handler), clock);

        Assert.Equal("one", await source.GetTokenAsync(Credentials, false, default));
        clock.Now += TimeSpan.FromMinutes(54);
        Assert.Equal("one", await source.GetTokenAsync(Credentials, false, default));
        clock.Now += TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1);
        Assert.Equal("two", await source.GetTokenAsync(Credentials, false, default));

        (HttpRequestMessage request, string? body) = handler.Requests[0];
        Assert.Equal("https://id.twitch.tv/oauth2/token", request.RequestUri!.AbsoluteUri);
        Assert.Equal("client_id=client-id&client_secret=very-secret&grant_type=client_credentials", body);
    }

    [Fact]
    public async Task ForceRefreshAndChangedCredentialsFetchANewToken()
    {
        TwitchTokenSource source = new(new ProviderHttp(Tokens("one", "two", "three")));
        Assert.Equal("one", await source.GetTokenAsync(Credentials, false, default));
        Assert.Equal("two", await source.GetTokenAsync(Credentials, true, default));
        Assert.Equal("three", await source.GetTokenAsync(Credentials with { ClientSecret = "other" }, false, default));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, ProviderErrorKind.InvalidCredentials)]
    [InlineData(HttpStatusCode.Unauthorized, ProviderErrorKind.InvalidCredentials)]
    [InlineData(HttpStatusCode.Forbidden, ProviderErrorKind.InvalidCredentials)]
    [InlineData(HttpStatusCode.InternalServerError, ProviderErrorKind.Unavailable)]
    public async Task FailuresMapToKindsWithoutTheSecret(HttpStatusCode status, ProviderErrorKind kind)
    {
        FakeHandler handler = FakeHandler.Returning(FakeHandler.Json(
            """{"status":400,"message":"invalid client secret very-secret"}""", status));
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(
            () => new TwitchTokenSource(new ProviderHttp(handler)).GetTokenAsync(Credentials, false, default));
        Assert.Equal(kind, error.Kind);
        Assert.DoesNotContain("very-secret", error.ToString());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"expires_in":3600}""")]
    [InlineData("""{"access_token":"","expires_in":3600}""")]
    [InlineData("""{"access_token":"t","expires_in":0}""")]
    public async Task MalformedTokenResponseIsMalformedData(string json)
    {
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(
            () => new TwitchTokenSource(new ProviderHttp(FakeHandler.Returning(FakeHandler.Json(json))))
                .GetTokenAsync(Credentials, false, default));
        Assert.Equal(ProviderErrorKind.MalformedData, error.Kind);
    }
}
```

- [ ] **Step 4: Run the tests and watch them fail**

Run: `dotnet test tests/DesktopGuides.Infrastructure.Tests/DesktopGuides.Infrastructure.Tests.csproj -c Release --filter "FullyQualifiedName~ProviderHttpTests|FullyQualifiedName~TwitchTokenSourceTests"`
Expected: build FAIL, `The type or namespace name 'Providers' does not exist in the namespace 'DesktopGuides.Infrastructure'`.

- [ ] **Step 5: Add the credential records to `ProviderContracts.cs`**

```csharp
public sealed record IgdbCredentials(string ClientId, string ClientSecret)
{
    public override string ToString() => $"IgdbCredentials {{ ClientId = {ClientId}, ClientSecret = *** }}";
}

public sealed record ProviderCredentials(IgdbCredentials? Igdb, string? SteamGridDbKey)
{
    public static ProviderCredentials None { get; } = new(null, null);

    public override string ToString() =>
        $"ProviderCredentials {{ Igdb = {(Igdb is null ? "none" : "saved")}, SteamGridDbKey = {(SteamGridDbKey is null ? "none" : "saved")} }}";
}
```

- [ ] **Step 6: Implement `ProviderHttp.cs`**

```csharp
using System.Net;
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Infrastructure.Providers;

public sealed record ProviderResponse(HttpStatusCode StatusCode, byte[] Body);

public sealed class ProviderHttp : IDisposable
{
    public const int MaxJsonBytes = 1024 * 1024;
    public const int MaxImageBytes = 5 * 1024 * 1024;
    private const int MaxRedirects = 3;

    // Recorded in Task 1 from the terms and a live grid query.
    public static IReadOnlySet<string> AllowedHosts { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "id.twitch.tv", "api.igdb.com", "images.igdb.com", "www.steamgriddb.com", "cdn2.steamgriddb.com"
    };

    private readonly HttpClient client;
    private readonly TimeSpan requestTimeout;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly SemaphoreSlim gate = new(1, 1);

    public ProviderHttp(HttpMessageHandler? handler = null)
        : this(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false },
            TimeSpan.FromSeconds(15), Task.Delay)
    {
    }

    internal ProviderHttp(
        HttpMessageHandler handler, TimeSpan requestTimeout, Func<TimeSpan, CancellationToken, Task> delay)
    {
        client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        this.requestTimeout = requestTimeout;
        this.delay = delay;
    }

    public async Task<ProviderResponse> SendAsync(
        HttpMethod method, Uri uri, Action<HttpRequestMessage> configure, int maxBytes, CancellationToken token)
    {
        RequireAllowed(uri);
        await gate.WaitAsync(token);
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                (HttpStatusCode status, byte[] body, TimeSpan? retryAfter) =
                    await SendFollowingRedirectsAsync(method, uri, configure, maxBytes, token);
                if (status != HttpStatusCode.TooManyRequests) return new ProviderResponse(status, body);
                if (attempt > 0 || retryAfter is not { } wait || wait > TimeSpan.FromSeconds(2))
                {
                    throw new ProviderException(ProviderErrorKind.RateLimited, $"{uri.Host} is busy.");
                }
                await delay(wait, token);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<byte[]> GetImageAsync(Uri uri, CancellationToken token)
    {
        ProviderResponse response = await SendAsync(HttpMethod.Get, uri, _ => { }, MaxImageBytes, token);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new ProviderException(ProviderErrorKind.Unavailable, $"{uri.Host} did not return the image.");
        }
        return response.Body;
    }

    private async Task<(HttpStatusCode, byte[], TimeSpan?)> SendFollowingRedirectsAsync(
        HttpMethod method, Uri uri, Action<HttpRequestMessage> configure, int maxBytes, CancellationToken token)
    {
        Uri current = uri;
        for (int hop = 0; hop <= MaxRedirects; hop++)
        {
            using CancellationTokenSource timer = CancellationTokenSource.CreateLinkedTokenSource(token);
            timer.CancelAfter(requestTimeout);
            try
            {
                using HttpRequestMessage request = new(method, current);
                configure(request);
                using HttpResponseMessage response = await client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, timer.Token);
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    Uri next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    if (next.Scheme != Uri.UriSchemeHttps || !string.Equals(next.Host, uri.Host, StringComparison.OrdinalIgnoreCase))
                    {
                        throw Unavailable(uri, "redirected to another host");
                    }
                    current = next;
                    continue;
                }
                byte[] body = await ReadLimitedAsync(response.Content, maxBytes, uri, timer.Token);
                return (response.StatusCode, body, RetryAfter(response));
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new ProviderException(ProviderErrorKind.Timeout, $"{uri.Host} took too long to respond.");
            }
            catch (HttpRequestException)
            {
                throw Unavailable(uri, "could not be reached");
            }
        }
        throw Unavailable(uri, "redirected too many times");
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int maxBytes, Uri uri, CancellationToken token)
    {
        if (content.Headers.ContentLength > maxBytes) throw TooLarge(uri);
        await using Stream stream = await content.ReadAsStreamAsync(token);
        using MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + read > maxBytes) throw TooLarge(uri);
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta ??
        (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);

    private static void RequireAllowed(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
            !AllowedHosts.Contains(uri.IdnHost))
        {
            throw new ProviderException(ProviderErrorKind.Unavailable, "The app does not contact that address.");
        }
    }

    private static ProviderException Unavailable(Uri uri, string reason) =>
        new(ProviderErrorKind.Unavailable, $"{uri.Host} {reason}.");

    private static ProviderException TooLarge(Uri uri) =>
        new(ProviderErrorKind.MalformedData, $"{uri.Host} returned more data than the app accepts.");

    public void Dispose()
    {
        client.Dispose();
        gate.Dispose();
    }
}
```

  The `HttpRequestException` handler throws a new exception without
  `inner`. This is deliberate: see the table above.

- [ ] **Step 7: Implement `TwitchTokenSource.cs`**

```csharp
using System.Net;
using System.Text.Json;
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Infrastructure.Providers;

public sealed class TwitchTokenSource(ProviderHttp http, TimeProvider? clock = null)
{
    private static readonly Uri TokenUri = new("https://id.twitch.tv/oauth2/token");
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(5);
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private (IgdbCredentials Credentials, string Token, DateTimeOffset RefreshAfter)? cached;

    public async Task<string> GetTokenAsync(IgdbCredentials credentials, bool forceRefresh, CancellationToken token)
    {
        if (!forceRefresh && cached is { } entry && entry.Credentials == credentials &&
            time.GetUtcNow() < entry.RefreshAfter)
        {
            return entry.Token;
        }
        cached = null;
        ProviderResponse response = await http.SendAsync(HttpMethod.Post, TokenUri, request =>
            request.Content = new FormUrlEncodedContent(
            [
                new("client_id", credentials.ClientId),
                new("client_secret", credentials.ClientSecret),
                new("grant_type", "client_credentials")
            ]), ProviderHttp.MaxJsonBytes, token);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new ProviderException(ProviderErrorKind.InvalidCredentials, "Twitch rejected the IGDB client ID or secret.");
        }
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new ProviderException(ProviderErrorKind.Unavailable, "Twitch could not issue an IGDB token.");
        }
        (string accessToken, int expiresIn) = Parse(response.Body);
        cached = (credentials, accessToken, time.GetUtcNow() + TimeSpan.FromSeconds(expiresIn) - ExpiryMargin);
        return accessToken;
    }

    private static (string, int) Parse(byte[] body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("access_token", out JsonElement accessToken) &&
                accessToken.ValueKind == JsonValueKind.String && accessToken.GetString() is { Length: > 0 } value &&
                root.TryGetProperty("expires_in", out JsonElement expires) &&
                expires.TryGetInt32(out int seconds) && seconds > 0)
            {
                return (value, seconds);
            }
        }
        catch (JsonException) { }
        throw new ProviderException(ProviderErrorKind.MalformedData, "Twitch returned a token response the app couldn't read.");
    }
}
```

- [ ] **Step 8: Run the tests**

Run: `dotnet test tests/DesktopGuides.Infrastructure.Tests/DesktopGuides.Infrastructure.Tests.csproj -c Release`
Expected: PASS, 0 failed. If `FormUrlEncodedContent` encodes `-` or the
secret differently from the literal expected body, compare against
`WebUtility.UrlEncode` of each part and record a ruling. Do not weaken
the assertion to "contains".

- [ ] **Step 9: Commit** (after message approval)

```bash
git add src tests/DesktopGuides.Infrastructure.Tests
git commit -m "feat(providers): add allow-listed provider HTTP policy and Twitch token cache"
```

### Task 7: IGDB client

**Files:**
- Modify: `src/DesktopGuides.Core/Providers/ProviderContracts.cs` (add
  `IGameMetadataProvider`)
- Create: `src/DesktopGuides.Infrastructure/Providers/IgdbClient.cs`,
  `tests/DesktopGuides.Infrastructure.Tests/Providers/Fixtures/igdb-search.json`,
  `tests/DesktopGuides.Infrastructure.Tests/Providers/Fixtures/igdb-game.json`
- Modify: `tests/DesktopGuides.Infrastructure.Tests/DesktopGuides.Infrastructure.Tests.csproj`
  (copy `Providers/Fixtures/*.json` to the output folder)
- Test: `tests/DesktopGuides.Infrastructure.Tests/Providers/IgdbClientTests.cs`

**Interfaces:**
- Consumes (Tasks 2 and 6): `GameMetadataNormalizer`,
  `ProviderSearchResult`, `ProviderGameRecord`, `ArtworkHints`,
  `ProviderHttp.SendAsync`, `TwitchTokenSource.GetTokenAsync` and
  `IgdbCredentials`.
- Produces:
  - `interface IGameMetadataProvider` with
    `Task<IReadOnlyList<ProviderSearchResult>> SearchAsync(string query, CancellationToken token)`
    and `Task<ProviderGameRecord> GetAsync(string externalId, CancellationToken token)`
  - `sealed class IgdbClient(ProviderHttp http, TwitchTokenSource tokens, Func<CancellationToken, Task<IgdbCredentials?>> credentials) : IGameMetadataProvider`
  - `internal static string IgdbClient.BuildSearchBody(string query)` and
    `internal static string IgdbClient.BuildGetBody(string externalId)`
  - `const int IgdbClient.MaxQueryLength = 100`. `SearchAsync` throws
    `ArgumentException` for a blank query or one over 100 characters
    after trimming.

**Ruling T7-a (thumbnails):** A search result's `ThumbnailUrl` is built
by the app as
`https://images.igdb.com/igdb/image/upload/t_thumb/{imageId}.jpg`, where
`imageId` matches `^[a-z0-9]{1,40}$`. WinUI loads it directly as an
`Image` source. Only the saved cover goes through `ProviderHttp` and
`ArtworkValidator`. Cost if wrong: WinUI thumbnails bypass the byte
limit, but the host and path are fixed by the app and nothing is
written to disk.

- [ ] **Step 1: Add the fixtures.** These are sanitized samples shaped
  from the IGDB v4 documentation; Task 12 checks their field names
  against a live response.

`Fixtures/igdb-search.json`:

```json
[
  { "id": 231, "name": "Half-Life", "first_release_date": 911433600,
    "game_type": { "id": 0, "type": "Main Game" },
    "platforms": [ { "id": 6, "name": "PC (Microsoft Windows)" }, { "id": 8, "name": "PlayStation 2" } ],
    "cover": { "id": 1, "image_id": "co1abc" } },
  { "id": 7351, "name": "Half-Life: Source", "first_release_date": 1086048000,
    "game_type": { "id": 9, "type": "Remaster" }, "version_parent": 231,
    "platforms": [ { "id": 6, "name": "PC (Microsoft Windows)" } ] },
  { "id": 0, "name": "Broken" },
  { "id": 99, "name": "   " }
]
```

`Fixtures/igdb-game.json`:

```json
[
  { "id": 231, "name": "Half-Life", "summary": "Gordon Freeman\u0007 fights.\r\nAgain.",
    "url": "https://www.igdb.com/games/half-life", "first_release_date": 911433600,
    "game_type": { "id": 0, "type": "Main Game" },
    "genres": [ { "id": 5, "name": "Shooter" }, { "id": 5, "name": "Shooter" } ],
    "platforms": [ { "id": 6, "name": "PC (Microsoft Windows)" } ],
    "involved_companies": [
      { "id": 1, "developer": true, "publisher": false, "company": { "id": 56, "name": "Valve" } },
      { "id": 2, "developer": false, "publisher": true, "company": { "id": 57, "name": "Sierra Entertainment" } } ],
    "cover": { "id": 1, "image_id": "co1abc" },
    "external_games": [
      { "id": 9, "uid": "gog-1", "external_game_source": 5 },
      { "id": 10, "uid": "70", "external_game_source": 1 } ] }
]
```

  In the test `.csproj`, add:

```xml
  <ItemGroup>
    <None Update="Providers\Fixtures\*.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Write the failing tests**

```csharp
using System.Net;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Providers;

public sealed class IgdbClientTests
{
    private static readonly IgdbCredentials Credentials = new("client-id", "very-secret");

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Providers", "Fixtures", name));

    private static HttpResponseMessage Token(string token) =>
        FakeHandler.Json($$"""{"access_token":"{{token}}","expires_in":3600}""");

    private static (IgdbClient Client, FakeHandler Handler) Create(
        IgdbCredentials? credentials, params HttpResponseMessage[] responses)
    {
        FakeHandler handler = FakeHandler.Returning(responses);
        ProviderHttp http = new(handler);
        return (new IgdbClient(http, new TwitchTokenSource(http), _ => Task.FromResult(credentials)), handler);
    }

    [Fact]
    public void SearchBodyEscapesQuotesAndBackslashesAndKeepsUnicode()
    {
        Assert.Equal(
            "search \"Pokémon: \\\"Let's Go\\\" \\\\ Eevee\"; " +
            "fields name,first_release_date,game_type.type,version_parent,platforms.name,cover.image_id; limit 20;",
            IgdbClient.BuildSearchBody("  Pokémon: \"Let's Go\" \\ Eevee\n "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u0007")]
    public async Task BlankQueryIsRejectedWithoutARequest(string query)
    {
        (IgdbClient client, FakeHandler handler) = Create(Credentials);
        await Assert.ThrowsAsync<ArgumentException>(() => client.SearchAsync(query, default));
        await Assert.ThrowsAsync<ArgumentException>(() => client.SearchAsync(new string('a', 101), default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task MissingCredentialsAreNotConfiguredWithoutARequest()
    {
        (IgdbClient client, FakeHandler handler) = Create(null);
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => client.SearchAsync("zelda", default));
        Assert.Equal(ProviderErrorKind.NotConfigured, error.Kind);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SearchSendsHeadersAndMapsValidResultsOnly()
    {
        (IgdbClient client, FakeHandler handler) = Create(Credentials, Token("tok"), FakeHandler.Json(Fixture("igdb-search.json")));

        IReadOnlyList<ProviderSearchResult> results = await client.SearchAsync("half-life", default);

        (HttpRequestMessage request, string? body) = handler.Requests[1];
        Assert.Equal("https://api.igdb.com/v4/games", request.RequestUri!.AbsoluteUri);
        Assert.Equal("client-id", Assert.Single(request.Headers.GetValues("Client-ID")));
        Assert.Equal("Bearer tok", request.Headers.Authorization!.ToString());
        Assert.EndsWith("limit 20;", body);
        Assert.Equal(2, results.Count);
        Assert.Equal(new ProviderSearchResult("231", "Half-Life", 1998,
            ["PC (Microsoft Windows)", "PlayStation 2"], GameTypeTag.MainGame,
            "https://images.igdb.com/igdb/image/upload/t_thumb/co1abc.jpg"),
            results[0], new SearchResultComparer());
        Assert.Equal(GameTypeTag.Edition, results[1].Type);
        Assert.Null(results[1].ThumbnailUrl);
    }

    [Fact]
    public async Task GetMapsSnapshotAndArtworkHints()
    {
        (IgdbClient client, FakeHandler handler) = Create(Credentials, Token("tok"), FakeHandler.Json(Fixture("igdb-game.json")));

        ProviderGameRecord record = await client.GetAsync("231", default);

        Assert.Contains("where id = 231;", handler.Requests[1].Body);
        Assert.Equal("231", record.ExternalId);
        Assert.Equal("Half-Life", record.Title);
        Assert.Equal("Gordon Freeman fights.\nAgain.", record.Snapshot.Summary);
        Assert.Equal(new DateOnly(1998, 11, 19), record.Snapshot.FirstReleaseDate);
        Assert.Equal(["Shooter"], record.Snapshot.Genres);
        Assert.Equal(["Valve"], record.Snapshot.Developers);
        Assert.Equal(["Sierra Entertainment"], record.Snapshot.Publishers);
        Assert.Equal("https://www.igdb.com/games/half-life", record.Snapshot.ProviderUrl);
        Assert.Equal(new ArtworkHints("Half-Life", "70", "co1abc"), record.Hints);
    }

    [Fact]
    public async Task Unauthorized401RefreshesTokenOnceThenSucceeds()
    {
        (IgdbClient client, FakeHandler handler) = Create(Credentials,
            Token("old"), new HttpResponseMessage(HttpStatusCode.Unauthorized),
            Token("new"), FakeHandler.Json("[]"));

        Assert.Empty(await client.SearchAsync("zelda", default));
        Assert.Equal("Bearer new", handler.Requests[3].Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Second401IsInvalidCredentials()
    {
        (IgdbClient client, _) = Create(Credentials,
            Token("old"), new HttpResponseMessage(HttpStatusCode.Unauthorized),
            Token("new"), new HttpResponseMessage(HttpStatusCode.Unauthorized));

        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => client.SearchAsync("zelda", default));
        Assert.Equal(ProviderErrorKind.InvalidCredentials, error.Kind);
        Assert.DoesNotContain("very-secret", error.ToString());
        Assert.DoesNotContain("new", error.Message);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("[{\"id\":\"231\",\"name\":\"x\"}]")]
    public async Task MalformedGetResponseIsMalformedData(string json)
    {
        (IgdbClient client, _) = Create(Credentials, Token("tok"), FakeHandler.Json(json));
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => client.GetAsync("231", default));
        Assert.Equal(ProviderErrorKind.MalformedData, error.Kind);
    }

    [Fact]
    public async Task EmptyGetResponseIsMalformedData()
    {
        (IgdbClient client, _) = Create(Credentials, Token("tok"), FakeHandler.Json("[]"));
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => client.GetAsync("231", default));
        Assert.Equal(ProviderErrorKind.MalformedData, error.Kind);
    }

    [Fact]
    public async Task ServerErrorIsUnavailable()
    {
        (IgdbClient client, _) = Create(Credentials, Token("tok"), new HttpResponseMessage(HttpStatusCode.BadGateway));
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => client.SearchAsync("zelda", default));
        Assert.Equal(ProviderErrorKind.Unavailable, error.Kind);
    }

    private sealed class SearchResultComparer : IEqualityComparer<ProviderSearchResult>
    {
        public bool Equals(ProviderSearchResult? x, ProviderSearchResult? y) =>
            x is not null && y is not null && x.ExternalId == y.ExternalId && x.Title == y.Title &&
            x.ReleaseYear == y.ReleaseYear && x.Platforms.SequenceEqual(y.Platforms) &&
            x.Type == y.Type && x.ThumbnailUrl == y.ThumbnailUrl;

        public int GetHashCode(ProviderSearchResult obj) => obj.ExternalId.GetHashCode();
    }
}
```

  The `"id":"231"` case is a string ID, which the client rejects: IGDB
  returns numeric IDs. `GetAsync` also rejects a record whose ID is not
  the one requested.

- [ ] **Step 3: Run the tests and watch them fail**

Run: `dotnet test tests/DesktopGuides.Infrastructure.Tests/DesktopGuides.Infrastructure.Tests.csproj -c Release --filter FullyQualifiedName~IgdbClientTests`
Expected: build FAIL, `The type or namespace name 'IgdbClient' could not be found`.

- [ ] **Step 4: Add the interface to `ProviderContracts.cs`**

```csharp
public interface IGameMetadataProvider
{
    Task<IReadOnlyList<ProviderSearchResult>> SearchAsync(string query, CancellationToken token);
    Task<ProviderGameRecord> GetAsync(string externalId, CancellationToken token);
}
```

- [ ] **Step 5: Implement `IgdbClient.cs`**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Infrastructure.Providers;

public sealed partial class IgdbClient(
    ProviderHttp http,
    TwitchTokenSource tokens,
    Func<CancellationToken, Task<IgdbCredentials?>> credentials) : IGameMetadataProvider
{
    public const int MaxQueryLength = 100;
    private const int SteamSource = 1;
    private static readonly Uri GamesUri = new("https://api.igdb.com/v4/games");

    [GeneratedRegex("^[a-z0-9]{1,40}$")]
    private static partial Regex ImageId();

    public async Task<IReadOnlyList<ProviderSearchResult>> SearchAsync(string query, CancellationToken token)
    {
        string body = BuildSearchBody(query);
        using JsonDocument document = await PostAsync(body, token);
        List<ProviderSearchResult> results = [];
        foreach (JsonElement item in RequireArray(document).EnumerateArray())
        {
            try
            {
                results.Add(new ProviderSearchResult(
                    GameMetadataNormalizer.NormalizeExternalId(ReadId(item)),
                    GameMetadataNormalizer.NormalizeTitle(ReadString(item, "name")),
                    ReadReleaseDate(item)?.Year,
                    GameMetadataNormalizer.NormalizeList(ReadNames(item, "platforms")),
                    ReadType(item),
                    ReadCoverId(item) is { } id ? $"https://images.igdb.com/igdb/image/upload/t_thumb/{id}.jpg" : null));
            }
            catch (ProviderException) { }
            if (results.Count == 20) break;
        }
        return results;
    }

    public async Task<ProviderGameRecord> GetAsync(string externalId, CancellationToken token)
    {
        string id = GameMetadataNormalizer.NormalizeExternalId(externalId);
        using JsonDocument document = await PostAsync(BuildGetBody(id), token);
        JsonElement array = RequireArray(document);
        if (array.GetArrayLength() != 1 || array[0].ValueKind != JsonValueKind.Object)
        {
            throw Malformed();
        }
        JsonElement item = array[0];
        if (ReadId(item) != id) throw Malformed();
        string title = GameMetadataNormalizer.NormalizeTitle(ReadString(item, "name"));
        IEnumerable<JsonElement> companies = ReadArray(item, "involved_companies");
        GameMetadataSnapshot snapshot = new(
            GameMetadataSnapshot.CurrentSchemaVersion,
            GameMetadataNormalizer.NormalizeText(ReadString(item, "summary"), GameMetadataNormalizer.SummaryLimit),
            ReadReleaseDate(item),
            GameMetadataNormalizer.NormalizeList(ReadNames(item, "genres")),
            GameMetadataNormalizer.NormalizeList(CompanyNames(companies, "developer")),
            GameMetadataNormalizer.NormalizeList(CompanyNames(companies, "publisher")),
            GameMetadataNormalizer.NormalizeList(ReadNames(item, "platforms")),
            GameMetadataNormalizer.NormalizeProviderUrl(ReadString(item, "url")),
            ReadType(item));
        return new ProviderGameRecord(id, title, snapshot, new ArtworkHints(title, ReadSteamAppId(item), ReadCoverId(item)));
    }

    internal static string BuildSearchBody(string query)
    {
        string? trimmed = GameMetadataNormalizer.NormalizeText(query?.ReplaceLineEndings(" "), int.MaxValue);
        if (trimmed is null || trimmed.Length > MaxQueryLength)
        {
            throw new ArgumentException($"Enter 1 to {MaxQueryLength} characters to search.", nameof(query));
        }
        string escaped = trimmed.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"search \"{escaped}\"; fields name,first_release_date,game_type.type,version_parent," +
            "platforms.name,cover.image_id; limit 20;";
    }

    internal static string BuildGetBody(string externalId) =>
        "fields name,summary,url,first_release_date,game_type.type,version_parent,genres.name," +
        "platforms.name,involved_companies.developer,involved_companies.publisher," +
        "involved_companies.company.name,cover.image_id,external_games.uid," +
        "external_games.external_game_source,external_games.category; " +
        $"where id = {externalId}; limit 1;";

    private async Task<JsonDocument> PostAsync(string body, CancellationToken token)
    {
        IgdbCredentials current = await credentials(token) ??
            throw new ProviderException(ProviderErrorKind.NotConfigured, "Add IGDB credentials in Settings to search.");
        for (int attempt = 0; attempt < 2; attempt++)
        {
            string accessToken = await tokens.GetTokenAsync(current, forceRefresh: attempt > 0, token);
            ProviderResponse response = await http.SendAsync(HttpMethod.Post, GamesUri, request =>
            {
                request.Headers.Add("Client-ID", current.ClientId);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(body, Encoding.UTF8, "text/plain");
            }, ProviderHttp.MaxJsonBytes, token);
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    try { return JsonDocument.Parse(response.Body); }
                    catch (JsonException) { throw Malformed(); }
                case HttpStatusCode.Unauthorized when attempt == 0:
                    continue;
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    throw new ProviderException(ProviderErrorKind.InvalidCredentials, "IGDB rejected the client ID or secret.");
                default:
                    throw new ProviderException(ProviderErrorKind.Unavailable, "IGDB could not complete the request.");
            }
        }
        throw new ProviderException(ProviderErrorKind.InvalidCredentials, "IGDB rejected the client ID or secret.");
    }

    private static JsonElement RequireArray(JsonDocument document) =>
        document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement : throw Malformed();

    private static string? ReadId(JsonElement item) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out JsonElement id) &&
        id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out long value)
            ? value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;

    private static string? ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IEnumerable<JsonElement> ReadArray(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToArray()
            : [];

    private static IEnumerable<string?> ReadNames(JsonElement item, string name) =>
        ReadArray(item, name).Select(e => ReadString(e, "name"));

    private static IEnumerable<string?> CompanyNames(IEnumerable<JsonElement> companies, string role) =>
        companies
            .Where(c => c.TryGetProperty(role, out JsonElement flag) && flag.ValueKind == JsonValueKind.True)
            .Select(c => c.TryGetProperty("company", out JsonElement company) && company.ValueKind == JsonValueKind.Object
                ? ReadString(company, "name") : null);

    private static DateOnly? ReadReleaseDate(JsonElement item) =>
        item.TryGetProperty("first_release_date", out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long seconds) &&
        seconds is >= -62135596800 and <= 253402300799
            ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime)
            : null;

    private static GameTypeTag ReadType(JsonElement item) =>
        GameMetadataNormalizer.MapType(
            item.TryGetProperty("game_type", out JsonElement type) && type.ValueKind == JsonValueKind.Object
                ? ReadString(type, "type") : null,
            item.TryGetProperty("version_parent", out JsonElement parent) && parent.ValueKind == JsonValueKind.Number);

    private static string? ReadCoverId(JsonElement item) =>
        item.TryGetProperty("cover", out JsonElement cover) && cover.ValueKind == JsonValueKind.Object &&
        ReadString(cover, "image_id") is { } id && ImageId().IsMatch(id) ? id : null;

    private static string? ReadSteamAppId(JsonElement item) =>
        ReadArray(item, "external_games")
            .Where(e => IsSource(e, "external_game_source") || IsSource(e, "category"))
            .Select(e => ReadString(e, "uid"))
            .FirstOrDefault(uid => uid is { Length: >= 1 and <= 12 } && uid.All(char.IsAsciiDigit));

    private static bool IsSource(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int source) && source == SteamSource;

    private static ProviderException Malformed() =>
        new(ProviderErrorKind.MalformedData, "IGDB returned data the app couldn't read.");
}
```

  `BuildSearchBody` turns line breaks into spaces, then strips control
  characters with the snapshot text rules, before it checks the length.
  The behaviour is pinned by
  `SearchBodyEscapesQuotesAndBackslashesAndKeepsUnicode` and
  `BlankQueryIsRejectedWithoutARequest`.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/DesktopGuides.Infrastructure.Tests/DesktopGuides.Infrastructure.Tests.csproj -c Release`
Expected: PASS, 0 failed.

- [ ] **Step 7: Commit** (after message approval)

```bash
git add src tests/DesktopGuides.Infrastructure.Tests
git commit -m "feat(providers): add IGDB search and record client"
```

### Task 8: Artwork sources and fallback chain

**Files:**
- Modify: `src/DesktopGuides.Core/Providers/ProviderContracts.cs`
- Create: `src/DesktopGuides.Core/Providers/FallbackArtworkSource.cs`,
  `src/DesktopGuides.Infrastructure/Providers/SteamGridDbArtworkSource.cs`,
  `src/DesktopGuides.Infrastructure/Providers/IgdbCoverArtworkSource.cs`
- Test: `tests/DesktopGuides.Core.Tests/Providers/FallbackArtworkSourceTests.cs`,
  `tests/DesktopGuides.Infrastructure.Tests/Providers/ArtworkSourceTests.cs`

**Interfaces:**
- Consumes (Tasks 2 and 6): `ArtworkHints`, `ProviderHttp` and
  `ProviderException`.
- Produces:
  - `record ArtworkCandidate(Uri Url, string SourceName)`, where
    `SourceName` is `"SteamGridDB"` or `"IGDB"`
  - `interface IArtworkSource { Task<ArtworkCandidate?> FindAsync(ArtworkHints hints, CancellationToken token); }`
  - `sealed class FallbackArtworkSource(params IArtworkSource[] sources)` with
    `IAsyncEnumerable<ArtworkCandidate> FindCandidatesAsync(ArtworkHints hints, CancellationToken token)`
  - `sealed class SteamGridDbArtworkSource(ProviderHttp http, Func<CancellationToken, Task<string?>> apiKey) : IArtworkSource`
  - `sealed class IgdbCoverArtworkSource : IArtworkSource`

**Ruling T8-a (fallback continues past a bad download):** The spec says
the fallback "tries its sources in order". `FallbackArtworkSource`
yields each source's candidate in turn. It skips any source that throws
`ProviderException` (such as a rejected SteamGridDB key) and passes
cancellation on to the caller. The importer (Task 9) moves to the next
candidate when a download or validation fails. Cost if wrong: at most
one extra image request per add.

**Ruling T8-b (name lookup):** The SteamGridDB name fallback uses only an
autocomplete result whose name equals the IGDB title, ignoring case.
Without that check, a near match could attach another game's cover.

- [ ] **Step 1: Write the failing Core test**

```csharp
using DesktopGuides.Core.Providers;
using Xunit;

namespace DesktopGuides.Core.Tests.Providers;

public sealed class FallbackArtworkSourceTests
{
    private static readonly ArtworkHints Hints = new("Half-Life", "70", "co1abc");

    private sealed class Source(Func<ArtworkCandidate?> find) : IArtworkSource
    {
        public int Calls { get; private set; }
        public Task<ArtworkCandidate?> FindAsync(ArtworkHints hints, CancellationToken token)
        {
            Calls++;
            token.ThrowIfCancellationRequested();
            return Task.FromResult(find());
        }
    }

    private static ArtworkCandidate Candidate(string name) => new(new Uri($"https://{name}.test/a.png"), name);

    [Fact]
    public async Task YieldsCandidatesInOrderAndSkipsEmptyAndFailingSources()
    {
        Source failing = new(() => throw new ProviderException(ProviderErrorKind.InvalidCredentials, "no"));
        Source empty = new(() => null);
        Source igdb = new(() => Candidate("IGDB"));
        FallbackArtworkSource chain = new(failing, empty, igdb);

        List<ArtworkCandidate> found = [];
        await foreach (ArtworkCandidate candidate in chain.FindCandidatesAsync(Hints, default)) found.Add(candidate);

        Assert.Equal([Candidate("IGDB")], found);
        Assert.Equal((1, 1, 1), (failing.Calls, empty.Calls, igdb.Calls));
    }

    [Fact]
    public async Task StopsAtTheFirstCandidateTheCallerAccepts()
    {
        Source first = new(() => Candidate("SteamGridDB"));
        Source second = new(() => Candidate("IGDB"));

        await foreach (ArtworkCandidate _ in new FallbackArtworkSource(first, second).FindCandidatesAsync(Hints, default))
        {
            break;
        }
        Assert.Equal(0, second.Calls);
    }

    [Fact]
    public async Task CancellationIsNotSwallowed()
    {
        using CancellationTokenSource cancel = new();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (ArtworkCandidate _ in new FallbackArtworkSource(new Source(() => null))
                               .FindCandidatesAsync(Hints, cancel.Token)) { }
        });
    }
}
```

- [ ] **Step 2: Write the failing Infrastructure tests**

```csharp
using System.Net;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Providers;

public sealed class ArtworkSourceTests
{
    private const string Grid = """
        {"success":true,"data":[{"id":1,"url":"https://cdn2.steamgriddb.com/grid/a.png",
          "thumb":"https://cdn2.steamgriddb.com/thumb/a.png","width":600,"height":900,"mime":"image/png"}]}
        """;

    private static (SteamGridDbArtworkSource Source, FakeHandler Handler) Create(string? key, params HttpResponseMessage[] responses)
    {
        FakeHandler handler = FakeHandler.Returning(responses);
        return (new SteamGridDbArtworkSource(new ProviderHttp(handler), _ => Task.FromResult(key)), handler);
    }

    [Fact]
    public async Task SteamIdLookupUsesBearerKeyAndReturnsFirstGrid()
    {
        (SteamGridDbArtworkSource source, FakeHandler handler) = Create("sgdb-key", FakeHandler.Json(Grid));

        ArtworkCandidate? candidate = await source.FindAsync(new ArtworkHints("Half-Life", "70", null), default);

        Assert.Equal(new ArtworkCandidate(new Uri("https://cdn2.steamgriddb.com/grid/a.png"), "SteamGridDB"), candidate);
        HttpRequestMessage request = Assert.Single(handler.Requests).Request;
        Assert.Equal("https://www.steamgriddb.com/api/v2/grids/steam/70?dimensions=600x900", request.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer sgdb-key", request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task MissingSteamMatchFallsBackToExactNameMatch()
    {
        (SteamGridDbArtworkSource source, FakeHandler handler) = Create("sgdb-key",
            FakeHandler.Json("""{"success":false,"errors":["Game not found"]}""", HttpStatusCode.NotFound),
            FakeHandler.Json("""{"success":true,"data":[{"id":5,"name":"Half-Life 2"},{"id":6,"name":"half-life"}]}"""),
            FakeHandler.Json(Grid));

        ArtworkCandidate? candidate = await source.FindAsync(new ArtworkHints("Half-Life", "70", null), default);

        Assert.NotNull(candidate);
        Assert.Equal("https://www.steamgriddb.com/api/v2/search/autocomplete/Half-Life", handler.Requests[1].Request.RequestUri!.AbsoluteUri);
        Assert.Equal("https://www.steamgriddb.com/api/v2/grids/game/6?dimensions=600x900", handler.Requests[2].Request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task NoExactNameMatchReturnsNull()
    {
        (SteamGridDbArtworkSource source, FakeHandler handler) = Create("sgdb-key",
            FakeHandler.Json("""{"success":true,"data":[{"id":5,"name":"Half-Life 2"}]}"""));

        Assert.Null(await source.FindAsync(new ArtworkHints("Half-Life", null, null), default));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task NoKeyReturnsNullWithoutARequest()
    {
        (SteamGridDbArtworkSource source, FakeHandler handler) = Create(null);
        Assert.Null(await source.FindAsync(new ArtworkHints("Half-Life", "70", null), default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task EmptyGridAndOffListUrlsReturnNull()
    {
        (SteamGridDbArtworkSource source, _) = Create("sgdb-key",
            FakeHandler.Json("""{"success":true,"data":[{"id":1,"url":"https://evil.example/a.png"},{"id":2,"url":"http://cdn2.steamgriddb.com/a.png"}]}"""),
            FakeHandler.Json("""{"success":true,"data":[]}"""));
        Assert.Null(await source.FindAsync(new ArtworkHints("Half-Life", "70", null), default));
    }

    [Fact]
    public async Task RejectedKeyIsInvalidCredentialsWithoutTheKey()
    {
        (SteamGridDbArtworkSource source, _) = Create("sgdb-key", new HttpResponseMessage(HttpStatusCode.Unauthorized));
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(
            () => source.FindAsync(new ArtworkHints("Half-Life", "70", null), default));
        Assert.Equal(ProviderErrorKind.InvalidCredentials, error.Kind);
        Assert.DoesNotContain("sgdb-key", error.ToString());
    }

    [Fact]
    public async Task IgdbCoverUsesCoverBigSize()
    {
        IgdbCoverArtworkSource source = new();
        Assert.Equal(new ArtworkCandidate(new Uri("https://images.igdb.com/igdb/image/upload/t_cover_big/co1abc.jpg"), "IGDB"),
            await source.FindAsync(new ArtworkHints("Half-Life", null, "co1abc"), default));
        Assert.Null(await source.FindAsync(new ArtworkHints("Half-Life", null, null), default));
    }
}
```

  The empty-grid test's first response is a grid with only off-list URLs
  for the Steam ID lookup. That lookup returns no usable URL, so the name
  lookup runs, and the second response (an empty search result) makes it
  return null.

- [ ] **Step 3: Run the tests and watch them fail**

Run: `dotnet test tests/DesktopGuides.Core.Tests/DesktopGuides.Core.Tests.csproj -c Release --filter FullyQualifiedName~FallbackArtworkSourceTests`
Expected: build FAIL, `The type or namespace name 'IArtworkSource' could not be found`.

Run: `dotnet test tests/DesktopGuides.Infrastructure.Tests/DesktopGuides.Infrastructure.Tests.csproj -c Release --filter FullyQualifiedName~ArtworkSourceTests`
Expected: build FAIL on the same missing types.

- [ ] **Step 4: Implement the Core pieces.** Append to
  `ProviderContracts.cs`:

```csharp
public sealed record ArtworkCandidate(Uri Url, string SourceName);

public interface IArtworkSource
{
    Task<ArtworkCandidate?> FindAsync(ArtworkHints hints, CancellationToken token);
}
```

`FallbackArtworkSource.cs`:

```csharp
using System.Runtime.CompilerServices;

namespace DesktopGuides.Core.Providers;

public sealed class FallbackArtworkSource(params IArtworkSource[] sources)
{
    public async IAsyncEnumerable<ArtworkCandidate> FindCandidatesAsync(
        ArtworkHints hints, [EnumeratorCancellation] CancellationToken token)
    {
        foreach (IArtworkSource source in sources)
        {
            token.ThrowIfCancellationRequested();
            ArtworkCandidate? candidate;
            try
            {
                candidate = await source.FindAsync(hints, token);
            }
            catch (ProviderException)
            {
                continue;
            }
            if (candidate is not null) yield return candidate;
        }
    }
}
```

- [ ] **Step 5: Implement the Infrastructure sources**

`IgdbCoverArtworkSource.cs`:

```csharp
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Infrastructure.Providers;

public sealed class IgdbCoverArtworkSource : IArtworkSource
{
    public Task<ArtworkCandidate?> FindAsync(ArtworkHints hints, CancellationToken token) =>
        Task.FromResult(hints.IgdbCoverImageId is { } id
            ? new ArtworkCandidate(new Uri($"https://images.igdb.com/igdb/image/upload/t_cover_big/{id}.jpg"), "IGDB")
            : null);
}
```

  `IgdbClient` has already restricted the image ID to `^[a-z0-9]{1,40}$`
  (Task 7).

`SteamGridDbArtworkSource.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Infrastructure.Providers;

public sealed class SteamGridDbArtworkSource(
    ProviderHttp http, Func<CancellationToken, Task<string?>> apiKey) : IArtworkSource
{
    private const string Api = "https://www.steamgriddb.com/api/v2/";

    public async Task<ArtworkCandidate?> FindAsync(ArtworkHints hints, CancellationToken token)
    {
        if (await apiKey(token) is not { Length: > 0 } key) return null;
        if (hints.SteamAppId is { } appId &&
            await FirstGridAsync($"grids/steam/{appId}?dimensions=600x900", key, token) is { } bySteam)
        {
            return bySteam;
        }
        using JsonDocument? search = await GetAsync(
            $"search/autocomplete/{Uri.EscapeDataString(hints.Title)}", key, token);
        long? gameId = Data(search)
            .Where(e => ReadString(e, "name") is { } name &&
                string.Equals(name.Trim(), hints.Title, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.Number &&
                id.TryGetInt64(out long value) && value > 0 ? value : (long?)null)
            .FirstOrDefault(id => id is not null);
        return gameId is { } found
            ? await FirstGridAsync($"grids/game/{found}?dimensions=600x900", key, token)
            : null;
    }

    private async Task<ArtworkCandidate?> FirstGridAsync(string path, string key, CancellationToken token)
    {
        using JsonDocument? grids = await GetAsync(path, key, token);
        foreach (JsonElement grid in Data(grids))
        {
            if (ReadString(grid, "url") is { } url && Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
                uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && ProviderHttp.AllowedHosts.Contains(uri.IdnHost))
            {
                return new ArtworkCandidate(uri, "SteamGridDB");
            }
        }
        return null;
    }

    private async Task<JsonDocument?> GetAsync(string path, string key, CancellationToken token)
    {
        ProviderResponse response = await http.SendAsync(HttpMethod.Get, new Uri(Api + path), request =>
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }, ProviderHttp.MaxJsonBytes, token);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                try { return JsonDocument.Parse(response.Body); }
                catch (JsonException) { throw new ProviderException(ProviderErrorKind.MalformedData, "SteamGridDB returned data the app couldn't read."); }
            case HttpStatusCode.NotFound:
                return null;
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                throw new ProviderException(ProviderErrorKind.InvalidCredentials, "SteamGridDB rejected the API key.");
            default:
                throw new ProviderException(ProviderErrorKind.Unavailable, "SteamGridDB could not complete the request.");
        }
    }

    private static IEnumerable<JsonElement> Data(JsonDocument? document) =>
        document?.RootElement is { ValueKind: JsonValueKind.Object } root &&
        root.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array
            ? data.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToArray()
            : [];

    private static string? ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
```

- [ ] **Step 6: Run the tests**

Run both test commands from the file-structure section.
Expected: PASS, 0 failed, in both projects.

- [ ] **Step 7: Commit** (after message approval)

```bash
git add src tests
git commit -m "feat(providers): add SteamGridDB and IGDB cover sources with ordered fallback"
```

### Task 9: Provider game importer

**Files:**
- Create: `src/DesktopGuides.Core/Providers/ProviderGameImporter.cs`
- Test: `tests/DesktopGuides.Core.Tests/Providers/ImporterFakes.cs`,
  `tests/DesktopGuides.Core.Tests/Providers/ProviderGameImporterTests.cs`

**Interfaces:**
- Consumes:
  - Task 2: `ProviderGameRecord`, `GameMetadataSnapshot` and `ProviderGameLink`.
  - Task 4: `FindLinkedGameAsync`, `AddLinkedGameAsync`,
    `UpdateGameMetadataAsync`, `NewLinkedGame` and
    `DuplicateProviderLinkException`.
  - Task 5: `IArtworkStore` and `StoredArtwork`.
  - Task 7: `IGameMetadataProvider`.
  - Task 8: `FallbackArtworkSource` and `ArtworkCandidate`.
- Produces:
  - `record ProviderAddResult(Game Game, bool AlreadyInLibrary, bool ArtworkMissing)`
  - `record ProviderRefreshResult(Game Game, bool ArtworkMissing)`
  - `sealed class ProviderGameImporter(ILibraryRepository repository, IGameMetadataProvider provider, FallbackArtworkSource artwork, IArtworkStore store, Func<Uri, CancellationToken, Task<byte[]>> download, TimeProvider? clock = null)` with:
    - `Task<ProviderAddResult> AddAsync(string externalId, CancellationToken token)`
    - `Task<ProviderRefreshResult> RefreshAsync(Guid gameId, CancellationToken token)`

  Production passes `ProviderHttp.GetImageAsync` as `download`. Core
  cannot reference `ProviderHttp`, so it takes a delegate, in the same
  style as the credential delegates in Tasks 7 and 8.

**Ruling T9-a (a failed candidate moves on):** Per Ruling T8-a,
`ProviderException` from `download` and `InvalidDataException` from
`StoreAsync` skip to the next candidate. Only cancellation propagates.

**Ruling T9-b (refresh with no artwork):** The spec doesn't say what
refresh does when every source fails. Refresh keeps the current file and
its `ArtworkSource`, and reports `ArtworkMissing` only if the game has no
artwork at all. A connection failure should not remove a cover that was
already downloaded. Cost if wrong: an out-of-date cover stays until the
next successful refresh.

**Ruling T9-c (the file is cleaned up if it isn't committed):** The
importer deletes a newly stored file on every path where the database
does not come to reference it. That covers cancellation, a unique-index
race, a failed update and any other exception. The one exception is a
refresh that stored the same file the game already references (same
content, same path). The startup sweep (Task 5) covers a crash between
store and commit.

- [ ] **Step 1: Write the fakes** (`ImporterFakes.cs`)

```csharp
using System.Security.Cryptography;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Core.Tests.Providers;

internal sealed class FakeRepository : ILibraryRepository
{
    public Dictionary<Guid, Game> Games { get; } = [];
    public Guid? RaceWinner { get; set; }
    public bool FailUpdates { get; set; }
    public int FindCalls { get; private set; }

    public Task<Game?> FindLinkedGameAsync(string provider, string externalId, CancellationToken token = default)
    {
        FindCalls++;
        return Task.FromResult(Games.Values.FirstOrDefault(g =>
            g.Link?.Provider == provider && g.Link.ExternalId == externalId));
    }

    public Task<Game> AddLinkedGameAsync(NewLinkedGame game, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (RaceWinner is { } winner) throw new DuplicateProviderLinkException(winner);
        Game added = new(game.Id, game.Title, game.Platform, null, game.Link.RetrievedUtc,
            game.Link.RetrievedUtc, game.Link, game.Metadata, game.ArtworkRelativePath);
        Games.Add(added.Id, added);
        return Task.FromResult(added);
    }

    public Task<Game> UpdateGameMetadataAsync(Guid gameId, GameMetadataSnapshot metadata,
        DateTimeOffset retrievedUtc, string? artworkRelativePath, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (FailUpdates) throw new InvalidOperationException("update failed");
        Game current = Games[gameId];
        Game updated = current with
        {
            Link = current.Link! with { RetrievedUtc = retrievedUtc },
            Metadata = metadata,
            ArtworkRelativePath = artworkRelativePath,
            UpdatedUtc = retrievedUtc,
        };
        Games[gameId] = updated;
        return Task.FromResult(updated);
    }

    public Task<Game?> GetGameAsync(Guid gameId, CancellationToken token = default) =>
        Task.FromResult(Games.GetValueOrDefault(gameId));

    public StartupReconciliationReport? LastStartupReconciliation => null;
    public Task InitializeAsync(CancellationToken token = default) => throw new NotSupportedException();
    public Task<Game> AddGameAsync(string title, string? platform, string? notes, CancellationToken token = default) => throw new NotSupportedException();
    public Task<Game> UpdateGameAsync(Guid gameId, string title, string? platform, string? notes, CancellationToken token = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Game>> ListGamesAsync(CancellationToken token = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Guide>> ListGuidesAsync(Guid gameId, CancellationToken token = default) => throw new NotSupportedException();
    public Task<Guide?> GetGuideAsync(Guid guideId, CancellationToken token = default) => throw new NotSupportedException();
    public Task<ReadingState?> GetReadingStateAsync(Guid guideId, CancellationToken token = default) => throw new NotSupportedException();
    public Task SaveReadingLocationAsync(Guid guideId, string locatorJson, double? estimatedFraction, CancellationToken token = default) => throw new NotSupportedException();
    public Task<ReaderPreferences?> GetReaderPreferencesAsync(Guid guideId, CancellationToken token = default) => throw new NotSupportedException();
    public Task SaveReaderPreferencesAsync(Guid guideId, double? textScale, CancellationToken token = default) => throw new NotSupportedException();
    public Task<AppSettings> GetSettingsAsync(CancellationToken token = default) => throw new NotSupportedException();
    public Task SaveSettingsAsync(AppSettings settings, CancellationToken token = default) => throw new NotSupportedException();
    public Task<AppSettings> UpdateSettingsAsync(Func<AppSettings, AppSettings> update, CancellationToken token = default) => throw new NotSupportedException();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeStore : IArtworkStore
{
    public Dictionary<string, byte[]> Files { get; } = [];
    public List<string> Deleted { get; } = [];
    public Action? OnStored { get; set; }

    public Task<StoredArtwork> StoreAsync(Guid gameId, byte[] content, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (content.Length == 0) throw new InvalidDataException("not an image");
        string sha = Convert.ToHexStringLower(SHA256.HashData(content));
        string path = $"artwork/{gameId:N}/{sha}.png";
        Files[path] = content;
        OnStored?.Invoke();
        return Task.FromResult(new StoredArtwork(path, sha));
    }

    public void Delete(string relativePath)
    {
        Deleted.Add(relativePath);
        Files.Remove(relativePath);
    }

    public string? ResolveFile(string relativePath) => Files.ContainsKey(relativePath) ? relativePath : null;
}

internal sealed class FakeProvider(params ProviderGameRecord[] records) : IGameMetadataProvider
{
    public ProviderException? Failure { get; set; }
    public int GetCalls { get; private set; }

    public Task<IReadOnlyList<ProviderSearchResult>> SearchAsync(string query, CancellationToken token) =>
        throw new NotSupportedException();

    public Task<ProviderGameRecord> GetAsync(string externalId, CancellationToken token)
    {
        GetCalls++;
        token.ThrowIfCancellationRequested();
        if (Failure is not null) throw Failure;
        return Task.FromResult(records.Single(r => r.ExternalId == externalId));
    }
}

internal sealed class FixedSource(ArtworkCandidate? candidate) : IArtworkSource
{
    public Task<ArtworkCandidate?> FindAsync(ArtworkHints hints, CancellationToken token) => Task.FromResult(candidate);
}
```

  `FakeStore` names files by content, as `ManagedArtworkStore` does, so
  identical bytes give an identical path. An empty array plays the part
  of an invalid image. `OnStored` runs after a file is in place, which
  lets a test cancel between the store and the commit.

- [ ] **Step 2: Write the failing importer tests** (`ProviderGameImporterTests.cs`)

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;
using Xunit;

namespace DesktopGuides.Core.Tests.Providers;

public sealed class ProviderGameImporterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly ArtworkCandidate Grid = new(new Uri("https://cdn2.steamgriddb.com/grid/a.png"), "SteamGridDB");
    private static readonly ArtworkCandidate Cover = new(new Uri("https://images.igdb.com/igdb/image/upload/t_cover_big/co1.jpg"), "IGDB");

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ProviderGameRecord Record(params string[] platforms) => new("70", "Half-Life",
        new GameMetadataSnapshot(GameMetadataSnapshot.CurrentSchemaVersion, "Summary", new DateOnly(1998, 11, 19),
            ["Shooter"], ["Valve"], ["Sierra"], platforms, "https://www.igdb.com/games/half-life", GameTypeTag.MainGame),
        new ArtworkHints("Half-Life", "70", "co1"));

    private sealed class Harness
    {
        public FakeRepository Repository { get; } = new();
        public FakeStore Store { get; } = new();
        public FakeProvider Provider { get; init; } = new(Record("PC (Windows)"));
        public Dictionary<Uri, byte[]> Images { get; } = new() { [Grid.Url] = [1, 2, 3], [Cover.Url] = [4, 5, 6] };
        public List<Uri> Downloads { get; } = [];
        public Action? OnDownload { get; set; }
        public Clock Clock { get; } = new(Now);
        public ArtworkCandidate?[] Candidates { get; init; } = [Grid, Cover];

        public ProviderGameImporter Create() => new(Repository, Provider,
            new FallbackArtworkSource(Candidates.Select(c => (IArtworkSource)new FixedSource(c)).ToArray()),
            Store, (uri, token) =>
            {
                Downloads.Add(uri);
                OnDownload?.Invoke();
                token.ThrowIfCancellationRequested();
                return Images.TryGetValue(uri, out byte[]? bytes)
                    ? Task.FromResult(bytes)
                    : throw new ProviderException(ProviderErrorKind.Unavailable, "no image");
            }, Clock);
    }

    [Fact]
    public async Task AddCreatesOneLinkedGameWithArtworkAndItsOnlyPlatform()
    {
        Harness h = new();

        ProviderAddResult result = await h.Create().AddAsync("70", default);

        Game game = Assert.Single(h.Repository.Games.Values);
        Assert.Equal(game, result.Game);
        Assert.False(result.AlreadyInLibrary);
        Assert.False(result.ArtworkMissing);
        Assert.Equal(("Half-Life", "PC (Windows)"), (game.Title, game.Platform));
        Assert.Equal(new ProviderGameLink(ProviderGameLink.Igdb, "70", Now), game.Link);
        Assert.Equal("SteamGridDB", game.Metadata!.ArtworkSource);
        Assert.Equal(game.ArtworkRelativePath, Assert.Single(h.Store.Files.Keys));
        Assert.StartsWith($"artwork/{game.Id:N}/", game.ArtworkRelativePath);
    }

    [Fact]
    public async Task AddLeavesPlatformEmptyWhenSeveralAreListed()
    {
        Harness h = new() { Provider = new FakeProvider(Record("PC (Windows)", "PlayStation 2")) };
        Assert.Null((await h.Create().AddAsync("70", default)).Game.Platform);
    }

    [Fact]
    public async Task AddOfAnAlreadyLinkedIdOpensTheExistingGameWithoutFetching()
    {
        Harness h = new();
        Game existing = (await h.Create().AddAsync("70", default)).Game;

        ProviderAddResult again = await h.Create().AddAsync("70", default);

        Assert.True(again.AlreadyInLibrary);
        Assert.Equal(existing.Id, again.Game.Id);
        Assert.Equal(1, h.Provider.GetCalls);
        Assert.Single(h.Repository.Games);
    }

    [Fact]
    public async Task AddThatLosesAUniqueIndexRaceDeletesItsFileAndOpensTheWinner()
    {
        Harness h = new();
        Game winner = new(Guid.NewGuid(), "Half-Life", null, null, Now, Now, new ProviderGameLink("igdb", "70", Now));
        h.Repository.RaceWinner = winner.Id;
        h.OnDownload = () => h.Repository.Games[winner.Id] = winner;

        ProviderAddResult result = await h.Create().AddAsync("70", default);

        Assert.True(result.AlreadyInLibrary);
        Assert.Equal(winner, result.Game);
        Assert.Empty(h.Store.Files);
        Assert.Single(h.Store.Deleted);
    }

    [Fact]
    public async Task AddWithNoArtworkCandidateSavesGameWithNotice()
    {
        Harness h = new() { Candidates = [null, null] };

        ProviderAddResult result = await h.Create().AddAsync("70", default);

        Assert.True(result.ArtworkMissing);
        Assert.Null(result.Game.ArtworkRelativePath);
        Assert.Null(result.Game.Metadata!.ArtworkSource);
        Assert.Single(h.Repository.Games);
        Assert.Empty(h.Store.Files);
        Assert.Empty(h.Downloads);
    }

    [Fact]
    public async Task AddFallsBackToTheNextCandidateWhenADownloadOrImageFails()
    {
        Harness h = new();
        h.Images[Grid.Url] = [];

        ProviderAddResult result = await h.Create().AddAsync("70", default);

        Assert.Equal([Grid.Url, Cover.Url], h.Downloads);
        Assert.Equal("IGDB", result.Game.Metadata!.ArtworkSource);
        Assert.False(result.ArtworkMissing);

        Harness offline = new();
        offline.Images.Clear();
        ProviderAddResult none = await offline.Create().AddAsync("70", default);
        Assert.True(none.ArtworkMissing);
        Assert.Single(offline.Repository.Games);
    }

    [Fact]
    public async Task MalformedRecordStopsTheAddWithNothingCreated()
    {
        Harness h = new() { Provider = new FakeProvider { Failure = new(ProviderErrorKind.MalformedData, "bad") } };

        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => h.Create().AddAsync("70", default));

        Assert.Equal(ProviderErrorKind.MalformedData, error.Kind);
        Assert.Empty(h.Repository.Games);
        Assert.Empty(h.Downloads);
    }

    [Theory]
    [InlineData("fetch")]
    [InlineData("artwork")]
    [InlineData("commit")]
    public async Task CancelLeavesNoRowAndNoFile(string stage)
    {
        using CancellationTokenSource cancel = new();
        Harness h = new();
        switch (stage)
        {
            case "fetch": cancel.Cancel(); break;
            case "artwork": h.OnDownload = cancel.Cancel; break;
            case "commit": h.Store.OnStored = cancel.Cancel; break;
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Create().AddAsync("70", cancel.Token));

        Assert.Empty(h.Repository.Games);
        Assert.Empty(h.Store.Files);
    }
}
```


- [ ] **Step 3: Add the refresh tests** to the same class, above its
  closing brace:

```csharp
    private static async Task<(Harness H, Game Game)> LinkedWithLocalEdits()
    {
        Harness h = new();
        Game added = (await h.Create().AddAsync("70", default)).Game;
        Game edited = added with { Title = "My Half-Life", Platform = "PlayStation 2", Notes = "mine" };
        h.Repository.Games[added.Id] = edited;
        h.Clock.Now = Now.AddDays(1);
        return (h, edited);
    }

    [Fact]
    public async Task RefreshKeepsLocalFieldsAndDeletesTheOldFileAfterCommit()
    {
        (Harness h, Game before) = await LinkedWithLocalEdits();
        h.Images[Grid.Url] = [9, 9, 9];

        ProviderRefreshResult result = await h.Create().RefreshAsync(before.Id, default);

        Game after = h.Repository.Games[before.Id];
        Assert.Equal(after, result.Game);
        Assert.Equal(("My Half-Life", "PlayStation 2", "mine"), (after.Title, after.Platform, after.Notes));
        Assert.Equal(Now.AddDays(1), after.Link!.RetrievedUtc);
        Assert.NotEqual(before.ArtworkRelativePath, after.ArtworkRelativePath);
        Assert.Equal([before.ArtworkRelativePath!], h.Store.Deleted);
        Assert.Equal(after.ArtworkRelativePath, Assert.Single(h.Store.Files.Keys));
    }

    [Fact]
    public async Task RefreshWithIdenticalImageKeepsCurrentFile()
    {
        (Harness h, Game before) = await LinkedWithLocalEdits();

        ProviderRefreshResult result = await h.Create().RefreshAsync(before.Id, default);

        Assert.Equal(before.ArtworkRelativePath, result.Game.ArtworkRelativePath);
        Assert.Empty(h.Store.Deleted);
        Assert.True(h.Store.Files.ContainsKey(before.ArtworkRelativePath!));
    }

    [Fact]
    public async Task RefreshWithNoArtworkKeepsTheCurrentFileAndSource()
    {
        (Harness h, Game before) = await LinkedWithLocalEdits();
        h.Images.Clear();

        ProviderRefreshResult result = await h.Create().RefreshAsync(before.Id, default);

        Assert.False(result.ArtworkMissing);
        Assert.Equal(before.ArtworkRelativePath, result.Game.ArtworkRelativePath);
        Assert.Equal("SteamGridDB", result.Game.Metadata!.ArtworkSource);
        Assert.Empty(h.Store.Deleted);
    }

    [Fact]
    public async Task FailedRefreshKeepsTheRowAndRemovesOnlyTheNewFile()
    {
        (Harness h, Game before) = await LinkedWithLocalEdits();
        h.Images[Grid.Url] = [9, 9, 9];
        h.Repository.FailUpdates = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Create().RefreshAsync(before.Id, default));

        Assert.Equal(before, h.Repository.Games[before.Id]);
        Assert.Equal(before.ArtworkRelativePath, Assert.Single(h.Store.Files.Keys));
        Assert.DoesNotContain(before.ArtworkRelativePath!, h.Store.Deleted);
        Assert.Single(h.Store.Deleted);
    }

    [Fact]
    public async Task RefreshRejectsAnUnlinkedOrMissingGameWithoutARequest()
    {
        Harness h = new();
        Game manual = new(Guid.NewGuid(), "Manual", null, null, Now, Now);
        h.Repository.Games[manual.Id] = manual;

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Create().RefreshAsync(manual.Id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => h.Create().RefreshAsync(Guid.NewGuid(), default));
        Assert.Equal(0, h.Provider.GetCalls);
    }
```

- [ ] **Step 4: Run the tests and watch them fail**

Run: `dotnet test tests/DesktopGuides.Core.Tests/DesktopGuides.Core.Tests.csproj -c Release --filter FullyQualifiedName~ProviderGameImporterTests`
Expected: build FAIL, `The type or namespace name 'ProviderGameImporter' could not be found`.

- [ ] **Step 5: Implement `ProviderGameImporter.cs`**

```csharp
using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Providers;

public sealed record ProviderAddResult(Game Game, bool AlreadyInLibrary, bool ArtworkMissing);

public sealed record ProviderRefreshResult(Game Game, bool ArtworkMissing);

public sealed class ProviderGameImporter(
    ILibraryRepository repository,
    IGameMetadataProvider provider,
    FallbackArtworkSource artwork,
    IArtworkStore store,
    Func<Uri, CancellationToken, Task<byte[]>> download,
    TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    public async Task<ProviderAddResult> AddAsync(string externalId, CancellationToken token)
    {
        if (await repository.FindLinkedGameAsync(ProviderGameLink.Igdb, externalId, token) is { } existing)
        {
            return new ProviderAddResult(existing, AlreadyInLibrary: true, ArtworkMissing: false);
        }
        ProviderGameRecord record = await provider.GetAsync(externalId, token);
        Guid id = Guid.NewGuid();
        (StoredArtwork? stored, string? source) = await StoreArtworkAsync(id, record.Hints, token);
        try
        {
            token.ThrowIfCancellationRequested();
            Game game = await repository.AddLinkedGameAsync(new NewLinkedGame(
                id,
                record.Title,
                record.Snapshot.Platforms is [string only] ? only : null,
                new ProviderGameLink(ProviderGameLink.Igdb, record.ExternalId, clock.GetUtcNow()),
                record.Snapshot with { ArtworkSource = source },
                stored?.RelativePath), token);
            return new ProviderAddResult(game, AlreadyInLibrary: false, ArtworkMissing: stored is null);
        }
        catch (DuplicateProviderLinkException duplicate)
        {
            Delete(stored);
            Game winner = await repository.GetGameAsync(duplicate.ExistingGameId, CancellationToken.None)
                ?? throw new InvalidOperationException("The game that holds this provider link was removed.");
            return new ProviderAddResult(winner, AlreadyInLibrary: true, ArtworkMissing: false);
        }
        catch
        {
            Delete(stored);
            throw;
        }
    }

    public async Task<ProviderRefreshResult> RefreshAsync(Guid gameId, CancellationToken token)
    {
        Game current = await repository.GetGameAsync(gameId, token)
            ?? throw new KeyNotFoundException("The game no longer exists.");
        if (current.Link is not { } link)
        {
            throw new InvalidOperationException("Only a game added from search can be refreshed.");
        }
        ProviderGameRecord record = await provider.GetAsync(link.ExternalId, token);
        (StoredArtwork? stored, string? source) = await StoreArtworkAsync(gameId, record.Hints, token);
        StoredArtwork? added = stored?.RelativePath == current.ArtworkRelativePath ? null : stored;
        string? path = stored?.RelativePath ?? current.ArtworkRelativePath;
        GameMetadataSnapshot snapshot = record.Snapshot with
        {
            ArtworkSource = stored is null ? current.Metadata?.ArtworkSource : source,
        };
        Game updated;
        try
        {
            token.ThrowIfCancellationRequested();
            updated = await repository.UpdateGameMetadataAsync(gameId, snapshot, clock.GetUtcNow(), path, token);
        }
        catch
        {
            Delete(added);
            throw;
        }
        if (added is not null && current.ArtworkRelativePath is { } previous) store.Delete(previous);
        return new ProviderRefreshResult(updated, ArtworkMissing: path is null);
    }

    private async Task<(StoredArtwork? Stored, string? Source)> StoreArtworkAsync(
        Guid gameId, ArtworkHints hints, CancellationToken token)
    {
        await foreach (ArtworkCandidate candidate in artwork.FindCandidatesAsync(hints, token))
        {
            try
            {
                byte[] content = await download(candidate.Url, token);
                return (await store.StoreAsync(gameId, content, token), candidate.SourceName);
            }
            catch (Exception error) when (error is ProviderException or InvalidDataException)
            {
                // Ruling T9-a: try the next candidate.
            }
        }
        return (null, null);
    }

    private void Delete(StoredArtwork? stored)
    {
        if (stored is not null) store.Delete(stored.RelativePath);
    }
}
```

  `Delete` never throws (Task 5), so a cleanup failure cannot hide the
  original exception. If the process dies before cleanup runs, the
  startup sweep removes the file, because no row references it.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/DesktopGuides.Core.Tests/DesktopGuides.Core.Tests.csproj -c Release`
Expected: PASS, 0 failed.

- [ ] **Step 7: Commit** (after message approval)

```bash
git add src/DesktopGuides.Core tests/DesktopGuides.Core.Tests
git commit -m "feat(providers): add importer for search add and metadata refresh"
```

### Task 10: Credential storage, connection tests and the Settings card

**Files:**
- Modify: `src/DesktopGuides.Core/Providers/ProviderContracts.cs`,
  `src/DesktopGuides.Infrastructure/Providers/IgdbClient.cs`,
  `src/DesktopGuides.Infrastructure/Providers/SteamGridDbArtworkSource.cs`,
  `src/DesktopGuides.Production/ShellWindow.xaml(.cs)`
- Create: `src/DesktopGuides.Infrastructure/Providers/ProviderCredentialBlob.cs`,
  `src/DesktopGuides.Production/Providers/WindowsProviderCredentialStore.cs`,
  `src/DesktopGuides.Production/Providers/ProviderServices.cs`,
  `src/DesktopGuides.Production/ProviderSettingsCard.xaml(.cs)`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Providers/ProviderCredentialBlobTests.cs`,
  plus additions to `IgdbClientTests.cs` and `ArtworkSourceTests.cs`

**Interfaces:**
- Consumes:
  - Task 6: `IgdbCredentials`, `ProviderCredentials`, `ProviderHttp` and
    `TwitchTokenSource`.
  - Task 7: `IgdbClient`.
  - Task 8: `SteamGridDbArtworkSource`.
- Produces:
  - `interface IProviderCredentialStore` with:
    - `Task<ProviderCredentials> LoadAsync(CancellationToken token)`, which
      returns `ProviderCredentials.None` when nothing is saved or the blob
      is unreadable
    - `Task SaveAsync(ProviderCredentials credentials, CancellationToken token)`
    - `Task ClearAsync(CancellationToken token)`
  - `static class ProviderCredentialBlob` with:
    - `byte[] Format(ProviderCredentials credentials)`, which throws
      `ArgumentException` naming the field (never the value)
    - `ProviderCredentials Parse(ReadOnlySpan<byte> data)`
    - `ProviderCredentials Normalize(string? clientId, string? clientSecret, string? steamGridDbKey)`
    - constants `MaxBytes = 4096`, `MaxClientIdLength = 64` and
      `MaxSecretLength = 128`
  - `Task IgdbClient.TestConnectionAsync(CancellationToken token)`, which
    forces a fresh token and posts `fields id; limit 1;`
  - `Task SteamGridDbArtworkSource.TestConnectionAsync(CancellationToken token)`,
    which makes one authenticated request. A 404 counts as success, since
    it means the key was accepted.
  - Production `sealed class ProviderServices : IDisposable`, which owns
    the one `ProviderHttp`, the `TwitchTokenSource` and the credential
    store. Task 11 adds the importer to it.

**Ruling T10-a (expander, not a single card):** The spec names a "Game
data providers `SettingsCard`". Three fields and three buttons don't fit
in a single card's right-aligned content area. The plan uses a
`SettingsExpander` with that header (from the SettingsControls package
already referenced), holding one `SettingsCard` per field and one for the
actions. It is the same toolkit family, and the header, fields and
buttons match the spec.

**Ruling T10-b (field rules):** All three values are opaque tokens.
After trimming, they must be printable ASCII with no whitespace. The
client ID can be up to 64 characters; the secret and the key can each be
up to 128. An empty field on Save keeps the value already saved, which is
the only way a field that shows "Saved" can be left alone. The IGDB ID
and secret are saved together or not at all.

- [ ] **Step 1: Write the failing blob tests**

```csharp
using System.Text;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Providers;

public sealed class ProviderCredentialBlobTests
{
    private static readonly ProviderCredentials Full = new(new IgdbCredentials("abc123", "s3cret-value"), "sgdb-key-1");

    [Fact]
    public void RoundTripsEveryField()
    {
        Assert.Equal(Full, ProviderCredentialBlob.Parse(ProviderCredentialBlob.Format(Full)));
        ProviderCredentials igdbOnly = Full with { SteamGridDbKey = null };
        Assert.Equal(igdbOnly, ProviderCredentialBlob.Parse(ProviderCredentialBlob.Format(igdbOnly)));
        Assert.Equal(ProviderCredentials.None,
            ProviderCredentialBlob.Parse(ProviderCredentialBlob.Format(ProviderCredentials.None)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"v":2,"igdb":{"clientId":"a","clientSecret":"b"}}""")]
    [InlineData("""{"v":1,"igdb":{"clientId":"a"}}""")]
    [InlineData("""{"v":1,"igdb":{"clientId":"a b","clientSecret":"b"}}""")]
    [InlineData("""{"v":1,"steamGridDbKey":7}""")]
    public void UnreadableBlobIsNotConfigured(string json) =>
        Assert.Equal(ProviderCredentials.None, ProviderCredentialBlob.Parse(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void OversizedBlobIsNotConfigured()
    {
        byte[] big = Encoding.UTF8.GetBytes($$"""{"v":1,"steamGridDbKey":"{{new string('k', ProviderCredentialBlob.MaxBytes)}}"}""");
        Assert.Equal(ProviderCredentials.None, ProviderCredentialBlob.Parse(big));
    }

    [Fact]
    public void NormalizeTrimsAndTreatsBlankAsUnset()
    {
        Assert.Equal(Full, ProviderCredentialBlob.Normalize(" abc123 ", "s3cret-value\t", "sgdb-key-1"));
        Assert.Equal(ProviderCredentials.None, ProviderCredentialBlob.Normalize("", "  ", null));
    }

    [Theory]
    [InlineData("abc123", null, null, "IGDB client secret")]
    [InlineData(null, "s3cret-value", null, "IGDB client ID")]
    [InlineData("abc 123", "s3cret-value", null, "IGDB client ID")]
    [InlineData("abc123", "s3creté", null, "IGDB client secret")]
    [InlineData(null, null, "key\u0001", "SteamGridDB API key")]
    public void NormalizeNamesTheBadFieldWithoutEchoingIt(string? id, string? secret, string? key, string field)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => ProviderCredentialBlob.Normalize(id, secret, key));
        Assert.Contains(field, error.Message);
        foreach (string? value in new[] { id, secret, key })
        {
            if (value is { Length: > 3 }) Assert.DoesNotContain(value.Trim(), error.ToString());
        }
    }

    [Fact]
    public void NormalizeEnforcesLengthLimits()
    {
        Assert.Throws<ArgumentException>(() => ProviderCredentialBlob.Normalize(new string('a', 65), "s", null));
        Assert.Throws<ArgumentException>(() => ProviderCredentialBlob.Normalize("a", new string('s', 129), null));
        Assert.Throws<ArgumentException>(() => ProviderCredentialBlob.Normalize(null, null, new string('k', 129)));
        Assert.NotNull(ProviderCredentialBlob.Normalize(new string('a', 64), new string('s', 128), new string('k', 128)).Igdb);
    }
}
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test tests/DesktopGuides.Infrastructure.Tests/DesktopGuides.Infrastructure.Tests.csproj -c Release --filter FullyQualifiedName~ProviderCredentialBlobTests`
Expected: build FAIL, `The name 'ProviderCredentialBlob' does not exist in the current context`.

- [ ] **Step 3: Add the store interface** to `ProviderContracts.cs`:

```csharp
public interface IProviderCredentialStore
{
    Task<ProviderCredentials> LoadAsync(CancellationToken token);
    Task SaveAsync(ProviderCredentials credentials, CancellationToken token);
    Task ClearAsync(CancellationToken token);
}
```

- [ ] **Step 4: Implement `ProviderCredentialBlob.cs`**

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Infrastructure.Providers;

public static class ProviderCredentialBlob
{
    public const int MaxBytes = 4096;
    public const int MaxClientIdLength = 64;
    public const int MaxSecretLength = 128;
    private const int Version = 1;

    public static ProviderCredentials Normalize(string? clientId, string? clientSecret, string? steamGridDbKey)
    {
        string? id = Field(clientId, MaxClientIdLength, "IGDB client ID");
        string? secret = Field(clientSecret, MaxSecretLength, "IGDB client secret");
        string? key = Field(steamGridDbKey, MaxSecretLength, "SteamGridDB API key");
        if ((id is null) != (secret is null))
        {
            throw new ArgumentException(id is null
                ? "Enter the IGDB client ID with the secret."
                : "Enter the IGDB client secret with the client ID.");
        }
        return new ProviderCredentials(id is null ? null : new IgdbCredentials(id, secret!), key);
    }

    public static byte[] Format(ProviderCredentials credentials)
    {
        ProviderCredentials valid = Normalize(
            credentials.Igdb?.ClientId, credentials.Igdb?.ClientSecret, credentials.SteamGridDbKey);
        JsonObject root = new() { ["v"] = Version };
        if (valid.Igdb is { } igdb)
        {
            root["igdb"] = new JsonObject { ["clientId"] = igdb.ClientId, ["clientSecret"] = igdb.ClientSecret };
        }
        if (valid.SteamGridDbKey is { } key) root["steamGridDbKey"] = key;
        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    public static ProviderCredentials Parse(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || data.Length > MaxBytes) return ProviderCredentials.None;
        try
        {
            using JsonDocument document = JsonDocument.Parse(data.ToArray());
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("v", out JsonElement version) ||
                version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int v) || v != Version)
            {
                return ProviderCredentials.None;
            }
            string? id = null, secret = null;
            if (root.TryGetProperty("igdb", out JsonElement igdb))
            {
                if (igdb.ValueKind != JsonValueKind.Object) return ProviderCredentials.None;
                id = ReadString(igdb, "clientId");
                secret = ReadString(igdb, "clientSecret");
            }
            return Normalize(id, secret, ReadString(root, "steamGridDbKey"));
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            return ProviderCredentials.None;
        }
    }

    private static string? ReadString(JsonElement item, string name) =>
        !item.TryGetProperty(name, out JsonElement value) ? null
        : value.ValueKind == JsonValueKind.String ? value.GetString()
        : throw new InvalidOperationException("Credential field has the wrong type.");

    private static string? Field(string? raw, int maxLength, string name)
    {
        string? trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"The {name} can be at most {maxLength} characters.");
        }
        if (trimmed.Any(c => c is < '!' or > '~'))
        {
            throw new ArgumentException($"The {name} can contain only letters, digits and punctuation, with no spaces.");
        }
        return trimmed;
    }
}
```

  `Field` never includes `raw` in a message, and `Parse` catches the
  exception, so a value read from a corrupt blob can never reach an
  `ArgumentException` that escapes.

- [ ] **Step 5: Run the blob tests**

Run: `dotnet test tests/DesktopGuides.Infrastructure.Tests/DesktopGuides.Infrastructure.Tests.csproj -c Release --filter FullyQualifiedName~ProviderCredentialBlobTests`
Expected: PASS, 0 failed.

- [ ] **Step 6: Write the failing connection-test and message tests**

  Add to `IgdbClientTests`:

```csharp
    [Fact]
    public async Task TestConnectionForcesAFreshTokenAndSendsOneLimitOneQuery()
    {
        (IgdbClient client, FakeHandler handler) = Create(Credentials,
            Token("t1"), FakeHandler.Json("[]"), Token("t2"), FakeHandler.Json("[]"));

        await client.TestConnectionAsync(default);
        await client.TestConnectionAsync(default);

        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal("fields id; limit 1;", handler.Requests[1].Body);
        Assert.Equal("Bearer t2", handler.Requests[3].Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task TestConnectionWithRejectedCredentialsHidesTheSecret()
    {
        (IgdbClient client, _) = Create(Credentials, new HttpResponseMessage(HttpStatusCode.BadRequest));
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => client.TestConnectionAsync(default));
        Assert.Equal(ProviderErrorKind.InvalidCredentials, error.Kind);
        Assert.DoesNotContain("very-secret", error.ToString());
    }
```

  Add to `ArtworkSourceTests`:

```csharp
    [Fact]
    public async Task SteamGridDbTestConnectionAcceptsNotFoundAndRejectsABadKey()
    {
        (SteamGridDbArtworkSource ok, FakeHandler handler) = Create("sgdb-key",
            FakeHandler.Json("""{"success":false}""", HttpStatusCode.NotFound));
        await ok.TestConnectionAsync(default);
        Assert.Equal("Bearer sgdb-key", Assert.Single(handler.Requests).Request.Headers.Authorization!.ToString());

        (SteamGridDbArtworkSource rejected, _) = Create("sgdb-key", new HttpResponseMessage(HttpStatusCode.Unauthorized));
        Assert.Equal(ProviderErrorKind.InvalidCredentials,
            (await Assert.ThrowsAsync<ProviderException>(() => rejected.TestConnectionAsync(default))).Kind);

        (SteamGridDbArtworkSource missing, FakeHandler none) = Create(null);
        Assert.Equal(ProviderErrorKind.NotConfigured,
            (await Assert.ThrowsAsync<ProviderException>(() => missing.TestConnectionAsync(default))).Kind);
        Assert.Empty(none.Requests);
    }
```

  Create `tests/DesktopGuides.Core.Tests/Providers/ProviderMessagesTests.cs`.
  The expected strings are the spec's error table, copied exactly:

```csharp
using DesktopGuides.Core.Providers;
using Xunit;

namespace DesktopGuides.Core.Tests.Providers;

public sealed class ProviderMessagesTests
{
    [Theory]
    [InlineData(ProviderErrorKind.NotConfigured, "Add IGDB credentials in Settings to search.")]
    [InlineData(ProviderErrorKind.InvalidCredentials, "IGDB rejected the client ID or secret.")]
    [InlineData(ProviderErrorKind.Unavailable, "Can't reach IGDB. Check your connection.")]
    [InlineData(ProviderErrorKind.Timeout, "IGDB took too long to respond.")]
    [InlineData(ProviderErrorKind.RateLimited, "IGDB is busy. Try again in a moment.")]
    [InlineData(ProviderErrorKind.MalformedData, "IGDB returned data the app couldn't read.")]
    public void IgdbMessagesMatchTheSpecTable(ProviderErrorKind kind, string expected) =>
        Assert.Equal(expected, ProviderMessages.ForIgdb(kind));

    [Theory]
    [InlineData(ProviderErrorKind.InvalidCredentials, "SteamGridDB rejected the API key.")]
    [InlineData(ProviderErrorKind.Unavailable, "Can't reach SteamGridDB. Check your connection.")]
    [InlineData(ProviderErrorKind.Timeout, "Can't reach SteamGridDB. Check your connection.")]
    public void SteamGridDbMessagesNameTheService(ProviderErrorKind kind, string expected) =>
        Assert.Equal(expected, ProviderMessages.ForSteamGridDb(kind));

    [Fact]
    public void NoResultsMessageQuotesTheQuery() =>
        Assert.Equal("No games match \"zelda\".", ProviderMessages.NoResults("zelda"));
}
```

- [ ] **Step 7: Run the tests and watch them fail**

Run both test commands from the file-structure section.
Expected: build FAIL, `'IgdbClient' does not contain a definition for 'TestConnectionAsync'` and `The name 'ProviderMessages' does not exist in the current context`.

- [ ] **Step 8: Implement.** Create `src/DesktopGuides.Core/Providers/ProviderMessages.cs`:

```csharp
namespace DesktopGuides.Core.Providers;

public static class ProviderMessages
{
    public static string ForIgdb(ProviderErrorKind kind) => kind switch
    {
        ProviderErrorKind.NotConfigured => "Add IGDB credentials in Settings to search.",
        ProviderErrorKind.InvalidCredentials => "IGDB rejected the client ID or secret.",
        ProviderErrorKind.Timeout => "IGDB took too long to respond.",
        ProviderErrorKind.RateLimited => "IGDB is busy. Try again in a moment.",
        ProviderErrorKind.MalformedData => "IGDB returned data the app couldn't read.",
        _ => "Can't reach IGDB. Check your connection.",
    };

    public static string ForSteamGridDb(ProviderErrorKind kind) => kind switch
    {
        ProviderErrorKind.InvalidCredentials => "SteamGridDB rejected the API key.",
        ProviderErrorKind.NotConfigured => "Add a SteamGridDB API key to test it.",
        ProviderErrorKind.MalformedData => "SteamGridDB returned data the app couldn't read.",
        _ => "Can't reach SteamGridDB. Check your connection.",
    };

    public static string NoResults(string query) => $"No games match \"{query}\".";
}
```

  In `IgdbClient`, give `PostAsync` a `bool freshToken = false`
  parameter, pass `forceRefresh: freshToken || attempt > 0`, and add:

```csharp
    public async Task TestConnectionAsync(CancellationToken token)
    {
        using JsonDocument document = await PostAsync("fields id; limit 1;", token, freshToken: true);
        RequireArray(document);
    }
```

  In `SteamGridDbArtworkSource`, add:

```csharp
    public async Task TestConnectionAsync(CancellationToken token)
    {
        string key = await apiKey(token) is { Length: > 0 } saved ? saved :
            throw new ProviderException(ProviderErrorKind.NotConfigured, "Add a SteamGridDB API key to test it.");
        using JsonDocument? _ = await GetAsync("grids/steam/70?dimensions=600x900", key, token);
    }
```

  `GetAsync` already returns null for a 404 and throws
  `InvalidCredentials` for a 401 or 403.

- [ ] **Step 9: Run the tests**

Run both test commands.
Expected: PASS, 0 failed, in both projects.

- [ ] **Step 10: Commit the tested layers** (after message approval)

```bash
git add src/DesktopGuides.Core src/DesktopGuides.Infrastructure tests
git commit -m "feat(providers): add credential blob, connection tests and error messages"
```

The remaining steps are WinUI code in `DesktopGuides.Production`, which
has no unit-test project. TDD is skipped for them for that reason. The
tested pieces are the blob, the messages and the connection calls above;
Task 12's `provider-settings` E2E covers the card and the DPAPI store.

- [ ] **Step 11: Implement `Providers/WindowsProviderCredentialStore.cs`**

```csharp
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage.Streams;

namespace DesktopGuides.Production.Providers;

internal sealed class WindowsProviderCredentialStore(string localStatePath) : IProviderCredentialStore
{
    private const int MaxProtectedBytes = 64 * 1024;
    private readonly string path = Path.Combine(localStatePath, "providers.bin");
    private readonly SemaphoreSlim gate = new(1, 1);
    private ProviderCredentials? cached;

    public async Task<ProviderCredentials> LoadAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            return cached ??= await ReadAsync(token);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(ProviderCredentials credentials, CancellationToken token)
    {
        byte[] plain = ProviderCredentialBlob.Format(credentials);
        string temp = path + ".tmp";
        try
        {
            await gate.WaitAsync(token);
            try
            {
                IBuffer protectedData = await new DataProtectionProvider("LOCAL=user").ProtectAsync(plain.AsBuffer());
                await File.WriteAllBytesAsync(temp, protectedData.ToArray(), token);
                File.Move(temp, path, overwrite: true);
                cached = ProviderCredentialBlob.Parse(plain);
            }
            finally
            {
                File.Delete(temp);
                gate.Release();
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public async Task ClearAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            File.Delete(path);
            cached = ProviderCredentials.None;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ProviderCredentials> ReadAsync(CancellationToken token)
    {
        try
        {
            FileInfo file = new(path);
            if (!file.Exists || file.Length is 0 or > MaxProtectedBytes) return ProviderCredentials.None;
            byte[] protectedData = await File.ReadAllBytesAsync(path, token);
            IBuffer plain = await new DataProtectionProvider().UnprotectAsync(protectedData.AsBuffer());
            byte[] bytes = plain.ToArray();
            try
            {
                return ProviderCredentialBlob.Parse(bytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // A blob this user can't unprotect or parse counts as not configured.
            return ProviderCredentials.None;
        }
    }
}
```

  `File.Delete` does nothing when the file is missing, so the `finally`
  block is safe after a successful move. `LOCAL=user` ties the blob to
  this Windows user on this machine, and the file lives in the package's
  `LocalState`, outside the library folder.

- [ ] **Step 12: Implement `Providers/ProviderServices.cs`**

```csharp
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;

namespace DesktopGuides.Production.Providers;

internal sealed class ProviderServices : IDisposable
{
    public ProviderServices(string localStatePath)
    {
        Http = new ProviderHttp();
        Tokens = new TwitchTokenSource(Http);
        Credentials = new WindowsProviderCredentialStore(localStatePath);
    }

    public ProviderHttp Http { get; }
    public TwitchTokenSource Tokens { get; }
    public IProviderCredentialStore Credentials { get; }

    public IgdbClient CreateIgdb(Func<CancellationToken, Task<IgdbCredentials?>> credentials) =>
        new(Http, Tokens, credentials);

    public SteamGridDbArtworkSource CreateSteamGridDb(Func<CancellationToken, Task<string?>> apiKey) =>
        new(Http, apiKey);

    public void Dispose() => Http.Dispose();
}
```

- [ ] **Step 13: Create `ProviderSettingsCard.xaml`**

```xml
<UserControl
    x:Class="DesktopGuides.Production.ProviderSettingsCard"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:toolkit="using:CommunityToolkit.WinUI.Controls">
    <toolkit:SettingsExpander
        x:Name="ProviderSettingsExpander"
        Header="Game data providers"
        Description="Add your IGDB credentials to search for games."
        HorizontalAlignment="Stretch"
        AutomationProperties.AutomationId="ProviderSettingsExpander">
        <toolkit:SettingsExpander.HeaderIcon>
            <FontIcon Glyph="&#xE721;" AutomationProperties.AccessibilityView="Raw" />
        </toolkit:SettingsExpander.HeaderIcon>
        <toolkit:SettingsExpander.Items>
            <toolkit:SettingsCard Header="IGDB client ID"
                                  Description="From your Twitch developer application.">
                <PasswordBox x:Name="IgdbClientIdInput"
                             MinWidth="260" MaxLength="64"
                             AutomationProperties.AutomationId="IgdbClientIdInput"
                             AutomationProperties.Name="IGDB client ID" />
            </toolkit:SettingsCard>
            <toolkit:SettingsCard Header="IGDB client secret">
                <PasswordBox x:Name="IgdbClientSecretInput"
                             MinWidth="260" MaxLength="128"
                             AutomationProperties.AutomationId="IgdbClientSecretInput"
                             AutomationProperties.Name="IGDB client secret" />
            </toolkit:SettingsCard>
            <toolkit:SettingsCard Header="SteamGridDB API key"
                                  Description="Optional. Used for cover art.">
                <PasswordBox x:Name="SteamGridDbKeyInput"
                             MinWidth="260" MaxLength="128"
                             AutomationProperties.AutomationId="SteamGridDbKeyInput"
                             AutomationProperties.Name="SteamGridDB API key" />
            </toolkit:SettingsCard>
            <toolkit:SettingsCard ContentAlignment="Left">
                <StackPanel Spacing="{StaticResource DesktopGuidesSpacing8}">
                    <InfoBar x:Name="ProviderSettingsStatus"
                             IsClosable="True"
                             AutomationProperties.AutomationId="ProviderSettingsStatus" />
                    <StackPanel Orientation="Horizontal" Spacing="{StaticResource DesktopGuidesSpacing8}">
                        <Button x:Name="TestConnectionButton"
                                Content="Test connection"
                                Click="TestClicked"
                                AutomationProperties.AutomationId="ProviderTestButton" />
                        <Button x:Name="SaveButton"
                                Content="Save"
                                Style="{StaticResource AccentButtonStyle}"
                                Click="SaveClicked"
                                AutomationProperties.AutomationId="ProviderSaveButton" />
                        <Button x:Name="RemoveButton"
                                Content="Remove"
                                Click="RemoveClicked"
                                AutomationProperties.AutomationId="ProviderRemoveButton" />
                        <ProgressRing x:Name="ProviderBusy"
                                      Width="20" Height="20"
                                      IsActive="False"
                                      AutomationProperties.AutomationId="ProviderBusy" />
                    </StackPanel>
                    <StackPanel Orientation="Horizontal" Spacing="{StaticResource DesktopGuidesSpacing16}">
                        <HyperlinkButton Content="Twitch developer agreement (IGDB)"
                                         NavigateUri="https://legal.twitch.com/legal/developer-agreement/"
                                         AutomationProperties.AutomationId="IgdbTermsLink" />
                        <HyperlinkButton Content="SteamGridDB terms"
                                         NavigateUri="https://www.steamgriddb.com/terms"
                                         AutomationProperties.AutomationId="SteamGridDbTermsLink" />
                    </StackPanel>
                </StackPanel>
            </toolkit:SettingsCard>
        </toolkit:SettingsExpander.Items>
    </toolkit:SettingsExpander>
</UserControl>
```

  If Task 1 found that either terms page has moved, use the URL recorded
  in the spec.

- [ ] **Step 14: Create `ProviderSettingsCard.xaml.cs`**

```csharp
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;
using DesktopGuides.Production.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed partial class ProviderSettingsCard : UserControl
{
    private ProviderServices? services;
    private ProviderCredentials saved = ProviderCredentials.None;
    private CancellationTokenSource? pending;

    public ProviderSettingsCard()
    {
        InitializeComponent();
        SetBusy(true);
    }

    internal event EventHandler? CredentialsChanged;

    internal async Task InitializeAsync(ProviderServices providerServices)
    {
        services = providerServices;
        saved = await services.Credentials.LoadAsync(CancellationToken.None);
        ShowSaved();
        SetBusy(false);
    }

    internal void Cancel() => pending?.Cancel();

    private void SaveClicked(object sender, RoutedEventArgs args) => _ = RunAsync(async token =>
    {
        ProviderCredentials next = Pending();
        await services!.Credentials.SaveAsync(next, token);
        saved = next;
        ShowSaved();
        Show(InfoBarSeverity.Success, "Provider credentials saved.");
        CredentialsChanged?.Invoke(this, EventArgs.Empty);
    });

    private void RemoveClicked(object sender, RoutedEventArgs args) => _ = RunAsync(async token =>
    {
        await services!.Credentials.ClearAsync(token);
        saved = ProviderCredentials.None;
        ShowSaved();
        Show(InfoBarSeverity.Informational, "Provider credentials removed.");
        CredentialsChanged?.Invoke(this, EventArgs.Empty);
    });

    private void TestClicked(object sender, RoutedEventArgs args) => _ = RunAsync(async token =>
    {
        ProviderCredentials test = Pending();
        if (test.Igdb is null && test.SteamGridDbKey is null)
        {
            Show(InfoBarSeverity.Informational, "Enter credentials to test them.");
            return;
        }
        List<string> results = [];
        bool failed = false;
        if (test.Igdb is { } igdb)
        {
            try
            {
                await services!.CreateIgdb(_ => Task.FromResult<IgdbCredentials?>(igdb)).TestConnectionAsync(token);
                results.Add("IGDB connected.");
            }
            catch (ProviderException error)
            {
                failed = true;
                results.Add(ProviderMessages.ForIgdb(error.Kind));
            }
        }
        if (test.SteamGridDbKey is { } key)
        {
            try
            {
                await services!.CreateSteamGridDb(_ => Task.FromResult<string?>(key)).TestConnectionAsync(token);
                results.Add("SteamGridDB connected.");
            }
            catch (ProviderException error)
            {
                failed = true;
                results.Add(ProviderMessages.ForSteamGridDb(error.Kind));
            }
        }
        Show(failed ? InfoBarSeverity.Error : InfoBarSeverity.Success, string.Join(" ", results));
    });

    // Ruling T10-b: a blank field keeps the saved value.
    private ProviderCredentials Pending() => ProviderCredentialBlob.Normalize(
        Entered(IgdbClientIdInput) ?? saved.Igdb?.ClientId,
        Entered(IgdbClientSecretInput) ?? saved.Igdb?.ClientSecret,
        Entered(SteamGridDbKeyInput) ?? saved.SteamGridDbKey);

    private static string? Entered(PasswordBox box) =>
        string.IsNullOrWhiteSpace(box.Password) ? null : box.Password;

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (services is null || pending is not null) return;
        pending = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            await action(pending.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ArgumentException error)
        {
            // Normalize names the field and never includes its value.
            Show(InfoBarSeverity.Error, error.Message);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Show(InfoBarSeverity.Error, "Couldn't update the saved provider credentials.");
        }
        finally
        {
            pending.Dispose();
            pending = null;
            SetBusy(false);
        }
    }

    private void ShowSaved()
    {
        IgdbClientIdInput.Password = IgdbClientSecretInput.Password = SteamGridDbKeyInput.Password = string.Empty;
        IgdbClientIdInput.PlaceholderText = saved.Igdb is null ? "Not set" : "Saved";
        IgdbClientSecretInput.PlaceholderText = saved.Igdb is null ? "Not set" : "Saved";
        SteamGridDbKeyInput.PlaceholderText = saved.SteamGridDbKey is null ? "Not set" : "Saved";
        string description = saved.Igdb is null
            ? "Add your IGDB credentials to search for games."
            : "IGDB credentials saved.";
        ProviderSettingsExpander.Description = description;
        AutomationProperties.SetName(ProviderSettingsExpander, $"Game data providers. {description}");
    }

    private void Show(InfoBarSeverity severity, string message)
    {
        ProviderSettingsStatus.Severity = severity;
        ProviderSettingsStatus.Message = message;
        AutomationProperties.SetName(ProviderSettingsStatus, message);
        ProviderSettingsStatus.IsOpen = true;
    }

    private void SetBusy(bool busy)
    {
        ProviderBusy.IsActive = busy;
        TestConnectionButton.IsEnabled = SaveButton.IsEnabled = !busy && services is not null;
        RemoveButton.IsEnabled = !busy && services is not null && saved != ProviderCredentials.None;
    }
}
```

  `saved != ProviderCredentials.None` uses record value equality, so it
  is true whenever any value is saved. The Settings card never reads a
  saved value back into a field.

- [ ] **Step 15: Wire the card into the shell**

  In `ShellWindow.xaml`, add this after the `WindowMaterialSettingsCard`
  element, inside the same `StackPanel`:

```xml
                        <local:ProviderSettingsCard x:Name="ProviderSettings" />
```

  In `ShellWindow.xaml.cs`:
  - Add `using DesktopGuides.Production.Providers;` and the field
    `private ProviderServices? providers;`.
  - In `InitializeCoreAsync`, after `await repository.InitializeAsync();`,
    add:

```csharp
            providers = new ProviderServices(dataRoot);
            await ProviderSettings.InitializeAsync(providers);
```

  - Where the close path cancels `leaseWait`, also call
    `ProviderSettings.Cancel();`.
  - In `CloseWhenIdleAsync`, inside the `finally` that disposes
    `repository`, add `providers?.Dispose();` after the repository is
    disposed.

- [ ] **Step 16: Build on `pcsx2-win`**

```bash
git ls-files -z --cached --others --exclude-standard |
  tar --null -T - -czf - |
  ssh -o BatchMode=yes pcsx2-win "cmd /c if not exist E:\work\desktop-guides\t04-providers mkdir E:\work\desktop-guides\t04-providers && tar -xzf - -C E:\work\desktop-guides\t04-providers"
ssh -o BatchMode=yes pcsx2-win "cd /d E:\work\desktop-guides\t04-providers && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64"
```

Expected: `Build succeeded.` with 0 warnings from the new files and 0
errors.

- [ ] **Step 17: Commit** (after message approval)

```bash
git add src/DesktopGuides.Production
git commit -m "feat(settings): add game data providers card with DPAPI credential store"
```

### Task 11: Search-first Add game dialog and the game route metadata

**Files:**
- Create:
  - `src/DesktopGuides.Core/Providers/GameMetadataPresentation.cs`
  - `src/DesktopGuides.Production/AddGameDialog.xaml(.cs)`
- Modify:
  - `src/DesktopGuides.Core/Providers/ProviderMessages.cs`
  - `src/DesktopGuides.Production/Providers/ProviderServices.cs`
  - `src/DesktopGuides.Production/ShellWindow.xaml(.cs)`
  - `Directory.Packages.props` and the Production `.csproj`
- Test:
  - `tests/DesktopGuides.Core.Tests/Providers/GameMetadataPresentationTests.cs`
  - additions to `ProviderMessagesTests.cs`

**Interfaces:**
- Consumes:
  - Task 9: `ProviderGameImporter`, `ProviderAddResult` and
    `ProviderRefreshResult`.
  - Task 10: `ProviderServices`, `ProviderMessages` and
    `IProviderCredentialStore`.
  - Task 5: `ManagedArtworkStore.ResolveFile`.
- Produces:
  - `[Flags] enum ProviderRecovery { None = 0, OpenSettings = 1, AddManually = 2, Retry = 4 }`
    and `ProviderRecovery ProviderMessages.RecoveryFor(ProviderErrorKind kind)`
  - `static class GameMetadataPresentation` with:
    - `string TypeLabel(GameTypeTag type)`
    - `string ResultSummary(ProviderSearchResult result)`
    - `string? PlatformSummary(IReadOnlyList<string> platforms)`
    - `IReadOnlyList<string> Attribution(GameMetadataSnapshot snapshot)`
    - `string? Companies(GameMetadataSnapshot snapshot)`
  - `ProviderServices.Igdb`, `ProviderServices.CreateImporter(ILibraryRepository repository, ManagedArtworkStore store)`
    and `ProviderServices.HasIgdbCredentialsAsync(CancellationToken token)`
  - AutomationIds: `AddGameSearchDialog`, `GameSearchInput`,
    `GameSearchButton`, `GameSearchResults`, `GameSearchProgress`,
    `GameSearchCancel`, `GameSearchStatus`, `AddGameManuallyLink`,
    `GameCover`, `GameFacts`, `GameSummary`, `GameGenres`, `GameCompanies`,
    `GameAttribution`, `GameProviderLink` and `RefreshMetadataButton`.
    Task 12 drives these.

**Ruling T11-a (queries go out on submit only):** The spec's
`AutoSuggestBox` gets no suggestion list. `QuerySubmitted` (Enter, or the
box's query icon) and the Search button are the only triggers, so typing
sends nothing.

**Ruling T11-b (thumbnails):** Per Ruling T7-a, result thumbnails load
directly from the `images.igdb.com` URL that `IgdbClient` built. WinUI's
image cache fetches them, not `ProviderHttp`. The host is fixed because
the app builds the URL itself; no provider string becomes a URL. Only
the chosen game's cover is stored.

- [ ] **Step 1: Write the failing Core tests**

  Add to `ProviderMessagesTests`:

```csharp
    [Theory]
    [InlineData(ProviderErrorKind.NotConfigured, ProviderRecovery.OpenSettings | ProviderRecovery.AddManually)]
    [InlineData(ProviderErrorKind.InvalidCredentials, ProviderRecovery.OpenSettings)]
    [InlineData(ProviderErrorKind.Unavailable, ProviderRecovery.Retry | ProviderRecovery.AddManually)]
    [InlineData(ProviderErrorKind.Timeout, ProviderRecovery.Retry)]
    [InlineData(ProviderErrorKind.RateLimited, ProviderRecovery.Retry)]
    [InlineData(ProviderErrorKind.MalformedData, ProviderRecovery.AddManually)]
    public void RecoveryActionsMatchTheSpecTable(ProviderErrorKind kind, ProviderRecovery expected) =>
        Assert.Equal(expected, ProviderMessages.RecoveryFor(kind));
```

  Create `GameMetadataPresentationTests.cs`:

```csharp
using DesktopGuides.Core.Providers;
using Xunit;

namespace DesktopGuides.Core.Tests.Providers;

public sealed class GameMetadataPresentationTests
{
    [Theory]
    [InlineData(GameTypeTag.MainGame, "Main game")]
    [InlineData(GameTypeTag.Remaster, "Remaster")]
    [InlineData(GameTypeTag.Remake, "Remake")]
    [InlineData(GameTypeTag.Port, "Port")]
    [InlineData(GameTypeTag.Edition, "Edition")]
    [InlineData(GameTypeTag.Expansion, "Expansion")]
    [InlineData(GameTypeTag.Bundle, "Bundle")]
    [InlineData(GameTypeTag.Other, "Other")]
    public void TypeLabelsAreSentenceCase(GameTypeTag type, string expected) =>
        Assert.Equal(expected, GameMetadataPresentation.TypeLabel(type));

    [Fact]
    public void ResultSummaryShowsTypeAndYearWhenKnown()
    {
        ProviderSearchResult withYear = new("70", "Half-Life", 1998, ["PC (Windows)"], GameTypeTag.MainGame, null);
        Assert.Equal("Main game, 1998", GameMetadataPresentation.ResultSummary(withYear));
        Assert.Equal("Edition", GameMetadataPresentation.ResultSummary(withYear with { ReleaseYear = null, Type = GameTypeTag.Edition }));
    }

    [Fact]
    public void PlatformSummaryShowsThreeThenACount()
    {
        Assert.Null(GameMetadataPresentation.PlatformSummary([]));
        Assert.Equal("PC, PS2", GameMetadataPresentation.PlatformSummary(["PC", "PS2"]));
        Assert.Equal("PC, PS2, Xbox and 2 more",
            GameMetadataPresentation.PlatformSummary(["PC", "PS2", "Xbox", "Mac", "Linux"]));
    }

    [Fact]
    public void AttributionNamesEachSourceUsed()
    {
        GameMetadataSnapshot snapshot = new(1, null, null, [], [], [], [], null, GameTypeTag.MainGame);
        Assert.Equal(["Metadata from IGDB"], GameMetadataPresentation.Attribution(snapshot));
        Assert.Equal(["Metadata from IGDB", "Artwork from SteamGridDB"],
            GameMetadataPresentation.Attribution(snapshot with { ArtworkSource = "SteamGridDB" }));
        Assert.Equal(["Metadata and artwork from IGDB"],
            GameMetadataPresentation.Attribution(snapshot with { ArtworkSource = "IGDB" }));
    }

    [Fact]
    public void CompaniesNameDevelopersThenPublishers()
    {
        GameMetadataSnapshot snapshot = new(1, null, null, [], [], [], [], null, GameTypeTag.MainGame);
        Assert.Null(GameMetadataPresentation.Companies(snapshot));
        Assert.Equal("Developed by Valve. Published by Sierra, Valve.",
            GameMetadataPresentation.Companies(snapshot with { Developers = ["Valve"], Publishers = ["Sierra", "Valve"] }));
        Assert.Equal("Published by Sierra.",
            GameMetadataPresentation.Companies(snapshot with { Publishers = ["Sierra"] }));
    }
}
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test tests/DesktopGuides.Core.Tests/DesktopGuides.Core.Tests.csproj -c Release`
Expected: build FAIL, `The name 'GameMetadataPresentation' does not exist in the current context`.

- [ ] **Step 3: Implement.** Add to `ProviderMessages.cs`:

```csharp
[Flags]
public enum ProviderRecovery
{
    None = 0,
    OpenSettings = 1,
    AddManually = 2,
    Retry = 4,
}
```

  and inside `ProviderMessages`:

```csharp
    public static ProviderRecovery RecoveryFor(ProviderErrorKind kind) => kind switch
    {
        ProviderErrorKind.NotConfigured => ProviderRecovery.OpenSettings | ProviderRecovery.AddManually,
        ProviderErrorKind.InvalidCredentials => ProviderRecovery.OpenSettings,
        ProviderErrorKind.Timeout or ProviderErrorKind.RateLimited => ProviderRecovery.Retry,
        ProviderErrorKind.MalformedData => ProviderRecovery.AddManually,
        _ => ProviderRecovery.Retry | ProviderRecovery.AddManually,
    };
```

  Create `GameMetadataPresentation.cs`:

```csharp
namespace DesktopGuides.Core.Providers;

public static class GameMetadataPresentation
{
    public static string TypeLabel(GameTypeTag type) => type switch
    {
        GameTypeTag.MainGame => "Main game",
        _ => type.ToString(),
    };

    public static string ResultSummary(ProviderSearchResult result) =>
        result.ReleaseYear is { } year ? $"{TypeLabel(result.Type)}, {year}" : TypeLabel(result.Type);

    public static string? PlatformSummary(IReadOnlyList<string> platforms) => platforms.Count switch
    {
        0 => null,
        <= 3 => string.Join(", ", platforms),
        _ => $"{string.Join(", ", platforms.Take(3))} and {platforms.Count - 3} more",
    };

    public static IReadOnlyList<string> Attribution(GameMetadataSnapshot snapshot) => snapshot.ArtworkSource switch
    {
        "IGDB" => ["Metadata and artwork from IGDB"],
        "SteamGridDB" => ["Metadata from IGDB", "Artwork from SteamGridDB"],
        _ => ["Metadata from IGDB"],
    };

    public static string? Companies(GameMetadataSnapshot snapshot)
    {
        List<string> parts = [];
        if (snapshot.Developers.Count > 0) parts.Add($"Developed by {string.Join(", ", snapshot.Developers)}");
        if (snapshot.Publishers.Count > 0) parts.Add($"Published by {string.Join(", ", snapshot.Publishers)}");
        return parts.Count == 0 ? null : string.Join(". ", parts) + ".";
    }
}
```

  `TypeLabel` depends on the other enum members being one word, which
  `TypeLabelsAreSentenceCase` pins.

  If Task 1 recorded attribution wording that a terms page requires, use
  that exact wording in `Attribution` and in the test.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/DesktopGuides.Core.Tests/DesktopGuides.Core.Tests.csproj -c Release`
Expected: PASS, 0 failed.

- [ ] **Step 5: Commit** (after message approval)

```bash
git add src/DesktopGuides.Core tests/DesktopGuides.Core.Tests
git commit -m "feat(providers): add search presentation labels and recovery actions"
```


  The remaining steps are WinUI code. Production has no test project, so
  TDD is skipped for Steps 6–10; the Task 12 E2E covers them, and every
  decision they depend on (labels, recovery actions, messages, importer
  outcomes) is already pinned by the Core tests above.

- [ ] **Step 6: Extend `ProviderServices`, add the package, and give the
  editor a notice**

  In `Providers/ProviderServices.cs`, add
  `using DesktopGuides.Core.Library;` and
  `using DesktopGuides.Infrastructure.Storage;`, then add these members.
  The credential store caches its value (Task 10), so each delegate reads
  the saved credentials at request time without touching disk again, and a
  Save or Remove in Settings takes effect on the next request.

```csharp
    private IgdbClient? igdb;

    public IgdbClient Igdb => igdb ??= CreateIgdb(async token => (await Credentials.LoadAsync(token)).Igdb);

    public async Task<bool> HasIgdbCredentialsAsync(CancellationToken token) =>
        (await Credentials.LoadAsync(token)).Igdb is not null;

    public ProviderGameImporter CreateImporter(ILibraryRepository repository, ManagedArtworkStore store) =>
        new(repository,
            Igdb,
            new FallbackArtworkSource(
                CreateSteamGridDb(async token => (await Credentials.LoadAsync(token)).SteamGridDbKey),
                new IgdbCoverArtworkSource()),
            store,
            Http.GetImageAsync);
```

  In `Directory.Packages.props`, next to the existing
  `CommunityToolkit.WinUI.Controls.SettingsControls` entry, add:

```xml
    <PackageVersion Include="CommunityToolkit.WinUI.Controls.MetadataControl" Version="8.2.251219" />
```

  In `src/DesktopGuides.Production/DesktopGuides.Production.csproj`, next
  to the SettingsControls `PackageReference`, add:

```xml
    <PackageReference Include="CommunityToolkit.WinUI.Controls.MetadataControl" />
```

  Then restore so the lock file picks up the package. Restore runs on the
  Windows host, where the Production project builds:

```bash
ssh -o BatchMode=yes pcsx2-win "cd /d E:\work\desktop-guides\t04-providers && dotnet restore src\DesktopGuides.Production\DesktopGuides.Production.csproj -p:Platform=x64"
```

  Copy the updated `src/DesktopGuides.Production/packages.lock.json` back
  with `scp pcsx2-win:E:/work/desktop-guides/t04-providers/src/DesktopGuides.Production/packages.lock.json src/DesktopGuides.Production/`
  (stage the tree first as in Step 11 if the host copy is stale).

  The spec says that with no credentials, Add game opens the manual editor
  "with the `NotConfigured` notice". `GameEditorDialog` has no notice
  slot, so give it one. In `GameEditorDialog.xaml`, add this as the first
  child of the outer `StackPanel`:

```xml
        <InfoBar x:Name="EditorNotice"
                 IsOpen="False"
                 IsClosable="False"
                 Severity="Informational"
                 AutomationProperties.AutomationId="GameEditorNotice">
            <InfoBar.ActionButton>
                <HyperlinkButton x:Name="EditorNoticeSettingsLink"
                                 Content="Open Settings"
                                 Click="OpenSettingsClicked"
                                 AutomationProperties.AutomationId="GameEditorNoticeSettingsLink" />
            </InfoBar.ActionButton>
        </InfoBar>
```

  In `GameEditorDialog.xaml.cs`, change the constructor to
  `public GameEditorDialog(Game? game, Func<GameDetails, Task> save, string? notice = null)`,
  and add this after the `if (game is not null)` block:

```csharp
        if (notice is not null)
        {
            EditorNotice.Message = notice;
            AutomationProperties.SetName(EditorNotice, notice);
            EditorNotice.IsOpen = true;
        }
```

  UI Automation reads an `InfoBar`'s message only through its name, so
  every status `InfoBar` in this plan sets its name alongside `Message`.
  Add `using Microsoft.UI.Xaml.Automation;` to the file if it is missing.

  and these members:

```csharp
    internal bool OpenSettingsRequested { get; private set; }

    private void OpenSettingsClicked(object sender, RoutedEventArgs args)
    {
        OpenSettingsRequested = true;
        Hide();
    }
```

  The two existing callers pass no notice and are unchanged.

- [ ] **Step 7: Create `AddGameDialog.xaml`**

  Nothing in the dialog is a primary action: picking a result adds it.
  The dialog's own Close button is always enabled and cancels whatever is
  running. `GameSearchCancel` cancels only the current search or add and
  keeps the dialog open. Both are reachable with Tab.

```xml
<ContentDialog
    x:Class="DesktopGuides.Production.AddGameDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:DesktopGuides.Production"
    Title="Add game"
    CloseButtonText="Close"
    AutomationProperties.AutomationId="AddGameSearchDialog">
    <Grid Width="480"
          RowSpacing="{StaticResource DesktopGuidesSpacing12}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="360" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <Grid ColumnSpacing="{StaticResource DesktopGuidesSpacing8}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <AutoSuggestBox x:Name="GameSearchInput"
                            PlaceholderText="Search IGDB by title"
                            QueryIcon="Find"
                            QuerySubmitted="QuerySubmitted"
                            AutomationProperties.Name="Search for a game"
                            AutomationProperties.AutomationId="GameSearchInput" />
            <Button x:Name="GameSearchButton"
                    Grid.Column="1"
                    Content="Search"
                    Click="SearchClicked"
                    AutomationProperties.AutomationId="GameSearchButton" />
        </Grid>

        <StackPanel x:Name="GameSearchBusy"
                    Grid.Row="1"
                    Orientation="Horizontal"
                    Spacing="{StaticResource DesktopGuidesSpacing12}"
                    Visibility="Collapsed">
            <ProgressRing x:Name="GameSearchProgress"
                          Width="20"
                          Height="20"
                          IsActive="False"
                          AutomationProperties.AutomationId="GameSearchProgress" />
            <TextBlock x:Name="GameSearchBusyText"
                       VerticalAlignment="Center"
                       Style="{StaticResource DesktopGuidesSecondaryBodyStyle}"
                       AutomationProperties.LiveSetting="Polite" />
            <Button x:Name="GameSearchCancel"
                    Content="Cancel"
                    Click="CancelClicked"
                    Style="{StaticResource DesktopGuidesSecondaryActionButtonStyle}"
                    AutomationProperties.AutomationId="GameSearchCancel" />
        </StackPanel>

        <InfoBar x:Name="GameSearchStatus"
                 Grid.Row="2"
                 IsOpen="False"
                 IsClosable="False"
                 AutomationProperties.AutomationId="GameSearchStatus">
            <InfoBar.Content>
                <StackPanel x:Name="GameSearchActions"
                            Orientation="Horizontal"
                            Spacing="{StaticResource DesktopGuidesSpacing8}"
                            Margin="0,0,0,12">
                    <Button x:Name="GameSearchRetry"
                            Content="Retry"
                            Click="RetryClicked"
                            AutomationProperties.AutomationId="GameSearchRetry" />
                    <Button x:Name="GameSearchOpenSettings"
                            Content="Open Settings"
                            Click="OpenSettingsClicked"
                            AutomationProperties.AutomationId="GameSearchOpenSettings" />
                </StackPanel>
            </InfoBar.Content>
        </InfoBar>

        <ListView x:Name="GameSearchResults"
                  Grid.Row="3"
                  SelectionMode="None"
                  IsItemClickEnabled="True"
                  ItemClick="ResultClicked"
                  AutomationProperties.Name="Search results"
                  AutomationProperties.AutomationId="GameSearchResults">
            <ListView.ItemTemplate>
                <DataTemplate x:DataType="local:GameSearchItem">
                    <Grid Padding="0,8"
                          ColumnSpacing="{StaticResource DesktopGuidesSpacing12}"
                          AutomationProperties.Name="{x:Bind AccessibleName}">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="45" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <Border Width="45"
                                Height="60"
                                CornerRadius="4"
                                Background="{ThemeResource ControlFillColorSecondaryBrush}">
                            <Image Source="{x:Bind Thumbnail}"
                                   Stretch="UniformToFill"
                                   AutomationProperties.AccessibilityView="Raw" />
                        </Border>
                        <StackPanel Grid.Column="1"
                                    VerticalAlignment="Center"
                                    Spacing="{StaticResource DesktopGuidesSpacing4}">
                            <TextBlock Text="{x:Bind Title}"
                                       Style="{StaticResource DesktopGuidesBodyStyle}"
                                       TextTrimming="CharacterEllipsis" />
                            <TextBlock Text="{x:Bind Summary}"
                                       Style="{StaticResource DesktopGuidesMetadataStyle}" />
                            <TextBlock Text="{x:Bind Platforms}"
                                       Style="{StaticResource DesktopGuidesMetadataStyle}"
                                       TextTrimming="CharacterEllipsis" />
                        </StackPanel>
                    </Grid>
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>

        <HyperlinkButton x:Name="AddGameManuallyLink"
                         Grid.Row="4"
                         Content="Add manually"
                         Click="AddManuallyClicked"
                         AutomationProperties.AutomationId="AddGameManuallyLink" />
    </Grid>
</ContentDialog>
```

  `ListView` virtualizes its items by default, which covers the spec's
  "virtualized `ListView`". The fixed 360 px row keeps the dialog from
  growing as results arrive.

- [ ] **Step 8: Create `AddGameDialog.xaml.cs`**

  The spec's states map onto the controls as follows:
  - **Idle:** no results and no status.
  - **Searching** and **Adding:** the busy row is shown, and the search
    box, Search button and list are disabled.
  - **Results:** the list is filled.
  - **Empty:** the `NoResults` status is shown.
  - **Error:** the `ForIgdb` status is shown with the `RecoveryFor`
    buttons.

  Add manually and the dialog's Close button stay enabled in every state.

  **Ruling T11-c (closing during work):** If the dialog closes while a
  search or add is running, it cancels the work and holds the close until
  the work finishes. This matters for an add that commits just as the user
  closes: the dialog then reports `OpenGame`, and the shell opens the new
  game. Without the hold, the shell would read the outcome too early, and
  the game would appear in the library without being opened.

```csharp
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DesktopGuides.Production;

public sealed class GameSearchItem
{
    internal GameSearchItem(ProviderSearchResult result)
    {
        Result = result;
        Summary = GameMetadataPresentation.ResultSummary(result);
        Platforms = GameMetadataPresentation.PlatformSummary(result.Platforms) ?? "No platforms listed";
        Thumbnail = result.ThumbnailUrl is { } url
            ? new BitmapImage(new Uri(url)) { DecodePixelWidth = 90 }
            : null;
        AccessibleName = $"{result.Title}, {Summary}, {Platforms}";
    }

    internal ProviderSearchResult Result { get; }
    public string Title => Result.Title;
    public string Summary { get; }
    public string Platforms { get; }
    public ImageSource? Thumbnail { get; }
    public string AccessibleName { get; }
}

internal enum AddGameOutcome { None, OpenGame, AddManually, OpenSettings }

public sealed partial class AddGameDialog : ContentDialog
{
    private readonly IGameMetadataProvider provider;
    private readonly ProviderGameImporter importer;
    private CancellationTokenSource? operation;
    private Task running = Task.CompletedTask;
    private Func<Task>? retry;
    private bool closing;

    internal AddGameDialog(IGameMetadataProvider provider, ProviderGameImporter importer)
    {
        InitializeComponent();
        this.provider = provider;
        this.importer = importer;
        Opened += (_, _) => GameSearchInput.Focus(FocusState.Programmatic);
        Closing += DialogClosing;
    }

    internal AddGameOutcome Outcome { get; private set; }
    internal ProviderAddResult? Added { get; private set; }

    private async void DialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        closing = true;
        if (operation is not { } current)
        {
            return;
        }
        var deferral = args.GetDeferral();
        current.Cancel();
        try
        {
            await running;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        _ = SearchAsync(args.QueryText);

    private void SearchClicked(object sender, RoutedEventArgs args) => _ = SearchAsync(GameSearchInput.Text);

    private void ResultClicked(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is GameSearchItem item)
        {
            _ = AddAsync(item);
        }
    }

    private void CancelClicked(object sender, RoutedEventArgs args) => operation?.Cancel();

    private void RetryClicked(object sender, RoutedEventArgs args) => _ = retry?.Invoke();

    private void OpenSettingsClicked(object sender, RoutedEventArgs args) => Finish(AddGameOutcome.OpenSettings);

    private void AddManuallyClicked(object sender, RoutedEventArgs args) => Finish(AddGameOutcome.AddManually);

    private void Finish(AddGameOutcome outcome)
    {
        Outcome = outcome;
        Hide();
    }

    private async Task SearchAsync(string text)
    {
        string query = text.Trim();
        if (operation is not null || closing || query.Length == 0)
        {
            return;
        }
        if (query.Length > IgdbClient.MaxQueryLength)
        {
            ShowStatus(InfoBarSeverity.Warning,
                $"Enter up to {IgdbClient.MaxQueryLength} characters to search.", ProviderRecovery.None);
            return;
        }
        retry = () => SearchAsync(query);
        GameSearchResults.ItemsSource = null;
        await StartAsync("Searching IGDB…", async token =>
        {
            IReadOnlyList<ProviderSearchResult> results = await provider.SearchAsync(query, token);
            GameSearchResults.ItemsSource = results.Select(result => new GameSearchItem(result)).ToList();
            if (results.Count == 0)
            {
                ShowStatus(InfoBarSeverity.Informational, ProviderMessages.NoResults(query), ProviderRecovery.AddManually);
            }
        });
        if (!closing && GameSearchResults.Items.Count > 0)
        {
            GameSearchResults.Focus(FocusState.Programmatic);
        }
    }

    private async Task AddAsync(GameSearchItem item)
    {
        if (operation is not null || closing)
        {
            return;
        }
        retry = () => AddAsync(item);
        await StartAsync($"Adding {item.Title}…", async token =>
        {
            Added = await importer.AddAsync(item.Result.ExternalId, token);
            Outcome = AddGameOutcome.OpenGame;
        });
        if (Added is not null && !closing)
        {
            Hide();
        }
    }

    private Task StartAsync(string busyText, Func<CancellationToken, Task> work)
    {
        running = RunAsync(busyText, work);
        return running;
    }

    private async Task RunAsync(string busyText, Func<CancellationToken, Task> work)
    {
        using CancellationTokenSource cancel = new();
        operation = cancel;
        GameSearchStatus.IsOpen = false;
        SetBusy(busyText);
        try
        {
            await work(cancel.Token);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            // Cancelled: back to the previous state, with no status.
        }
        catch (ProviderException error)
        {
            ShowStatus(SeverityFor(error.Kind), ProviderMessages.ForIgdb(error.Kind),
                ProviderMessages.RecoveryFor(error.Kind));
        }
        catch (Exception error)
        {
            ShowStatus(InfoBarSeverity.Error, $"Could not add this game: {error.Message}", ProviderRecovery.Retry);
        }
        finally
        {
            operation = null;
            SetBusy(null);
        }
    }

    private static InfoBarSeverity SeverityFor(ProviderErrorKind kind) => kind switch
    {
        ProviderErrorKind.NotConfigured => InfoBarSeverity.Informational,
        ProviderErrorKind.InvalidCredentials or ProviderErrorKind.MalformedData => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Warning,
    };

    private void SetBusy(string? text)
    {
        bool busy = text is not null;
        GameSearchInput.IsEnabled = !busy;
        GameSearchButton.IsEnabled = !busy;
        GameSearchResults.IsEnabled = !busy;
        GameSearchBusy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        GameSearchProgress.IsActive = busy;
        GameSearchBusyText.Text = text ?? string.Empty;
        if (busy)
        {
            GameSearchCancel.Focus(FocusState.Programmatic);
        }
        else if (!closing && GameSearchResults.Items.Count == 0)
        {
            GameSearchInput.Focus(FocusState.Programmatic);
        }
    }

    private void ShowStatus(InfoBarSeverity severity, string message, ProviderRecovery recovery)
    {
        bool canRetry = recovery.HasFlag(ProviderRecovery.Retry) && retry is not null;
        bool canOpenSettings = recovery.HasFlag(ProviderRecovery.OpenSettings);
        GameSearchRetry.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        GameSearchOpenSettings.Visibility = canOpenSettings ? Visibility.Visible : Visibility.Collapsed;
        GameSearchActions.Visibility = canRetry || canOpenSettings ? Visibility.Visible : Visibility.Collapsed;
        GameSearchStatus.Severity = severity;
        GameSearchStatus.Message = message;
        AutomationProperties.SetName(GameSearchStatus, message);
        GameSearchStatus.IsOpen = true;
    }
}
```

  Notes for the implementer:
  - The Add manually link is always visible, so `ProviderRecovery.AddManually`
    needs no extra button in the `InfoBar`. The flag still matters, because
    Task 12 checks that the link is enabled in those states.
  - The general `catch (Exception)` follows `GameEditorDialog`'s existing
    save-error pattern. `ProviderHttp` and `IgdbClient` turn every network
    failure into a `ProviderException`, so this branch only sees library
    and storage errors, and their messages carry no credentials.
  - `MalformedData` stops the add without saving anything, which the
    importer already guarantees (Task 9). The dialog only reports it.

- [ ] **Step 9: Route Add game through search in `ShellWindow.xaml.cs`**

  Add `using DesktopGuides.Core.Providers;` and these fields:

```csharp
    private ManagedArtworkStore? artwork;
    private ProviderGameImporter? importer;
    private AddGameDialog? activeAddGameDialog;
```

  In `InitializeCoreAsync`, replace
  `repository = new SqliteLibraryRepository(new ManagedPathResolver(dataRoot));`
  with the lines below. The repository and the artwork store must resolve
  paths the same way, which is why they share one resolver.

```csharp
            ManagedPathResolver paths = new(dataRoot);
            repository = new SqliteLibraryRepository(paths);
            artwork = new ManagedArtworkStore(paths);
```

  After the Task 10 lines `providers = new ProviderServices(dataRoot);`
  and `await ProviderSettings.InitializeAsync(providers);`, add:

```csharp
            importer = providers.CreateImporter(repository, artwork);
```

  In the close handler, next to `activeGameEditor?.Hide();`, add
  `activeAddGameDialog?.Hide();`. `AddGameDialog` cancels its own work when
  it closes (Ruling T11-c), and `CloseWhenIdleAsync` already drains the
  navigation queue before it disposes the repository and `providers`.

  Replace the body of the `RunNavigationAsync` lambda in `AddGameClicked`
  (everything after the `closeRequested || navigator.Current is not LibraryRoute`
  guard) with:

```csharp
                if (importer is null || providers is null ||
                    !await providers.HasIgdbCredentialsAsync(CancellationToken.None))
                {
                    await ShowManualAddAsync(ProviderMessages.ForIgdb(ProviderErrorKind.NotConfigured));
                    return;
                }
                AddGameDialog search = new(providers.Igdb, importer)
                {
                    XamlRoot = Navigation.XamlRoot
                };
                DialogSurface.Apply(search, EffectiveMaterial);
                activeAddGameDialog = search;
                try
                {
                    await search.ShowAsync();
                }
                finally
                {
                    activeAddGameDialog = null;
                }
                if (closeRequested)
                {
                    return;
                }
                switch (search.Outcome)
                {
                    case AddGameOutcome.OpenGame when search.Added is { } added:
                        navigator.OpenGame(added.Game.Id);
                        await RenderCurrentAsync();
                        if (added.AlreadyInLibrary)
                        {
                            ShowTransientStatus($"{added.Game.Title} is already in your library.");
                        }
                        else if (added.ArtworkMissing)
                        {
                            ShowWarningStatus("Game added. Its cover couldn't be downloaded.");
                        }
                        break;
                    case AddGameOutcome.AddManually:
                        await ShowManualAddAsync(null);
                        break;
                    case AddGameOutcome.OpenSettings:
                        await OpenSettingsFromDialogAsync();
                        break;
                }
```

  Move the previous body (the manual editor) into these two methods. The
  editor code itself is unchanged except for the notice argument and the
  Open Settings check:

```csharp
    private async Task ShowManualAddAsync(string? notice)
    {
        Game? created = null;
        GameEditorDialog editor = new(null, async details =>
        {
            created = await RequireRepository().AddGameAsync(
                details.Title, details.Platform, details.Notes);
        }, notice)
        {
            XamlRoot = Navigation.XamlRoot
        };
        DialogSurface.Apply(editor, EffectiveMaterial);
        activeGameEditor = editor;
        ContentDialogResult result;
        try
        {
            result = await editor.ShowAsync();
        }
        finally
        {
            activeGameEditor = null;
        }
        if (closeRequested)
        {
            return;
        }
        if (result == ContentDialogResult.Primary && created is not null)
        {
            navigator.OpenGame(created.Id);
            await RenderCurrentAsync();
        }
        else if (editor.OpenSettingsRequested)
        {
            await OpenSettingsFromDialogAsync();
        }
    }

    private async Task OpenSettingsFromDialogAsync()
    {
        navigator.OpenSettings();
        await RenderCurrentAsync();
        ProviderSettings.Expand();
    }
```

  Add `internal void Expand() => ProviderSettingsExpander.IsExpanded = true;`
  to `ProviderSettingsCard.xaml.cs`. `ProviderSettingsExpander` is the
  `SettingsExpander` from Task 10 Step 13.

  `AddGameButton` stays disabled for the whole flow, because the existing
  `finally` re-enables it only after the lambda returns. This covers the
  search dialog, the manual editor, and the Settings hand-off.

- [ ] **Step 10: Show the snapshot on the game route and add Refresh**

  In `ShellWindow.xaml`:
  - Add
    `xmlns:toolkit="using:CommunityToolkit.WinUI.Controls"` to the root
    element, unless Task 10 already added it for `SettingsExpander`.
  - In the heading `Grid`, add a third `Auto` column and put this button in
    it, before `EditGameButton`. Move `EditGameButton` to `Grid.Column="2"`.

```xml
                    <Button x:Name="RefreshMetadataButton"
                            Grid.Column="1"
                            Content="Refresh metadata"
                            Style="{StaticResource DesktopGuidesSecondaryActionButtonStyle}"
                            VerticalAlignment="Center"
                            Visibility="Collapsed"
                            Click="RefreshMetadataClicked"
                            AutomationProperties.AutomationId="RefreshMetadataButton" />
```

  Replace the `StackPanel` inside `GameMetadataSurface` with the grid
  below. The cover sits on the left; the local fields (platform, notes)
  stay first on the right, and the provider snapshot follows under them.

```xml
                    <Grid ColumnSpacing="{StaticResource DesktopGuidesSpacing16}">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <Border x:Name="GameCoverFrame"
                                Width="120"
                                Height="180"
                                CornerRadius="4"
                                VerticalAlignment="Top"
                                Visibility="Collapsed">
                            <Image x:Name="GameCover"
                                   Stretch="UniformToFill"
                                   AutomationProperties.Name="Cover art"
                                   AutomationProperties.AutomationId="GameCover" />
                        </Border>
                        <StackPanel Grid.Column="1"
                                    Spacing="{StaticResource DesktopGuidesSpacing8}">
                            <TextBlock x:Name="GamePlatform"
                                       Style="{StaticResource DesktopGuidesBodyStyle}"
                                       AutomationProperties.AutomationId="GamePlatform" />
                            <ScrollViewer x:Name="GameNotesScroll"
                                          MaxHeight="96"
                                          HorizontalScrollBarVisibility="Disabled"
                                          VerticalScrollBarVisibility="Auto"
                                          AutomationProperties.AutomationId="GameNotesScroll"
                                          AutomationProperties.Name="Game notes">
                                <TextBlock x:Name="GameNotes"
                                           Style="{StaticResource DesktopGuidesSecondaryBodyStyle}"
                                           IsTextSelectionEnabled="True"
                                           AutomationProperties.AutomationId="GameNotes" />
                            </ScrollViewer>
                            <StackPanel x:Name="GameProviderDetails"
                                        Spacing="{StaticResource DesktopGuidesSpacing8}"
                                        Visibility="Collapsed">
                                <toolkit:MetadataControl x:Name="GameFacts"
                                                         AutomationProperties.AutomationId="GameFacts" />
                                <ScrollViewer MaxHeight="120"
                                              HorizontalScrollBarVisibility="Disabled"
                                              VerticalScrollBarVisibility="Auto"
                                              AutomationProperties.Name="Game summary">
                                    <TextBlock x:Name="GameSummary"
                                               Style="{StaticResource DesktopGuidesSecondaryBodyStyle}"
                                               TextWrapping="Wrap"
                                               IsTextSelectionEnabled="True"
                                               AutomationProperties.AutomationId="GameSummary" />
                                </ScrollViewer>
                                <TextBlock x:Name="GameGenres"
                                           Style="{StaticResource DesktopGuidesMetadataStyle}"
                                           TextWrapping="Wrap"
                                           AutomationProperties.AutomationId="GameGenres" />
                                <TextBlock x:Name="GameCompanies"
                                           Style="{StaticResource DesktopGuidesMetadataStyle}"
                                           TextWrapping="Wrap"
                                           AutomationProperties.AutomationId="GameCompanies" />
                                <StackPanel Orientation="Horizontal"
                                            Spacing="{StaticResource DesktopGuidesSpacing12}">
                                    <TextBlock x:Name="GameAttribution"
                                               VerticalAlignment="Center"
                                               Style="{StaticResource DesktopGuidesMetadataStyle}"
                                               AutomationProperties.AutomationId="GameAttribution" />
                                    <HyperlinkButton x:Name="GameProviderLink"
                                                     Content="View on IGDB"
                                                     Visibility="Collapsed"
                                                     AutomationProperties.AutomationId="GameProviderLink" />
                                </StackPanel>
                            </StackPanel>
                        </StackPanel>
                    </Grid>
```

  All provider strings reach the UI through `TextBlock.Text` or
  `MetadataItem.Label`, which render as plain text. The only
  provider-derived link is `GameProviderLink`, and its target is checked in
  code below.

  In `ShellWindow.xaml.cs`, add
  `using System.Globalization;`, `using CommunityToolkit.WinUI.Controls;`,
  `using Microsoft.UI.Xaml.Media.Imaging;` and these fields:

```csharp
    private CancellationTokenSource? refreshCancel;
    private Task refreshTask = Task.CompletedTask;
```

  In the `GameRoute` case of `RenderCurrentAsync`, where the loading state
  clears `GamePlatform` and `GameNotes`, also call `ClearProviderMetadata();`.
  Replace the block that sets `GamePlatform`, `GameNotes` and
  `GameMetadataSurface.Visibility` after the game loads with:

```csharp
                    GameHeading.Text = game.Title;
                    GamePlatform.Text = game.Platform ?? string.Empty;
                    GamePlatform.Visibility = game.Platform is null ? Visibility.Collapsed : Visibility.Visible;
                    GameNotes.Text = game.Notes ?? string.Empty;
                    GameNotesScroll.Visibility = game.Notes is null ? Visibility.Collapsed : Visibility.Visible;
                    ImageSource? cover = await LoadCoverAsync(game.ArtworkRelativePath);
                    if (generation != renderGeneration)
                    {
                        return false;
                    }
                    ShowProviderMetadata(game, cover);
                    GameMetadataSurface.Visibility =
                        game.Platform is null && game.Notes is null &&
                        game.Metadata is null && cover is null
                            ? Visibility.Collapsed
                            : Visibility.Visible;
```

  Add these methods:

```csharp
    private void ClearProviderMetadata()
    {
        GameCover.Source = null;
        GameCoverFrame.Visibility = Visibility.Collapsed;
        GameProviderDetails.Visibility = Visibility.Collapsed;
        GameProviderLink.Visibility = Visibility.Collapsed;
        RefreshMetadataButton.Visibility = Visibility.Collapsed;
    }

    // Reads the cached file through a stream, so a cover never triggers a
    // network request and a damaged file only hides the cover.
    private async Task<ImageSource?> LoadCoverAsync(string? relativePath)
    {
        if (relativePath is null || artwork?.ResolveFile(relativePath) is not { } path)
        {
            return null;
        }
        try
        {
            using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            BitmapImage bitmap = new() { DecodePixelWidth = 240 };
            await bitmap.SetSourceAsync(file.AsRandomAccessStream());
            return bitmap;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException)
        {
            return null;
        }
    }

    private void ShowProviderMetadata(Game game, ImageSource? cover)
    {
        GameCover.Source = cover;
        GameCoverFrame.Visibility = cover is null ? Visibility.Collapsed : Visibility.Visible;
        RefreshMetadataButton.Visibility = game.Link is null ? Visibility.Collapsed : Visibility.Visible;
        RefreshMetadataButton.IsEnabled = refreshCancel is null;
        if (game.Metadata is not { } metadata)
        {
            GameProviderDetails.Visibility = Visibility.Collapsed;
            return;
        }
        List<MetadataItem> facts = [];
        if (metadata.FirstReleaseDate is { } released)
        {
            string year = released.Year.ToString(CultureInfo.InvariantCulture);
            facts.Add(new MetadataItem { Label = year, AccessibleLabel = $"Released {year}" });
        }
        facts.Add(new MetadataItem { Label = GameMetadataPresentation.TypeLabel(metadata.Type) });
        if (GameMetadataPresentation.PlatformSummary(metadata.Platforms) is { } platforms)
        {
            facts.Add(new MetadataItem { Label = platforms, AccessibleLabel = $"Platforms: {platforms}" });
        }
        GameFacts.Items = facts;
        SetOptionalText(GameSummary, metadata.Summary);
        SetOptionalText(GameGenres,
            metadata.Genres.Count == 0 ? null : $"Genres: {string.Join(", ", metadata.Genres)}");
        SetOptionalText(GameCompanies, GameMetadataPresentation.Companies(metadata));
        GameAttribution.Text = string.Join(". ", GameMetadataPresentation.Attribution(metadata)) + ".";
        if (GameMetadataNormalizer.NormalizeProviderUrl(metadata.ProviderUrl) is { } url)
        {
            GameProviderLink.NavigateUri = new Uri(url);
            GameProviderLink.Visibility = Visibility.Visible;
        }
        else
        {
            GameProviderLink.Visibility = Visibility.Collapsed;
        }
        GameProviderDetails.Visibility = Visibility.Visible;
    }

    private static void SetOptionalText(TextBlock block, string? text)
    {
        block.Text = text ?? string.Empty;
        block.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }
```

  `GameMetadataNormalizer.NormalizeProviderUrl` is the Task 1 check that
  accepts only `https://www.igdb.com/games/` URLs. It runs again here
  because `MetadataJson` is read from disk, and the spec allows no other
  link. Add `using System.Runtime.InteropServices;` for `COMException`.
  `SetSourceAsync` throws `COMException` when the bytes do not decode.

  Refresh runs outside the navigation queue, so a slow provider never
  blocks Back or other navigation. Only the final re-render goes through
  the queue. Closing the window cancels the refresh and waits for it.

```csharp
    private void RefreshMetadataClicked(object sender, RoutedEventArgs args)
    {
        if (closeRequested || refreshCancel is not null || importer is null ||
            navigator.Current is not GameRoute route)
        {
            return;
        }
        refreshTask = RefreshMetadataAsync(route.GameId, importer);
    }

    private async Task RefreshMetadataAsync(Guid gameId, ProviderGameImporter refresher)
    {
        using CancellationTokenSource cancel = new();
        refreshCancel = cancel;
        RefreshMetadataButton.IsEnabled = false;
        ShowBusyStatus("Refreshing metadata…");
        ProviderRefreshResult? result = null;
        string? error = null;
        try
        {
            result = await refresher.RefreshAsync(gameId, cancel.Token);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return;
        }
        catch (ProviderException failure)
        {
            error = ProviderMessages.ForIgdb(failure.Kind);
        }
        catch (KeyNotFoundException)
        {
            error = "This game is no longer in your library.";
        }
        catch (Exception failure)
        {
            error = $"Could not refresh metadata: {failure.Message}";
        }
        finally
        {
            refreshCancel = null;
        }
        if (closeRequested)
        {
            return;
        }
        await RunNavigationAsync(async () =>
        {
            if (navigator.Current is GameRoute current && current.GameId == gameId)
            {
                await RenderCurrentAsync();
            }
            if (error is not null)
            {
                ShowErrorStatus(error);
            }
            else if (result!.ArtworkMissing)
            {
                ShowWarningStatus("Metadata refreshed. A new cover couldn't be downloaded.");
            }
            else
            {
                ShowTransientStatus("Metadata refreshed.");
            }
        });
    }
```

  In the close handler, next to `activeAddGameDialog?.Hide();`, add
  `refreshCancel?.Cancel();`. In `CloseWhenIdleAsync`, change
  `await Task.WhenAll(initializationTask, pendingNavigation);` to
  `await Task.WhenAll(initializationTask, pendingNavigation, refreshTask);`.
  `refreshTask` never faults, because `RefreshMetadataAsync` catches
  everything, so this only adds waiting. The repository and `providers`
  are then disposed after the refresh has stopped using them.

  A refresh of a game that the user has since navigated away from still
  commits and still reports its status. The re-render only happens when
  that game is on screen.

- [ ] **Step 11: Build on `pcsx2-win`**

```bash
git ls-files -z --cached --others --exclude-standard |
  tar --null -T - -czf - |
  ssh -o BatchMode=yes pcsx2-win "cmd /c if not exist E:\work\desktop-guides\t04-providers mkdir E:\work\desktop-guides\t04-providers && tar -xzf - -C E:\work\desktop-guides\t04-providers"
ssh -o BatchMode=yes pcsx2-win "cd /d E:\work\desktop-guides\t04-providers && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64"
```

Expected: `Build succeeded.` with 0 warnings from the new or changed files
and 0 errors.

  Then run the whole local suite again, because Step 6 touched shared
  package files:

Run: `dotnet test tests/DesktopGuides.Core.Tests/DesktopGuides.Core.Tests.csproj -c Release && dotnet test tests/DesktopGuides.Infrastructure.Tests/DesktopGuides.Infrastructure.Tests.csproj -c Release`
Expected: PASS, 0 failed.

- [ ] **Step 12: Commit** (after message approval)

```bash
git add Directory.Packages.props src/DesktopGuides.Production
git commit -m "feat(add-game): search IGDB from Add game and show game metadata"
```

### Task 12: Installed provider E2E, evidence and docs

**Files:**
- Create:
  - `tools/p1/windows_provider_credentials.ps1`
  - `tools/p1/test_windows_provider_credentials.ps1`
  - `tools/p1/windows_provider_offline_controller.ps1`
- Modify:
  - `tools/p1/DesktopGuides.ShellSeed/Program.cs`
  - `tools/p1/windows_shell_ui_smoke.ps1`
  - `tools/p1/windows_shell_install.ps1`
  - `docs/p1/e2e-testing.md`, `docs/p1/results.md`,
    `docs/p1/implementation-plan.md` and `docs/work-breakdown.md`

**Interfaces:**
- Consumes:
  - The Task 10 AutomationIds: `ProviderSettingsExpander`,
    `IgdbClientIdInput`, `IgdbClientSecretInput`, `SteamGridDbKeyInput`,
    `ProviderTestButton`, `ProviderSaveButton`, `ProviderRemoveButton` and
    `ProviderSettingsStatus`.
  - The Task 11 AutomationIds, plus `GameEditorNotice` and
    `GameEditorNoticeSettingsLink`.
  - Task 4 `AddLinkedGameAsync`, Task 5 `ManagedArtworkStore.StoreAsync`,
    and Task 6 `ProviderHttp` and `TwitchTokenSource`.
- Produces:
  - `windows_provider_credentials.ps1` with `Read-LabelledValues`,
    `ConvertTo-SendKeysLiteral` and `Find-SecretInFiles`.
  - ShellSeed commands `seed-linked-game`, `describe-providers` and
    `check-igdb-fields`.
  - Smoke modes `provider-none`, `provider-offline`, `provider-settings`,
    `provider-live` and `provider-remove`.
  - Installer switches `-ProviderOnly` and `-AllowOfflineFirewallRule`,
    and credential-path parameters `-IgdbCredentialFile` and
    `-SteamGridDbCredentialFile`.
  - `windows_provider_offline_controller.ps1`, the elevated side of the
    `offline-handshake` file exchange (`request.json`, `blocked.json`,
    `done.json`, `restored.json`).

**Secret handling in this task (binding):**
- The harness reads the credential files only inside the interactive smoke
  process (to type them), inside ShellSeed (for the fixture check) and
  inside the installer process (in memory, only for the leak scan).
- Values are never passed as scheduled-task or process arguments, because
  Task Scheduler and process listings show arguments. Only file paths are
  passed.
- Values are never written to a result file, trace, screenshot or console.
- After the run, `Find-SecretInFiles` scans the result directory, the
  screenshots and the package `LocalState` for each value, in UTF-8 and
  UTF-16LE. It reports file names only. `providers.bin` is included,
  because DPAPI output must not contain the plaintext either.
- If either credential file is missing or malformed, its live scenarios
  are recorded as `skipped`, never as passed.

**Ruling T12-a (cancel during search):** A live search can finish before
Cancel is pressed. The mode tries up to three times. It records
`cancelObserved = true` only if the busy row closed with no status and no
results. Otherwise it records `cancelObserved = false` with the reason,
and does not report a pass. Task 9's `CancelLeavesNoRowAndNoFile` pins the
cancel behavior itself.

- [ ] **Step 1: Write the failing helper test**

  Create `tools/p1/test_windows_provider_credentials.ps1`:

```powershell
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows_provider_credentials.ps1')

$root = Join-Path $env:TEMP "DesktopGuides-Creds-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $file = Join-Path $root 'igdb.txt'
    Set-Content -LiteralPath $file -Encoding UTF8 -Value @(
        'IGDB Client ID:  abc123 ',
        'igdb client secret: s3cr+t{x}',
        'unrelated: ignored')
    $values = Read-LabelledValues $file @('igdb client id', 'igdb client secret')
    if ($values['igdb client id'] -ne 'abc123' -or $values['igdb client secret'] -ne 's3cr+t{x}') {
        throw 'Labelled values were not parsed.'
    }

    Set-Content -LiteralPath $file -Encoding UTF8 -Value 'igdb client id: only-this'
    $message = $null
    try { [void](Read-LabelledValues $file @('igdb client id', 'igdb client secret')) }
    catch { $message = $_.Exception.Message }
    if (-not $message -or $message -notmatch 'igdb client secret' -or $message -match 'only-this') {
        throw "A missing label was not reported by name alone: $message"
    }

    if ((ConvertTo-SendKeysLiteral 'a+b^c%d~e(f)g{h}i[j]') -ne
        'a{+}b{^}c{%}d{~}e{(}f{)}g{{}h{}}i{[}j{]}') {
        throw 'SendKeys metacharacters were not escaped.'
    }

    $clean = Join-Path $root 'clean.json'
    $utf8 = Join-Path $root 'leak-utf8.json'
    $utf16 = Join-Path $root 'leak-utf16.txt'
    Set-Content -LiteralPath $clean -Encoding UTF8 -Value '{"ok":true}'
    Set-Content -LiteralPath $utf8 -Encoding UTF8 -Value '{"x":"s3cr+t{x}"}'
    [System.IO.File]::WriteAllText($utf16, 'value s3cr+t{x}', [System.Text.Encoding]::Unicode)
    $found = @(Find-SecretInFiles @($root) @('s3cr+t{x}', 'never-there'))
    $names = @($found | ForEach-Object { Split-Path $_ -Leaf } | Sort-Object)
    if (($names -join ',') -ne 'leak-utf16.txt,leak-utf8.json') {
        throw "Leak scan found the wrong files: $($names -join ',')"
    }
    'Provider credential helper checks passed.'
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
```

- [ ] **Step 2: Run it on `pcsx2-win` and watch it fail**

  Stage the tree as in Task 11 Step 11, then:

```bash
ssh -o BatchMode=yes pcsx2-win "powershell -NoProfile -ExecutionPolicy Bypass -File E:\work\desktop-guides\t04-providers\tools\p1\test_windows_provider_credentials.ps1"
```

Expected: FAIL, because `windows_provider_credentials.ps1` does not exist.

- [ ] **Step 3: Implement `tools/p1/windows_provider_credentials.ps1`**

```powershell
# Reads `label: value` lines. Errors name the missing label, never a value.
function Read-LabelledValues([string] $Path, [string[]] $Labels) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Credential file $Path is missing."
    }
    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        $parts = $line -split ':', 2
        if ($parts.Count -ne 2) { continue }
        $label = $parts[0].Trim().ToLowerInvariant()
        if ($Labels -contains $label) { $values[$label] = $parts[1].Trim() }
    }
    foreach ($label in $Labels) {
        if (-not $values.ContainsKey($label) -or
            $values[$label].Length -lt 1 -or $values[$label].Length -gt 128) {
            throw "Credential file $Path has no usable '$label' line."
        }
    }
    return $values
}

# SendKeys treats these characters as commands; braces make them literal.
function ConvertTo-SendKeysLiteral([string] $Value) {
    $builder = [System.Text.StringBuilder]::new()
    foreach ($character in $Value.ToCharArray()) {
        if ('+^%~(){}[]'.IndexOf($character) -ge 0) {
            [void]$builder.Append('{').Append($character).Append('}')
        }
        else {
            [void]$builder.Append($character)
        }
    }
    return $builder.ToString()
}

# Returns the paths of files that contain any value as UTF-8 or UTF-16LE.
function Find-SecretInFiles([string[]] $Roots, [string[]] $Secrets) {
    $latin1 = [System.Text.Encoding]::GetEncoding(28591)
    $needles = foreach ($secret in $Secrets) {
        $latin1.GetString([System.Text.Encoding]::UTF8.GetBytes($secret))
        $latin1.GetString([System.Text.Encoding]::Unicode.GetBytes($secret))
    }
    foreach ($root in $Roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse -Force) {
            $text = $latin1.GetString([System.IO.File]::ReadAllBytes($file.FullName))
            foreach ($needle in $needles) {
                if ($text.IndexOf($needle, [StringComparison]::Ordinal) -ge 0) {
                    $file.FullName
                    break
                }
            }
        }
    }
}
```

  Latin-1 maps every byte to one character, so an ordinal `IndexOf`
  searches the raw bytes.

- [ ] **Step 4: Run the helper test**

  Stage the tree again, then run the Step 2 command.
Expected: `Provider credential helper checks passed.`

- [ ] **Step 5: Add the ShellSeed commands**

  In `tools/p1/DesktopGuides.ShellSeed/Program.cs`, add these usings:

```csharp
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Artwork;
using DesktopGuides.Infrastructure.Providers;
```

  Place the three commands below before the existing
  `if (args.Length != 2 || args[0] is not (...))` usage check, next to
  `set-material`. Add their forms to the usage text:
  - `seed-linked-game|describe-providers <app-data-root>`
  - `check-igdb-fields <igdb-credential-file> <fixture-dir>`

  **`seed-linked-game`** adds one linked game with a full snapshot and a
  real cover, without any network access. The external ID `900001` cannot
  collide with the live Half-Life add.

```csharp
if (args.Length == 2 && args[0] == "seed-linked-game")
{
    ManagedPathResolver linkedPaths = new(args[1]);
    await using SqliteLibraryRepository linkedRepository = new(linkedPaths);
    await linkedRepository.InitializeAsync();
    Guid gameId = Guid.NewGuid();
    StoredArtwork cover = await new ManagedArtworkStore(linkedPaths)
        .StoreAsync(gameId, SolidPng(60, 90, 0x2E, 0x5E, 0x8C), CancellationToken.None);
    GameMetadataSnapshot snapshot = new(
        GameMetadataSnapshot.CurrentSchemaVersion,
        "A seeded summary that the offline check reads back.",
        new DateOnly(2004, 11, 16),
        ["Shooter"],
        ["Seed Developer"],
        ["Seed Publisher"],
        ["PC (Microsoft Windows)", "PlayStation 2"],
        "https://www.igdb.com/games/seeded-linked-game",
        GameTypeTag.MainGame,
        "SteamGridDB");
    await linkedRepository.AddLinkedGameAsync(new NewLinkedGame(
        gameId, "Seeded Linked Game", null,
        new ProviderGameLink(ProviderGameLink.Igdb, "900001", DateTimeOffset.UtcNow),
        snapshot, cover.RelativePath), CancellationToken.None);
    Console.WriteLine($"Seeded linked game {gameId:N}.");
    return 0;
}
```

  **`describe-providers`** prints the persisted state as JSON. It prints
  whether `providers.bin` exists, never its contents.

```csharp
if (args.Length == 2 && args[0] == "describe-providers")
{
    ManagedPathResolver describePaths = new(args[1]);
    await using SqliteLibraryRepository describeRepository = new(describePaths);
    await describeRepository.InitializeAsync();
    ManagedArtworkStore store = new(describePaths);
    var games = (await describeRepository.ListGamesAsync()).Select(game => new
    {
        game.Title,
        game.Platform,
        game.Notes,
        Provider = game.Link?.Provider,
        ExternalId = game.Link?.ExternalId,
        HasMetadata = game.Metadata is not null,
        ArtworkSource = game.Metadata?.ArtworkSource,
        game.ArtworkRelativePath,
        ArtworkExists = game.ArtworkRelativePath is { } path && store.ResolveFile(path) is not null,
    });
    int CountFiles(string root) => Directory.Exists(root)
        ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count()
        : 0;
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Games = games,
        ArtworkFiles = CountFiles(describePaths.ArtworkRoot),
        StagingFiles = CountFiles(describePaths.ArtworkStagingRoot),
        CredentialBlobExists = File.Exists(Path.Combine(args[1], "providers.bin")),
    }));
    return 0;
}
```

  **`check-igdb-fields`** sends every field path found in the Task 7
  fixtures to IGDB in one query. IGDB answers HTTP 400 for an unknown
  field, so a 200 with at least one result means the fixture field names
  are all real. The command reads the credential file itself and prints
  only the field count or IGDB's error body, which never contains a
  credential.

```csharp
if (args.Length == 3 && args[0] == "check-igdb-fields")
{
    Dictionary<string, string> labelled = File.ReadLines(args[1])
        .Select(line => line.Split(':', 2))
        .Where(parts => parts.Length == 2)
        .GroupBy(parts => parts[0].Trim().ToLowerInvariant())
        .ToDictionary(group => group.Key, group => group.Last()[1].Trim());
    if (!labelled.TryGetValue("igdb client id", out string? clientId) ||
        !labelled.TryGetValue("igdb client secret", out string? clientSecret))
    {
        Console.Error.WriteLine("The IGDB credential file has no client id or client secret line.");
        return 2;
    }
    SortedSet<string> fields = [];
    foreach (string fixture in Directory.EnumerateFiles(args[2], "igdb-*.json"))
    {
        CollectFieldPaths(JsonNode.Parse(File.ReadAllText(fixture)), string.Empty, fields);
    }
    using ProviderHttp http = new();
    TwitchTokenSource tokens = new(http);
    string token = await tokens.GetTokenAsync(
        new IgdbCredentials(clientId, clientSecret), false, CancellationToken.None);
    string body = $"fields {string.Join(",", fields)}; search \"Half-Life\"; limit 1;";
    ProviderResponse response = await http.SendAsync(
        HttpMethod.Post, new Uri("https://api.igdb.com/v4/games"), request =>
        {
            request.Headers.Add("Client-ID", clientId);
            request.Headers.Authorization = new("Bearer", token);
            request.Content = new StringContent(body, Encoding.UTF8, "text/plain");
        }, ProviderHttp.MaxJsonBytes, CancellationToken.None);
    if (response.StatusCode != HttpStatusCode.OK ||
        JsonNode.Parse(response.Body) is not JsonArray { Count: > 0 })
    {
        Console.Error.WriteLine(
            $"IGDB rejected the fixture fields (HTTP {(int)response.StatusCode}): " +
            Encoding.UTF8.GetString(response.Body));
        return 1;
    }
    Console.WriteLine($"IGDB accepted {fields.Count} fixture fields: {string.Join(",", fields)}");
    return 0;
}
```

  Add these helpers at the end of the file, next to `InsertGuideAsync`:

```csharp
static void CollectFieldPaths(JsonNode? node, string prefix, SortedSet<string> fields)
{
    switch (node)
    {
        case JsonArray array:
            foreach (JsonNode? item in array) CollectFieldPaths(item, prefix, fields);
            break;
        case JsonObject item:
            foreach ((string name, JsonNode? value) in item)
            {
                string path = prefix.Length == 0 ? name : $"{prefix}.{name}";
                // Expanded objects and object arrays become dotted paths
                // such as platforms.name; scalars and ID arrays are leaves.
                bool expanded = value is JsonObject ||
                    (value is JsonArray items && items.Any(entry => entry is JsonObject));
                if (expanded)
                {
                    CollectFieldPaths(value, path, fields);
                }
                else
                {
                    fields.Add(path);
                }
            }
            break;
    }
}

// A valid, decodable RGB PNG of one colour, so the offline check needs no
// provider image.
static byte[] SolidPng(int width, int height, byte red, byte green, byte blue)
{
    byte[] raw = new byte[height * (1 + width * 3)];
    for (int row = 0; row < height; row++)
    {
        int offset = row * (1 + width * 3);
        for (int x = 0; x < width; x++)
        {
            raw[offset + 1 + x * 3] = red;
            raw[offset + 2 + x * 3] = green;
            raw[offset + 3 + x * 3] = blue;
        }
    }
    using MemoryStream compressed = new();
    using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
    {
        zlib.Write(raw);
    }
    byte[] header = new byte[13];
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, width);
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
    header[8] = 8; // bit depth
    header[9] = 2; // colour type: RGB
    using MemoryStream png = new();
    png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    WriteChunk(png, "IHDR", header);
    WriteChunk(png, "IDAT", compressed.ToArray());
    WriteChunk(png, "IEND", []);
    return png.ToArray();
}

static void WriteChunk(Stream stream, string type, byte[] data)
{
    Span<byte> length = stackalloc byte[4];
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
    stream.Write(length);
    byte[] typed = [.. Encoding.ASCII.GetBytes(type), .. data];
    stream.Write(typed);
    Span<byte> crc = stackalloc byte[4];
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typed));
    stream.Write(crc);
}

static uint Crc32(byte[] bytes)
{
    uint crc = 0xFFFFFFFF;
    foreach (byte value in bytes)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
    }
    return ~crc;
}
```

  Build and try the offline-safe commands against a scratch data root on
  the host:

```bash
ssh -o BatchMode=yes pcsx2-win "cd /d E:\work\desktop-guides\t04-providers && dotnet run --project tools\p1\DesktopGuides.ShellSeed -c Release -- seed-linked-game %TEMP%\dg-seed-check && dotnet run --project tools\p1\DesktopGuides.ShellSeed -c Release --no-build -- describe-providers %TEMP%\dg-seed-check && rmdir /s /q %TEMP%\dg-seed-check"
```

Expected: `Seeded linked game …`, then one JSON line with one game whose
`Provider` is `igdb`, `ExternalId` is `900001`, `ArtworkExists` is true,
`ArtworkFiles` is 1, `StagingFiles` is 0 and `CredentialBlobExists` is false.
The seeded PNG passing `StoreAsync` proves that `ArtworkValidator` accepts it.

- [ ] **Step 6: Add the provider smoke modes**

  All edits are in `tools/p1/windows_shell_ui_smoke.ps1`.

  **6a. Parameters.** Add the five modes to the `-Mode` `ValidateSet`:
  `'provider-none', 'provider-offline', 'provider-settings',
  'provider-live', 'provider-remove'`. Add these parameters after
  `-SwitchToMaterial`. The two file parameters take paths only; values are
  read inside this process.

```powershell
    [string] $IgdbCredentialFile = '',

    [string] $SteamGridDbCredentialFile = '',

    # The provider failure Refresh and search should report: no saved
    # credentials, or credentials saved while the network is blocked.
    [ValidateSet('NotConfigured', 'Unavailable')]
    [string] $ExpectedProviderFailure = 'NotConfigured'
```

  Dot-source the helpers next to the existing `Add-Type` lines:

```powershell
    . (Join-Path $PSScriptRoot 'windows_provider_credentials.ps1')
```

  **6b. Let `Wait-Status` accept several messages.** Refresh can end with
  either of two messages, depending on whether a cover could be
  downloaded. Change the parameter from `[string] $expected` to
  `[string[]] $expected`, and make these two replacements inside the
  function. Callers that pass one string are unchanged.

```powershell
        $transient = @($expected | Where-Object { $_ -in @(
            'Library ready.',
            'Game ready.',
            'Guide details ready.',
            'Settings ready.') }).Count -gt 0
```

```powershell
            if ($message -in $expected -and
                $sequence -gt $script:lastStatusSequence) {
```

  and inside the visible check, replace
  `$visibleStatus.Current.Name -eq $expected` with
  `$visibleStatus.Current.Name -eq $message`. Change the final `throw` to
  `"Expected shell status '$($expected -join "' or '")'; $lastObserved."`.

  **6c. Helpers.** Add these after `Assert-ShellForeground`:

```powershell
    $providerFailureMessages = @{
        NotConfigured = 'Add IGDB credentials in Settings to search.'
        Unavailable = "Can't reach IGDB. Check your connection."
    }

    function Open-ProviderSettings {
        Select-Element 'Settings'
        [void](Wait-Name 'SettingsHeading' 'Settings')
        Expand-ProviderSettings
    }

    function Expand-ProviderSettings {
        $expander = Wait-VisibleById 'ProviderSettingsExpander'
        $pattern = $null
        if ($expander.TryGetCurrentPattern(
            [System.Windows.Automation.ExpandCollapsePattern]::Pattern,
            [ref]$pattern)) {
            if ($pattern.Current.ExpandCollapseState -ne
                [System.Windows.Automation.ExpandCollapseState]::Expanded) {
                $pattern.Expand()
            }
        }
        else {
            Click-Element $expander
        }
        [void](Wait-VisibleById 'IgdbClientIdInput')
    }

    function Assert-ProviderSettingsExpanded {
        $expander = Wait-VisibleById 'ProviderSettingsExpander'
        $pattern = $null
        if ($expander.TryGetCurrentPattern(
            [System.Windows.Automation.ExpandCollapsePattern]::Pattern,
            [ref]$pattern) -and
            $pattern.Current.ExpandCollapseState -ne
                [System.Windows.Automation.ExpandCollapseState]::Expanded) {
            throw 'The provider settings card was not expanded.'
        }
        [void](Wait-VisibleById 'IgdbClientIdInput')
    }

    # Types a value into a PasswordBox. It checks focus first, so a value
    # is never typed into the wrong control, and its errors never include
    # the value.
    function Enter-Secret([string] $id, [string] $value, [switch] $Replace) {
        $element = Wait-EnabledById $id
        $element.SetFocus()
        Start-Sleep -Milliseconds 150
        $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
        if (-not $focused -or $focused.Current.AutomationId -ne $id) {
            throw "Focus did not reach $id, so nothing was typed."
        }
        try {
            if ($Replace) { [System.Windows.Forms.SendKeys]::SendWait('^a{DEL}') }
            [System.Windows.Forms.SendKeys]::SendWait((ConvertTo-SendKeysLiteral $value))
        }
        catch {
            throw "Typing into $id failed."
        }
    }

    function Assert-Absent([string] $id) {
        $element = Find-ById $id
        if ($element -and -not $element.Current.IsOffscreen) {
            throw "'$id' was visible."
        }
    }

    function Open-AddGameSearch {
        Invoke-Element (Wait-EnabledById 'AddGameButton')
        [void](Wait-VisibleById 'GameSearchInput')
        Assert-Absent 'GameTitleInput'
    }

    # The AutoSuggestBox exposes its text through its inner edit box.
    function Set-SearchQuery([string] $query) {
        $box = Wait-VisibleById 'GameSearchInput'
        $pattern = $null
        if (-not $box.TryGetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) {
            $edit = $box.FindFirst($scope,
                [System.Windows.Automation.PropertyCondition]::new(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Edit))
            if (-not $edit) { throw 'The search box has no editable text.' }
            $pattern = $edit.GetCurrentPattern(
                [System.Windows.Automation.ValuePattern]::Pattern)
        }
        $pattern.SetValue($query)
    }

    function Get-SearchResults {
        $list = Find-ById 'GameSearchResults'
        if (-not $list -or $list.Current.IsOffscreen) { return @() }
        return @($list.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::ListItem)))
    }

    function Search-Games([string] $query) {
        Set-SearchQuery $query
        Invoke-Element (Wait-EnabledById 'GameSearchButton')
        $deadline = (Get-Date).AddSeconds(30)
        do {
            Start-Sleep -Milliseconds 250
            # The busy row is a StackPanel with no automation peer; its
            # Cancel button shows exactly while work runs.
            $busy = Find-ById 'GameSearchCancel'
            $running = $busy -and -not $busy.Current.IsOffscreen
            $status = Find-ById 'GameSearchStatus'
            $hasStatus = $status -and -not $status.Current.IsOffscreen -and
                $status.Current.Name
            $results = Get-SearchResults
            if (-not $running -and ($results.Count -gt 0 -or $hasStatus)) {
                return $results
            }
        } while ((Get-Date) -lt $deadline)
        throw "Search for '$query' did not finish."
    }

    function Activate-Item($item) {
        $pattern = $null
        if ($item.TryGetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
            $pattern.Invoke()
        }
        else {
            Click-Element $item
        }
    }

    function Close-AddGameSearch {
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Wait-HiddenById 'GameSearchInput'
    }

    function Get-FactsName {
        $facts = Wait-VisibleById 'GameFacts'
        if ($facts.Current.Name) { return $facts.Current.Name }
        $text = $facts.FindFirst($scope,
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Text))
        if ($text) { return $text.Current.Name }
        return ''
    }
```

  `Get-FactsName` reads the accessible text that app code builds from the
  `MetadataItem` labels. It does not check how the toolkit lays the items
  out.

  **6d. Modes.** Add these branches to the mode chain, before
  `elseif ($Mode -eq 'normal')`.

  `provider-none` runs on a fresh profile with no saved credentials:

```powershell
    elseif ($Mode -eq 'provider-none') {
        [void](Wait-Status 'Library ready.')
        Invoke-Element (Wait-EnabledById 'AddGameButton')
        [void](Wait-Name 'GameEditorNotice' $providerFailureMessages.NotConfigured)
        [void](Wait-VisibleById 'GameTitleInput')
        Assert-Absent 'GameSearchInput'
        $report.noCredentialsScreenshot = Save-WindowScreenshot 'add-game-no-credentials'
        $report.phases += 'no-credentials-opens-manual-add-with-notice'

        Invoke-Element (Wait-VisibleById 'GameEditorNoticeSettingsLink')
        Wait-EditorClosed
        [void](Wait-Name 'SettingsHeading' 'Settings')
        Assert-ProviderSettingsExpanded
        [void](Wait-Name 'ProviderSettingsExpander' `
            'Game data providers. Add your IGDB credentials to search for games.')
        $report.phases += 'notice-opens-expanded-provider-settings'
        Go-Back
        [void](Wait-Status 'Library ready.')
    }
```

  `provider-offline` opens the seeded linked game. It runs twice: first
  with no saved credentials, then (only with the firewall rule) with
  credentials saved and the executable's outbound traffic blocked. Neither
  run can reach a provider, so the snapshot and cover must come from
  local data.

```powershell
    elseif ($Mode -eq 'provider-offline') {
        [void](Wait-Status 'Library ready.')
        (Wait-GameRow 'Seeded Linked Game').GetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        [void](Wait-Name 'GameHeading' 'Seeded Linked Game')
        [void](Wait-Status 'Game ready.')
        [void](Wait-VisibleById 'GameCover')
        $facts = Get-FactsName
        foreach ($expected in @('Released 2004', 'Main game',
            'Platforms: PC (Microsoft Windows), PlayStation 2')) {
            if (-not $facts.Contains($expected)) {
                throw "Game facts '$facts' did not include '$expected'."
            }
        }
        [void](Wait-Name 'GameSummary' 'A seeded summary that the offline check reads back.')
        [void](Wait-Name 'GameGenres' 'Genres: Shooter')
        [void](Wait-Name 'GameCompanies' 'Developed by Seed Developer. Published by Seed Publisher.')
        [void](Wait-Name 'GameAttribution' 'Metadata from IGDB. Artwork from SteamGridDB.')
        [void](Wait-VisibleById 'GameProviderLink')
        $report.gameFacts = $facts
        $report.offlineGameScreenshot = Save-WindowScreenshot "linked-game-$($ExpectedProviderFailure.ToLowerInvariant())"
        $report.phases += 'snapshot-and-cover-from-local-data'

        Invoke-Element (Wait-EnabledById 'RefreshMetadataButton')
        $expectedFailure = $providerFailureMessages[$ExpectedProviderFailure]
        [void](Wait-Status $expectedFailure)
        [void](Wait-Name 'GameHeading' 'Seeded Linked Game')
        [void](Wait-Name 'GameAttribution' 'Metadata from IGDB. Artwork from SteamGridDB.')
        [void](Wait-EnabledById 'RefreshMetadataButton')
        $report.phases += 'failed-refresh-keeps-snapshot'

        if ($ExpectedProviderFailure -eq 'Unavailable') {
            Go-Back
            [void](Wait-Status 'Library ready.')
            Open-AddGameSearch
            [void](Search-Games 'Half-Life')
            [void](Wait-Name 'GameSearchStatus' $expectedFailure)
            [void](Wait-VisibleById 'GameSearchRetry')
            if ((Get-SearchResults).Count -ne 0) {
                throw 'A failed search showed results.'
            }
            $report.offlineSearchScreenshot = Save-WindowScreenshot 'search-offline'
            Close-AddGameSearch
            $report.phases += 'offline-search-reports-unavailable'
        }
    }
```

  `provider-settings` first checks that IGDB rejects a wrong secret, then
  types and saves the real values. The screenshot is taken after Save,
  when every field is empty again.

```powershell
    elseif ($Mode -eq 'provider-settings') {
        $igdb = Read-LabelledValues $IgdbCredentialFile @('igdb client id', 'igdb client secret')
        $steamGridDbKey = $null
        if ($SteamGridDbCredentialFile) {
            $steamGridDbKey = (Read-LabelledValues $SteamGridDbCredentialFile @('steamgriddb api key'))['steamgriddb api key']
        }
        [void](Wait-Status 'Library ready.')
        Open-ProviderSettings
        Invoke-Element (Wait-EnabledById 'ProviderTestButton')
        [void](Wait-Name 'ProviderSettingsStatus' 'Enter credentials to test them.')
        $report.phases += 'test-with-no-credentials-asks-for-them'

        Enter-Secret 'IgdbClientIdInput' $igdb['igdb client id']
        Enter-Secret 'IgdbClientSecretInput' 'desktop-guides-wrong-secret'
        Invoke-Element (Wait-EnabledById 'ProviderTestButton')
        [void](Wait-Name 'ProviderSettingsStatus' 'IGDB rejected the client ID or secret.')
        $report.phases += 'wrong-secret-is-rejected'

        Enter-Secret 'IgdbClientSecretInput' $igdb['igdb client secret'] -Replace
        $expectedTest = 'IGDB connected.'
        if ($steamGridDbKey) {
            Enter-Secret 'SteamGridDbKeyInput' $steamGridDbKey
            $expectedTest = 'IGDB connected. SteamGridDB connected.'
        }
        Invoke-Element (Wait-EnabledById 'ProviderTestButton')
        [void](Wait-Name 'ProviderSettingsStatus' $expectedTest)
        $report.steamGridDbTested = [bool]$steamGridDbKey
        $report.phases += 'real-credentials-connect'

        Invoke-Element (Wait-EnabledById 'ProviderSaveButton')
        [void](Wait-Name 'ProviderSettingsStatus' 'Provider credentials saved.')
        [void](Wait-Name 'ProviderSettingsExpander' 'Game data providers. IGDB credentials saved.')
        $report.settingsScreenshot = Save-WindowScreenshot 'provider-settings-saved'
        $report.phases += 'credentials-saved'
        Go-Back
        [void](Wait-Status 'Library ready.')
    }
```

  `provider-live` needs saved credentials and network access. IGDB's
  record for the 1998 Half-Life is labelled `Half-Life, Main game, 1998`.

```powershell
    elseif ($Mode -eq 'provider-live') {
        [void](Wait-Status 'Library ready.')
        Open-AddGameSearch
        Set-SearchQuery 'Half-Life'
        Start-Sleep -Seconds 1
        Assert-Absent 'GameSearchCancel'
        if ((Get-SearchResults).Count -ne 0) { throw 'Typing alone showed results.' }
        $report.searchDialogScreenshot = Save-WindowScreenshot 'add-game-search'
        $report.phases += 'typing-does-not-search'

        $results = Search-Games 'Half-Life'
        if ($results.Count -lt 1 -or $results.Count -gt 20) {
            throw "Search returned $($results.Count) results; expected 1 to 20."
        }
        $report.searchResultCount = $results.Count
        $report.searchResultNames = @($results | ForEach-Object { $_.Current.Name })
        $report.searchResultsScreenshot = Save-WindowScreenshot 'search-results'
        $halfLife = @($results | Where-Object { $_.Current.Name -like 'Half-Life, Main game, 1998, *' })
        if ($halfLife.Count -ne 1) {
            throw "Expected one 1998 Half-Life result; found $($halfLife.Count)."
        }
        $report.phases += 'search-returns-labelled-results'

        Activate-Item $halfLife[0]
        Wait-HiddenById 'GameSearchInput'
        [void](Wait-Name 'GameHeading' 'Half-Life')
        [void](Wait-VisibleById 'GameProviderDetails')
        $attribution = (Wait-VisibleById 'GameAttribution').Current.Name
        if ($attribution -notin @(
            'Metadata from IGDB. Artwork from SteamGridDB.',
            'Metadata and artwork from IGDB.')) {
            throw "Unexpected attribution '$attribution'."
        }
        [void](Wait-VisibleById 'GameCover')
        [void](Wait-VisibleById 'GameProviderLink')
        $report.addedAttribution = $attribution
        $report.addedFacts = Get-FactsName
        $report.linkedGameScreenshot = Save-WindowScreenshot 'linked-game-live'
        $report.phases += 'pick-adds-linked-game-with-cover'

        Go-Back
        [void](Wait-Status 'Library ready.')
        Open-AddGameSearch
        $again = Search-Games 'Half-Life'
        Activate-Item @($again | Where-Object { $_.Current.Name -like 'Half-Life, Main game, 1998, *' })[0]
        Wait-HiddenById 'GameSearchInput'
        [void](Wait-Status 'Half-Life is already in your library.' -AllowHidden)
        [void](Wait-Name 'GameHeading' 'Half-Life')
        Go-Back
        [void](Wait-Status 'Library ready.')
        if ((Count-GameRows 'Half-Life') -ne 1) { throw 'A duplicate add created a second row.' }
        $report.phases += 'duplicate-add-opens-existing-game'

        (Wait-GameRow 'Half-Life').GetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        [void](Wait-Status 'Game ready.')
        Invoke-Element (Wait-EnabledById 'EditGameButton')
        Set-Text 'GamePlatformInput' 'My test platform'
        Set-Text 'GameNotesInput' 'My local notes.'
        Press-Enter (Wait-VisibleById 'GameTitleInput')
        Wait-EditorClosed
        [void](Wait-Name 'GamePlatform' 'My test platform')
        Invoke-Element (Wait-EnabledById 'RefreshMetadataButton')
        $refreshed = Wait-Status @('Metadata refreshed.',
            "Metadata refreshed. A new cover couldn't be downloaded.") -AllowHidden
        $report.refreshStatus = $refreshed.Current.ItemStatus
        [void](Wait-Name 'GameHeading' 'Half-Life')
        [void](Wait-Name 'GamePlatform' 'My test platform')
        [void](Wait-Name 'GameNotes' 'My local notes.')
        $report.phases += 'refresh-preserves-local-fields'

        Go-Back
        [void](Wait-Status 'Library ready.')
        Open-AddGameSearch
        [void](Search-Games 'zzqx no such game 91377')
        [void](Wait-Name 'GameSearchStatus' 'No games match "zzqx no such game 91377".')
        $report.phases += 'empty-search-says-so'

        # Ruling T12-a: record whether Cancel was seen, never assume it.
        $report.cancelObserved = $false
        $report.cancelAttempts = 0
        for ($attempt = 1; $attempt -le 3 -and -not $report.cancelObserved; $attempt++) {
            $report.cancelAttempts = $attempt
            Set-SearchQuery 'Final Fantasy'
            Invoke-Element (Wait-EnabledById 'GameSearchButton')
            $cancel = Find-ById 'GameSearchCancel'
            if (-not $cancel -or $cancel.Current.IsOffscreen) {
                $report.cancelReason = 'The search finished before Cancel could be pressed.'
                Start-Sleep -Seconds 2
                continue
            }
            Invoke-Element $cancel
            Wait-HiddenById 'GameSearchCancel'
            $status = Find-ById 'GameSearchStatus'
            if ((Get-SearchResults).Count -eq 0 -and
                (-not $status -or $status.Current.IsOffscreen)) {
                $report.cancelObserved = $true
                $report.cancelReason = $null
            }
            else {
                $report.cancelReason = 'The search completed while Cancel was pressed.'
            }
        }
        if ($report.cancelObserved) { $report.phases += 'cancel-during-search-shows-nothing' }

        Invoke-Element (Wait-VisibleById 'AddGameManuallyLink')
        [void](Wait-VisibleById 'GameTitleInput')
        Assert-Absent 'GameEditorNotice'
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Wait-EditorClosed
        [void](Wait-Status 'Library ready.' -AllowHidden)
        $report.phases += 'add-manually-from-search'
    }
```

  `provider-remove` removes the saved credentials and checks that Add
  game falls back to manual add:

```powershell
    elseif ($Mode -eq 'provider-remove') {
        [void](Wait-Status 'Library ready.')
        Open-ProviderSettings
        Invoke-Element (Wait-EnabledById 'ProviderRemoveButton')
        [void](Wait-Name 'ProviderSettingsStatus' 'Provider credentials removed.')
        [void](Wait-Name 'ProviderSettingsExpander' `
            'Game data providers. Add your IGDB credentials to search for games.')
        Go-Back
        [void](Wait-Status 'Library ready.')
        Invoke-Element (Wait-EnabledById 'AddGameButton')
        [void](Wait-Name 'GameEditorNotice' $providerFailureMessages.NotConfigured)
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Wait-EditorClosed
        $report.phases += 'remove-restores-manual-add'
    }
```

  **6e. Failure inspection.** In the `catch` block, extend the
  `if ($Mode -eq 'game-editor' -and $root)` condition to
  `if (($Mode -eq 'game-editor' -or $Mode -like 'provider-*') -and $root)`,
  and add `'GameSearchStatus'`, `'ProviderSettingsStatus'`,
  `'GameEditorNotice'` and `'GameAttribution'` to its list of IDs. None of
  these elements ever shows a credential. The `provider-settings` failure
  screenshot is safe too, because a `PasswordBox` draws only dots.

  Stage the tree and parse-check the script on the host. A real run needs
  the installer (Step 7):

```bash
ssh -o BatchMode=yes pcsx2-win "powershell -NoProfile -Command \"\$e = \$null; [void][System.Management.Automation.Language.Parser]::ParseFile('E:\work\desktop-guides\t04-providers\tools\p1\windows_shell_ui_smoke.ps1', [ref]\$null, [ref]\$e); if (\$e) { \$e; exit 1 } else { 'Smoke script parses.' }\""
```

Expected: `Smoke script parses.`

- [ ] **Step 7: Orchestrate the provider scenarios in the installer**

  The firewall rule is owned by a separate elevated controller (7c) that
  runs in the SSH session, not by the installer. The rule's lifetime then
  does not depend on the interactive task: if the installer hangs or dies,
  the controller's watchdog still removes it. The two sides talk through
  files in the result directory, and no credential passes through them. The firewall rule blocks outbound traffic from
  the app's executable only. The network adapter, SSH and every other
  program keep working, so this is less invasive than the adapter
  procedure in step 6 of the runbook.

  **7a. Installer parameters and smoke arguments.** In
  `tools/p1/windows_shell_install.ps1`, add these after `-DesignOnly`:

```powershell
    [switch] $ProviderOnly,

    # Paths only. The values are read in memory and never passed on.
    [string] $IgdbCredentialFile = 'E:\work\igdb_credentials.txt',

    [string] $SteamGridDbCredentialFile = 'E:\work\steamgriddb_credentials.txt',

    # Wait for windows_provider_offline_controller.ps1 to block the app's
    # network access for one scenario. Needs the user's authorization.
    [switch] $AllowOfflineFirewallRule
```

  Dot-source `windows_provider_credentials.ps1` next to the other helper
  scripts. Give `Run-ShellSmoke` three more parameters,
  `[string] $IgdbCredentialFile = ''`,
  `[string] $SteamGridDbCredentialFile = ''` and
  `[string] $ExpectedProviderFailure = ''`, and append them to
  `$arguments` only when set, the same way as `$ExpectedMaterial`:

```powershell
    if ($IgdbCredentialFile) {
        $arguments += ' -IgdbCredentialFile "' + $IgdbCredentialFile + '"'
    }
    if ($SteamGridDbCredentialFile) {
        $arguments += ' -SteamGridDbCredentialFile "' + $SteamGridDbCredentialFile + '"'
    }
    if ($ExpectedProviderFailure) {
        $arguments += ' -ExpectedProviderFailure ' + $ExpectedProviderFailure
    }
```

  Live provider calls take several seconds, so raise the smoke result
  timeout for provider modes only. Replace
  `$deadline = (Get-Date).AddSeconds(60)` with:

```powershell
    $timeoutSeconds = if ($mode -like 'provider-*') { 240 } else { 60 }
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
```

  **7b. `Run-ProviderScenarios`.** Add these functions after
  `Run-MaterialScenarios`:

```powershell
function Invoke-ShellSeed([string[]] $SeedArguments) {
    $output = @(dotnet run --project $seedProject -c Release --no-restore -- @SeedArguments)
    if ($LASTEXITCODE -ne 0) {
        throw "ShellSeed $($SeedArguments[0]) failed: $($output | Select-Object -Last 3)"
    }
    return $output | Select-Object -Last 1
}

function Get-ProviderState {
    return Invoke-ShellSeed @('describe-providers', $dataRoot) | ConvertFrom-Json
}

function Assert-NoCredentialLeak([string[]] $Secrets, [string] $Label) {
    $found = @(Find-SecretInFiles @($ResultDirectory, $dataRoot) $Secrets)
    $report.providers["leakScan$Label"] = [ordered]@{
        roots = @($ResultDirectory, $dataRoot)
        filesWithCredentialValues = $found
    }
    if ($found.Count -gt 0) {
        throw "Credential values were found in $($found.Count) file(s): $($found -join ', ')"
    }
}

function Wait-HandshakeFile([string] $Path, [int] $Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $Path) {
            $content = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
            if ($content.runId -eq $runId) { return $content }
        }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

function Run-BlockedNetworkScenario {
    $handshake = Join-Path $ResultDirectory 'offline-handshake'
    New-Item -ItemType Directory -Force $handshake | Out-Null
    [ordered]@{ runId = $runId; program = $expectedExecutablePath } |
        ConvertTo-Json | Set-Content (Join-Path $handshake 'request.json') -Encoding UTF8
    $blocked = Wait-HandshakeFile (Join-Path $handshake 'blocked.json') 120
    if (-not $blocked) {
        throw 'The offline controller did not confirm the firewall rule.'
    }
    $report.providers.firewallRule = $blocked.ruleName
    try {
        Start-InstalledShell
        $report.providers.blockedNetwork = Run-ShellSmoke 'provider-offline' `
            -ResultName 'provider-offline-blocked' -ExpectedProviderFailure 'Unavailable'
        Close-InstalledShell
    }
    finally {
        [ordered]@{ runId = $runId } | ConvertTo-Json |
            Set-Content (Join-Path $handshake 'done.json') -Encoding UTF8
        $restored = Wait-HandshakeFile (Join-Path $handshake 'restored.json') 60
        $report.providers.firewallRuleRemoved = [bool]($restored -and $restored.removed)
        if (-not $report.providers.firewallRuleRemoved) {
            $cleanupErrors.Add('The offline controller did not confirm that it removed the firewall rule.')
        }
    }
}

function Run-ProviderScenarios {
    $report.providers = [ordered]@{}
    $providers = $report.providers
    $fixtureDirectory = Join-Path $PSScriptRoot `
        '..\..\tests\DesktopGuides.Infrastructure.Tests\Providers\Fixtures'
    $secrets = @()
    $igdbReady = $false
    $steamGridDbFile = ''
    try {
        $secrets += @((Read-LabelledValues $IgdbCredentialFile `
            @('igdb client id', 'igdb client secret')).Values)
        $igdbReady = $true
    }
    catch {
        $providers.live = 'skipped'
        $providers.liveSkipReason = $_.Exception.Message
    }
    try {
        $secrets += @((Read-LabelledValues $SteamGridDbCredentialFile `
            @('steamgriddb api key')).Values)
        $steamGridDbFile = $SteamGridDbCredentialFile
    }
    catch {
        $providers.steamGridDb = 'skipped'
        $providers.steamGridDbSkipReason = $_.Exception.Message
    }

    [void](Invoke-ShellSeed @('seed-linked-game', $dataRoot))
    Start-InstalledShell
    $providers.none = Run-ShellSmoke 'provider-none'
    $providers.offlineWithoutCredentials = Run-ShellSmoke 'provider-offline' `
        -ResultName 'provider-offline-none' -ExpectedProviderFailure 'NotConfigured'
    if ($igdbReady) {
        $providers.settings = Run-ShellSmoke 'provider-settings' `
            -IgdbCredentialFile $IgdbCredentialFile `
            -SteamGridDbCredentialFile $steamGridDbFile
    }
    Close-InstalledShell
    if (-not $igdbReady) {
        $providers.blockedNetwork = 'skipped: no IGDB credential file'
        $providers.finalState = Get-ProviderState
        return
    }

    $providers.fixtureFields = Invoke-ShellSeed @(
        'check-igdb-fields', $IgdbCredentialFile, $fixtureDirectory)
    $saved = Get-ProviderState
    if (-not $saved.CredentialBlobExists) {
        throw 'Saving provider credentials did not create providers.bin.'
    }

    Start-InstalledShell
    $providers.live = Run-ShellSmoke 'provider-live'
    Close-InstalledShell
    $providers.cancelDuringSearch = if ($providers.live.cancelObserved) {
        'observed'
    } else {
        "not-observed: $($providers.live.cancelReason)"
    }
    $state = Get-ProviderState
    $halfLife = @($state.Games | Where-Object { $_.Title -eq 'Half-Life' })
    if ($halfLife.Count -ne 1 -or $halfLife[0].Provider -ne 'igdb' -or
        -not $halfLife[0].HasMetadata -or
        $halfLife[0].Platform -ne 'My test platform' -or
        $halfLife[0].Notes -ne 'My local notes.') {
        throw "The added game was not stored as expected: $($halfLife | ConvertTo-Json -Compress)"
    }
    $withArtwork = @($state.Games | Where-Object { $_.ArtworkRelativePath })
    if (@($withArtwork | Where-Object { -not $_.ArtworkExists }).Count -gt 0 -or
        $state.ArtworkFiles -ne $withArtwork.Count -or $state.StagingFiles -ne 0) {
        throw "Artwork files did not match the rows: $($state | ConvertTo-Json -Compress -Depth 4)"
    }
    $providers.liveState = $state
    Assert-NoCredentialLeak $secrets 'AfterLive'

    if ($AllowOfflineFirewallRule) {
        Run-BlockedNetworkScenario
    }
    else {
        $providers.blockedNetwork = 'not-run: -AllowOfflineFirewallRule was not passed'
    }

    Start-InstalledShell
    $providers.remove = Run-ShellSmoke 'provider-remove'
    Close-InstalledShell
    $providers.finalState = Get-ProviderState
    if ($providers.finalState.CredentialBlobExists) {
        throw 'Removing provider credentials left providers.bin behind.'
    }
    Assert-NoCredentialLeak $secrets 'AfterRemove'
}
```

  `Get-ProviderState` prints file counts and titles, never credential
  values. The scan covers `providers.bin` in the `AfterLive` pass, because
  DPAPI output must not contain the plaintext either.

  In the main flow, after the `-DesignOnly` block, add:

```powershell
    if ($ProviderOnly) {
        Run-ProviderScenarios
        $report.success = $true
        return
    }
```

  and at the end of the full run, after `Run-MaterialScenarios`, start the
  provider pass from an empty profile:

```powershell
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    Run-ProviderScenarios
```

  In the outer `finally`, before the scheduled-task cleanup, delete the
  blob as a fallback if a failure skipped `provider-remove`, and record
  that it happened:

```powershell
    if ($dataRoot) {
        $blob = Join-Path $dataRoot 'providers.bin'
        if (Test-Path -LiteralPath $blob) {
            Stop-InstalledShell -BestEffort
            Remove-Item -LiteralPath $blob -Force -ErrorAction SilentlyContinue
            $report.credentialBlobDeletedByCleanup = -not (Test-Path -LiteralPath $blob)
            if (-not $report.credentialBlobDeletedByCleanup) {
                $cleanupErrors.Add('Could not delete providers.bin during cleanup.')
            }
        }
    }
```

  Declare `$dataRoot = $null` with the other top-level variables so the
  check is safe when installation fails early. Uninstalling the package
  also deletes `LocalState`. The explicit delete covers a failure that
  leaves the package installed.

  **7c. Create `tools/p1/windows_provider_offline_controller.ps1`.** It
  runs in the elevated SSH session while the installer task runs. It adds
  one outbound block rule for the exact installed executable. It removes
  the rule when the installer says it is done, when its own watchdog
  expires, or when it exits for any reason.

```powershell
param(
    [Parameter(Mandatory = $true)]
    [string] $ResultDirectory,

    [ValidateRange(1, 60)]
    [int] $TimeoutMinutes = 45,

    # The longest the rule may stay in place, whatever the installer does.
    [ValidateRange(1, 15)]
    [int] $WatchdogMinutes = 10
)

$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'The offline controller must run elevated.'
}
$handshake = Join-Path $ResultDirectory 'offline-handshake'
$finalReport = Join-Path $ResultDirectory 'signed-install.json'
$ruleName = $null
$log = [ordered]@{ observedAt = (Get-Date).ToUniversalTime().ToString('o') }

function Read-Handshake([string] $Name) {
    $path = Join-Path $handshake $Name
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

function Write-Handshake([string] $Name, $Value) {
    $Value | ConvertTo-Json | Set-Content (Join-Path $handshake $Name) -Encoding UTF8
}

try {
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    $request = $null
    while (-not $request -and (Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $finalReport) { break }
        Start-Sleep -Seconds 1
        $request = Read-Handshake 'request.json'
    }
    if (-not $request) {
        $log.outcome = 'no-request'
        return
    }
    if ($request.runId -notmatch '^[0-9a-f]{32}$' -or
        $request.program -notlike "$env:ProgramFiles\WindowsApps\DesktopGuides.Preview_*_x64__*\DesktopGuides.Production.exe" -or
        -not (Test-Path -LiteralPath $request.program -PathType Leaf)) {
        throw 'The offline request did not name the installed Desktop Guides executable.'
    }
    $ruleName = "DesktopGuides-P1-ProviderOffline-$($request.runId)"
    New-NetFirewallRule -Name $ruleName -DisplayName $ruleName `
        -Direction Outbound -Action Block -Program $request.program -Profile Any | Out-Null
    if (-not (Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue)) {
        throw "Firewall rule $ruleName was not created."
    }
    $log.ruleName = $ruleName
    $log.blockedAt = (Get-Date).ToUniversalTime().ToString('o')
    Write-Handshake 'blocked.json' ([ordered]@{ runId = $request.runId; ruleName = $ruleName })

    $watchdog = (Get-Date).AddMinutes($WatchdogMinutes)
    while (-not (Read-Handshake 'done.json') -and (Get-Date) -lt $watchdog) {
        Start-Sleep -Seconds 1
    }
    $log.outcome = if (Read-Handshake 'done.json') { 'done' } else { 'watchdog-expired' }
}
finally {
    if ($ruleName) {
        Remove-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue
        $removed = -not (Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue)
        $log.ruleRemoved = $removed
        Write-Handshake 'restored.json' ([ordered]@{ runId = $request.runId; removed = $removed })
    }
    $log | ConvertTo-Json | Set-Content (Join-Path $ResultDirectory 'offline-controller.json') -Encoding UTF8
}
if ($ruleName -and -not $log.ruleRemoved) { exit 1 }
```

  The installer creates `offline-handshake` before it writes
  `request.json`, and the controller only reads from that folder until
  then. If the installer never asks, the controller exits without touching
  the firewall once `signed-install.json` appears or its timeout passes.

  Parse-check both changed scripts on the host:

```bash
for script in windows_shell_install.ps1 windows_provider_offline_controller.ps1; do
  ssh -o BatchMode=yes pcsx2-win "powershell -NoProfile -Command \"\$e = \$null; [void][System.Management.Automation.Language.Parser]::ParseFile('E:\work\desktop-guides\t04-providers\tools\p1\\$script', [ref]\$null, [ref]\$e); if (\$e) { \$e; exit 1 } else { '$script parses.' }\""
done
```

Expected: `windows_shell_install.ps1 parses.` and
`windows_provider_offline_controller.ps1 parses.`

- [ ] **Step 8: Run the provider scenarios on `pcsx2-win`**

  1. Stage the tree into a fresh run folder and run the harness checks:

```bash
RUN=t04-providers-$(date +%Y%m%d%H%M)
git ls-files -z --cached --others --exclude-standard |
  tar --null -T - -czf - |
  ssh -o BatchMode=yes pcsx2-win "cmd /c mkdir E:\work\desktop-guides\\$RUN && tar -xzf - -C E:\work\desktop-guides\\$RUN"
for test in test_windows_provider_credentials.ps1 test_windows_shell_screenshot_stats.ps1 test_windows_shell_smoke_result.ps1 test_windows_shell_task_cleanup.ps1; do
  ssh -o BatchMode=yes pcsx2-win "powershell -NoProfile -ExecutionPolicy Bypass -File E:\work\desktop-guides\\$RUN\tools\p1\\$test" || break
done
```

Expected: each script prints its `... checks passed.` line.

  2. Check the credential files by label and length only. This command
     prints no value:

```bash
ssh -o BatchMode=yes pcsx2-win "powershell -NoProfile -Command \"foreach (\$f in 'E:\work\igdb_credentials.txt','E:\work\steamgriddb_credentials.txt') { if (-not (Test-Path \$f)) { \\\"\$f missing\\\"; continue }; Get-Content \$f | ForEach-Object { \$p = \$_ -split ':', 2; if (\$p.Count -eq 2) { \\\"\$(\$p[0].Trim().ToLowerInvariant()): \$(\$p[1].Trim().Length) characters\\\" } } }\""
```

Expected: `igdb client id: N characters`, `igdb client secret: N
characters` and `steamgriddb api key: N characters`, each between 1 and
128. If a file is missing, continue; its scenarios are recorded as
`skipped`.

  3. Build the unsigned x64 MSIX as CI does:

```bash
ssh -o BatchMode=yes pcsx2-win "cd /d E:\work\desktop-guides\\$RUN && dotnet restore src\DesktopGuides.Production\DesktopGuides.Production.csproj --locked-mode -p:Platform=x64 && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false && dir /s /b src\DesktopGuides.Production\AppPackages\DesktopGuides.Production_*_x64.msix"
```

Expected: `Build succeeded.` and one MSIX path.

  4. Follow `e2e-testing.md` runner-contract steps 1–3. Confirm that no
     `DesktopGuides.Preview` package or profile exists, or back it up first.
     The installer refuses to run over an existing profile.

  5. Run the provider pass without the firewall rule. Launch it exactly
     as T11.4 did:

```bash
ssh -o BatchMode=yes pcsx2-win "cd /d E:\work\desktop-guides\\$RUN && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\windows_shell_install.ps1 -PackagePath <msix from step 3> -ResultDirectory E:\work\desktop-guides\\$RUN\results-providers -ProviderOnly"
```

Expected: exit code 0. In `signed-install.json`:
- `success` is true.
- `providers.none`, `providers.offlineWithoutCredentials`,
  `providers.settings`, `providers.live` and `providers.remove` each list
  their phases.
- `providers.fixtureFields` starts `IGDB accepted`.
- `providers.cancelDuringSearch` is `observed`, or else `not-observed:`
  with a reason.
- `providers.blockedNetwork` is `not-run: …`.
- Both `leakScan…` entries have an empty `filesWithCredentialValues`.
- `packageStillInstalled` is false.

If `fixtureFields` fails, IGDB's error body names the field that doesn't
exist. Fix the Task 7 fixture and the `IgdbClient` field list together,
then rerun.

  6. **Stop and ask the user before this step (R2).** It adds a Windows
     Firewall rule on the host. Say that the rule blocks outbound traffic
     only for `DesktopGuides.Production.exe` in the test package, that it
     lasts at most 10 minutes, and that the controller removes it and
     records the removal. Only with the user's explicit approval, open a
     second SSH session for the controller, then run the installer with
     the switch:

```bash
ssh -o BatchMode=yes pcsx2-win "powershell -NoProfile -ExecutionPolicy Bypass -File E:\work\desktop-guides\\$RUN\tools\p1\windows_provider_offline_controller.ps1 -ResultDirectory E:\work\desktop-guides\\$RUN\results-offline" &
ssh -o BatchMode=yes pcsx2-win "cd /d E:\work\desktop-guides\\$RUN && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\windows_shell_install.ps1 -PackagePath <msix from step 3> -ResultDirectory E:\work\desktop-guides\\$RUN\results-offline -ProviderOnly -AllowOfflineFirewallRule"
wait
ssh -o BatchMode=yes pcsx2-win "powershell -NoProfile -Command \"Get-NetFirewallRule -Name 'DesktopGuides-P1-ProviderOffline-*' -ErrorAction SilentlyContinue | Measure-Object | Select-Object -ExpandProperty Count\""
```

Expected: both commands exit 0. `providers.blockedNetwork` lists
`snapshot-and-cover-from-local-data`, `failed-refresh-keeps-snapshot` and
`offline-search-reports-unavailable`, and `providers.firewallRuleRemoved`
is true. `offline-controller.json` has `outcome: done` and
`ruleRemoved: true`. The final command prints `0`. If the user declines,
record `blockedNetwork` as not run, with the reason, in the evidence.

  7. After each run, confirm the cleanup: no test package, task or
     process is left, `providers.bin` is gone, and any backed-up profile
     is restored (runbook steps 5 and 7).

- [ ] **Step 9: Record the evidence and update the docs**

  1. Copy the results back, keeping only reviewed, non-secret files:

```bash
mkdir -p docs/p1/evidence/t04-provider-search
scp "pcsx2-win:E:/work/desktop-guides/$RUN/results-providers/signed-install.json" docs/p1/evidence/t04-provider-search/windows-11-x64-result.json
for shot in add-game-no-credentials linked-game-notconfigured provider-settings-saved add-game-search search-results linked-game-live; do
  scp "pcsx2-win:E:/work/desktop-guides/$RUN/results-providers/*.$shot.png" docs/p1/evidence/t04-provider-search/ || true
done
```

  If the blocked-network run happened, also copy its `signed-install.json`
  as `windows-11-x64-offline-result.json`, `offline-controller.json` and
  `*.search-offline.png`. Open each PNG and read each JSON before
  committing. Any credential value in them fails the task. The installer's
  scan should already have caught one, so treat a find here as a harness
  defect as well.

  2. Before committing, run the leak scan once more on the copied files
     from the host, where the credential files are:

```bash
tar -czf - docs/p1/evidence/t04-provider-search | ssh -o BatchMode=yes pcsx2-win "cmd /c mkdir E:\work\desktop-guides\\$RUN\evidence-check && tar -xzf - -C E:\work\desktop-guides\\$RUN\evidence-check"
ssh -o BatchMode=yes pcsx2-win "powershell -NoProfile -Command \". E:\work\desktop-guides\\$RUN\tools\p1\windows_provider_credentials.ps1; \$s = @((Read-LabelledValues E:\work\igdb_credentials.txt @('igdb client id','igdb client secret')).Values) + @((Read-LabelledValues E:\work\steamgriddb_credentials.txt @('steamgriddb api key')).Values); \$found = @(Find-SecretInFiles @('E:\work\desktop-guides\\$RUN\evidence-check') \$s); \\\"\$(\$found.Count) files with credential values\\\"; \$found\""
```

Expected: `0 files with credential values`.

  3. `docs/p1/e2e-testing.md`: add a paragraph after the `-DesignOnly`
     paragraph. It should cover:
     - `-ProviderOnly`, its credential-path parameters, and the rule that
       values never go into arguments or results.
     - The leak scan.
     - `-AllowOfflineFirewallRule` with the elevated controller: the rule
       covers the app executable only, lasts at most 10 minutes, and needs
       the user's authorization each time.
     - A missing credential file is recorded as `skipped`.
     - The full run adds the provider pass after the material passes.

  4. `docs/p1/results.md`: add a T04.4 section in the style of the T11.4
     entry. Cover:
     - Source commit, package SHA-256 and OS build.
     - Each scenario's phases.
     - The result count for "Half-Life", the attribution observed, and
       the refresh status.
     - `cancelDuringSearch`, reported honestly.
     - The fixture-field check result.
     - Whether the blocked-network run happened, and if so, the
       controller's removal record.
     - Both leak scans, and the cleanup state.
     - Link the evidence files.

  5. `docs/p1/implementation-plan.md` and `docs/work-breakdown.md`: mark
     T04.4 done, and link this plan, the design and the evidence. Keep the
     R → S → T → TR traceability rows consistent: the TR entry names the
     installed provider E2E and the Core and Infrastructure test classes
     from Tasks 2–9.

  6. `docs/p1/t04-provider-search-design.md`: set the status line to
     implemented, with the date and the result link.

- [ ] **Step 10: Commit** (after message approval)

```bash
git add tools/p1 docs/p1/e2e-testing.md docs/p1/results.md \
  docs/p1/implementation-plan.md docs/work-breakdown.md \
  docs/p1/t04-provider-search-design.md docs/p1/evidence/t04-provider-search
git commit -m "test(add-game): cover provider search on the installed app"
```

  Then run the local suites one last time:

Run: `dotnet test tests/DesktopGuides.Core.Tests/DesktopGuides.Core.Tests.csproj -c Release && dotnet test tests/DesktopGuides.Infrastructure.Tests/DesktopGuides.Infrastructure.Tests.csproj -c Release`
Expected: PASS, 0 failed.
