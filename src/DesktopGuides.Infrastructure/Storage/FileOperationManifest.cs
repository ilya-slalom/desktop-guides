using System.Text;
using System.Text.Json;
using DesktopGuides.Core.Paths;

namespace DesktopGuides.Infrastructure.Storage;

internal enum FileOperationKind
{
    Import,
    DeleteGuide,
    DeleteGame
}

internal enum FileOperationPhase
{
    Prepared,
    Committed
}

internal sealed record FileOperationManifest(IReadOnlyList<Guid> GuideIds)
{
    private const int MaxGuideCount = 10_000;
    private const int MaxManifestBytes = 1_048_576;

    public static string Create(
        FileOperationKind kind, Guid operationId, IEnumerable<Guid> guideIds)
    {
        Guid[] ids = NormalizeGuideIds(kind, guideIds.ToArray());
        string[] names = ids.Select(id => id.ToString("N")).ToArray();
        string json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            guideIds = names,
            ownedPaths = ExpectedPaths(kind, operationId, names)
        });
        if (Encoding.UTF8.GetByteCount(json) > MaxManifestBytes)
        {
            throw new InvalidDataException("File-operation manifest is too large.");
        }
        return json;
    }

    public static FileOperationManifest Parse(
        string json, FileOperationKind kind, Guid operationId)
    {
        if (string.IsNullOrWhiteSpace(json) ||
            Encoding.UTF8.GetByteCount(json) > MaxManifestBytes)
        {
            throw new InvalidDataException("File-operation manifest is missing or too large.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("File-operation manifest is invalid JSON.", error);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("File-operation manifest must be an object.");
            }
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!names.Add(property.Name) ||
                    property.Name is not ("schemaVersion" or "guideIds" or "ownedPaths"))
                {
                    throw new InvalidDataException("File-operation manifest has unknown fields.");
                }
            }
            if (names.Count != 3 ||
                root.GetProperty("schemaVersion").ValueKind != JsonValueKind.Number ||
                !root.GetProperty("schemaVersion").TryGetInt32(out int version) ||
                version != 1)
            {
                throw new InvalidDataException("File-operation manifest version is unsupported.");
            }

            JsonElement guideArray = root.GetProperty("guideIds");
            JsonElement pathArray = root.GetProperty("ownedPaths");
            if (guideArray.ValueKind != JsonValueKind.Array ||
                pathArray.ValueKind != JsonValueKind.Array ||
                guideArray.GetArrayLength() is < 1 or > MaxGuideCount ||
                pathArray.GetArrayLength() != guideArray.GetArrayLength() * 2)
            {
                throw new InvalidDataException("File-operation manifest has invalid path counts.");
            }

            List<Guid> guideIds = [];
            List<string> guideNames = [];
            foreach (JsonElement item in guideArray.EnumerateArray())
            {
                string? text = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (text is null ||
                    !Guid.TryParseExact(text, "N", out Guid id) ||
                    id == Guid.Empty ||
                    text != id.ToString("N"))
                {
                    throw new InvalidDataException("File-operation manifest has an invalid guide ID.");
                }
                guideIds.Add(id);
                guideNames.Add(text);
            }
            Guid[] canonical = NormalizeGuideIds(kind, guideIds.ToArray());
            if (!canonical.SequenceEqual(guideIds))
            {
                throw new InvalidDataException("File-operation guide IDs are not canonical.");
            }

            string[] expectedPaths = ExpectedPaths(kind, operationId, guideNames);
            int index = 0;
            foreach (JsonElement item in pathArray.EnumerateArray())
            {
                string? path = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (path is null || ManagedRelativePath.Parse(path) != expectedPaths[index++])
                {
                    throw new InvalidDataException("File-operation path is not owned by its IDs.");
                }
            }
            return new FileOperationManifest(canonical);
        }
    }

    private static Guid[] NormalizeGuideIds(FileOperationKind kind, Guid[] guideIds)
    {
        if (!Enum.IsDefined(kind) ||
            guideIds.Length is < 1 or > MaxGuideCount ||
            guideIds.Any(id => id == Guid.Empty) ||
            (kind != FileOperationKind.DeleteGame && guideIds.Length != 1))
        {
            throw new InvalidDataException("File-operation guide count is invalid.");
        }
        Guid[] sorted = guideIds.OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToArray();
        if (sorted.Distinct().Count() != sorted.Length)
        {
            throw new InvalidDataException("File-operation guide IDs must be unique.");
        }
        return sorted;
    }

    private static string[] ExpectedPaths(
        FileOperationKind kind, Guid operationId, IEnumerable<string> guideNames)
    {
        if (operationId == Guid.Empty)
        {
            throw new InvalidDataException("File-operation ID is invalid.");
        }
        return guideNames.SelectMany(guide => kind == FileOperationKind.Import
                ? new[] { $".staging/{operationId:N}/{guide}", $"content/{guide}" }
                : new[] { $"content/{guide}", $".trash/{operationId:N}/{guide}" })
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }
}
