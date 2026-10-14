# T17.2 piece D: the fake-provider CI lane design

Status: merged in #104 (merge commit `928407c`), 14 October 2026.
Pieces A, B, C and E merged in #90, #91, #100 and #95; see the
[T17.2 design](t17-2-release-ci-design.md).

## Intent

CI has no IGDB or SteamGridDB credentials, so the installed `provider` group
there runs only `provider-none` and `provider-offline`. Saving credentials,
search and add, artwork, refresh, the credential-leak scans and removal run
only on the host, with the user's real keys.

Piece D runs that whole provider journey in CI against recorded fixtures, so
a provider regression fails CI. Drift in the real APIs isn't covered: a host
run with real keys stays the check for that.

Traces: T17.2 in [implementation-plan.md](implementation-plan.md) and the
[installed E2E procedure](e2e-testing.md) ("a fake-provider CI lane ... [is]
T17.2 work"). The provider design is T04.4: IGDB with the user's Twitch keys
and SteamGridDB artwork with the user's key, with no scraping and no proxy.

## Today

- **One HTTP path:** all provider traffic goes through
  `Infrastructure/Providers/ProviderHttp`. It takes an
  `HttpMessageHandler`, uses hardcoded HTTPS endpoints, and enforces an
  allow-list of five hosts: `id.twitch.tv`, `api.igdb.com`,
  `images.igdb.com`, `www.steamgriddb.com` and `cdn2.steamgriddb.com`.
- **Existing fixtures:** the Infrastructure unit tests have two IGDB
  fixtures, `igdb-search.json` and `igdb-game.json`.
- **The installer without credentials:** with no credential files it skips
  `provider-settings`, `provider-live`, `check-igdb-fields` and
  `provider-remove`, and the leak scans with them.

## Decision

The fake sits **inside the app**, as a canned `HttpMessageHandler` that never
touches the network. It's available **only in Preview-lane builds**:
- **Credentials stay in the process.** Even fake credentials are never
  sent anywhere.
- **The Public build can't turn it on:** the wiring is compiled out by the
  existing `PUBLIC_LANE` constant.
- **The cost:** the real HTTP stack below the handler (TLS, sockets,
  redirects) isn't exercised in CI.

## Parts

### `FixtureProviderHandler` (Infrastructure)

An `HttpMessageHandler` that answers from a fixture folder:

- **Routes:** `manifest.json` lists them. Each route has a `method`, a
  `host`, a `path`, an optional `contains` (a substring of the query or
  the body), a `status`, an optional `retryAfterSeconds`, and a `body` file
  (or none). The first matching route answers.
- **Unmatched requests:** a request no route matches gets status 599.
  The handler appends `{ method, host, path }` to `unmatched.json` in the
  same folder, with no query values and no headers. It never falls through
  to the network.
- **Independent of the app:** it knows nothing about the app.
  `ProviderHttp`'s allow-list still applies, because `ProviderHttp` checks
  each URI before calling the handler.

### The wiring (`ProviderServices`, `#if !PUBLIC_LANE`)

`ProviderServices` uses the fixture handler only when both of these hold:

- the named event `Local\DesktopGuides.Preview.ProviderFixtures` exists,
  checked when `ProviderServices` is built at startup through `TestGate`;
- `<data root>\test\providers\manifest.json` exists. The data root is the
  package's `LocalState`, or the portable data folder.

When both hold, it builds `new ProviderHttp(new FixtureProviderHandler(folder))`.
Otherwise behaviour is unchanged. A Public build has no such code path.

### Fixtures (`tests/fixtures/p1/providers/`)

Authored for this lane, with no captured user data:
- `manifest.json`;
- a Twitch token response;
- IGDB search results that include Half-Life, and its game details;
- an IGDB cover image;
- SteamGridDB search and grids responses, and a grid image;
- `igdb-credentials.txt` and `steamgriddb-credentials.txt`: fake values in
  the installer's labelled credential-file format, clearly marked fake.

## The installer and CI

**A new installer switch, `-FakeProviders`.** It takes effect only when no
real IGDB credential file is found, so a host run with real keys is
unchanged. Before the provider group launches the app, the installer:

1. copies `tests/fixtures/p1/providers/` into `<data root>\test\providers`;
2. creates and holds `Local\DesktopGuides.Preview.ProviderFixtures` for the
   whole group;
3. uses the fixtures' fake credential files as `-IgdbCredentialFile` and
   `-SteamGridDbCredentialFile`.

The group then runs as it does with real keys: `provider-settings`,
`provider-live`, then `provider-remove`. Its existing checks stay
unchanged:
- the stored Half-Life game, with its IGDB link, metadata and local edits;
- artwork files matching the rows, with no staging files;
- the credential-leak scans, which now look for the fake values;
- `providers.bin` removed after removal.

Two parts stay skipped, and are recorded as skipped:
- `check-igdb-fields`, because comparing the fixtures with themselves
  proves nothing;
- the blocked-network scenario, which needs the user's authorization.

After the group, the installer fails it if `unmatched.json` has any entries.
The report records `providers.source = 'fixtures'` or `'live'`. The evidence
checklist adds a "Providers: fixtures" or "Providers: live" line.

**CI:** the install step passes `-FakeProviders` on every shard. Only the
`html` shard runs the `provider` group, so the full provider journey runs
against the fixtures on both lanes. Cleanup removes `test\providers` with the
rest of the data folder.

## Tests

- **Infrastructure unit tests**, written first:
  - **Matching:** routes match on method, host, path and `contains`, and
    the first match wins. A 429 with `retryAfterSeconds` is retried by
    `ProviderHttp`.
  - **Unmatched requests:** they get 599 and a line in `unmatched.json`,
    with no query values or headers.
  - **The allow-list:** a host that isn't allowed is refused by
    `ProviderHttp` before the handler sees it.
  - **The fixtures themselves:** the shipped fixture set drives the real
    `TwitchTokenSource`, `IgdbClient` and SteamGridDB source in-process.
    "Half-Life" is found, and the artwork bytes decode as an image.
- **The Public build:** CI's "Build and verify the public lane" step
  checks that the Public `DesktopGuides.Production.dll` doesn't contain the
  string `ProviderFixtures`.

## Docs

`e2e-testing.md` says CI runs the provider journey against fixtures, and that
real-API drift is checked by a host run with real keys.

## Delivery and verification

One PR:
1. The unit tests pass.
2. A dispatch with `shell-scope=provider` and `shell-lane=both` runs the
   full provider journey against the fixtures, with no unmatched requests.
3. The full PR run passes.

## Risks

- **Cancelling a search:** `provider-live` looks for a cancel during
  search. The fixtures answer instantly, so it may not be seen. It is
  already recorded as observed or not observed rather than failed, so this
  lane accepts "not observed".
- **Fixtures drifting from the real API:** they can drift from what IGDB
  and SteamGridDB return. Only a host run with real keys catches that.

## Rulings

From the [implementation plan](t17-2-fake-provider-plan.md), and from the
first installed runs:

1. **The fake credentials stay out of the copied folder, and the manifest
   holds no secret.** The leak scan searches the app's data folder for the
   credential values. So the credential files live in `credentials/`,
   which the installer doesn't copy. The token route matches only the
   smoke's known wrong secret (`desktop-guides-wrong-secret`, giving 400),
   before a catch-all route giving 200.
2. **Routes have `delayMilliseconds` and `times`.** The "Final Fantasy"
   search is held for 3 s so `provider-live` can press Cancel; `times` lets
   a route answer only N times, for a 429-then-200 retry.
3. **One generated PNG serves every image URL**, because the artwork
   validator accepts a file by its header.
4. **`provider-live` waits for Cancel to appear.** It pressed Search and
   looked for Cancel once, at once. Not seeing it, it assumed the search had
   finished, then typed into a box the still-running search kept disabled.
   Fast real searches hid this.
5. **`provider-live` resizes to 1500 × 720 first,** as other layout-dependent
   modes do. In the runner's 768 × 519 window the Add game dialog's fixed
   360 px results row puts "Add manually" out of view. `provider-live` had
   never run in CI before.
6. **`Expand-ProviderSettings` scrolls until the Test button row shows.**
   A fresh launch's Settings put the expanded provider card at the bottom
   edge, with its fields below the fold. Two earlier attempts were reverted:
   scrolling to the IGDB field hid the Test button, and resizing wasn't the
   cause. A screenshot capture showed the real layout. The capture stays in
   the smoke.
7. **Every provider scenario starts at 1500 × 720.** Whether
   `provider-settings` passed depended on the window size a launch got; in
   the runner's 768 × 519 window the Test button fell below the fold.

## T17.2 piece D verification record

- **Infrastructure tests:** the handler tests (6) and the fixture-set
  tests (3) failed first, then passed on the Windows host. The suite is at
  784/784.
- **Preview-only check:** dispatch
  [38403593637](https://github.com/ilya-slalom/desktop-guides/actions/runs/38403593637)
  failed it before the wiring ("The Preview package lacks the
  fixture-provider wiring"). After the wiring,
  [38404425541](https://github.com/ilya-slalom/desktop-guides/actions/runs/38404425541)
  reported "Fixture providers: in the Preview package, absent from the
  Public package."
- **The provider journey against fixtures:** dispatch
  [38413022128](https://github.com/ilya-slalom/desktop-guides/actions/runs/38413022128)
  passed on both lanes.
  - All five stages passed: `provider-none`, `provider-offline-none`,
    `provider-settings`, `provider-live` and `provider-remove`.
  - The search cancel was observed, with no unmatched requests.
  - Both leak scans were clean, and `providers.bin` was gone after removal.
  - Earlier dispatches, from 38405079953 to 38411889341, found rulings 4–6.
- **PR run:** [38419706291](https://github.com/ilya-slalom/desktop-guides/actions/runs/38419706291)
  passed on the first attempt, with all 10 installed shards; both `html`
  shards show "Providers: fixtures" with all five provider stages. The run
  before it, 38416197769, failed portable `provider-settings` in a small
  window, which led to ruling 7 (every provider scenario starts at
  1500 × 720). A review fix makes a fixture key file that doesn't parse
  fail the run instead of skipping the provider steps.

## Out of scope

- The blocked-network scenario in CI.
- Recording live responses automatically.
