# T17.2 Piece D (Fake-Provider CI Lane) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** CI runs the whole installed provider journey (settings, search
and add, artwork, refresh, leak scans, removal) against recorded fixtures,
in Preview-lane builds only.

**Architecture:**
- **Fixture handler:** `FixtureProviderHandler` (Infrastructure) is an
  `HttpMessageHandler` that answers from a fixture folder and never opens
  a socket.
- **App wiring:** `ProviderServices` uses it, inside `#if !PUBLIC_LANE`,
  when the `ProviderFixtures` test gate is open and
  `<data root>\test\providers\manifest.json` exists.
- **Installer:** its `-FakeProviders` switch stages the fixtures and the
  gate when no real credential file exists, and fails the group on any
  unmatched request.

**Tech Stack:** C# / .NET 10 (`System.Net.Http`, `System.Text.Json`),
xUnit, PowerShell 5.1 and 7, GitHub Actions.

**Spec:** `docs/p1/t17-2-fake-provider-design.md`

**Target:** T17.2, piece D, in one PR on `feat/p1-t17-2-fake-providers`
(spec commit `8d5f952`). The PR also carries the pending merge-record docs
(Task 5).
**Prerequisites:** T17.2 pieces A, B, C and E (#90, #91, #100, #95), plus
#101 and #103, all merged.

## Global Constraints

- **Preview builds only:** the wiring compiles only in Preview builds
  (`#if !PUBLIC_LANE`). A Public build must not contain the string
  `ProviderFixtures`.
- **No network:** the handler never opens a socket. An unmatched request
  gets status 599 and a `{ method, host, path }` line in `unmatched.json`,
  with no query values and no headers.
- **The allow-list stays unchanged:** `ProviderHttp`'s allow-list still
  applies.
- **Gate and folder:** the gate is
  `Local\DesktopGuides.Preview.ProviderFixtures`, through `TestGate` and
  `AppLane.Current.LocalEvent("ProviderFixtures")`. The folder is
  `<data root>\test\providers`.
- **No secrets in the copied fixtures.** The fake credential values must
  not appear in any file copied into the app's data folder, because the
  installer's leak scan searches it. The fake credential files live in
  `tests/fixtures/p1/providers/credentials/`, which isn't copied.
- **Host runs unchanged:** `-FakeProviders` changes nothing when a real
  IGDB credential file exists.
- **Recorded skips:** `check-igdb-fields` and the blocked-network scenario
  stay skipped in fixture runs, and the skips are recorded.
- **Shell rules:** PowerShell stays ASCII-only and runs under 5.1 and 7.

## Rulings against the spec

1. **The fake credentials sit in a subfolder that isn't copied, and the
   manifest holds no secret.** The leak scan searches the app's data
   folder for the credential values. So the token route matches only the
   smoke's known wrong secret (`desktop-guides-wrong-secret`, giving 400),
   before a catch-all route giving 200. The real fake secret appears
   nowhere in the copied files.
2. **Routes gain `delayMilliseconds` and `times`.**
   - `delayMilliseconds` holds the "Final Fantasy" search open for 3 s,
     so `provider-live` can press Cancel and observe it.
   - `times` lets a route answer only N times, so a 429-then-200 retry can
     be tested.
3. **The PNG images are generated and committed.** The artwork validator
   accepts a file by its header, so one small valid PNG serves every image
   URL. A short Python snippet in Task 2 makes it.

## Review Focus

1. **A fixture file that names a path outside the fixture folder,** for
   example `"body": "..\\..\\x"`. The handler must refuse to read it.
   Pinned by Task 1's path-escape case.
2. **A request the fixtures don't cover, in an installed run.** The group
   must fail, naming the request, not pass on a 599 the app showed as
   "provider unavailable". Pinned by Task 4's unmatched check.
3. **The fake secret leaking into a scanned folder.** The leak scans must
   still pass. Pinned by ruling 1, and by Task 2's test that no copied
   fixture contains the fake credential values.
4. **A Public build containing the fake.** Pinned by Task 3's check of
   the Public MSIX's `DesktopGuides.Production.dll`, together with a
   positive control on the Preview MSIX, so the check can't pass
   vacuously.
5. **A host run with real keys.** It must be unaffected by
   `-FakeProviders`. Pinned by Task 4's condition (a real IGDB credential
   file wins) and its recorded `providers.source = 'live'`.

## Host commands

`pcsx2-win` runs the .NET and script tests:

```bash
s(){ ssh -o BatchMode=yes -o ConnectTimeout=15 -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t17-2d) { Remove-Item -Recurse -Force E:\work\desktop-guides\t17-2d }; New-Item -ItemType Directory E:\work\desktop-guides\t17-2d | Out-Null"'
  COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --exclude=.claude --exclude=.git --exclude=.superpowers --exclude='*/bin' --exclude='*/obj' --exclude=artifacts -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t17-2d'
}
```

- **Infrastructure tests:** `stage && s 'cd /d E:\work\desktop-guides\t17-2d && dotnet test tests\DesktopGuides.Infrastructure.Tests --filter "<filter>" -nologo'`
- **Production build:** `s 'cd /d E:\work\desktop-guides\t17-2d && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64 -nologo'`
- **Script tests:** `s 'cd /d E:\work\desktop-guides\t17-2d && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\<test>.ps1'`
- **When the host is unreachable:** use a `dev-fast` dispatch, whose
  `core-tests` runs the same tests.
- **ASCII check:** `grep -nP '[^\x00-\x7F]' <file>`.

---

