# Online sources

For contributors who touch **Find online** or the metadata providers. It
records what each site allows, how the app reaches it, and the rules a new
source must meet. The terms notes are an interpretation from the sites'
pages (reviewed 20 October 2026), not legal advice.

Re-check a source's terms before each release that changes its adapter, and
update the date here.

## Summary

| Source | Search in the app | Pasted URL | How a guide is saved |
| --- | --- | --- | --- |
| GameFAQs | No | Opens in the in-app browser | User-driven capture in the in-app browser |
| Neoseeker | No | Opens in the in-app browser | User-driven capture in the in-app browser |
| Steam Community Guides | Yes, through the Steam Web API with the user's own key | Fetched by the app | App fetch of the guide page and its images |
| VGMaps | Yes, locally over the site's index | Fetched by the app | App fetch of the maps the user picks |

The four sources are listed in
[`OnlineSources.cs`](../src/DesktopGuides.Core/Online/OnlineSources.cs).
Each has a build switch (`IncludeGameFaqs`, `IncludeNeoseeker`,
`IncludeSteam`, `IncludeVgMaps`, all on by default in
`Directory.Build.props`). A source that's off isn't offered, and its URLs
are refused like any unsupported site. Guides already saved from it still
read. Whether the Store build keeps GameFAQs and Neoseeker is decided at
T17.5 ([roadmap](roadmap.md)).

## Rules for every source

- **Reading never reaches the network.** A saved guide is an ordinary
  static copy that goes through the same validation as an import: no
  scripts, local assets only, the size limits. Captured HTML is untrusted.
- **The offline copy keeps the guide, not the page:** its text, images and
  the author's notice, with a header naming the title, author, source, URL
  and date saved. Site navigation, ads, scripts and the site's CSS are
  dropped. Each copy gets a stylesheet the app ships, which makes no
  requests.
- **Attribution stays with each saved copy.** The guide stores its source,
  identity at the source, URL, author, notice, saved time, source update
  time and source title. The game page shows the source and author, and the
  copy's header links to the original.
- **Restrictive notices:** when a guide's notice forbids electronic copies,
  Save shows it and asks before going on.
- **Only the guide is captured.** No other link is followed and no site is
  crawled.
- **Never:** block or hide ads; get around a challenge, a rate limit or
  robots.txt; spoof a browser for app-made requests; replay the in-app
  browser's cookies in a separate HTTP client; search GameFAQs or
  Neoseeker from the app; check for updated guides in the background.
  Saving again is always the user's action.
- **App fetches** go through `ProviderHttp`:
  - https on the default port only, to hosts in `ProviderHttp.AllowedHosts`;
  - one request at a time;
  - redirects only to the same host, at most 3;
  - a 15 s timeout;
  - a 429 is retried once only if `Retry-After` is 2 s or less, else the
    request stops with "busy";
  - a user agent naming Desktop Guides and the repository.
- **Failures never touch the library.** A capture lives in its own staging
  folder until it publishes. It can be resumed, saved as partial or
  discarded, and startup removes captures older than 7 days.

## GameFAQs

- **Terms:** Fandom's terms forbid scraping, any interface other than
  theirs, software copies of content without written permission, and
  impairing ads. Viewing for personal, non-commercial use is allowed.
  Guides belong to their authors and carry their own notices.
- **Defences:** a Cloudflare challenge on every non-browser request.
- **How the app reaches it:** only through the in-app browser, a WebView2
  in its own profile, with the site's ads and scripts as served. Navigation
  stays on `gamefaqs.gamespot.com`; other links open in the system browser.
  The app never fetches a GameFAQs page by itself, and the host isn't in
  `AllowedHosts`.
- **Save:** steps the visible browser through the guide's own pages, as a
  person clicking Next would. Images come from each page load's own
  responses.
- **Pacing:** at most one page every 2 seconds, and at most 200 pages per
  guide; a longer guide is saved as partial. A challenge, error, 403 or 429
  pauses the save until the user solves it in the browser and resumes.
- **Attribution:** the author and their notice, unaltered.

## Neoseeker

- **Terms:** content is for personal, non-commercial use; no automated
  access except public search engines or with written permission.
- **Defences:** a Cloudflare challenge on every non-browser request.
- **How the app reaches it:** as for GameFAQs, only through the in-app
  browser on `www.neoseeker.com`, with the same pacing, pause rules and
  200-page cap.
