# T04.4 provider-backed search-first Add game design

Status: design approved in conversation, 29 September 2026; written spec
awaiting review. Prerequisites T03.2, T03.3, T04.1, T11.1, T11.4, and T15.2
are merged (T11.4 through PR #13, merge commit
`23e0694faae1938993c8f228116324d61fe5b1e1`). This document is the reviewed
external-provider decision that T04.4 requires.

## Intent

A reader adds a game by searching for it instead of typing its details. The
selected result becomes a normal local game with one stable local Game ID.
It also carries provider provenance, a bounded metadata snapshot, and cover
artwork, and all of these stay readable offline. Manual add (T04.1) remains
the fallback whenever search is unavailable.

Success criteria:

- Submitting a query returns at most 20 labelled games and editions. Typing
  alone sends no provider request.
- Picking a result creates exactly one game. If that provider ID is already
  in the library, the app opens the existing game.
- Cancel, timeout, unavailable service, malformed data, a crash, and artwork
  failure leave neither a partial row nor an unreferenced file.
- Refresh never changes the local title, platform, or notes.
- The package contains no provider secret. Credentials never reach SQLite,
  logs, or exception text.

## Provider decision

| Provider | Role | Access | Decision |
| --- | --- | --- | --- |
| IGDB API v4 (Twitch) | Game and edition search, metadata snapshot, fallback cover | The user's own Twitch client ID and secret, entered in Settings. The app obtains a client-credentials token directly. | Choose |
| SteamGridDB API v2 | Preferred 600×900 cover grid | The user's own API key, entered in Settings; optional | Choose |
| App-hosted proxy holding a shared secret | Would avoid per-user keys | Requires hosting, a user account, and abuse protection; the user declined | Reject for P1 |
| Scraping the IGDB website | Would avoid keys | Breaks IGDB's terms and requires evading bot protection | Reject |

Rules that follow from the decision:

- Requests to IGDB go out one at a time, which keeps them under the
  published limit of 4 requests per second and 8 open requests.
- On an HTTP 429, the app retries once if `Retry-After` is 2 seconds or
  less; otherwise it reports the service as busy.
- Linked games show "Metadata from IGDB" and, where the artwork came from
  SteamGridDB, "Artwork from SteamGridDB". The Settings card links to each
  service's terms.