### Task 1: `FixtureProviderHandler`

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Providers/FixtureProviderHandler.cs`
- Create: `tests/DesktopGuides.Infrastructure.Tests/Providers/FixtureProviderHandlerTests.cs`

**Interfaces:**
- Consumes: `ProviderHttp(HttpMessageHandler? handler)`,
  `ProviderHttp.SendAsync(HttpMethod, Uri, Action<HttpRequestMessage>, int maxBytes, CancellationToken)`
  returning `ProviderResponse(HttpStatusCode StatusCode, byte[] Body)`.
- Produces:
  - `public sealed class FixtureProviderHandler(string folder) : HttpMessageHandler`;
  - `FixtureProviderHandler.ManifestName = "manifest.json"` and
    `UnmatchedName = "unmatched.json"`;
  - the manifest schema: `{ "routes": [ { "method", "host", "path",
    "contains"?, "status", "body"?, "retryAfterSeconds"?,
    "delayMilliseconds"?, "times"? } ] }`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using DesktopGuides.Infrastructure.Providers;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Providers;

// T17.2 D: the fake-provider lane's handler, driven through the real ProviderHttp.
public sealed class FixtureProviderHandlerTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "dg-fixtures-" + Guid.NewGuid().ToString("N"));

    public FixtureProviderHandlerTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private ProviderHttp Http(object manifest, params (string Name, string Text)[] files)
    {
        File.WriteAllText(Path.Combine(folder, FixtureProviderHandler.ManifestName), JsonSerializer.Serialize(manifest));
        foreach ((string name, string text) in files) File.WriteAllText(Path.Combine(folder, name), text);
        return new ProviderHttp(new FixtureProviderHandler(folder));
    }

    private static Task<ProviderResponse> Post(ProviderHttp http, string uri, string body) =>
        http.SendAsync(HttpMethod.Post, new Uri(uri),
            request => request.Content = new StringContent(body, Encoding.UTF8, "text/plain"),
            ProviderHttp.MaxJsonBytes, CancellationToken.None);

    [Fact]
    public async Task RoutesMatchOnMethodHostPathAndContainsAndTheFirstMatchWins()
    {
        using ProviderHttp http = Http(new
        {
            routes = new object[]
            {
                new { method = "POST", host = "api.igdb.com", path = "/v4/games", contains = "search \"Half-Life\"", status = 200, body = "search.json" },
                new { method = "POST", host = "api.igdb.com", path = "/v4/games", contains = "where id = ", status = 200, body = "game.json" },
                new { method = "POST", host = "api.igdb.com", path = "/v4/games", status = 200, body = "fallback.json" },
                new { method = "POST", host = "api.igdb.com", path = "/v4/games", status = 500 },
            },
        }, ("search.json", "[1]"), ("game.json", "[2]"), ("fallback.json", "[3]"));

        Assert.Equal("[1]", Encoding.UTF8.GetString((await Post(http, "https://api.igdb.com/v4/games", "search \"Half-Life\"; fields name;")).Body));
        Assert.Equal("[2]", Encoding.UTF8.GetString((await Post(http, "https://api.igdb.com/v4/games", "fields name; where id = 231;")).Body));
        ProviderResponse other = await Post(http, "https://api.igdb.com/v4/games", "fields id; limit 1;");
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal("[3]", Encoding.UTF8.GetString(other.Body));
    }

    [Fact]
    public async Task AnUnmatchedRequestGets599AndIsRecordedWithoutQueryOrHeaders()
    {
        using ProviderHttp http = Http(new { routes = Array.Empty<object>() });

        ProviderResponse response = await http.SendAsync(HttpMethod.Get,
            new Uri("https://www.steamgriddb.com/api/v2/grids/game/9?secret=abc123"),
            request => request.Headers.Add("Authorization", "Bearer key-456"), ProviderHttp.MaxJsonBytes, CancellationToken.None);

        Assert.Equal((HttpStatusCode)599, response.StatusCode);
        string recorded = File.ReadAllText(Path.Combine(folder, FixtureProviderHandler.UnmatchedName));
        Assert.Contains("/api/v2/grids/game/9", recorded);
        Assert.Contains("www.steamgriddb.com", recorded);
        Assert.DoesNotContain("abc123", recorded);
        Assert.DoesNotContain("key-456", recorded);
    }

    [Fact]
    public async Task ARouteWithTimesAnswersOnlyThatOftenSoARetryReachesTheNext()
    {
        using ProviderHttp http = Http(new
        {
            routes = new object[]
            {
                new { method = "GET", host = "www.steamgriddb.com", path = "/api/v2/grids/steam/70", status = 429, retryAfterSeconds = 0, times = 1 },
                new { method = "GET", host = "www.steamgriddb.com", path = "/api/v2/grids/steam/70", status = 200, body = "grids.json" },
            },
        }, ("grids.json", "{\"success\":true,\"data\":[]}"));

        ProviderResponse response = await http.SendAsync(HttpMethod.Get,
            new Uri("https://www.steamgriddb.com/api/v2/grids/steam/70?dimensions=600x900"), _ => { },
            ProviderHttp.MaxJsonBytes, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"success\":true,\"data\":[]}", Encoding.UTF8.GetString(response.Body));
    }

    [Fact]
    public async Task AHostOutsideTheAllowListIsRefusedBeforeTheHandler()
    {
        using ProviderHttp http = Http(new
        {
            routes = new object[] { new { method = "GET", host = "example.com", path = "/", status = 200 } },
        });

        await Assert.ThrowsAnyAsync<Exception>(() => http.SendAsync(HttpMethod.Get, new Uri("https://example.com/"),
            _ => { }, ProviderHttp.MaxJsonBytes, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(folder, FixtureProviderHandler.UnmatchedName)));
    }

    // Review Focus 1: a body file outside the fixture folder is never read.
    [Fact]
    public void ABodyPathOutsideTheFolderIsRefused()
    {
        File.WriteAllText(Path.Combine(folder, FixtureProviderHandler.ManifestName), JsonSerializer.Serialize(new
        {
            routes = new object[] { new { method = "GET", host = "api.igdb.com", path = "/x", status = 200, body = "..\\outside.json" } },
        }));

        Assert.Throws<InvalidDataException>(() => new FixtureProviderHandler(folder));
    }

    [Fact]
    public async Task ADelayedRouteHonoursCancellation()
    {
        using ProviderHttp http = Http(new
        {
            routes = new object[] { new { method = "POST", host = "api.igdb.com", path = "/v4/games", status = 200, body = "a.json", delayMilliseconds = 30000 } },
        }, ("a.json", "[]"));
        using CancellationTokenSource cancel = new(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => http.SendAsync(HttpMethod.Post,
            new Uri("https://api.igdb.com/v4/games"), request => request.Content = new StringContent("x"),
            ProviderHttp.MaxJsonBytes, cancel.Token));
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: **Infrastructure tests** with `FullyQualifiedName~FixtureProviderHandlerTests`.
Expected: a compile failure, because `FixtureProviderHandler` doesn't exist.

- [ ] **Step 3: Write the handler**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DesktopGuides.Infrastructure.Providers;

// T17.2 D: answers provider requests from a recorded fixture folder for the
// fake-provider CI lane. It never opens a socket: an unmatched request gets
// 599 and a { method, host, path } line in unmatched.json, never its query
// or headers, which can hold keys.
public sealed class FixtureProviderHandler : HttpMessageHandler
{
    public const string ManifestName = "manifest.json";
    public const string UnmatchedName = "unmatched.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private readonly string folder;
    private readonly IReadOnlyList<FixtureRoute> routes;
    private readonly int[] used;
    private readonly object gate = new();

    public FixtureProviderHandler(string folder)
    {
        this.folder = Path.GetFullPath(folder);
        string manifest = File.ReadAllText(Path.Combine(this.folder, ManifestName));
        routes = JsonSerializer.Deserialize<FixtureManifest>(manifest, Options)?.Routes ?? [];
        foreach (FixtureRoute route in routes)
        {
            if (route.Body is string body && Path.GetFileName(body) != body)
            {
                throw new InvalidDataException($"Fixture body '{body}' must be a file name in the fixture folder.");
            }
        }
        used = new int[routes.Count];
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uri uri = request.RequestUri ?? throw new InvalidOperationException("A fixture request needs a URI.");
        string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        string searched = Uri.UnescapeDataString(uri.Query) + "\n" + body;
        int index = -1;
        lock (gate)
        {
            for (int i = 0; i < routes.Count; i++)
            {
                FixtureRoute route = routes[i];
                if (route.Times is int times && used[i] >= times) continue;
                if (!string.Equals(route.Method, request.Method.Method, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(route.Host, uri.Host, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(route.Path, uri.AbsolutePath, StringComparison.Ordinal) ||
                    (route.Contains is string contains && !searched.Contains(contains, StringComparison.Ordinal)))
                {
                    continue;
                }
                used[i]++;
                index = i;
                break;
            }
            if (index < 0) RecordUnmatched(request.Method.Method, uri);
        }
        if (index < 0) return new HttpResponseMessage((HttpStatusCode)599) { RequestMessage = request };

        FixtureRoute match = routes[index];
        if (match.DelayMilliseconds > 0) await Task.Delay(match.DelayMilliseconds, cancellationToken);
        HttpResponseMessage response = new((HttpStatusCode)match.Status) { RequestMessage = request };
        response.Content = match.Body is string file
            ? new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(folder, file), cancellationToken))
            : new ByteArrayContent([]);
        if (match.RetryAfterSeconds is int seconds)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        }
        return response;
    }

    // Called under the gate.
    private void RecordUnmatched(string method, Uri uri)
    {
        string path = Path.Combine(folder, UnmatchedName);
        List<UnmatchedRequest> entries = File.Exists(path)
            ? JsonSerializer.Deserialize<List<UnmatchedRequest>>(File.ReadAllText(path), Options) ?? []
            : [];
        entries.Add(new UnmatchedRequest(method, uri.Host, uri.AbsolutePath));
        File.WriteAllText(path, JsonSerializer.Serialize(entries, Options));
    }

    private sealed record FixtureManifest(IReadOnlyList<FixtureRoute> Routes);

    private sealed record FixtureRoute(
        string Method, string Host, string Path, string? Contains, int Status, string? Body,
        int? RetryAfterSeconds, int DelayMilliseconds, int? Times);

    private sealed record UnmatchedRequest(string Method, string Host, string Path);
}
```

