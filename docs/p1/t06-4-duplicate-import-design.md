# T06.4 duplicate import design

Status: implemented on `feat/p1-t06-4-duplicate-import`; verified by CI run 36675695341.
Prerequisite T06.3 was merged through PR #19 on 30 September 2026, merge
commit `494cb02`.

## Intent

When someone imports a file whose content is already a guide in the selected
game, the preview says so before anything is copied. They then choose
between two actions:

- **Open existing** goes to the guide they already have.
- **Import another copy** publishes a second, independent guide.

An import never overwrites an existing guide, and it never adds a duplicate
without the person's choice. Replacing an existing guide's bytes is a later,
explicit workflow, not a side effect of import.

Traces: the T06.4 row of [implementation-plan.md](implementation-plan.md)
(TR06.3), [work-breakdown.md](../work-breakdown.md) T06.4, and
[p1-technical-design.md](../p1-technical-design.md) (T06.4, 573–578).

Decisions made during brainstorming:

- **Detect at preview.** The preview computes the fingerprint for every
  format and looks it up. The person learns about the duplicate before
  pressing anything, and nothing is copied and discarded.
- **Match** means the same game, the same `Format` and the same
  `ContentSha256`. The same file in another game isn't a duplicate. For TXT,
  a different encoding choice is still a duplicate, because the bytes are
  the same.
- **The publisher enforces the choice too.** `PublishAsync` rejects a
  duplicate unless the caller passes `allowDuplicate: true`. That makes "no
  silent duplicate" a unit-tested rule, since Production has no test project.
- **The copied bytes must match the preview fingerprint.** This replaces the
  T06.3 ruling that re-checked a TXT or PDF source only by size and
  last-write time.
- **Open existing** closes the dialog and opens the guide through the shell's
  existing reader route. Until M3 adds the readers, that route shows the
  guide's details.
- **Typed errors** were already delivered by T06.2. `Missing`, `Unsupported`,
  `Encrypted` and `Unreadable` stay distinct, and T06.4 adds `Duplicate`
  plus a test that keeps the issues distinct.
- **No schema change.** `IX_Guides_GameId` bounds the lookup to one game's
  guides, and the schema stays at version 3.
- **Out of scope:**
  - replacing a guide's bytes;
  - duplicate detection across games;
  - showing the duplicate before a legacy TXT encoding is chosen.

## Components

### Fingerprint on every manifest (Core/Import)

`Fingerprint` moves from `HtmlImportManifest` to the base record:

```csharp
public abstract record ImportManifest(
    ImportSource Source, GuideFormat Format, string SuggestedTitle, string Fingerprint);
```

Every subtype passes it through. The value is the lowercase hex SHA-256 that
T06.3 already stores in `Guides.ContentSha256`:

- **TXT and PDF:** the hash of the file's bytes.
- **HTML:** `GuideFingerprint.OfHtml`, unchanged.

`ImportNeedsTxtEncoding` doesn't get a fingerprint. The fingerprint arrives
with the manifest, once an encoding is chosen.

### Validator (Infrastructure/Import)

- **`InspectTxtAsync` and `ResolveTxtEncodingAsync`** already read every byte,
  so they hash the same buffer with `SHA256.HashData`.
- **`InspectPdfAsync`** hashes the stream first, rewinds it, and then runs
  the existing `ReadPdf` checks. The hash runs on the thread pool, under the
  preview's busy state and Cancel button, and checks the token between
  81,920-byte reads. `ReadPdf` takes the fingerprint as a parameter, so the
  publisher's `VerifyPdf` can pass the staged copy's hash.
- **`InspectHtmlAsync`** is unchanged.

### Repository lookup (Infrastructure/Storage)

```csharp
public Task<Guide?> FindGuideByFingerprintAsync(
    Guid gameId, GuideFormat format, string contentSha256, CancellationToken token = default);
```

- It returns the oldest matching guide (`ORDER BY ImportedUtcMs, Id`), or
  null.
- The lookup reads without taking the write gate, like `ListGuidesAsync`.
- `IImportJournal` gains the same query, as a synchronous
  `Guid? FindGuide(Guid gameId, GuideFormat format, string contentSha256)`.
  The publisher runs it while it already holds the gate.
- Both methods share one SQL text.

### Publisher (Infrastructure/Import)

```csharp
public Task<Guid> PublishAsync(
    ImportManifest manifest, Guid gameId, string title, bool allowDuplicate,
    IProgress<ImportProgress>? progress, CancellationToken token);
```

