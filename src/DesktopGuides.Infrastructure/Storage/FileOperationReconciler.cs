using DesktopGuides.Core.Library;
using DesktopGuides.Core.Paths;
using Microsoft.Data.Sqlite;

namespace DesktopGuides.Infrastructure.Storage;

internal sealed class FileOperationReconciler(ILibraryPaths paths)
{
    private sealed record JournalRow(
        Guid Id, FileOperationKind Kind, FileOperationPhase Phase,
        FileOperationManifest Manifest);

    private sealed record GuidePlan(
        string ContentPath, OwnedGuideTree Content,
        string SidePath, OwnedGuideTree Side);

    private sealed record OperationPlan(
        JournalRow Row, IReadOnlyList<GuidePlan> Guides, string SideOperationRoot);

    public StartupReconciliationReport Run(SqliteConnection connection)
    {
        HashSet<Guid> committedGuides = ReadGuideIds(connection);
        IReadOnlyList<JournalRow> rows = ReadJournalRows(connection);
        List<OperationPlan> plans = Preflight(rows, committedGuides);
        // S22: a replacement's guide row exists in both phases; checked before anything moves.
        JournalRow[] replacements = rows.Where(row => row.Kind == FileOperationKind.ReplaceGuide).ToArray();
        foreach (JournalRow row in replacements)
        {
            ReplacementPaths(connection, row);
        }

        foreach (OperationPlan plan in plans)
        {
            Resolve(plan);
            RemoveJournalRow(connection, plan.Row.Id);
        }
        foreach (JournalRow row in replacements)
        {
            if (row.Phase == FileOperationPhase.Prepared)
            {
                UndoReplacement(connection, row);
            }
            else
            {
                FinishReplacement(connection, row);
            }
        }

        return new StartupReconciliationReport(
            plans.Count + replacements.Length, CountReviewOrphans(committedGuides));
    }

    private List<OperationPlan> Preflight(
        IReadOnlyList<JournalRow> rows, HashSet<Guid> committedGuides)
    {
        HashSet<Guid> claimedGuides = [];
        List<OperationPlan> plans = [];
        foreach (JournalRow row in rows)
        {
            List<GuidePlan> guides = [];
            string? operationRoot = null;
            foreach (Guid guideId in row.Manifest.GuideIds)
            {
                if (!claimedGuides.Add(guideId))
                {
                    throw new InvalidDataException("File operations claim the same guide.");
                }
                if (row.Kind == FileOperationKind.ReplaceGuide)
                {
                    continue;
                }
                bool hasGuide = committedGuides.Contains(guideId);
                bool shouldHaveGuide =
                    row.Kind != FileOperationKind.Import &&
                    row.Phase == FileOperationPhase.Prepared;
                if (hasGuide != shouldHaveGuide)
                {
                    throw new InvalidDataException(
                        "File-operation phase conflicts with committed guide metadata.");
                }

                string contentPath = paths.GetGuideRoot(guideId);
                string sidePath = row.Kind == FileOperationKind.Import
                    ? paths.GetStagedGuideRoot(row.Id, guideId)
                    : paths.GetTrashedGuideRoot(row.Id, guideId);
                operationRoot ??= Path.GetDirectoryName(sidePath)!;
                RequireDirectoryOrMissing(operationRoot);
                OwnedGuideTree content = OwnedGuideTree.Capture(contentPath);
                OwnedGuideTree side = OwnedGuideTree.Capture(sidePath);
                if (row.Kind == FileOperationKind.Import &&
                    content.Exists && side.Exists)
                {
                    throw new InvalidDataException(
                        "Prepared import has conflicting staging and content directories.");
                }
                if (row.Kind != FileOperationKind.Import &&
                    row.Phase == FileOperationPhase.Prepared &&
                    content.Exists && side.Exists)
                {
                    throw new InvalidDataException(
                        "Prepared deletion has conflicting content and trash directories.");
                }
                if (row.Kind != FileOperationKind.Import &&
                    row.Phase == FileOperationPhase.Committed &&
                    content.Exists)
                {
                    throw new InvalidDataException(
                        "Committed deletion still has a content directory.");
                }
                guides.Add(new GuidePlan(contentPath, content, sidePath, side));
            }
            if (row.Kind != FileOperationKind.ReplaceGuide)
            {
                plans.Add(new OperationPlan(row, guides, operationRoot!));
            }
        }
        return plans;
    }