- [ ] **Step 4: Run them to verify they pass**

Run: **Infrastructure tests** with `FullyQualifiedName~FixtureProviderHandlerTests`,
then the whole suite (`FullyQualifiedName~DesktopGuides`).
Expected: all 6 new tests pass, and the suite has no failures.

If `ProviderHttp` treats a 599 or an empty body differently from what a test
expects, read `ProviderHttp.SendFollowingRedirectsAsync`. Fix the handler,
not the test, and ledger a ruling.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Infrastructure/Providers/FixtureProviderHandler.cs tests/DesktopGuides.Infrastructure.Tests/Providers/FixtureProviderHandlerTests.cs
git commit -m "feat(infra): a fixture HTTP handler for the fake-provider lane"
```

### Task 2: The fixture set

**Files:**
- Create in `tests/fixtures/p1/providers/`:
  - `manifest.json`, `token.json`, `token-rejected.json`, `igdb-test.json`;
  - `igdb-search-half-life.json`, `igdb-search-final-fantasy.json`,
    `igdb-empty.json`, `igdb-game-half-life.json`;
  - `steamgriddb-grids.json`, `image.png`;
  - `credentials/igdb-credentials.txt`,
    `credentials/steamgriddb-credentials.txt`.
- Modify: `tests/DesktopGuides.Infrastructure.Tests/DesktopGuides.Infrastructure.Tests.csproj`
  (copy the fixture set to the output)
- Create: `tests/DesktopGuides.Infrastructure.Tests/Providers/ProviderFixtureSetTests.cs`

**Interfaces:**
- Consumes: `FixtureProviderHandler` (Task 1); `TwitchTokenSource(ProviderHttp)`;
  `IgdbClient(ProviderHttp, TwitchTokenSource, Func<CancellationToken, Task<IgdbCredentials?>>)`
  with `TestConnectionAsync`, `SearchAsync`, `GetAsync`;
  `SteamGridDbArtworkSource(ProviderHttp, Func<CancellationToken, Task<string?>>)`
  with `TestConnectionAsync`, `FindAsync(ArtworkHints)`; `IgdbCoverArtworkSource`;
  `ProviderHttp.GetImageAsync(Uri)`; `ArtworkValidator.Validate` (internal).
- Produces:
  - the fixture folder Task 4 copies, everything except `credentials/`;
  - the fake credentials:
    - `igdb client id: desktop-guides-fixture-client`
    - `igdb client secret: desktop-guides-fixture-secret`
    - `steamgriddb api key: desktop-guides-fixture-steamgriddb-key`

- [ ] **Step 1: Write the failing tests**

```csharp
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Artwork;
using DesktopGuides.Infrastructure.Providers;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Providers;