- Terms checked 2026-09-29: IGDB API (https://www.igdb.com/api), Twitch
  Developer Agreement (https://legal.twitch.com/legal/developer-agreement/),
  and SteamGridDB Terms (https://www.steamgriddb.com/terms) pages not
  retrievable via standard HTTP fetch (JavaScript-rendered or access
  restricted); re-check before release to confirm attribution requirements and
  verify no prohibition on offline caching of metadata or artwork.
- The authentication layer is swappable. A future proxy would replace
  `TwitchTokenSource` and `IgdbClient` behind `IGameMetadataProvider`,
  and no other component would change.

## Components

### Core (no network or platform code)

| Unit | Responsibility |
| --- | --- |
| `GameMetadataSnapshot` | App-owned record, `SchemaVersion = 1`, holding: summary (≤ 4,000 characters), first release date, genres, developers, publishers, and platforms (each list ≤ 16 items of ≤ 80 characters), provider page URL, and game type |
| `GameMetadataNormalizer` | Rejects missing or invalid IDs and titles. Strips control characters, truncates text to the limits, removes duplicates from lists, and maps the game type to a tag such as Main game, Remaster, Remake, Port, Edition, Expansion, or Bundle. |
| `ProviderGameLink` | `Provider` (`"igdb"`), `ExternalId` (a canonical positive integer string), and `RetrievedUtc` |
| `Game` (extended) | Gains optional `Link`, `Metadata`, and `ArtworkRelativePath`. Existing manual games leave all three null. |
| `ProviderSearchResult` | External ID, title, release year, platforms, type tag, and an optional thumbnail URL |
| `IGameMetadataProvider` | `SearchAsync(query, token)` returns at most 20 results. `GetAsync(externalId, token)` returns the snapshot plus `ArtworkHints` (Steam app ID and IGDB cover image ID). |
| `IArtworkSource` / `FallbackArtworkSource` | `FindAsync(hints, token)` returns a candidate URL and a source name, or null. The fallback tries its sources in order. |
| `IProviderCredentialStore` | Loads, saves, and clears `IgdbCredentials(ClientId, ClientSecret)` and `SteamGridDbKey` |
| `ProviderException` | A typed `Kind`: `NotConfigured`, `InvalidCredentials`, `Unavailable`, `Timeout`, `RateLimited`, or `MalformedData` |
| `ProviderGameImporter` | Orchestrates add and refresh over the interfaces above, the repository, and the artwork store |

### Infrastructure (`net10.0`)

| Unit | Responsibility |
| --- | --- |
| `TwitchTokenSource` | Posts `client_credentials` to `https://id.twitch.tv/oauth2/token`. Caches the token in memory until 5 minutes before `expires_in`, and never writes it to disk or logs. |
| `IgdbClient` | Sends `POST https://api.igdb.com/v4/games` with explicit Apicalypse fields and `limit 20`. The query is trimmed, limited to 1–100 characters, and escaped for the string literal (backslash, quote). A 401 triggers one token refresh; a second 401 means `InvalidCredentials`. Maps the response through the normalizer. |
| `SteamGridDbArtworkSource` | Calls `/api/v2/grids/steam/{appId}?dimensions=600x900`. If that returns nothing, calls `/search/autocomplete/{title}` and then `/grids/game/{id}?dimensions=600x900`. |
| `IgdbCoverArtworkSource` | Builds `https://images.igdb.com/igdb/image/upload/t_cover_big/{imageId}.jpg` |
| `ProviderHttp` | A shared `HttpClient` policy: HTTPS only, a fixed host allow-list, no redirect to another host, a 15-second timeout per request, a 1 MB limit on JSON bodies and 5 MB on images, and one request at a time |
| `ArtworkValidator` | Parses the signature and dimensions of PNG, JPEG, or WebP in managed code. Rejects any other format and images over 4096 px on either side. |
| `ManagedArtworkStore` | Stages the file under `library/.artwork-staging/`, validates it, and moves it to `artwork/{gameId}/{sha256}.{ext}`. Runs the startup sweep. |
| `ProviderCredentialBlob` | Formats and parses the credential JSON, with size and field limits. A corrupt blob is treated as not configured. |
| Schema v3 | Migrates `Games` in one transaction (below) |

The allow-list holds `id.twitch.tv`, `api.igdb.com`, `images.igdb.com`,
`www.steamgriddb.com`, and `cdn2.steamgriddb.com`.

### Production (WinUI)

| Unit | Responsibility |
| --- | --- |
| `WindowsProviderCredentialStore` | Protects the credential blob with `DataProtectionProvider("LOCAL=user")` and stores it at `LocalState\providers.bin`. Remove deletes the file. |
| "Game data providers" `SettingsCard` | `PasswordBox` fields for the IGDB client ID and secret, and for the SteamGridDB key. Saved values are never shown; the card displays "Saved". Buttons: Test connection, Save, Remove. Links to each provider's terms. |
| `AddGameDialog` | An `AutoSuggestBox` that queries only on submit, a Search button, a virtualized `ListView` of results (thumbnail, title, year, platforms, type tag), a `ProgressRing` with Cancel, an `InfoBar` for status messages, and an "Add manually" link to the existing `GameEditorDialog` |
| Game route | A `MetadataControl` for release year, type, and platforms. Also shows the summary, genres, companies, cover, attribution, and a Refresh metadata command (linked games only). |

## Schema v3

```sql
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
```

The migration runs in a single transaction. If it fails, the library stays
at version 2 with its data unchanged. Metadata and link columns are either
all set or all null; the repository enforces this when it writes.

## Data flow

### Add from search

1. The user submits a query with Enter or Search. `SearchAsync` runs under
   a token linked to Cancel and to closing the dialog.
2. On selection, the app checks the repository for an existing
   `(igdb, id)`. If one exists, the dialog closes and opens that game.
3. `GetAsync(id)` fetches and normalizes the record. `MalformedData`
   stops the add, and nothing is created.
4. The artwork chain runs in order: SteamGridDB (Steam app ID, then name),
   then the IGDB cover, then none. The image is downloaded to staging,
   validated, and moved to `artwork/{gameId}/`.
5. One transaction inserts the game row with its link, `MetadataJson`,
   `MetadataRetrievedUtcMs`, and `ArtworkRelativePath`. If the unique
   index is hit (a race), the moved file is deleted and the existing game
   opens.
6. On cancel or any failure before step 5 commits, the staged or moved file
   is deleted and no row exists.
7. If every artwork source fails, the game is still saved without artwork,
   and a notice says the cover could not be downloaded. The platform is
   taken from IGDB only when the record lists exactly one platform;
   otherwise it is left empty.

### Refresh

1. Refresh re-runs steps 3 and 4 for the linked ID. If the new image's
   SHA-256 matches the current file, the current file is kept.
2. One `UPDATE` changes only `MetadataJson`, `MetadataRetrievedUtcMs`,
   `ArtworkRelativePath`, and `UpdatedUtcMs`.
3. On failure, the row stays as it was and any new file is removed. After
   a successful commit, the previous file is deleted if possible.

### Artwork crash safety

Artwork files are immutable and named by content, and a database row
references a file only after the file is in place. At startup, after the
existing guide reconciliation runs, a sweep deletes everything under
`library/.artwork-staging/` and every file under `artwork/` that no row
references. Artwork staging uses its own directory because
`FileOperationReconciler.CountReviewOrphans` treats all entries under
`library/.staging/` as orphans needing review. Deleting a game leaves its
artwork unreferenced, and the sweep removes it too. The sweep only touches
files that match the canonical artwork path pattern, and it records a count
of anything else instead of deleting it.

### Offline

A linked game shows its snapshot and cached artwork without making any
network request. Search reports `Unavailable` and offers Add manually. If
`MetadataJson` fails to parse, the game still loads and shows no metadata
panel.

## Errors and UI states

The dialog moves through these states: Idle, then Searching (progress
and Cancel), then Results, Empty, or Error. From Results it moves to
Adding (progress and Cancel), and finally opens the game. Search and the
list are disabled while Searching or Adding. Cancel and Add manually always
remain available and can be reached with the keyboard.

| Condition | `InfoBar` message | Action |
| --- | --- | --- |
| `NotConfigured` | Add IGDB credentials in Settings to search. | Open Settings; Add manually |
| `InvalidCredentials` | IGDB rejected the client ID or secret. | Open Settings |
| `Unavailable` | Can't reach IGDB. Check your connection. | Retry; Add manually |
| `Timeout` | IGDB took too long to respond. | Retry |
| `RateLimited` | IGDB is busy. Try again in a moment. | Retry |
| `MalformedData` | IGDB returned data the app couldn't read. | Add manually |
| Cancelled | None; returns to Idle | None |
| No results | No games match "{query}". | Add manually |

When no IGDB credentials are saved, Add game opens the manual editor
directly, with the `NotConfigured` notice.

## Security

- Credentials exist only in the protected blob. The secret field is never
  pre-filled. Test connection requests a token and runs a single `limit 1`
  query for IGDB, and makes one authenticated request for SteamGridDB.
- Provider strings are rendered only as plain text. The only link shown is
  an `https://www.igdb.com/games/` URL.
- No exception message, log entry, or evidence file may contain a client
  secret, API key, or access token. Tests assert this for the token and
  client paths.

## Testing

Tests cover only code the app controls; they do not re-test how WinUI
renders images or materials.

Core, using fakes:

- The importer's add creates exactly one row and one file.
- A duplicate ID opens the existing game.
- A unique-index race removes the file.
- Cancelling during search, fetch, artwork, or before commit leaves no row
  and no file.
- Artwork failure saves the game without artwork.
- Refresh preserves title, platform, and notes.
- Refresh with an identical image keeps the file.
- The normalizer enforces its limits, strips control characters, and maps
  type tags.
- The fallback artwork chain tries its sources in order.

Infrastructure, using `HttpMessageHandler` fakes and a temporary library:

- **`IgdbClient`:** the request body, including escaping and `limit 20`;
  headers; mapping of sanitized recorded fixtures; the 401 refresh path and
  a second 401; 429 with `Retry-After`; timeout; an oversized body; and
  malformed JSON.
- **`TwitchTokenSource`:** caching and the expiry margin; no secret appears
  in exception text.
- **SteamGridDB:** the Steam ID lookup, the name fallback, and an empty
  result.
- **HTTP policy:** rejects an HTTP URL, a host outside the allow-list, and
  a redirect to another host.
- **`ArtworkValidator`:** accepts valid PNG, JPEG, and WebP headers, and
  rejects a bad signature, an oversized file, and oversized dimensions.
- **`ManagedArtworkStore`:** staging under `library/.artwork-staging/`,
  moving into place, and a sweep that keeps referenced files and removes
  staged and unreferenced ones.
- **`ProviderCredentialBlob`:** round-trip, size limits, and a corrupt blob
  treated as not configured.
- **Schema v3:** a v2 library migrates with its games intact; a failing
  migration rolls back to v2; many unlinked games are allowed while a
  duplicate link is rejected; path and pairing checks.

Installed E2E on `pcsx2-win`, following
[e2e-testing.md](e2e-testing.md) and its backup and cleanup rules:

- These scenarios run every time:
  - With no credentials, Add game opens manual add with the notice.
  - Settings saves and removes credentials.
  - A seeded linked game shows its snapshot and artwork while the app runs
    offline and after a disconnected relaunch.
- The live scenarios are search, add, duplicate add, refresh preserving
  local edits, and cancel during search. They also capture the PR
  screenshots.
- The harness reads the user's credential files,
  `E:\work\igdb_credentials.txt` and `E:\work\steamgriddb_credentials.txt`,
  each in `label: value` format. It enters the values through the Settings
  UI and removes the saved credentials during cleanup.
- Values are never written to the repository, evidence, transcripts, or
  logs. If a file is missing, its live scenarios are recorded as skipped,
  not passed.
- Assertions use UI Automation state and persisted state: result count,
  `InfoBar` severity and message, the database row, and files on disk.

## Out of scope

- Editing the snapshot by hand.
- Choosing among several artwork options.
- Automatic background refresh.
- Providers other than IGDB and SteamGridDB.
- Filtering the catalog by metadata.
- Reusable catalog components. T05.4 extracts these.
