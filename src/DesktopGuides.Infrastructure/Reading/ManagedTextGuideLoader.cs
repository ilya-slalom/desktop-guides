using DesktopGuides.Core.Library;
using DesktopGuides.Core.Text;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Storage;

namespace DesktopGuides.Infrastructure.Reading;

/// <summary>Reads a TXT guide's managed copy and decodes it by its stored encoding.
/// Opens only the managed copy and writes nothing.</summary>
public sealed class ManagedTextGuideLoader
{
    private readonly ManagedPathResolver paths;
    private readonly long maxBytes = new GuideImportLimits().MaxTxtBytes;

    public ManagedTextGuideLoader(ManagedPathResolver paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        this.paths = paths;
    }

    public Task<TextGuideLoad> LoadAsync(Guide guide, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(guide);
        return Task.Run(() => Load(guide, token), token);
    }

    private TextGuideLoad Load(Guide guide, CancellationToken token)
    {
        if (!ManagedTextDecoder.HasValidMetadata(guide))
        {
            return Failed(TextGuideLoadError.InvalidMetadata);
        }

        byte[] bytes;
        try
        {
            string path = paths.ResolveExistingGuideFile(guide.Id, guide.PrimaryRelativePath);
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > maxBytes)
            {
                return Failed(TextGuideLoadError.TooLarge);
            }
            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException or InvalidDataException)
        {
            // Not where the app owns it: gone, outside the guide root, or behind a link.
            return Failed(TextGuideLoadError.Missing);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failed(TextGuideLoadError.Unreadable);
        }

        token.ThrowIfCancellationRequested();
        return ManagedTextDecoder.Decode(bytes, guide);
    }

    private static TextGuideLoadFailed Failed(TextGuideLoadError error) => new(error);
}
