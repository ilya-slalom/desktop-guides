using System.Buffers;
using System.Text.Json;
using DesktopGuides.Core.Paths;

namespace DesktopGuides.Infrastructure.Storage;

internal enum RestoreMarkerPhase { Swapping, Confirmed, RollingBack }

/// <summary>
/// restore.marker beside library/: which stage a swap promoted, whether a
/// prior library was parked, and how far the swap got. Startup reads it
/// before the repository opens.
/// </summary>
internal sealed record RestoreMarker(Guid StageId, bool PriorExists, RestoreMarkerPhase Phase)
{
    public const string FileName = "restore.marker";

    public static string PathFor(ILibraryPaths paths) => Path.Combine(paths.DataRoot, FileName);

    public static string PriorRoot(ILibraryPaths paths, Guid stageId) =>
        Path.Combine(paths.RecoveryRoot, $"restore-{stageId:N}", "library");

    public static string StagedLibrary(ILibraryPaths paths, Guid stageId) =>
        Path.Combine(LibraryRestorer.StageRoot(paths, stageId), LibraryRestorer.LibraryFolderName);

    public static string Unverified(ILibraryPaths paths, Guid stageId) =>
        Path.Combine(paths.DataRoot, LibraryRestorer.StagingFolderName, $"{stageId:N}-unverified");

    /// <summary>Writes a temporary file, flushes it, then renames it over the marker.</summary>
    public void Write(ILibraryPaths paths)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("stageId", StageId.ToString("N"));
            json.WriteBoolean("priorExists", PriorExists);
            json.WriteString("phase", Phase.ToString());
            json.WriteEndObject();
        }
        string target = PathFor(paths);
        string temp = $"{target}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (FileStream file = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(buffer.WrittenSpan);
                file.Flush(flushToDisk: true);
            }
            File.Move(temp, target, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    public static RestoreMarker? Read(ILibraryPaths paths)
    {
        string path = PathFor(paths);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = document.RootElement;
            RestoreMarkerPhase phase = Enum.Parse<RestoreMarkerPhase>(root.GetProperty("phase").GetString()!);
            if (!Enum.IsDefined(phase))
            {
                throw new FormatException($"Unknown restore phase {phase}.");
            }
            return new RestoreMarker(
                Guid.ParseExact(root.GetProperty("stageId").GetString()!, "N"),
                root.GetProperty("priorExists").GetBoolean(),
                phase);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or FormatException or
                                      ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("The restore marker is malformed.", error);
        }
    }

    public static void Delete(ILibraryPaths paths) => File.Delete(PathFor(paths));
}