// T17.2 D: the fixture set CI's fake-provider lane installs, checked against
// the real provider clients so a broken fixture fails here, not in an
// installed run.
public sealed class ProviderFixtureSetTests : IDisposable
{
    private static readonly string Shipped = Path.Combine(AppContext.BaseDirectory, "ProviderFixtures");
    private static readonly IgdbCredentials Fake = new("desktop-guides-fixture-client", "desktop-guides-fixture-secret");
    private const string FakeSteamGridDbKey = "desktop-guides-fixture-steamgriddb-key";
    private readonly string scratch = Path.Combine(Path.GetTempPath(), "dg-provider-set-" + Guid.NewGuid().ToString("N"));

    // The installer copies every top-level file but credentials/.
    public ProviderFixtureSetTests()
    {
        Directory.CreateDirectory(scratch);
        foreach (string file in Directory.GetFiles(Shipped))
        {
            File.Copy(file, Path.Combine(scratch, Path.GetFileName(file)));
        }
    }

    public void Dispose() => Directory.Delete(scratch, recursive: true);

    [Fact]
    public async Task TheFixturesDriveTheRealProviderClients()
    {
        using ProviderHttp http = new(new FixtureProviderHandler(scratch));
        using TwitchTokenSource tokens = new(http);
        IgdbClient igdb = new(http, tokens, _ => Task.FromResult<IgdbCredentials?>(Fake));

        await igdb.TestConnectionAsync(CancellationToken.None);
        IReadOnlyList<ProviderSearchResult> results = await igdb.SearchAsync("Half-Life", CancellationToken.None);
        ProviderSearchResult halfLife = Assert.Single(results, r => r.Title == "Half-Life" && r.ReleaseYear == 1998);
        Assert.Empty(await igdb.SearchAsync("zzqx no such game 91377", CancellationToken.None));
        Assert.NotEmpty(await igdb.SearchAsync("Final Fantasy", CancellationToken.None));

        ProviderGameRecord record = await igdb.GetAsync(halfLife.ExternalId, CancellationToken.None);
        Assert.Equal("Half-Life", record.Title);
        Assert.Equal("70", record.Hints.SteamAppId);

        SteamGridDbArtworkSource grids = new(http, _ => Task.FromResult<string?>(FakeSteamGridDbKey));
        await grids.TestConnectionAsync(CancellationToken.None);
        ArtworkCandidate grid = Assert.IsType<ArtworkCandidate>(await grids.FindAsync(record.Hints, CancellationToken.None));
        ArtworkCandidate cover = Assert.IsType<ArtworkCandidate>(
            await new IgdbCoverArtworkSource().FindAsync(record.Hints, CancellationToken.None));
        foreach (Uri image in new[] { grid.Url, cover.Url, new Uri(halfLife.ThumbnailUrl!) })
        {
            ArtworkValidator.Validate(await http.GetImageAsync(image, CancellationToken.None));
        }

        string unmatched = Path.Combine(scratch, FixtureProviderHandler.UnmatchedName);
        Assert.False(File.Exists(unmatched), File.Exists(unmatched) ? File.ReadAllText(unmatched) : "");
    }

    [Fact]
    public async Task TheSmokesWrongSecretIsRejected()
    {
        using ProviderHttp http = new(new FixtureProviderHandler(scratch));
        using TwitchTokenSource tokens = new(http);

        ProviderException rejected = await Assert.ThrowsAsync<ProviderException>(() => tokens.GetTokenAsync(
            Fake with { ClientSecret = "desktop-guides-wrong-secret" }, forceRefresh: true, CancellationToken.None));
        Assert.Equal(ProviderErrorKind.InvalidCredentials, rejected.Kind);
    }

    // Review Focus 3: the installer's leak scan searches the app's data folder,
    // where these files are copied, for the fake credential values.
    [Fact]
    public void NoCopiedFixtureHoldsAFakeCredentialValue()
    {
        string[] values = [Fake.ClientId, Fake.ClientSecret, FakeSteamGridDbKey];
        foreach (string file in Directory.GetFiles(scratch))
        {
            string text = File.ReadAllText(file);
            foreach (string value in values) Assert.DoesNotContain(value, text);
        }
        Assert.True(File.Exists(Path.Combine(Shipped, "credentials", "igdb-credentials.txt")));
    }
}
```

In the `.csproj`, next to the existing `Providers\Fixtures` line:

```xml
    <None Include="..\fixtures\p1\providers\**\*" LinkBase="ProviderFixtures" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 2: Run them to verify they fail**

Run: **Infrastructure tests** with `FullyQualifiedName~ProviderFixtureSetTests`.
Expected: FAIL, because `ProviderFixtures` doesn't exist in the output.

If `ProviderException` exposes its kind under another name than `Kind`, use
that name and ledger it.

- [ ] **Step 3: Write the fixtures**

`tests/fixtures/p1/providers/manifest.json`:

```json
{
  "routes": [
    { "method": "POST", "host": "id.twitch.tv", "path": "/oauth2/token", "contains": "client_secret=desktop-guides-wrong-secret", "status": 400, "body": "token-rejected.json" },
    { "method": "POST", "host": "id.twitch.tv", "path": "/oauth2/token", "status": 200, "body": "token.json" },
    { "method": "POST", "host": "api.igdb.com", "path": "/v4/games", "contains": "fields id; limit 1;", "status": 200, "body": "igdb-test.json" },
    { "method": "POST", "host": "api.igdb.com", "path": "/v4/games", "contains": "search \"Half-Life\"", "status": 200, "body": "igdb-search-half-life.json" },
    { "method": "POST", "host": "api.igdb.com", "path": "/v4/games", "contains": "search \"zzqx no such game 91377\"", "status": 200, "body": "igdb-empty.json" },
    { "method": "POST", "host": "api.igdb.com", "path": "/v4/games", "contains": "search \"Final Fantasy\"", "status": 200, "body": "igdb-search-final-fantasy.json", "delayMilliseconds": 3000 },
    { "method": "POST", "host": "api.igdb.com", "path": "/v4/games", "contains": "where id = 231;", "status": 200, "body": "igdb-game-half-life.json" },
    { "method": "GET", "host": "images.igdb.com", "path": "/igdb/image/upload/t_thumb/co1abc.jpg", "status": 200, "body": "image.png" },
    { "method": "GET", "host": "images.igdb.com", "path": "/igdb/image/upload/t_cover_big/co1abc.jpg", "status": 200, "body": "image.png" },
    { "method": "GET", "host": "www.steamgriddb.com", "path": "/api/v2/grids/steam/70", "status": 200, "body": "steamgriddb-grids.json" },
    { "method": "GET", "host": "cdn2.steamgriddb.com", "path": "/grid/desktop-guides-fixture.png", "status": 200, "body": "image.png" }
  ]
}
```

`token.json`: `{ "access_token": "desktop-guides-fixture-access-token", "expires_in": 5000000, "token_type": "bearer" }`

`token-rejected.json`: `{ "status": 400, "message": "invalid client secret" }`

`igdb-test.json`: `[ { "id": 231 } ]`

`igdb-empty.json`: `[]`

`igdb-search-half-life.json`:

```json
[
  { "id": 231, "name": "Half-Life", "first_release_date": 911433600,
    "game_type": { "id": 0, "type": "Main Game" },
    "platforms": [ { "id": 6, "name": "PC (Microsoft Windows)" } ],
    "cover": { "id": 1, "image_id": "co1abc" } },
  { "id": 7351, "name": "Half-Life: Source", "first_release_date": 1086048000,
    "game_type": { "id": 9, "type": "Remaster" }, "version_parent": 231,
    "platforms": [ { "id": 6, "name": "PC (Microsoft Windows)" } ] }
]
```

`igdb-search-final-fantasy.json`:

```json
[
  { "id": 385, "name": "Final Fantasy", "first_release_date": 566179200,
    "game_type": { "id": 0, "type": "Main Game" },
    "platforms": [ { "id": 99, "name": "Family Computer" } ] }
]
```

`igdb-game-half-life.json`:

```json
[
  { "id": 231, "name": "Half-Life", "summary": "A scientist fights his way out of a research facility. (Fixture text for CI.)",
    "url": "https://www.igdb.com/games/half-life", "first_release_date": 911433600,
    "game_type": { "id": 0, "type": "Main Game" },
    "genres": [ { "id": 5, "name": "Shooter" } ],
    "platforms": [ { "id": 6, "name": "PC (Microsoft Windows)" } ],
    "involved_companies": [
      { "id": 1, "developer": true, "publisher": false, "company": { "id": 56, "name": "Valve" } },
      { "id": 2, "developer": false, "publisher": true, "company": { "id": 57, "name": "Sierra Entertainment" } } ],
    "cover": { "id": 1, "image_id": "co1abc" },
    "external_games": [ { "id": 10, "uid": "70", "external_game_source": 1 } ] }
]
```

`steamgriddb-grids.json`:

```json
{ "success": true, "data": [ { "id": 1, "width": 600, "height": 900, "mime": "image/png",
  "url": "https://cdn2.steamgriddb.com/grid/desktop-guides-fixture.png" } ] }
```

`credentials/igdb-credentials.txt`:

```text
# Fake IGDB credentials for the fake-provider CI lane, T17.2 D. Not real keys.
igdb client id: desktop-guides-fixture-client
igdb client secret: desktop-guides-fixture-secret
```

`credentials/steamgriddb-credentials.txt`:

```text
# Fake SteamGridDB key for the fake-provider CI lane, T17.2 D. Not a real key.
steamgriddb api key: desktop-guides-fixture-steamgriddb-key
```

Make `image.png`, a 60 × 90 solid-colour PNG, from the repo root:

```bash
python3 - <<'PY'
import struct, zlib
def chunk(kind, data):
    return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)
width, height = 60, 90
rows = b''.join(b'\x00' + b'\x66\x88\xaa' * width for _ in range(height))
png = (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0)) +
       chunk(b'IDAT', zlib.compress(rows, 9)) + chunk(b'IEND', b''))
open('tests/fixtures/p1/providers/image.png', 'wb').write(png)
PY
```

- [ ] **Step 4: Run them to verify they pass**

Run: **Infrastructure tests** with `FullyQualifiedName~ProviderFixtureSetTests`.
Expected: all 3 pass. The first takes about 3 s, because of the delayed
"Final Fantasy" route.

When a client needs a request the manifest lacks, the first test's
unmatched-file message names it. Add the route, and its fixture file if
needed, without loosening the test.

- [ ] **Step 5: Commit**

```bash
git add tests/fixtures/p1/providers tests/DesktopGuides.Infrastructure.Tests/DesktopGuides.Infrastructure.Tests.csproj tests/DesktopGuides.Infrastructure.Tests/Providers/ProviderFixtureSetTests.cs
git commit -m "test(infra): the fake-provider fixture set drives the real provider clients"
```

### Task 3: Wire the fake into Preview builds, and prove Public builds lack it

**Files:**
- Modify: `src/DesktopGuides.Production/Providers/ProviderServices.cs` (the
  constructor, lines 10–16)
