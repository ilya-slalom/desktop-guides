# T22.1 source review: GameFAQs, Neoseeker, Steam Community Guides, VGMaps

Status: reviewed 20 October 2026, from each site's live pages, or a
Wayback capture where the live site served a Cloudflare challenge. This
records what each site's terms, robots rules and defences allow. It is an
interpretation, not legal advice. The [P3 design](../p3-technical-design.md)
decides what the app does with it.

Re-check a source's terms before each release that changes its adapter,
and record the date here.

## Summary

| Source | In-app search | App fetches a pasted URL | User saves the page they view in the in-app browser |
|---|---|---|---|
| Steam Community Guides | Yes, through the official Web API with the user's own key | Yes; robots.txt allows guide pages | Yes |
| VGMaps | Yes, over the site's index, fetched on request | Yes; static files | Yes |
| Neoseeker | No | No | Grey area: chosen, 20 October 2026 |
| GameFAQs | No | No | Grey area: chosen, 20 October 2026 |

For the two grey-area sources, the decision (20 October 2026) was:
- the in-app browser keeps the site's ads, and the app blocks nothing;
- Save captures the guide's own pages and images automatically;
- there's no in-app search, and the app never fetches a page by itself
  outside the user's browser session.

Whether the Microsoft Store build includes these two is decided at the
Store submission milestone (T17.5).

## GameFAQs (Fandom)

- **Terms:** [Fandom's terms for GameFAQs](https://www.fandom.com/terms-of-service-pp1),
  revised 22 August 2025, cover gamefaqs.com and its subdomains. They forbid:
  - "unauthorized spidering, 'scraping,' data mining or harvesting";
  - accessing the services "using any interface other than ours";
  - using any software to "download, stream capture, store in a database,
    archive or otherwise copy" content, unless "expressly authorized by us
    in writing";
  - framing the site, or anything that would "deprive us of revenue";
  - anything that would "remove, modify, disable, block or otherwise impair
    any advertising";
  - anything that would "circumvent any access control measures".

  Viewing is allowed "for personal, non-commercial purposes".
- **robots.txt** (Wayback, 14 October 2026): for every user agent, it
  blocks `/search`, `/search_advanced`, `/user/` and `/community/*`. Guide
  pages (`/{platform}/{id}-{game}/faqs/{n}`) are not blocked.
- **Defences:** every non-browser request got HTTP 403 with
  `cf-mitigated: challenge`. The site also sends `x-frame-options: SAMEORIGIN`.
- **Content:** guides belong to their authors and carry the author's own
  notice. Many allow personal use, for example: "You may of course print,
  view, or reproduce this guide … for your personal use only". Others forbid
  any electronic distribution.
- **API:** none found. Text Mode is a client-side view, not a download.
- **Attribution:** "You shall not remove or alter any copyright, trademark,
  or other legal notices."

## Neoseeker (Neo Era Media)

- **Terms:** [Terms of Use](https://www.neoseeker.com/info/legal.html),
  from the Wayback capture of 1 April 2026. They say:
  - "You may view, read, and use the content on this site for personal,
    non-commercial use only."
  - "You may not copy our content, including images, for any purpose other
    than personal, non-commercial use."
  - "You may not access the site using automated means such as robots,
    botnets, scrapers, bots, AI training, scripts except in the case of
    public search engines … or with our written permission."
  - "None of our content may be used by bots."
- **robots.txt** (live): `Content-Signal: search=yes,ai-train=no,use=reference`
  and `Allow: /`. Walkthrough and wiki paths are not blocked. About 80 named
  bots are blocked outright, including Instapaper.
- **Defences:** a Cloudflare challenge on every non-browser request.
- **Content:**
  - Wiki walkthroughs become "the property of Neo Era Media".
  - Text FAQs "remain the property of their respective authors". The site
    asks that they "should NOT be reproduced or credited to Neoseeker", and
    not be linked directly.
- **API:** none documented. Site search is a Google Custom Search.
- **Attribution:** credit the FAQ's author, or Neo Era Media for wiki pages.

## Steam Community Guides (Valve)

- **Terms:**
  - The [Steam Subscriber Agreement](https://store.steampowered.com/subscriber_agreement/)
    §4.C bans "non-human-controlled systems ('Automation')"; its examples
    concern accounts and rewards. In §6.A, authors give Valve a
    non-exclusive licence to their content, which doesn't extend to third
    parties.
  - The [Web API terms](https://steamcommunity.com/dev/apiterms) allow
    "distribut[ing] Steam Data to end users for their personal use via your
    Application". They also set four limits:
    - keep the key confidential;
    - "inform the end user about any Steam Data you will store";
    - make no more than 100,000 calls a day;
    - don't imply that the app is endorsed by or affiliated with Valve.
- **robots.txt** (live): blocks only `/actions/`, `/linkfilter/`,
  `/tradeoffer/`, `/trade/` and `/email/`. `/sharedfiles/` is allowed.
- **API:**
  - `IPublishedFileService/QueryFiles` searches guides for an app
    (`filetype` 10 = all guides, `search_text`). It needs a Web API key.
  - `ISteamRemoteStorage/GetPublishedFileDetails` returns the title, author
    SteamID, app, summary, preview image and update time. Corrected on
    24 October 2026: without a key it answers Workshop items, but it
    answers guides as missing (`result` 9). So it's asked with the key,
    and a guide pasted without one takes its date from the page.
  - Neither returns the guide's body. That is only in the page HTML.
- **Defences:** a plain fetch of a guide page got 200, with no challenge.
  Bursts reportedly get HTTP 429.
- **Content:** guides belong to their authors.
- **Attribution:** the author's name with a link to their profile, a
  working link to the guide, and a "not affiliated with Valve" notice.

## VGMaps (The Video Game Atlas)

- **Terms:**
  - There are none. FAQ, About and Legal pages return 404.
  - The footer says "All directly 'ripped' game images are the property of
    their respective copyright holders" and "Protected by DMCA.com."
  - Mappers on the site's forum object to their maps being hosted elsewhere
    without permission.
- **robots.txt** (live): only Cloudflare's content-signal header, with no
  rules and no signals.
- **Structure:**
  - `/Atlas/index.htm` (about 1.9 MB) lists every game, linking to
    `/Atlas/<System>/index.htm#<Game>`.
  - Each system page is one large windows-1252 HTML file. It has a table
    per game, with one row per map: area, link, size, format, credit.
  - Maps are plain image files. Some entries are SWF or other formats.
  - There's no site search, API or sitemap.
- **Defences:** none seen. HTML and images returned 200 with any Referer.
- **Attribution:** each map's ripper (link text in its row), the game's
  copyright line, and the page URL.
- **Courtesy:** asking the site's owner, Jonathan Leung, before shipping
  would be the clean step.

## Prior art

[Pixel Guide](https://github.com/rexmont/Pixel-Guide-Android) (Android,
v1.5) imports from GameFAQs and Neoseeker. Its source isn't public.
According to its README and release notes:
- the user browses the site in an in-app WebView and downloads the guide
  being viewed;
- the app downloads the guide's images separately, with retries;
- it saves multi-page Neoseeker walkthroughs;
- it blocks ads while browsing;
- it has added VGMaps and ZeldaDungeon.net.

None of its documents mention the sites' terms, permission or crediting
authors. Desktop Guides keeps the browse-and-save pattern but doesn't
block ads.
