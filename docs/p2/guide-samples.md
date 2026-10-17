# Saving a real guide for development

Sometimes a real guide, map or manual is needed to triage a bug or to check
a reader by hand. This page says how to keep a private copy. It doesn't
change the P2 rule that test fixtures are authored, never copied from real
guides ([implementation plan](implementation-plan.md), rule 5).

## Rules

- **Private copies only.** A saved copy is never committed, attached to an
  issue or PR, or pasted into a document beyond a few identifying words.
  To show a problem, describe its structure, or author a fixture that
  reproduces it.
- **Save it the way a reader would.** Use the browser or the site's own
  download link, one item at a time. No scripts, crawlers or bulk
  downloads, and nothing that gets around a site's blocking or its terms.
- **Keep it outside every repository,** in `E:\work\guide-samples` on the
  Windows host, or in a folder of your own elsewhere.
- **Delete it** once the work it was saved for is done.

## Layout

One folder per item:

```text
E:\work\guide-samples\<site>\<game>\<short title>\
    source.txt
    <the saved files>
```

`source.txt` holds:
- the URL, or one URL per page, in reading order;
- the site, the author and the date saved;
- the browser and method used (see below);
- why the copy was saved (an issue or task number).

## How to save each kind

| Kind | How | Notes |
| --- | --- | --- |
| Plain-text guide | Open the guide's plain-text view, press Ctrl+S, and save as a text file. | Don't copy and paste: that loses spacing, tabs and the original encoding. |
| Single HTML page | Ctrl+S, "Webpage, Complete". This gives a `.html` file and a `_files` folder. | Check it opens with the network off. Note any missing images or lost text. |
| One page as one file | Ctrl+S, "Webpage, Single File" (`.mhtml`, Edge and Chrome). | Keep it beside the "Complete" copy if both are useful. |
| Multi-page guide | Save each page as "Webpage, Complete" into one folder, named `01-`, `02-` and so on, in reading order. | List every page's URL in `source.txt`. Links between pages still point at the site. |
| Image map | Open the image itself, not the page around it, then "Save image as". | Keep the original file; never screenshot it. Note its pixel size. |
| PDF, CBZ or EPUB (for example from archive.org) | Download the original from the item's download options. | Prefer the original PDF. Skip lending-only or encrypted copies. Note the size and page count. |
| Print to PDF | Only when nothing else keeps the content. | Mark it "derived" in `source.txt`: it isn't the format readers download. |

## Using a copy

- Import it through the app, as a reader would, into a test library, not
  your own.
- When a copy shows a bug, record the structure that triggers it (for
  example "a 72 MB PNG map, 12809 by 9363 pixels") in the issue. Then
  author a fixture with that structure for the test.
