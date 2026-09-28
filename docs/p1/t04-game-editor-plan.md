# T04.1 game editor implementation plan

Status: implemented and signed installed Windows x64 checks passed,
27 September 2026; pending PR #11 merge. T03.2 and T11.1 are merged. T07.2
merged through PR #10 at `72f43785fad2b01b3f79b739017f0623d46b6665`.

## Decision

Use one window-owned WinUI `ContentDialog` for both adding and editing a
game. A separate window would add navigation and ownership work to a short
metadata form; two forms would let their validation drift. The dialog owns
draft text only. Its primary action calls the repository once after all
fields validate and keeps the dialog open with an actionable error if the
write fails. Cancel never issues a repository write.

The visual direction is a quiet reading workspace. Use system theme
surfaces and text brushes so high contrast remains native. Keep the shell's
existing large left-aligned page title. Put one plain action beside each
heading: `Add game` in Library and `Edit game` in Game. The dialog uses the
native body type scale, stacked labels, and small character counts, with
the game title first. No decorative cards or color blocks are needed.

```text
Library                                      [Add game]
No games in your library.

Game title                                   [Edit game]
Platform / notes when provided
Guides

Add game
Title *                         0 / 160
Platform                        0 / 80
Notes                           0 / 2000
                              [Cancel] [Add game]
```

Normalize whitespace at save, allow duplicate titles, and identify edits by
stable Game ID. Match the existing schema limits: title 1–160, platform
0–80, and notes 0–2,000 characters. An empty optional field becomes `null`.
Use the same validator in UI and repository so a direct repository caller
cannot bypass limits.

## Sequence and exit

| Step | Output and check |
| --- | --- |
| Contract | Shared normalized metadata input and `UpdateGameAsync` preserve ID and CreatedUtc, update UpdatedUtc, and leave guides and state untouched. Repository tests cover Unicode, duplicate titles, optional fields, limits, and missing IDs. |
| UI | Library Add and Game Edit open the same dialog. Primary is disabled until the trimmed title is valid. Length feedback, keyboard submission, accessible labels/focus, cancel, and retryable write errors work in a narrow WinUI window. |
| Installed check | Signed x64 production shell UIA creates a Unicode game with optional fields, cancels a draft, creates a duplicate title, edits by ID, and verifies persisted rows after relaunch. Capture an actual Add/Edit dialog screenshot for the PR description. |

T04.4 will place provider search and edition selection ahead of this dialog,
using it for `Create manually` and local overrides. T04.2 will add removal and
the full rename/selection refresh gate after T04.4. This task proves the shared
manual edit form and basic ID-based update.