Inside the gate, `PublishLockedAsync` runs these steps in order:

1. `CheckSource`.
2. **Duplicate check** (new). If `allowDuplicate` is false and
   `journal.FindGuide(gameId, manifest.Format, manifest.Fingerprint)` returns
   a guide, the import stops with `ImportIssue.Duplicate` before any journal
   row or file exists.
3. `PlanAsync`, `Prepare`, and the copy.
4. **Fingerprint check** (new). The hash of the copy must equal
   `manifest.Fingerprint`; otherwise the import fails with `Changed` and
   rolls back. For HTML, `PlanAsync` already compares the re-scan
   fingerprint, and this check also covers the bytes actually copied.
5. `CheckSource`, verification, rename and publication, unchanged.

`NewImportedGuide.ContentSha256` stays the copied hash, which now always
equals the manifest's fingerprint.

## Errors

| Issue | Where it's raised | Message |
| --- | --- | --- |
| `Duplicate` (new) | Publisher, when `allowDuplicate` is false | "This file is already a guide for this game." |
| `Changed` | Publisher, when the copied bytes differ from the preview fingerprint | The existing `ChangedMessage` |

The dialog never passes `allowDuplicate: false` for a known duplicate, so
`Duplicate` only reaches a person if the library gains a match after the
preview. It shows in the dialog's error `InfoBar`, like any
`GuideImportException`. Existing issues and messages are unchanged.

## Dialog

`ImportGuideDialog` gains a lookup function. The shell binds it to
`FindGuideByFingerprintAsync` for the selected game:

```csharp
Func<ImportManifest, CancellationToken, Task<Guide?>> findDuplicate
```

The import function gains the `allowDuplicate` argument.

1. When a manifest is ready (after inspection, or after the encoding choice
   for legacy TXT), the same `RunAsync` work calls `findDuplicate` before
   `ShowManifest`.
   - A lookup failure takes the existing failure path: the generic "couldn't
     be checked" message, no manifest, and Import disabled.
   - Cancel and generation checks behave as they do for inspection.
2. On a match:
   - A new informational `InfoBar`, `ImportDuplicate`, opens inside the
     preview. Its message is *"This file is already in {game} as "{guide
     title}"."*
   - The `InfoBar`'s action button reads **Open existing**
     (`ImportOpenExisting`).
   - The primary button reads **Import another copy**. Without a match it
     reads **Import**.
   - The duplicate `InfoBar` isn't closable. It closes when another file is
     checked, when a new encoding is chosen, or when an import starts.
3. **Open existing** sets `internal Guid? OpenGuideId` and hides the dialog.
   After `ShowAsync` returns, if the same Game route is still shown and no
   close was requested, the shell opens that guide with
   `OpenGuideAsync(id, route.GameId)`.
4. **Import another copy** calls the import function with
   `allowDuplicate: true`. Success follows T06.3: `ImportedGuideId`, then
   hide, then the new guide selected and focused. The copy has a new Guide
   ID and its own empty reading state.
5. **Import** without a match passes `allowDuplicate: false`.

`OpenGuideId` and `ImportedGuideId` are never both set.

## Testing

The T06.2 validator tests already pin `Missing`, `Unsupported`, `Encrypted`,
`Unreadable` and `UnsupportedEncoding` to their fixtures, and they must keep
passing unchanged. Core has no behavior change beyond the moved
`Fingerprint`, so Core tests only update the manifests they build.

### Infrastructure tests (TDD, real SQLite and NTFS on `pcsx2-win`)

**Validator**
- TXT (UTF-8), TXT after `ResolveTxtEncodingAsync`, and PDF manifests carry
  the SHA-256 of the file's bytes, pinned for the fixtures.
- The HTML fingerprint is unchanged (`HtmlStaticFingerprint`).
- Cancelling during a stream hash stops after the current read
  (`GuideFingerprint.OfStream`).

**Repository**
- `FindGuideByFingerprintAsync` finds a match in the same game.
- It returns null for the same hash in another game, and for the same hash
  with another format.
- With two matches, it returns the oldest.

**Publisher**
- A second import of the same file with `allowDuplicate: false` throws
  `Duplicate` and leaves no `FileOperations` row, staging entry or content
  directory.
- The same import with `allowDuplicate: true` publishes a second guide. The
  copy has a new ID, its own `ReadingStates` row and its own content
  directory, and the first guide is unchanged.
