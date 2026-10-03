# T09.1 Managed-guide HTML adapter design

Status: implemented in PR #35; CI run 37125967628 passed the installed
`html-reader` and `html-runtime-missing` scenarios.
Prerequisites: T06.3 is merged (PR #19, merge commit `494cb02`); T07.3 is
merged (PR #34, merge commit `3339fc7`).

## Intent

T07.3 put a restricted WebView2 session for one managed guide into the
Reader. T09.1 finishes that adapter, so an HTML guide fails in a way the
user can act on and never leaves profile data behind (TR09.2, TR09.3):

- **Leftover profiles are swept.** Each session already uses its own
  profile folder and deletes it on close, but a lock or a crash leaves it
  behind. Startup now removes those folders.
- **Runtime problems are told apart.** A missing WebView2 Runtime offers a
  **Get WebView2 Runtime** button. A runtime that is installed but fails to
  start offers **Reopen**. TXT guides (and PDF guides, once T10 lands) keep
  opening either way.
- **Missing and changed files are told apart.** A missing entry file says
  so, in the same words as TXT. A changed file keeps the existing "changed"
  message. Neither falls back to the original file or the network.
- **A crash is visible.** If the WebView2 browser or renderer process dies
  while a guide is open, the Reader says the guide stopped responding and
  offers **Reopen**, instead of showing a blank page.

Traces: the T09.1 row of [implementation-plan.md](implementation-plan.md)
(TR09.2, TR09.3), [work-breakdown.md](../work-breakdown.md) S09 and T09.1,
[p1-technical-design.md](../p1-technical-design.md) §6 (S09, the app-data
layout and service errors), and the T07.3
[design and implementation notes](t07-3-webview2-policy-design.md), whose
out-of-scope list hands these items to T09.1.

Decisions made during brainstorming:

- **Missing runtime: a button that opens Microsoft's page.** The button
  opens `https://developer.microsoft.com/microsoft-edge/webview2/` through
  the existing external-link launcher. It's an explicit click, so nothing
  goes to the network on its own. The other options were copy only, or
  opening Windows Settings, which can't help when the runtime was never
  installed.
- **Crash: show an error with Reopen.** Leaving it to T15.1 would keep a
  blank page possible until then.
- **The sweep runs once at startup.** It runs after the library lease is
  held and before any guide can open, so it can't race a live session. A
  folder still locked by a slow close in this run is removed at the next
  launch. Sweeping on every open would add disk work to opening a guide and
  race the folder the previous session is still releasing. One shared
  profile would break per-guide isolation.

## Infrastructure: `WebView2ProfileSweeper`

`src/DesktopGuides.Infrastructure/Storage/WebView2ProfileSweeper.cs`:

```csharp
public static class WebView2ProfileSweeper
{
    // Returns the number of profile folders removed.
    public static int Sweep(string cacheRoot);
}
```

- It looks only in `<cacheRoot>\WebView2\`. A missing folder is a no-op.
- It removes only direct children whose names parse as a `Guid` in `"N"`
  form, which are the names `HtmlReaderSession` generates. Other entries
  are left alone and aren't counted.
- A child that is a reparse point (junction or symlink) is removed as a
  link. Its target is never entered. Inside a profile, a recursive delete
  also removes links without following them. The sweeper checks
  `FileAttributes.ReparsePoint` before each descent instead of relying on
  `Directory.Delete(recursive: true)`.
- `IOException` and `UnauthorizedAccessException` on one folder skip it.
  The sweep never throws for a locked or unreadable folder, and never
  blocks or fails startup.
- It doesn't touch `<cacheRoot>\diagnostics\` or anything else in the cache
  root.

`ShellWindow` calls it right after `AppCacheRoot.Resolve`, on a worker
thread, before the first render. Its result isn't shown to the user.

## Core: errors, copy and actions

`HtmlGuideLoadError` gains `Missing`, `RuntimeFailed` and `Crashed`.
`HtmlGuideLoadMessages` gains an action mapping:

```csharp
public enum HtmlGuideLoadAction { None, GetRuntime, Reopen }

public static class HtmlGuideLoadMessages
{
    public const string RuntimeDownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";
    public static string For(HtmlGuideLoadError error);
    public static HtmlGuideLoadAction ActionFor(HtmlGuideLoadError error);
    public static string ActionLabel(HtmlGuideLoadAction action); // throws for None
}
```

| Error | Message | Action | Label |
|---|---|---|---|
| `RuntimeMissing` | Web page guides need the Microsoft Edge WebView2 Runtime. | `GetRuntime` | Get WebView2 Runtime |
| `RuntimeFailed` | Web page guides couldn't start. | `Reopen` | Reopen |
| `Crashed` | This guide stopped responding. | `Reopen` | Reopen |
| `Missing` | This guide's file is missing from the library. | `None` | |
| `NoManifest` | Re-import this guide to read it. | `None` | |
| `Changed` | This guide's files have changed. Re-import it to read it. | `None` | |

`Missing` uses the same words as `TextGuideLoadMessages` for
`TextGuideLoadError.Missing`. A Core test pins that they match.

## Infrastructure: missing versus changed entry

`ManagedHtmlGuideLoader.LoadAsync` returns `Missing` when the entry row's
managed file doesn't exist, or when resolving it fails because the file is
gone (`FileNotFoundException` or `DirectoryNotFoundException`). Every other
failure keeps returning `Changed`:

- a hash or size mismatch;
- more or fewer than one entry row, or an entry that isn't the guide's
  primary path;
- a link, an escaping path, an access error, or a bad path.

Nothing about serving changes. A non-entry asset that goes missing or
changes after open is still answered 403 and counted as `FileMissing` or
`HashMismatch`. It is never fetched from the source path or the network.

## Production: session

### Runtime probe

`HtmlReaderSession.OpenAsync` first calls
`CoreWebView2Environment.GetAvailableBrowserVersionString(browserFolder)`:

- A thrown exception (`WebView2RuntimeNotFoundException` or any other
  non-cancellation error) or a null or empty version gives
  `RuntimeMissing`.
- Once a version is found, any non-cancellation failure while creating the
  environment, the profile folder or the view (`EnsureCoreWebView2Async`)
  gives `RuntimeFailed`.

`browserFolder` is `null`, meaning the installed Evergreen runtime, unless
the missing-runtime test gate is open. In that case it is
`<cacheRoot>\missing-runtime-test\`, an empty folder the session creates.
That makes the real probe fail the same way it does on a machine without
WebView2. The environment call uses the same folder.

The failures after navigation starts, such as a timeout, a COM failure, or
an entry that never loads, keep mapping to `Changed`, per T07.3's Task 4
ruling.

### Process failure

`OpenAsync` subscribes `CoreWebView2.ProcessFailed`. The handler acts only
for `BrowserProcessExited`, `RenderProcessExited` and
`RenderProcessUnresponsive`. Chromium restarts its GPU, utility and frame
renderer processes by itself, so other kinds are ignored. The handler:

- runs once per session;
- does nothing after dispose;
- raises a new `Failed` event (`EventHandler<HtmlGuideLoadError>`) with
  `Crashed`.

If the failure happens while `OpenAsync` is still waiting for navigation,
`OpenAsync` throws `HtmlGuideLoadException(Crashed)` instead of raising
`Failed`.

`DisposeAsync` unsubscribes the handler and clears `Failed`. After a
browser crash, `BrowserProcessExited` may already have fired, or may never
fire. The 5-second wait stays, and a profile that can't be deleted is left
for the next startup sweep.

## Production: shell

### Reader error surface

In `ShellWindow.xaml`, the `ReaderLoadError` `TextBlock` keeps its
AutomationId and position. It moves into a vertical `StackPanel` with a
new `Button`:

- AutomationId `ReaderLoadErrorAction`;
- the standard button style (not accent), left-aligned;
- spacing from the design resources;
- collapsed unless the error has an action.

`ShowReaderSurface` gains an optional action parameter.
`ShowHtmlLoadError` passes `ActionFor(error)` and sets the button's content
from `ActionLabel`. Only HTML errors set the button, so TXT errors never
show one.

### Actions

- **Get WebView2 Runtime** opens `RuntimeDownloadUrl` through
  `ExternalLinkLaunchers.Create(cacheRoot)`, the same launcher as
  **Open in browser**. If the launch fails, it shows the existing warning
  "This link couldn't be opened." The test launcher records the URL, as it
  does for the external-link bar.
- **Reopen** queues a re-render of the current Reader route
  (`RunNavigationAsync(() => RenderCurrentAsync())`). The current session
  is closed and a new one opens with a new profile. If the route has
  changed in the meantime, the click does nothing.

### Crash while open

The shell subscribes the session's `Failed` event next to
`ExternalLinkRequested`. If the session is still `readerSession`, the shell:

1. clears it from the Reader actions;
2. hides the external-link bar;
3. removes the view from the surface;
4. disposes the session;
5. calls `ShowHtmlLoadError(Crashed)`.

A late event from an older session is ignored.

### Startup sweep

`WebView2ProfileSweeper.Sweep(cacheRoot)` runs right after `cacheRoot` is
resolved, as described above. The library lease is already held there, so
another window of the same app can't have a live profile in that cache.

## Test gates

The missing-runtime gate is the named event
`Local\DesktopGuides.Preview.WebView2Missing.<pid>`, checked with the
existing `TestGate.IsOpen`, like the other preview gates. It doesn't widen
the trust boundary. A process that can create a named event in the user's
session already runs as that user. All the gate does is make HTML guides
fail to open.

The crash test needs no gate. The smoke script kills the renderer process
of the app's WebView2: the `msedgewebview2.exe` child with `--type=renderer`
on its command line, below the browser process whose parent is the app.

## Testing

### Core xUnit

- `For`, `ActionFor` and `ActionLabel` cover every error, and
  `ActionLabel(None)` throws.
- `Missing` matches the TXT copy.
- `RuntimeDownloadUrl` is an absolute `https` URL on
  `developer.microsoft.com`.

### Infrastructure xUnit

- **Sweeper:**
  - It removes GUID-named profile folders, including nested files.
  - It keeps non-GUID names, files at the top level, and folders outside
    `WebView2\`.
  - A missing `WebView2\` folder is a no-op.
  - A locked file inside a profile skips that folder, removes the rest, and
    doesn't throw. This test runs on Windows only.
  - A junction inside a profile that points at a folder outside the cache
    is removed, and the target's files survive. A GUID-named junction at
    the top level is handled the same way. These tests run on Windows only.
- **Loader:**
  - A deleted entry file gives `Missing`.
  - A changed entry gives `Changed`.
  - A deleted guide folder gives `Missing`.
  - An entry that is a link still gives `Changed`.

### Installed smoke (`production-shell-ui`)

Additions to the `html-reader` scenario:

- **`html-profile-sweep`:** before launch, the install script plants
  `Cache\WebView2\<guid>\leftover.txt` and `Cache\WebView2\keep-me\`. After
  startup, the GUID folder is gone and `keep-me` remains.
- **`html-crash`:** at the end of the online pass, after `html-canary-b`.
  1. With Canary Guide B open, the smoke script kills the renderer.
  2. It waits for `ReaderLoadError` "This guide stopped responding." and a
     visible `ReaderLoadErrorAction` "Reopen".
  3. It clicks Reopen and waits for "Canary guide B loaded" on the page.
- **`html-runtime-missing`:** a third pass, after the offline pass, with
  the gate open and the canary listening. Before launch, the install script
  deletes Canary Guide B's managed entry file.
  1. Opening Canary Guide A shows the RuntimeMissing message and a visible
     "Get WebView2 Runtime" action.
  2. Clicking it records `RuntimeDownloadUrl` in `external-launches.json`.
  3. **`html-missing-entry`:** opening Canary Guide B shows the `Missing`
     message with no action. The loader runs before WebView2, so the
     missing runtime doesn't hide it.
  4. Back to the game, a TXT guide opens and shows its rows.
  5. After the pass, the canary still has only its health-check ACCEPT.

  This pass needs a TXT guide in the Web Reader Game. If the
  `seed-html-reader` seed doesn't already add one, it gains one.

The existing canary assertions, served-set checks and offline pass are
unchanged. The PR includes light and dark screenshots of the Reader error
surface with the action button.

## Docs

- `docs/p1/implementation-plan.md`:
  - Change "T07.3 is in review in PR #34" to the merged form: merged
    through PR #34 on 3 October 2026, merge commit `3339fc7`, with CI run
    37084631667 as its final-HEAD run.
  - Add a T09.1 paragraph after it.
- `docs/work-breakdown.md`: add the T09.1 PR pointer.
- `docs/p1-technical-design.md` §6 S09: link this design, and record the
  startup-only sweep and the error-to-action mapping.

## Out of scope

- Theme, font and text scale (T09.2), and position capture and restore
  (T09.3).
- A Remove action for missing guides, and database and other service
  errors (T15.1).
- Retrying a locked profile within the same run.
- Detecting a too-old runtime separately from a failed start. A runtime
  that lacks an API this app needs shows as `RuntimeFailed`.

## Implementation notes

Planning rulings, from the
[implementation plan](t09-1-html-adapter-plan.md):

- **R1. The crash phase runs in both the online and offline passes.** Each
  `html-reader` pass expects sessions A, B, B, and the two passes give the
  light and dark screenshots of the error surface with Reopen. The
  runtime-missing pass adds a dark screenshot with Get WebView2 Runtime.
- **R2. The shell handles `Failed` inside the navigation queue.** A render
  holding the queue finishes first, so a crash just after `OpenAsync`
  returns is still torn down, and no render interleaves with the teardown.
- **R3. `ReaderSurface` is collapsed when it has no view**, so an empty
  `ContentControl` never sits over the action button.
- **R4. The loader checks the planned entry path before reading it.**
  `ManagedHtmlAssetReader.Read` returns `Missing` for link and access errors
  too, so its status alone can't tell missing from changed.

Execution notes:

- The installed smoke checks the sweep through the report field
  `htmlReader.profileSweep` (`removed`), not a separate phase.
- `seed-html-reader` adds a TXT guide, "Plain Text Guide", to the Web Reader
  Game for the runtime-missing pass.
- RED was observed in separate CI runs per task, because a compile error in
  one test project stops the other from building. No local .NET toolchain
  was used.

## Verification

CI run 37125967628 passed `core-tests` and the installed `production-shell-ui`
scenarios. Screenshots of the Reader error surface:

- [Crash, light](evidence/t09-1-html-adapter/html-crash-light.png)
- [Crash, dark](evidence/t09-1-html-adapter/html-crash-dark.png)
- [Missing runtime, dark](evidence/t09-1-html-adapter/html-runtime-missing-dark.png)