- Modify: `.github/workflows/windows-ci.yml` (`production-packages`: a new
  step after "Build and verify the public lane", before "Upload production
  package and manifest")

**Interfaces:**
- Consumes: `FixtureProviderHandler(string folder)` and
  `FixtureProviderHandler.ManifestName` (Task 1); `TestGate.IsOpen`;
  `AppLane.Current.LocalEvent("ProviderFixtures")`, which is
  `Local\DesktopGuides.Preview.ProviderFixtures` in Preview builds.
- Produces:
  - Preview builds use the fixtures when the gate is open and
    `<data root>\test\providers\manifest.json` exists;
  - a CI step that fails if the Public MSIX's `DesktopGuides.Production.dll`
    contains `ProviderFixtures`, or if the Preview MSIX's doesn't.

- [ ] **Step 1: Write the failing check**

Add after "Build and verify the public lane":

```yaml
      - name: Check the fixture providers are Preview-only
        if: matrix.architecture == 'x64'
        shell: pwsh
        run: |
          # T17.2 D: the fake-provider wiring is compiled out of the Public
          # lane (#if !PUBLIC_LANE). The Preview package is the positive
          # control, so this check can't pass because the string moved.
          Add-Type -AssemblyName System.IO.Compression.FileSystem
          function Test-DllString([string] $msix, [string] $text) {
            $zip = [System.IO.Compression.ZipFile]::OpenRead($msix)
            try {
              $entry = $zip.Entries | Where-Object { $_.FullName -eq 'DesktopGuides.Production.dll' } |
                Select-Object -First 1
              if (-not $entry) { throw "$msix has no DesktopGuides.Production.dll." }
              $stream = $entry.Open()
              $memory = [System.IO.MemoryStream]::new()
              try { $stream.CopyTo($memory) } finally { $stream.Dispose() }
              $bytes = $memory.ToArray()
              # String literals are UTF-16 and may start at an odd offset.
              foreach ($start in 0, 1) {
                $decoded = [Text.Encoding]::Unicode.GetString($bytes, $start, $bytes.Length - $start)
                if ($decoded.Contains($text)) { return $true }
              }
              return $false
            }
            finally { $zip.Dispose() }
          }
          $preview = @(Get-ChildItem artifacts/production-build -Filter 'DesktopGuides.Production_*_x64.msix')[0].FullName
          $public = @(Get-ChildItem artifacts/public-lane -Recurse -Filter 'DesktopGuides.Production_*_x64.msix')[0].FullName
          if (-not (Test-DllString $preview 'ProviderFixtures')) {
            throw 'The Preview package lacks the fixture-provider wiring; the check would prove nothing.'
          }
          if (Test-DllString $public 'ProviderFixtures') {
            throw 'The Public package contains the fixture-provider wiring.'
          }
          Write-Output 'Fixture providers: in the Preview package, absent from the Public package.'
```

- [ ] **Step 2: Run it to verify it fails**

Commit, push, and dispatch:

```bash
git add .github/workflows/windows-ci.yml
git commit -m "ci(p1): check the fixture providers are compiled into Preview builds only"
git push -u origin feat/p1-t17-2-fake-providers
gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p1-t17-2-fake-providers -f shell-scope=workflow -f shell-lane=msix -f dev-fast=true
```

Expected: `dev-production-packages` fails on "The Preview package lacks the
fixture-provider wiring", because nothing references `ProviderFixtures`
yet. Cancel the rest of the run.

- [ ] **Step 3: Wire it**

Replace the constructor's first line, and add the factory:

```csharp
    public ProviderServices(string localStatePath)
    {
        Http = CreateHttp(localStatePath);
        Tokens = new TwitchTokenSource(Http);
        Thumbnails = new ProviderThumbnailLoader(Http);
        Credentials = WindowsProviderCredentialStore.Create(localStatePath);
    }

    private static ProviderHttp CreateHttp(string localStatePath)
    {
#if !PUBLIC_LANE
        // T17.2 D: the fake-provider CI lane. Preview builds only, and only
        // while an installed test holds this gate open with fixtures staged.
        string fixtures = Path.Combine(localStatePath, "test", "providers");
        if (TestGate.IsOpen(AppLane.Current.LocalEvent("ProviderFixtures")) &&
            File.Exists(Path.Combine(fixtures, FixtureProviderHandler.ManifestName)))
        {
            return new ProviderHttp(new FixtureProviderHandler(fixtures));
        }
#endif
        return new ProviderHttp();
    }
```

- [ ] **Step 4: Run it to verify it passes**

Run:
- **Production build**;
- a Public-lane build:
  `s 'cd /d E:\work\desktop-guides\t17-2d && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64 -p:PackageLane=Public -nologo'`.

Expected: both succeed with 0 warnings. Then commit, push and dispatch, as
in Step 2.

```bash
git add src/DesktopGuides.Production/Providers/ProviderServices.cs
git commit -m "feat(app): use the fixture providers in Preview builds while the test gate is open"
git push
gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p1-t17-2-fake-providers -f shell-scope=workflow -f shell-lane=msix -f dev-fast=true
```

Expected: the new step prints "Fixture providers: in the Preview package,
absent from the Public package.", and the dispatch passes.

### Task 4: The installer's `-FakeProviders`, evidence and CI

**Files:**
- Modify: `tools/p1/windows_shell_install.ps1`:
  - the `param` block, adding the switch after `$AllowOfflineFirewallRule`;
  - `Run-ProviderScenarios`.
- Modify: `tools/p1/start_installed_task.ps1` (mirror the switch; the
  parity test requires it)
- Modify: `tools/p1/write_shell_evidence.ps1` and
  `tools/p1/test_write_shell_evidence.ps1` (the Providers line)
- Modify: `.github/workflows/windows-ci.yml` (the install step passes
  `-FakeProviders`)

**Interfaces:**
- Consumes: the fixture folder and fake credentials (Task 2); the app
  wiring (Task 3).
- Produces:
  - `windows_shell_install.ps1 -FakeProviders`;
  - `$report.providers.source` (`'fixtures'` or `'live'`);
  - `$report.providers.unmatched`;
  - the evidence line `Providers: <source>`.

- [ ] **Step 1: Write the failing evidence test**

In `test_write_shell_evidence.ps1`, add to the full report's `$report`
literal, after `applicationEvents`:

```powershell
        providers = [ordered]@{ source = 'fixtures' }
```

Then, after the `'The cleanup line is missing.'` assertion:

```powershell
    Assert-True ($text.Contains("- $pass Providers: fixtures")) 'The providers line is missing.'
```

- [ ] **Step 2: Run it to verify it fails**

Run: **Script tests** with `test_write_shell_evidence.ps1`.
Expected: FAIL with "The providers line is missing."

- [ ] **Step 3: Add the evidence line**

In `write_shell_evidence.ps1`, after the `Run result:` line in the `### Run`
section:

```powershell
    $providers = Field $report 'providers'
    if ($providers -and $providers.PSObject.Properties['source']) {
        $lines.Add((Line $true "Providers: $($providers.source)"))
    }
```

Run the test again. Expected: `write_shell_evidence: all cases passed.`

- [ ] **Step 4: Add the switch to the installer and the entry point**

In both `param` blocks, after `[switch] $AllowOfflineFirewallRule`:

```powershell
    [switch] $AllowOfflineFirewallRule,

    # T17.2 D: with no real IGDB credential file, run the provider group
    # against the recorded fixtures in tests/fixtures/p1/providers.
    [switch] $FakeProviders
```

In `start_installed_task.ps1`, the switch goes before the entry point's own
parameters. Run **Script tests** with `test_start_installed_task.ps1`.
Expected: it passes, including the parameter-parity case.

- [ ] **Step 5: Stage the fixtures in `Run-ProviderScenarios`**

Change its first two lines and add the fixture choice before the
credential reads:

```powershell
function Run-ProviderScenarios {
    $report.providers = [ordered]@{ source = 'live' }
    $providers = $report.providers
    $fixtureDirectory = Join-Path $PSScriptRoot `
        '..\..\tests\DesktopGuides.Infrastructure.Tests\Providers\Fixtures'
    # T17.2 D: real keys win. Without them, the fixtures stand in for the
    # providers, answered inside a Preview build through the gate below.
    $fixtureSource = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures\p1\providers')).Path
    $fixtureGate = $null
    if ($FakeProviders -and -not (Test-Path -LiteralPath $IgdbCredentialFile -PathType Leaf)) {
        $IgdbCredentialFile = Join-Path $fixtureSource 'credentials\igdb-credentials.txt'
        $SteamGridDbCredentialFile = Join-Path $fixtureSource 'credentials\steamgriddb-credentials.txt'
        $providers.source = 'fixtures'
    }
```

These assignments make function-local copies, so every later use in the
function, including the smokes' `-IgdbCredentialFile` arguments, gets the
fake files. The script-level values don't change.

After `[void](Invoke-ShellSeed @('seed-linked-game', $dataRoot))`, add:

```powershell
    if ($providers.source -eq 'fixtures') {
        # The credential files stay behind: the leak scans search this folder.
        $fixtureFolder = Join-Path $dataRoot 'test\providers'
        New-Item -ItemType Directory -Force -Path $fixtureFolder | Out-Null
        Get-ChildItem -LiteralPath $fixtureSource -File | Copy-Item -Destination $fixtureFolder
        $fixtureGate = [System.Threading.EventWaitHandle]::new(
            $false, [System.Threading.EventResetMode]::ManualReset,
            'Local\DesktopGuides.Preview.ProviderFixtures')
    }
```

Replace the `$providers.fixtureFields = Invoke-ShellSeed @(...)` line with:

```powershell
    $providers.fixtureFields = if ($providers.source -eq 'fixtures') {
        'skipped: the fixtures would be compared with themselves'
    } else {
        Invoke-ShellSeed @('check-igdb-fields', $IgdbCredentialFile, $fixtureDirectory)
    }
```

Before the final `Assert-NoCredentialLeak $secrets 'AfterRemove'`, add:

```powershell
    if ($fixtureGate) {
        $fixtureGate.Dispose()
        $unmatchedPath = Join-Path $dataRoot 'test\providers\unmatched.json'
        $providers.unmatched = @(if (Test-Path -LiteralPath $unmatchedPath) {
            Get-Content -LiteralPath $unmatchedPath -Raw | ConvertFrom-Json
        })
        if ($providers.unmatched.Count -gt 0) {
            Copy-Item -LiteralPath $unmatchedPath -Destination (Join-Path $ResultDirectory 'provider-unmatched.json')
            throw "The fixtures had no route for $($providers.unmatched.Count) provider request(s): " +
                (($providers.unmatched | ForEach-Object { "$($_.method) $($_.host)$($_.path)" }) -join '; ')
        }
    }
```

- [ ] **Step 6: Pass `-FakeProviders` in CI**

In "Install or unpack, and test production shell", add `-FakeProviders`
to the entry-point call, after `-TimeoutMinutes 45`:

```yaml
            -TimeoutMinutes 45 `
            -FakeProviders `
            @filter
```

- [ ] **Step 7: Check locally**

Run:
- **Script tests** with `test_start_installed_task.ps1`,
  `test_write_shell_evidence.ps1` and `test_windows_scripts_parse.ps1`;
- the **ASCII check** on every changed file;
- the YAML load: `ruby -C /Users/ilya.lissoboi/work/desktop-guides -ryaml -e 'f = ".github/workflows/windows-ci.yml"; begin; YAML.load_file(f, aliases: true); rescue ArgumentError; YAML.load_file(f); end'`.

Expected: all pass.

- [ ] **Step 8: Commit, push and dispatch the provider group**

```bash
git add tools/p1/windows_shell_install.ps1 tools/p1/start_installed_task.ps1 tools/p1/write_shell_evidence.ps1 tools/p1/test_write_shell_evidence.ps1 .github/workflows/windows-ci.yml
git commit -m "ci(p1): run the installed provider journey against recorded fixtures"
git push
gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p1-t17-2-fake-providers -f shell-scope=provider -f shell-lane=both -f dev-fast=true
```

Expected, on both lanes:
- `dev-production-shell-ui (<lane>, provider)` passes;
- the evidence shows "Providers: fixtures";
- the report has `providers.settings`, `providers.live` and
  `providers.remove`;
- `liveState` includes Half-Life with the local platform and notes;
- both leak scans list no files;
- `providers.unmatched` is empty;
- `providers.cancelDuringSearch` is `observed`.

When a request isn't routed, the failure names it. Add the route (Task 2's
files) and its unit-test expectation, then dispatch again. Ledger each added
route as a ruling.

### Task 5: Docs, the merge record, the full PR run and the PR

**Files:**
- Modify: `docs/p1/e2e-testing.md` (the provider paragraph under
  "Installation decision" and "Gates of record")
- Modify: `docs/p1/t17-2-fake-provider-design.md` (Status; "Rulings"; "T17.2
  piece D verification record")
- Modify: `docs/p1/t17-2-portable-ci-design.md` (the verification record's
  "PR run: pending" line)
- Modify: `docs/p1/results.md` (rows for #100, #101 and #103)
- Modify: `docs/p1/implementation-plan.md` (the T17.2 note) and
  `docs/progress.md` (the T17.2 rows and "Open:")

- [ ] **Step 1: Update `e2e-testing.md`**

In "Gates of record", replace "Provider live scenarios skip on CI, because
the runner has no credential files." with:

```markdown
On CI the `provider` group runs the whole provider journey against recorded
fixtures (T17.2 piece D). The installer's `-FakeProviders` switch, used only
when no real IGDB credential file exists, copies
`tests/fixtures/p1/providers` into the app's `test\providers` folder, holds
the `ProviderFixtures` test gate open, and types the fixtures' fake
credentials. A Preview build then answers every provider request from those
files, with no network. A Public build doesn't contain this path. Any
request the fixtures don't cover fails the group. The fixtures don't follow
changes in the real APIs.
```

The next sentence ("Run them on the Windows host with `-ProviderOnly`, using
the user's keys and normal provider requests.") stays. Add after it: "That
host run is the check for real-API drift."

- [ ] **Step 2: Update the design and the merge record**

In the piece D design:
- Set Status to "approved; implemented on `feat/p1-t17-2-fake-providers`".
- Add a "Rulings" section holding rulings 1–3 and every Task 4 route ruling.
- Add a "T17.2 piece D verification record" section with the Task 3 and
  Task 4 dispatch IDs, and a line for the PR run.

In the piece C design, replace "- **PR run:** pending." with:

```markdown
- **PR run:** [38383088085](https://github.com/ilya-slalom/desktop-guides/actions/runs/38383088085)
  passed on the first attempt, with all 10 shards. Merged in #100 (merge
  commit `77af7a5`).
```

In `results.md`, after the `#98` row, add rows for #100, #101 and #103 in
the table's format (PR, date, merge commit, task, run, Core and
Infrastructure counts, record, notes):
- **#100:** `77af7a5`, T17.2 piece C, run 38383088085, 1167 Core and 773
  Infrastructure tests. Note: "Every installed shard also runs against the
  portable exe: 10 shards in two lanes."