    private static IReadOnlyList<JournalRow> ReadJournalRows(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Kind, Phase, ManifestJson
            FROM FileOperations ORDER BY CreatedUtcMs, Id
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        List<JournalRow> rows = [];
        while (reader.Read())
        {
            string idText = reader.GetString(0);
            string kindText = reader.GetString(1);
            string phaseText = reader.GetString(2);
            if (!Guid.TryParseExact(idText, "N", out Guid id) ||
                id == Guid.Empty || idText != id.ToString("N") ||
                !Enum.TryParse(kindText, out FileOperationKind kind) ||
                !Enum.IsDefined(kind) || kind.ToString() != kindText ||
                !Enum.TryParse(phaseText, out FileOperationPhase phase) ||
                !Enum.IsDefined(phase) || phase.ToString() != phaseText ||
                (kind == FileOperationKind.Import &&
                 phase != FileOperationPhase.Prepared))
            {
                throw new InvalidDataException("File-operation row is invalid.");
            }
            FileOperationManifest manifest =
                FileOperationManifest.Parse(reader.GetString(3), kind, id);
            rows.Add(new JournalRow(id, kind, phase, manifest));
        }
        return rows;
    }

    private static HashSet<Guid> ReadGuideIds(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM Guides";
        using SqliteDataReader reader = command.ExecuteReader();
        HashSet<Guid> ids = [];
        while (reader.Read())
        {
            if (!Guid.TryParseExact(reader.GetString(0), "N", out Guid id) ||
                id == Guid.Empty || !ids.Add(id))
            {
                throw new InvalidDataException("Guide ID is invalid or duplicated.");
            }
        }
        return ids;
    }

    private static void Resolve(OperationPlan plan)
    {
        if (plan.Row.Kind == FileOperationKind.Import)
        {
            foreach (GuidePlan guide in plan.Guides)
            {
                guide.Side.Delete();
                guide.Content.Delete();
            }
        }
        else if (plan.Row.Phase == FileOperationPhase.Prepared)
        {
            foreach (GuidePlan guide in plan.Guides)
            {
                if (guide.Side.Exists)
                {
                    Directory.Move(guide.SidePath, guide.ContentPath);
                }
            }
        }
        else
        {
            foreach (GuidePlan guide in plan.Guides)
            {
                guide.Side.Delete();
            }
        }

        RemoveIfEmpty(plan.SideOperationRoot);
    }

    /// <summary>
    /// Rolls back one in-process import. The rename is atomic, so while the
    /// stage exists, content/&lt;guide&gt; isn't this import's and is kept.
    /// </summary>
    public void RollBackImport(SqliteConnection connection, Guid operationId)
    {
        JournalRow row = ReadJournalRows(connection).SingleOrDefault(candidate => candidate.Id == operationId)
            ?? throw new InvalidDataException("The import is not in the file-operation journal.");
        if (row.Kind != FileOperationKind.Import)
        {
            throw new InvalidDataException("The file operation is not an import.");
        }
        HashSet<Guid> committedGuides = ReadGuideIds(connection);
        string? operationRoot = null;
        foreach (Guid guideId in row.Manifest.GuideIds)
        {
            if (committedGuides.Contains(guideId))
            {
                throw new InvalidDataException(
                    "File-operation phase conflicts with committed guide metadata.");
            }
            string stagedPath = paths.GetStagedGuideRoot(row.Id, guideId);
            operationRoot ??= Path.GetDirectoryName(stagedPath)!;
            RequireDirectoryOrMissing(operationRoot);
            OwnedGuideTree staged = OwnedGuideTree.Capture(stagedPath);
            (staged.Exists ? staged : OwnedGuideTree.Capture(paths.GetGuideRoot(guideId))).Delete();
        }
        RemoveIfEmpty(operationRoot);
        RemoveJournalRow(connection, row.Id);
    }

