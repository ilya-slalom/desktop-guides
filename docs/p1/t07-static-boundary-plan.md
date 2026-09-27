# T07.2 static HTML import boundary

Status: implementation plan, 27 September 2026. T03.3 and T07.1 are merged.
T06.1 will display preview warnings, and T06.3 will call the post-copy check
before publishing a managed guide.

## Decision

Treat the directory containing the selected HTML entry as the source root.
The T07.1 scanner receives only a root-relative, disk-backed stream source.
For each open, validate the managed relative name and every existing path
segment, reject reparse points, open one handle, and on Windows compare that
handle's final path with the requested path. A path-only check would leave a
link-swap window between inspection and opening. No source file is changed.
Keep managed path validation strict for URL assets. A selected Windows HTML
filename containing a literal `%` uses `guide.html` or `guide.htm` inside the
manifest; the rooted source maps that alias to the selected file for preview,
copy, and post-copy verification. T06.3 must stage the entry under the alias
while opening the original through this mapping. Relative asset URLs still
resolve from the same root directory.
For that entry, recognize its matching `<entry title>_files` companion
directory in literal, percent-escaped, or fully URL-escaped form. Map it to
`__desktop_guides_files` in the managed tree; a different percent-bearing
path remains unsafe. Only the known root segment is remapped, so encoded
traversal, a raw managed-alias request, and filesystem links still fail.
Each included asset carries both a safe `RelativePath` for stage copies and
a decoded `RequestRelativePath` for the later per-guide URL allowlist. T06.3
must retain that mapping; T07.3 must serve only an allowlisted managed file
for a validated request path, never read the original request path from disk.

Return T07.1's non-included references as typed preview warnings for missing,
remote, unsafe, and unsupported assets. A warning means the referenced
resource is excluded from the static import; T06.1 supplies its display UI.
Reject two included asset names that differ only in Windows case, because
they would collide at a managed destination. Keep the source root and
preview manifest transient; do not persist the selected absolute path.

T06.3 will copy only manifest-listed assets into an isolated stage. A
post-copy check reopens each listed source and staged file through the same
boundary, compares bounded length and SHA-256 with the preview manifest,
and fails with a typed changed-source or changed-stage error before database
publication. Hashing the stage alone would miss a source changed after
preview. This task proves that contract with a simulated stage; T06.3 owns
the copy and journal.

## Sequence and exit

| Step | Output and check |
| --- | --- |
| Source boundary | Absolute selected HTML file becomes a safe root and entry name. Missing paths return `Missing`; parent traversal, encoded paths, and link traversal never open outside files. NTFS symlink and junction fixtures fail closed. |
| Preview | Scanner result plus typed warnings; remote and unsupported references remain visible. Included assets have unique case-folded destinations. A percent-named guide's matching companion CSS/images get safe managed names and retain their request-path mappings. |
| Post-copy check | Matching source/stage copies pass. A changed, removed, linked, or case-colliding file fails before publication, including a change after preview but before a simulated copy. Verification reads at most one byte beyond the recorded length to detect growth. Mapped companion paths obey the same checks. |
| Windows exit | Locked Release Infrastructure tests and a Release x64 production MSIX build pass on Windows 11 x64. ARM64 headless and package CI jobs pass. No installed import claim is made before T06.1 and T06.3. |