- **#101:** `42a204b`, Issue #94, run 38390100062, 1167 Core and 775
  Infrastructure tests. Note: "Diagnostics: a failed save's write records
  its gate wait, thread wait and SQL time, plus the thread pool's counts."
- **#103:** `9b4aead`, T17.2 piece E follow-up, run 38394489895, 1167 Core
  and 775 Infrastructure tests. Note: "The installed-run task runs under
  `conhost --headless`, and a timeout kills the inner tree before stopping
  the task."

In `implementation-plan.md`, change "Piece C runs every installed shard
against the portable exe as well; see the [piece C design](...)." to
"Piece C (#100, merge commit `77af7a5`) runs every installed shard against
the portable exe as well; see the [piece C design](...). Piece D runs the
provider journey against recorded fixtures; see the
[piece D design](t17-2-fake-provider-design.md)."

In `progress.md`:
- Change "the fake-provider lane remains; the portable CI job is in
  review)" to "the fake-provider lane is in review)".
- Add a "P1 T17.2 release CI, piece C" row after the piece E row:

```markdown
| P1 T17.2 release CI, piece C | Merged through [PR #100](https://github.com/ilya-slalom/desktop-guides/pull/100) on 13 October 2026, merge commit `77af7a5`. | Every installed CI shard runs against the portable single-file exe as well as the signed MSIX, on every PR and push: 10 shards in two lanes. A Preview-lane portable zip is built by `portable-packages`, checked against its `.sha256`, and unzipped; every scenario group passes on it. CI run [38383088085](https://github.com/ilya-slalom/desktop-guides/actions/runs/38383088085) passed. See the [verification record](p1/t17-2-portable-ci-design.md#t172-piece-c-verification-record). |
```