    /// <summary>
    /// Rolls back one in-process deletion that hasn't committed: every trashed
    /// guide goes back to content, under the same rules as startup recovery.
    /// </summary>
    public void RollBackDeletion(SqliteConnection connection, Guid operationId)
    {
        JournalRow row = ReadDeletion(connection, operationId, FileOperationPhase.Prepared);
        HashSet<Guid> committedGuides = ReadGuideIds(connection);
        string? operationRoot = null;
        foreach (Guid guideId in row.Manifest.GuideIds)
        {
            if (!committedGuides.Contains(guideId))
            {
                throw new InvalidDataException(
                    "File-operation phase conflicts with committed guide metadata.");
            }
            string contentPath = paths.GetGuideRoot(guideId);
            string trashPath = paths.GetTrashedGuideRoot(row.Id, guideId);
            operationRoot ??= Path.GetDirectoryName(trashPath)!;
            RequireDirectoryOrMissing(operationRoot);
            OwnedGuideTree content = OwnedGuideTree.Capture(contentPath);
            OwnedGuideTree trash = OwnedGuideTree.Capture(trashPath);
            if (content.Exists && trash.Exists)
            {
                throw new InvalidDataException(
                    "Prepared deletion has conflicting content and trash directories.");
            }
            if (trash.Exists)
            {
                Directory.Move(trashPath, contentPath);
            }
        }
        RemoveIfEmpty(operationRoot);
        RemoveJournalRow(connection, row.Id);
    }

    /// <summary>Deletes a committed deletion's trash, then its journal row.</summary>
    public void FinishDeletion(SqliteConnection connection, Guid operationId)
    {
        JournalRow row = ReadDeletion(connection, operationId, FileOperationPhase.Committed);
        HashSet<Guid> committedGuides = ReadGuideIds(connection);
        string? operationRoot = null;
        foreach (Guid guideId in row.Manifest.GuideIds)
        {
            if (committedGuides.Contains(guideId))
            {
                throw new InvalidDataException(
                    "File-operation phase conflicts with committed guide metadata.");
            }
            string trashPath = paths.GetTrashedGuideRoot(row.Id, guideId);
            operationRoot ??= Path.GetDirectoryName(trashPath)!;
            RequireDirectoryOrMissing(operationRoot);
            if (OwnedGuideTree.Capture(paths.GetGuideRoot(guideId)).Exists)
            {
                throw new InvalidDataException("Committed deletion still has a content directory.");
            }
            OwnedGuideTree.Capture(trashPath).Delete();
        }
        RemoveIfEmpty(operationRoot);
        RemoveJournalRow(connection, row.Id);
    }

    /// <summary>
    /// S22: undoes a replacement that hasn't committed. While the old copy
    /// is in the trash, content/&lt;guide&gt; is the new copy or nothing, so it
    /// is deleted and the old copy moves back; the stage is deleted.
    /// </summary>
    public void RollBackReplace(SqliteConnection connection, Guid operationId) =>
        UndoReplacement(connection, ReadReplacement(connection, operationId, FileOperationPhase.Prepared));

    /// <summary>S22: deletes a committed replacement's old copy and stage, then its row.</summary>
    public void FinishReplace(SqliteConnection connection, Guid operationId) =>
        FinishReplacement(connection, ReadReplacement(connection, operationId, FileOperationPhase.Committed));

