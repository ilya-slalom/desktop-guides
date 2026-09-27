# T07.1 static HTML dependency scan

Status: implementation plan, 27 September 2026. Prerequisite T03.3 is
merged. T07.2 will supply the Windows filesystem boundary, source-change
revalidation, and user-facing warnings before an import can publish.

## Decision

The scanner takes an HTML entry path and a caller-provided, path-keyed stream
source. It never opens a URL or the network. AngleSharp parses HTML and
AngleSharp.Css identifies CSS imports and bounds the rule traversal. A small
lexer reads URL functions and quoted `image-set()` choices from declaration
values in the original CSS text, including nested rules, custom properties,
and escaped URL function names. It skips comments, unrelated quoted strings,
and at-rule preludes. A failed URL argument advances the cursor past the
failed span so malformed CSS cannot make repeated scans of the same suffix.
Path checks receive the original target without escape decoding.

Before AngleSharp.Css parses a stylesheet, a linear preflight counts block
openings and at-rules at statement starts outside comments and strings.
It stops when cumulative markers exceed twice `MaxCssRules`; an at-rule
with a block can contribute both markers. This conservative guard bounds
parser work on rule-heavy untrusted CSS. The parsed rule walk still enforces
the exact configured rule count. A parser-level stopping hook would avoid
the conservative margin but would couple the budget to parser internals.

Linked CSS uses its byte-order mark or leading `@charset` declaration when
present. Recognized code-page labels use the .NET code-page provider without
process-wide registration; browser aliases for ISO-8859-1 and ASCII map to
Windows-1252. Unknown declarations and undecodable declared bytes fail
preview with a CSS encoding error. With neither a mark nor a declaration,
the scanner retains its UTF-8 fallback.

Parsing CSS by regex can misread comments, quoted strings, and nested
functions. Traversing only typed CSS values misses references in custom
properties. A parser-only walk of serialized rules also loses URLs in CSS
nesting that AngleSharp.Css omits. Reading declaration values from the source
text keeps those references while the parser still handles imports and rule
structure.

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
| Parse and classify | T03.3 | Parse one `.html`/`.htm` entry, image `src`/`srcset`, stylesheet links, `<style>`, style attributes, CSS `url()`/quoted `image-set()` choices, and `@import`. Decode declared CSS encodings before scanning. Keep the referring document, original target, safe normalized target, and supported/unsupported classification. Fragment-only references need no asset. |
| Bound traversal | Parse and classify | Visit each local CSS file once; terminate cycles; cap CSS depth, distinct asset count, entry/asset/total bytes, and rule count. A preparse CSS marker bound limits parser allocation, and the parsed rule count keeps the exact `MaxCssRules` contract. Stop with a typed preview failure when a budget is exceeded. |
| Manifest | Bound traversal | Hash each distinct supported source file and return a deterministically ordered manifest of relative names, lengths, kinds, and SHA-256 values. Missing/blocked references remain visible to T07.2. No file is staged or published in this task. |
| Windows exit | Manifest | On Windows 11 x64, locked Infrastructure tests cover nested CSS, cycles, multiple `srcset` and CSS URL images, local queries/fragments, unsupported targets, and each budget. Build Release x64 production MSIX. Locked ARM64 headless and package checks run in CI. |

The stream source is deliberately abstract so T07.2 can enforce NTFS links,
case collisions, safe roots, and revalidation before a source file is read.
T06.3 will stage verified bytes through its cross-boundary import journal.
The T07.1 manifest is a preview snapshot, never authority to serve a later
changed file.

## PR #9 custom-property URL follow-up

A relative URL inside an unregistered CSS custom property resolves when a
`var()` use supplies a property value. Resolving it as soon as its declaration
is read gives the wrong base path when the use is in another stylesheet.
Dropping all custom-property URLs would regress the same-file image case.
Computing the full CSS cascade is outside a static dependency scan.

Collect custom-property image URLs, variable dependencies, and use sites
while reading each declaration. After all reachable CSS and inline styles
have been scanned, follow the bounded variable graph from each use site and
resolve its image URLs against that site's document or stylesheet path. Do
not import an unused custom-property URL from its definition path. Keep
ordinary declaration URLs immediate and the existing asset/reference limits;
cap retained variable tokens and terminate variable cycles. This is a
conservative dependency scan across possible definitions, not a computed
style evaluator.

On Windows 11 x64, verify that a variable defined in `a/vars.css` and used
in `b/main.css` includes `b/icon.png` without a false `a/icon.png` warning.
Also cover same-file use, an HTML style-attribute use, an unused variable,
and a variable cycle. Re-run the locked Infrastructure suite and production
x64 package build.
