using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Storage;

namespace DesktopGuides.Infrastructure.Online;

public abstract record OnlineSaveResult
{
    public sealed record Added(Guid GuideId) : OnlineSaveResult;
    public sealed record Replaced(Guid GuideId) : OnlineSaveResult;
    public sealed record UpToDate(Guid GuideId) : OnlineSaveResult;

    /// <summary>The saved copy is in another format; saving again needs allowFormatChange.</summary>
    public sealed record NeedsFormatChange(Guid GuideId, GuideFormat Saved, GuideFormat New) : OnlineSaveResult;
}

/// <summary>
/// S22: saves a capture to its game. The offline copy is built in a work
/// folder, checked by the guide import, then published as a new guide or as
/// the replacement of the one saved from the same site item. The draft is
/// kept until the save succeeds, and the work folder never outlives a save.
/// </summary>
public sealed class OnlineGuideSaver(
    SqliteLibraryRepository repository, GuideImportPublisher publisher, GuideImportValidator validator,
    string workRoot, TimeProvider clock)
{
    public Task<OnlineSaveResult> SaveAsync(
        CaptureDraft draft, bool allowFormatChange, IProgress<ImportProgress>? progress, CancellationToken token) =>
        SaveCoreAsync(draft, allowFormatChange, asNew: false, progress, token);

    /// <summary>Saves the capture as another guide, leaving the one saved before ("Keep both").</summary>
    public Task<OnlineSaveResult> SaveAsNewAsync(
        CaptureDraft draft, IProgress<ImportProgress>? progress, CancellationToken token) =>
        SaveCoreAsync(draft, allowFormatChange: false, asNew: true, progress, token);

    private async Task<OnlineSaveResult> SaveCoreAsync(
        CaptureDraft draft, bool allowFormatChange, bool asNew, IProgress<ImportProgress>? progress, CancellationToken token)
    {
        IReadOnlyList<CapturedPage> captured = draft.Captured;
        if (captured.Count == 0) throw new InvalidOperationException("Nothing has been captured.");
        DateTimeOffset now = clock.GetUtcNow();
        string work = Path.Combine(workRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            OfflineCopy copy = OfflineCopyBuilder.Build(captured, work, now);
            ImportManifest manifest = await validator.InspectAsync(copy.EntryPath, token) switch
            {
                ImportReady ready => ready.Manifest,
                _ => throw new GuideImportException(ImportIssue.Unreadable, "The saved copy couldn't be read as a guide."),
            };
            GuidePageReading first = captured[0].Reading;
            GuideSource source = new(
                draft.Item.Site, draft.Item.ItemId, draft.Pages[0].Url.AbsoluteUri,
                Cut(first.Author, 200), captured.Select(page => page.Reading.Notice).FirstOrDefault(notice => notice is not null),
                Cut(first.Title, 512), now, first.SourceUpdated,
                draft.IsComplete ? null : captured.Count, draft.IsComplete ? null : draft.TotalPages);

            OnlineSaveResult result;
            if (!asNew &&
                await repository.FindSourcedGuideAsync(draft.GameId, draft.Item.Site, draft.Item.ItemId, token) is { } saved)
            {
                try
                {
                    result = await publisher.ReplaceAsync(manifest, saved.Id, source, allowFormatChange, progress, token) switch
                    {
                        GuideReplaceOutcome.UpToDate => new OnlineSaveResult.UpToDate(saved.Id),
                        _ => new OnlineSaveResult.Replaced(saved.Id),
                    };
                }
                catch (GuideImportException error) when (error.Issue == ImportIssue.FormatChanged)
                {
                    return new OnlineSaveResult.NeedsFormatChange(saved.Id, saved.Format, manifest.Format);
                }
            }
            else
            {
                // The source tells this guide apart from an imported file with the same bytes.
                Guid id = await publisher.PublishAsync(
                    manifest, draft.GameId, Cut(first.Title, 200)!, allowDuplicate: true, progress, token, source);
                result = new OnlineSaveResult.Added(id);
            }
            draft.Discard();
            return result;
        }
        finally
        {
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        }
    }

    private static string? Cut(string? text, int length) => text is { Length: > 0 } value && value.Length > length
        ? value[..length].TrimEnd() : text;
}
