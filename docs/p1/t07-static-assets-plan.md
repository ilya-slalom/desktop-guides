# T07.1 static HTML dependency scan

Status: implementation plan, 27 September 2026. Prerequisite T03.3 is
merged. T07.2 will supply the Windows filesystem boundary, source-change
revalidation, and user-facing warnings before an import can publish.

## Decision

The scanner takes an HTML entry path and a caller-provided, path-keyed stream
source. It never opens a URL or the network. AngleSharp parses HTML and
AngleSharp.Css parses CSS rules and declarations. A small URL-function lexer
walks the CSS declaration values supplied by the parser, including multiple
background images. This keeps comment, selector, and rule handling with a
real parser while retaining references in CSS custom properties.

An alternative was to parse CSS by regex. It can misread comments, quoted
strings, and nested functions. Traversing only typed CSS values was also
considered, but the parser exposes custom property values as untyped text,
so that approach would miss references that a browser could later use.

Pin AngleSharp `1.8.2` and AngleSharp.Css `1.1.2` through central NuGet
versions and locked restore. Their package metadata declares MIT licenses;
AngleSharp.Css `1.1.2` declares AngleSharp `[1.5.0, 2.0.0)` for .NET 10.
The package metadata and parser README are the license and compatibility
review for this task.
See the [AngleSharp 1.8.2 package](https://www.nuget.org/packages/AngleSharp/1.8.2)
and [AngleSharp.Css 1.1.2 package](https://www.nuget.org/packages/AngleSharp.Css/1.1.2)
for the pinned MIT license metadata.

## Contract and sequence

| Step | Dependency | Output and success check |
| --- | --- | --- |
| Parse and classify | T03.3 | Parse one `.html`/`.htm` entry, image `src`/`srcset`, stylesheet links, `<style>`, style attributes, CSS `url()`, and `@import`. Keep the referring document, original target, safe normalized target, and supported/unsupported classification. Fragment-only references need no asset. |
| Bound traversal | Parse and classify | Visit each local CSS file once; terminate cycles; cap CSS depth, distinct asset count, entry/asset/total bytes, and rule count. Stop with a typed preview failure when a budget is exceeded. |
| Manifest | Bound traversal | Hash each distinct supported source file and return a deterministically ordered manifest of relative names, lengths, kinds, and SHA-256 values. Missing/blocked references remain visible to T07.2. No file is staged or published in this task. |
| Windows exit | Manifest | On Windows 11 x64, locked Infrastructure tests cover nested CSS, cycles, multiple `srcset` and CSS URL images, local queries/fragments, unsupported targets, and each budget. Build Release x64 production MSIX. Locked ARM64 headless and package checks run in CI. |

The stream source is deliberately abstract so T07.2 can enforce NTFS links,
case collisions, safe roots, and revalidation before a source file is read.
T06.3 will stage verified bytes through its cross-boundary import journal.
The T07.1 manifest is a preview snapshot, never authority to serve a later
changed file.
