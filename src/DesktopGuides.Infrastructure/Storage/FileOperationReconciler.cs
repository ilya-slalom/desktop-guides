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
        HashSet<string> committedGuides = ReadGuideNames(connection);
        IReadOnlyList<JournalRow> rows = ReadJournalRows(connection);
        List<OperationPlan> plans = Preflight(rows, committedGuides);

        foreach (OperationPlan plan in plans)
        {
            Resolve(plan);
            RemoveJournalRow(connection, plan.Row.Id);
        }

        return new StartupReconciliationReport(
            plans.Count, CountReviewOrphans(committedGuides));
    }

    private List<OperationPlan> Preflight(
        IReadOnlyList<JournalRow> rows, HashSet<string> committedGuides)
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
                bool hasGuide = committedGuides.Contains(guideId.ToString("N"));
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
            plans.Add(new OperationPlan(row, guides, operationRoot!));
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

    private static HashSet<string> ReadGuideNames(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM Guides";
        using SqliteDataReader reader = command.ExecuteReader();
        HashSet<string> ids = new(StringComparer.Ordinal);
        while (reader.Read())
        {
            ids.Add(reader.GetString(0));
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

        if (Directory.Exists(plan.SideOperationRoot) &&
            !Directory.EnumerateFileSystemEntries(plan.SideOperationRoot).Any())
        {
            Directory.Delete(plan.SideOperationRoot);
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

    private int CountReviewOrphans(HashSet<string> committedGuides)
    {
        int content = Directory.EnumerateFileSystemEntries(paths.ContentRoot)
            .Count(path => !committedGuides.Contains(Path.GetFileName(path)));
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
