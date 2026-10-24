# S22 PR 4b (Steam game picker) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** In Find online's Steam search, a game without a Steam app ID is matched by name against Steam's game list, rather than by asking for a store URL.
- If exactly one game matches the title exactly, it's used without asking.
- Otherwise a picker lists the matches, and the reader chooses one.
- A "Steam game: {name} · Change" line lets the reader correct any match, including one IGDB gave.

**Spec:** the user's live-check feedback and choice on 24 October 2026 ("Official app list"), recorded here. It extends [the P3 design](../p3-technical-design.md) 5.1 ("A game without one asks once for its Steam store or guides URL"), which Task 3 amends.

**Architecture:**
- **Infrastructure:**
  - `SteamClient.GetAppListAsync` pages `IStoreService/GetAppList` with the key, for games only.
  - `SteamAppIndex` keeps the list in the cache folder and matches names.
- **Production:** the picker and the Change line, in `ShellWindow.Steam.cs`.

## Global Constraints

- **The call:**
  - `IStoreService/GetAppList/v1/?key=…&include_games=true&include_dlc=false&include_software=false&include_videos=false&include_hardware=false&max_results=50000&last_appid=…`;
  - it follows `have_more_results` and `last_appid`, one request at a time and at least 1 s apart;
  - each response may be up to 8 MB, and the whole fetch stops after 10 pages.
- **The cache:**
  - The list is cached in `{cache}/steam-apps.json` as `{ fetchedUtc, apps: [[appId, name], …] }`.
  - It's fetched again when it's older than 30 days, or on **Refresh**.
  - An unreadable file counts as no cache.
- **Matching:**
  - Names are compared lower-cased, with ™, ® and © removed, other punctuation turned to spaces, and spaces collapsed.
  - Rank: an exact match first, then names that start with the query, then names containing every query word. Within a rank, shorter names come first, then lower app IDs.
  - At most 20 results.
- **Picking:**
  - "Exactly one exact match" picks without asking.
  - The URL box stays as a fallback.
  - A chosen app replaces the game's stored app ID.
- **Without a key:** the no-key bar shows, as for guide search; the list needs the key.

## Review Focus

1. A list fetch that fails part way (a 429, a timeout) leaves no cache file and says so; the next search tries again.
2. A title with ™ or punctuation ("FINAL FANTASY VII REBIRTH™") still finds its exact match.
3. Changing the game while guide results show runs the search again for the new app.
4. A cache older than 30 days is fetched again; a fresh one isn't.

### Task 1: Infrastructure: the list and its matching

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Online/SteamAppIndex.cs`.
- Modify: `SteamClient.cs`.
- Fixtures: `tests/fixtures/p3/steam/applist-1.json` (with `have_more_results` true) and `applist-2.json`.
- Test: `SteamAppIndexTests.cs`, `SteamClientTests.cs`.

**Interfaces:**
- `SteamClient.GetAppListAsync(IProgress<int>? pages, CancellationToken) → IReadOnlyList<(string AppId, string Name)>`.
- `SteamAppIndex(string cacheFile, TimeProvider clock)`:
  - `LoadAsync(Func<CancellationToken, Task<IReadOnlyList<(string, string)>>> fetch, bool refresh, CancellationToken)`;
  - `Match(string query, int max = 20) → IReadOnlyList<SteamApp>`;
  - `ExactSingle(string title) → SteamApp?`;
  - `NameOf(string appId) → string?`.
  - `record SteamApp(string AppId, string Name)`.

- [ ] **Step 1: Write the failing tests:**
  - **Client:**
    - the client pages through both fixtures with `last_appid`, sending the key and the game-only flags;
    - a 429 on page 2 throws `RateLimited`.
  - **Index:**
    - **Ranking:** for "online test game", the order is exact, then prefix, then all-words, with the length tie-break.
    - **Normalizing:** "FINAL FANTASY VII REBIRTH™" matches "Final Fantasy VII Rebirth" exactly.
    - **`ExactSingle`:** it picks only when there's one exact match.
    - **Caching:**
      - a fetch saves the cache;
      - a cache 29 days old is used without fetching, and one 31 days old is fetched again;
      - a failed fetch leaves no file;
      - an unreadable file is fetched again.
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run Infrastructure tests; they pass.**
- [ ] **Step 5: Commit** `feat(p3): Steam's game list, cached and matched by name`.

### Task 2: Production: the picker

**Files:**
- Modify: `ShellWindow.xaml`, where `SteamAppInfo` becomes the picker:
  - its search box `SteamAppQuery` with **Find**;
  - its list `SteamAppResults`;
  - **Refresh**;
  - the URL fallback.
- Modify also:
  - `ShellWindow.Steam.cs`;
  - the smoke and install scripts;
  - the fixture manifest.

- [ ] **Step 1: Write the failing installed checks.** In `online-steam-search`, after the key:
  1. With no app ID, the list loads, and the picker shows "Online Test Game Remastered" and "Online Test Game II", with no exact match.
  2. Choosing the first searches guides, which gives "2 Steam guides.".
  3. The line reads "Steam game: Online Test Game Remastered".
  4. **Change** shows the picker again; choosing the second searches again.
- [ ] **Step 2: Run the `online` group in CI; it fails.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the full CI; it passes.** Take a screenshot of the picker.
- [ ] **Step 5: Commit** `feat(p3): pick the game's Steam app by name`.

### Task 3: Records

- [ ] Amend design 5.1.
- [ ] Update the `online` row in `docs/p1/e2e-testing.md`.
- [ ] Write the PR description, with the picker's screenshot and the live-check steps.
