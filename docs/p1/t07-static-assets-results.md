# T07.1 static HTML dependency scan result

Status: implementation check on 27 September 2026. T03.3 is merged; T07.2
filesystem validation and T06.3 staging remain later M2 tasks.

`StaticHtmlDependencyScanner` uses AngleSharp `1.8.2` and AngleSharp.Css
`1.1.2`, pinned in the central version file and NuGet locks. An abstract
path-keyed stream source keeps file access outside the parser. The scanner
returns a deterministic manifest of supported entry/CSS/image paths with
byte lengths and SHA-256 values. It records missing, unsupported, unsafe,
and remote references for later preview warnings. It never accesses the
network or stages a guide.

On the Windows 11 x64 host (build `10.0.26200.0`, .NET SDK `10.0.401`), source
was staged under `E:\work\desktop-guides\t07-check-20260927`. Locked
solution and ShellSeed restores passed. Release tests passed **71/71 Core**
and **58/58 Infrastructure**, including **11/11 T07.1 checks**. The scanner
tests cover nested CSS and import cycles, HTML and CSS image variants,
`srcset`, query/fragment normalization, inline styles, string/comment
false positives, blocked references, and entry/asset/tree byte, asset,
reference, depth, and CSS-rule budgets.

The unsigned Release x64 production MSIX build passed with zero errors.
It reported the host's existing `mspdbcmf.exe` warning, so no symbols package
was generated. Both Windows CI runs for commit `37bcacb`
(`36311213988` and `36311226103`) passed native ARM64 Core and
Infrastructure tests, and x64/ARM64 test and production package builds.
The CI UI jobs were still running when this result was recorded. No
installed reader or import scenario is claimed by T07.1.

## PR #9 review follow-up

Four review regressions were reproduced and fixed: images in nested CSS
rules and escaped `url()` functions are now included; `@namespace` and
condition-prelude URLs are excluded; missing files remain classified as
missing after the distinct-asset cap is reached. The Windows 11 x64 staged
source passed **62/62 Infrastructure tests**, including all four permanent
regression tests. The unsigned Release x64 production MSIX build passed
with zero errors and the same `mspdbcmf.exe` symbols warning. This scanner
is still disconnected from the installed import workflow.

## PR #9 second review follow-up

The Windows 11 x64 review probes reproduced a quadratic scan of malformed
`url(` tokens, omission of quoted `image-set()` images, and a false missing
image from Windows-1252 CSS with a leading `@charset`. The fixes advance
past malformed URL arguments, collect quoted image choices, and decode
linked CSS using a byte-order mark or leading declaration. Unknown
declared encodings now fail preview explicitly. Permanent regression tests
also cover multiple image choices and BOM precedence. The staged Windows
source passed **67/67 Infrastructure tests**. The unsigned Release x64
production MSIX build passed with zero errors and the existing
`mspdbcmf.exe` symbols warning.