    private void UndoReplacement(SqliteConnection connection, JournalRow row)
    {
        (Guid guideId, string staged, string trash) = ReplacementPaths(connection, row);
        string content = paths.GetGuideRoot(guideId);
        if (OwnedGuideTree.Capture(trash).Exists)
        {
            OwnedGuideTree.Capture(content).Delete();
            Directory.Move(trash, content);
        }
        OwnedGuideTree.Capture(staged).Delete();
        RemoveIfEmpty(Path.GetDirectoryName(staged));
        RemoveIfEmpty(Path.GetDirectoryName(trash));
        RemoveJournalRow(connection, row.Id);
    }

    private void FinishReplacement(SqliteConnection connection, JournalRow row)
    {
        (_, string staged, string trash) = ReplacementPaths(connection, row);
        OwnedGuideTree.Capture(trash).Delete();
        OwnedGuideTree.Capture(staged).Delete();
        RemoveIfEmpty(Path.GetDirectoryName(staged));
        RemoveIfEmpty(Path.GetDirectoryName(trash));
        RemoveJournalRow(connection, row.Id);
    }

    // A replacement's guide row exists in both phases.
    private (Guid GuideId, string Staged, string Trash) ReplacementPaths(SqliteConnection connection, JournalRow row)
    {
        Guid guideId = row.Manifest.GuideIds.Single();
        if (!ReadGuideIds(connection).Contains(guideId))
        {
            throw new InvalidDataException("File-operation phase conflicts with committed guide metadata.");
        }
        string staged = paths.GetStagedGuideRoot(row.Id, guideId);
        string trash = paths.GetTrashedGuideRoot(row.Id, guideId);
        RequireDirectoryOrMissing(Path.GetDirectoryName(staged)!);
        RequireDirectoryOrMissing(Path.GetDirectoryName(trash)!);
        return (guideId, staged, trash);
    }

    public static bool ClaimsGuide(SqliteConnection connection, Guid guideId) =>
        ReadJournalRows(connection).Any(row => row.Manifest.GuideIds.Contains(guideId));

    private static JournalRow ReadDeletion(
        SqliteConnection connection, Guid operationId, FileOperationPhase phase)
    {
        JournalRow row = ReadJournalRows(connection).SingleOrDefault(candidate => candidate.Id == operationId)
            ?? throw new InvalidDataException("The deletion is not in the file-operation journal.");
        if (row.Kind is not (FileOperationKind.DeleteGuide or FileOperationKind.DeleteGame) || row.Phase != phase)
        {
            throw new InvalidDataException("The file operation is not a deletion in the expected phase.");
        }
        return row;
    }

    private static JournalRow ReadReplacement(
        SqliteConnection connection, Guid operationId, FileOperationPhase phase)
    {
        JournalRow row = ReadJournalRows(connection).SingleOrDefault(candidate => candidate.Id == operationId)
            ?? throw new InvalidDataException("The replacement is not in the file-operation journal.");
        if (row.Kind != FileOperationKind.ReplaceGuide || row.Phase != phase)
        {
            throw new InvalidDataException("The file operation is not a replacement in the expected phase.");
        }
        return row;
    }

    private static void RemoveIfEmpty(string? directory)
    {
        if (directory is not null && Directory.Exists(directory) &&
            !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }

    private static void RemoveJournalRow(SqliteConnection connection, Guid operationId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM FileOperations WHERE Id = $id";
        command.Parameters.AddWithValue("$id", operationId.ToString("N"));
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidDataException("File-operation journal changed during recovery.");
        }
    }

    private int CountReviewOrphans(HashSet<Guid> committedGuides)
    {
        int content = Directory.EnumerateFileSystemEntries(paths.ContentRoot)
            .Count(path =>
                !Guid.TryParseExact(Path.GetFileName(path), "N", out Guid id) ||
                !committedGuides.Contains(id));
        int staging = Directory.EnumerateFileSystemEntries(paths.StagingRoot).Count();
        int trash = Directory.EnumerateFileSystemEntries(paths.TrashRoot).Count();
        return content + staging + trash;
    }

    private static void RequireDirectoryOrMissing(string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) !=
            FileAttributes.Directory)
        {
            throw new InvalidDataException("File-operation directory is unsafe.");
        }
    }
}