- [ ] **Step 3: Commit, push and open the PR**

```bash
git add docs
git commit -m "docs(p1): record the T17.2 fake-provider lane and the #100, #101 and #103 merges"
git push
```

Open the PR, titled "T17.2 piece D: run the installed provider journey
against recorded fixtures". The body names:
- the task: T17.2, piece D, plus the merge records;
- the prerequisites;
- the outcome;
- the Preview-only proof;
- the dispatch runs;
- every ruling.

There are no UI changes, so no screenshot.

- [ ] **Step 4: Wait for the PR run**

Expected: every job passes, including the `html` shards on both lanes,
whose evidence shows "Providers: fixtures", and the Preview-only check.
Record the run in the PR description, not with a push: a docs push to an
open PR starts a full run. Then stop for the user's merge decision.

## Traceability

| Spec item | Task |
| --- | --- |
| `FixtureProviderHandler`: routes, first match, 599, `unmatched.json`, allow-list | 1 |
| Fixtures: token, IGDB, covers, SteamGridDB, image, fake credentials | 2 |
| Wiring under `#if !PUBLIC_LANE`, gate and folder | 3 |
| Public build lacks the fake | 3 |
| Installer `-FakeProviders`, staging, gate, skips, unmatched check | 4 |
| Report `providers.source` and the evidence line | 4 |
| CI passes `-FakeProviders` | 4 |
| Tests | 1, 2, 3, 4 |
| Docs | 5 |
| Delivery and verification | 3, 4, 5 |