- A source rewritten during the copy (at the `Prepared` checkpoint, before the copy reads it) with the
  same length and restored last-write time fails with `Changed`, for both TXT
  and PDF. T06.3 missed this case.
- Existing publisher tests pass `allowDuplicate` and still pass.

### Installed smoke

The T06.3 group ran `import-publish` in light and then in dark. With T06.4,
the second run is a duplicate, so the group becomes:

| Run | Mode | Checks |
| --- | --- | --- |
| Light | `import-publish` | Unchanged: the first import of `txt-legacy` |
| Dark | `import-duplicate-copy` | Pick `txt-legacy` and choose CP437. The `ImportDuplicate` `InfoBar` is visible, and the primary button's name is "Import another copy". Press it; the new guide is selected and focused. Screenshots `import-duplicate` and `import-copied` |
| Light | `import-duplicate-open` | Pick `txt-legacy` and choose CP437, then invoke `ImportOpenExisting`. The dialog hides, and the reader shows the existing guide's title (`ReaderHeading`). Screenshot `import-open-existing` |

Afterwards, `importState` expects 2 guides, 0 file operations, 0 staging
entries, 2 content directories and 2 CP437 guides. These are the same
totals as T06.3, because one run opens an existing guide instead of
importing.

### Traceability

| Requirement | Evidence |
| --- | --- |
| TR06.3: fingerprints and IDs distinguish an unchanged copy | Validator fingerprint tests; repository lookup tests; the publisher's `Duplicate` and `allowDuplicate` tests; the `import-duplicate-copy` and `import-duplicate-open` smoke runs |
| TR06.1: the original is never written | The fingerprint check test, plus the T06.3 source-fingerprint assertions, which still pass |
| TR06.2: nothing is published after a failed check | The `Duplicate` test leaves no row or file |

## Documentation

When T06.4 is implemented, update these:

- this status line and a verification record;
- the T06.4 paragraph in [implementation-plan.md](implementation-plan.md);
- a T06.4 row in [progress.md](../progress.md), with the T06.3 row updated to
  "Merged through PR #19, merge commit `494cb02`";
- the `Import publication` row of [e2e-testing.md](e2e-testing.md), and a
  new `Duplicate import` row;
- the T06.3 design's Duplicates bullet, which should point here.

## PR outcome

- **Target task:** T06.4.
- **Prerequisite:** T06.3 (PR #19), merged.
- **Outcome:** importing a file that's already in the game offers **Open
  existing** or **Import another copy**. The PR includes light and dark
  screenshots of the duplicate preview and of each outcome.

## T06.4 verification record

- **Unit tests.** On `pcsx2-win`, Infrastructure 349/349 and Core 198/198
  passed. The new tests are:
  - `GuideFingerprintTests`: the stream hash across buffers and on
    cancellation;
  - `GuideImportValidatorTests`: the UTF-8, resolved TXT and PDF manifest
    fingerprints, and the typed issues staying distinct;
  - `ImportJournalTests`: the fingerprint lookup's same-game match, the
    other game, format and hash cases, the oldest match, and the
    journal's `FindGuide`;
  - `GuideImportPublisherTests`: `Duplicate`, the ignored encoding,
    `allowDuplicate`, another game, and the same-length rewrite for TXT
    and PDF.
- **Installed.** CI run [36675695341](https://github.com/ilya-slalom/desktop-guides/actions/runs/36675695341)
  passed `production-shell-ui`. In the import group:
  - the light `import-publish` run imported `txt-legacy`;
  - the dark `import-duplicate-copy` run showed the duplicate InfoBar
    naming that guide and imported a copy through **Import another copy**;
  - the light `import-duplicate-open` run opened the first guide through
    **Open existing**.

  The final state was 2 guides, 0 file operations, 0 staging entries, 2
  content directories and 2 CP437 guides.
- **Rulings.** The 11 rulings in the [plan](t06-4-duplicate-import-plan.md#rulings-against-the-spec),
  plus one made during implementation: the cancellation test's stream
  overrides only `Read(byte[], int, int)`, because a derived `MemoryStream`
  routes its span `Read` through that overload and overriding both
  recursed.
- **Evidence.**
  - [Duplicate preview, light](evidence/t06-4-duplicate-import/duplicate-light.png)
  - [Duplicate preview, dark](evidence/t06-4-duplicate-import/duplicate-dark.png)
  - [Copy imported, dark](evidence/t06-4-duplicate-import/copied-dark.png)
  - [Open existing, light](evidence/t06-4-duplicate-import/open-existing-light.png)
