using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    private const string ApproximateRestoreMessage =
        "Opened near your last place. The guide changed since you were here.";
    private const string UnavailableRestoreMessage =
        "Couldn't return to your last place, so the guide opened at the start.";

    // Once a session is showing, returns it to the guide's saved place and
    // says so when that place is approximate or lost. Returns false when a
    // newer render took over.
    private async Task<bool> OpenAtSavedPlaceAsync(
        Guide guide, IReaderSession session, int generation, string contentSha256,
        string? htmlEntry, CancellationToken token)
    {
        string? stored;
        RestoreKind? kind = null;
        try
        {
            stored = RestoreLocatorForTest() ??
                (await repository!.GetReadingStateAsync(guide.Id, token))?.LocatorJson;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception)
        {
            // An unreadable reading state is a lost place, not a failed open.
            stored = null;
            kind = RestoreKind.Unavailable;
        }
        if (generation != renderGeneration)
        {
            return false;
        }
        if (stored is not null)
        {
            // Stored and override locators are untrusted; only the codec's
            // result reaches the session.
            LocationDecodeResult decoded = ReaderLocationCodec.Deserialize(
                stored, guide.Format, contentSha256, htmlEntry);
            kind = RestoreKind.Unavailable;
            if ((decoded.Status is LocationDecodeStatus.Valid or LocationDecodeStatus.ContentChanged) &&
                decoded.Location is ReaderLocation location)
            {
                try
                {
                    kind = (await session.RestoreLocationAsync(location, token)).Kind;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return false;
                }
                catch (Exception)
                {
                    kind = RestoreKind.Unavailable;
                }
                if (generation != renderGeneration)
                {
                    return false;
                }
            }
        }
        ShowRestoreStatus(kind);
        return true;
    }

    private void ShowRestoreStatus(RestoreKind? kind)
    {
        if (kind == RestoreKind.Approximate)
        {
            ShowStatus(ApproximateRestoreMessage, InfoBarSeverity.Informational, true, false);
        }
        else if (kind == RestoreKind.Unavailable)
        {
            ShowWarningStatus(UnavailableRestoreMessage);
        }
        else
        {
            ShowTransientStatus("Guide ready.");
        }
    }

    // Test gate only: replaces the stored locator. The file is untrusted and
    // goes through the codec like a stored locator.
    private string? RestoreLocatorForTest()
    {
        if (dataRoot is null ||
            !TestGate.IsOpen($@"Local\DesktopGuides.Preview.ProgressOverride.{Environment.ProcessId}"))
        {
            return null;
        }
        string path = Path.Combine(dataRoot, "test", "restore-locator.json");
        try
        {
            FileInfo file = new(path);
            if (!file.Exists) return null;
            // Longer than any locator: decodes as Invalid.
            if (file.Length > ReaderLocationCodec.MaxBytes) return string.Empty;
            return File.ReadAllText(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