- **Two kinds of guide:**
  - **Text FAQs** belong to their authors. They're credited to the author
    alone, as the site asks; Neoseeker appears only as where it was saved
    from, in the copy's header with the original link.
  - **Wiki walkthroughs** belong to Neo Era Media. Their pages are the ones
    the walkthrough's contents lists, plus the same game's wiki articles
    those pages link to directly. Articles' own links are never followed,
    and the cap trims articles first.

## Steam Community Guides

- **Terms:** the Steam Web API terms allow giving Steam data to end users
  for personal use. Limits: keep the key confidential, tell the user what
  is stored, at most 100,000 calls a day, and don't imply endorsement by or
  affiliation with Valve. robots.txt allows `/sharedfiles/`.
- **The key:** the user's own Steam Web API key, entered, tested
  (one `QueryFiles` call), saved and cleared on the Settings provider card.
  It's never logged and is sent only to Steam's API.
- **How the app reaches it:** app fetches on `api.steampowered.com`,
  `steamcommunity.com` and `images.steamusercontent.com`.
  - **Search:** `IPublishedFileService/QueryFiles`, with the key, 20 results
    a page, run only on Enter or **Search**.
  - **The game's Steam app:** from IGDB, or matched by name against
    `IStoreService/GetAppList` (with the key, its pages 1 s apart, cached
    30 days). The user can change the match, or paste a store URL.
  - **A guide:** its page, then the images in its content, one request at a
    time with at least 1 s between them, and at most 200 images. Metadata
    comes from `GetPublishedFileDetails` when there's a key; a pasted URL
    without a key uses the page alone.
- **Attribution:** the author's name with a link to their profile, a link
  to the guide, and "Not affiliated with Valve".

## VGMaps (The Video Game Atlas)

- **Terms:** none published. The footer says ripped images belong to their
  copyright holders, and mappers object to their maps being hosted
  elsewhere without permission. No robots rules.
- **How the app reaches it:** app fetches on `vgmaps.com`, only on the
  user's action, with at least 1 s between requests:
  - `/Atlas/index.htm` on the first search, cached 30 days, with
    **Refresh**; search then runs locally;
  - a system page only when the user opens a game;
  - map images only for the maps the user picks, one at a time.
- **Formats:** PNG and JPEG save as they are, GIF is converted to PNG, and
  SWF and other formats are listed as not saveable. Maps over the image-map
  limits are listed as too large.
- **Attribution:** each map's ripper, the game's copyright line and the page
  URL.
- **Courtesy note:** a note to the site's owner, Jonathan Leung, is
  optional and blocks nothing. Sending it is the maintainer's; it's still
  open.

## Metadata providers

Game search and artwork, separate from Find online. Each uses the user's own
credentials, entered on the Settings provider card and stored in
`ProviderCredentialFile`, encrypted for the current Windows user. Search is
an explicit action: typing alone sends nothing. The app has no scraping or
proxy.

| Provider | Credentials | Hosts |
| --- | --- | --- |
| IGDB | The user's Twitch client ID and secret | `id.twitch.tv`, `api.igdb.com`, `images.igdb.com` |
| SteamGridDB | The user's API key | `www.steamgriddb.com`, `cdn2.steamgriddb.com` |

## Proposing a new source

1. **Terms review first.** Read the site's terms, robots.txt and defences,
   and add a section here. A site that forbids the planned access is out.
2. **Pick the access path** the terms allow: an official API, an app fetch
   of static pages, or user-driven capture in the in-app browser. Nothing
   that gets around a challenge or rate limit.
3. **Attribution:** decide what each saved copy credits, and to whom.
4. **Offline copy:** the result must pass the existing TXT, HTML or image
   map validation, and read with networking off.
5. **Allowed hosts:** add each host the app fetches to
   `ProviderHttp.AllowedHosts`, and the browsing hosts to `OnlineSources`,
   behind a new build switch.
6. **Tests use fixtures only.** No CI test reaches a live site. The adapter
   takes a base URL, and the loopback fixture server serves synthetic pages
   written for the tests; no copied guide text.
7. **A manual live check** by a maintainer with real guides, before the
   release that ships it. A failed live check is a finding to triage, not a
   CI failure.
