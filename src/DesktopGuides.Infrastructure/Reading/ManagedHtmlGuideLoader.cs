using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;

namespace DesktopGuides.Infrastructure.Reading;

public abstract record HtmlGuideLoad;
public sealed record HtmlGuideLoaded(HtmlRequestPolicy Policy, ManagedHtmlAssetReader Reader, string EntryFilePath) : HtmlGuideLoad;
public sealed record HtmlGuideLoadFailed(HtmlGuideLoadError Error) : HtmlGuideLoad;

public sealed class ManagedHtmlGuideLoader(SqliteLibraryRepository repository, ManagedPathResolver paths)
{
    public async Task<HtmlGuideLoad> LoadAsync(Guide guide, CancellationToken token)
    {
        IReadOnlyList<GuideAsset> assets = await repository.GetGuideAssetsAsync(guide.Id, token);
        if (assets.Count == 0) return new HtmlGuideLoadFailed(HtmlGuideLoadError.NoManifest);
        GuideAsset[] entries = [.. assets.Where(asset => asset.Kind == GuideAssetKind.EntryHtml)];
        if (entries.Length != 1 ||
            !string.Equals(entries[0].RelativePath, guide.PrimaryRelativePath, StringComparison.Ordinal))
        {
            return new HtmlGuideLoadFailed(HtmlGuideLoadError.Changed);
        }
        // The reader reports links and access errors as missing too, so
        // look at the planned path first: only an absent file is Missing.
        try
        {
            string planned = paths.GetPlannedGuideFile(guide.Id, entries[0].RelativePath);
            if (!File.Exists(planned) && !Directory.Exists(planned))
            {
                return new HtmlGuideLoadFailed(HtmlGuideLoadError.Missing);
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                          UnauthorizedAccessException or ArgumentException)
        {
            return new HtmlGuideLoadFailed(HtmlGuideLoadError.Changed);
        }
        ManagedHtmlAssetReader reader = new(paths, guide.Id);
        HtmlAssetRead entry = await Task.Run(() => reader.Read(entries[0]), token);
        if (entry.Status != HtmlAssetReadStatus.Served) return new HtmlGuideLoadFailed(HtmlGuideLoadError.Changed);
        string entryFile;
        try
        {
            entryFile = paths.ResolveExistingGuideFile(guide.Id, entries[0].RelativePath);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return new HtmlGuideLoadFailed(HtmlGuideLoadError.Missing);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                          UnauthorizedAccessException or ArgumentException)
        {
            return new HtmlGuideLoadFailed(HtmlGuideLoadError.Changed);
        }
        return new HtmlGuideLoaded(new HtmlRequestPolicy(guide.Id, assets), reader, entryFile);
    }
}
