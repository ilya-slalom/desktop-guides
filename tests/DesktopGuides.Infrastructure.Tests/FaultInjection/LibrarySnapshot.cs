using System.Globalization;
using System.Security.Cryptography;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.FaultInjection;

/// <summary>
/// Every row of the library tables and every entry under the data root,
/// keyed so two captures can be diffed. Links are recorded, not followed.
/// </summary>
internal sealed class LibrarySnapshot
{
    private static readonly (string Table, string[] Key)[] Tables =
    [
        ("Games", ["Id"]),
        ("Guides", ["Id"]),
        ("GuideAssets", ["GuideId", "RequestPath"]),
        ("ReadingStates", ["GuideId"]),
        ("ReaderPreferences", ["GuideId"]),
        ("Settings", ["Key"]),
        ("FileOperations", ["Id"]),
    ];

    private LibrarySnapshot(IReadOnlyDictionary<string, string> entries) => Entries = entries;

    public IReadOnlyDictionary<string, string> Entries { get; }

    public static LibrarySnapshot Capture(ManagedPathResolver paths)
    {
        SortedDictionary<string, string> entries = new(StringComparer.Ordinal);
        using (SqliteConnection connection = new($"Data Source={paths.DatabasePath};Pooling=False"))
        {
            connection.Open();
            foreach ((string table, string[] key) in Tables)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT * FROM {table}";
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string id = string.Join("/", key.Select(column => Convert.ToString(reader[column], CultureInfo.InvariantCulture)));
                    entries[$"db:{table}/{id}"] = string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(index =>
                        $"{reader.GetName(index)}={(reader.IsDBNull(index) ? "null" : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture))}"));
                }
            }
        }
        // The \\?\ prefix lets reserved names such as CON be listed and read.
        string prefix = OperatingSystem.IsWindows() ? @"\\?\" : "";
        string root = prefix + Path.GetFullPath(paths.DataRoot);
        Walk(root, root, prefix + Path.GetFullPath(paths.DatabasePath), entries);
        return new LibrarySnapshot(entries);
    }

    public IEnumerable<string> KeysContaining(Guid id) =>
        Entries.Keys.Where(key => key.Contains(id.ToString("N"), StringComparison.Ordinal));

    public static SnapshotDiff Diff(LibrarySnapshot before, LibrarySnapshot after) => new(
        after.Entries.Keys.Except(before.Entries.Keys).Order(StringComparer.Ordinal).ToArray(),
        before.Entries.Keys.Except(after.Entries.Keys).Order(StringComparer.Ordinal).ToArray(),
        before.Entries.Keys.Intersect(after.Entries.Keys)
            .Where(key => before.Entries[key] != after.Entries[key]).Order(StringComparer.Ordinal).ToArray());

    private static void Walk(string root, string directory, string database, SortedDictionary<string, string> entries)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            if (path.StartsWith(database, StringComparison.OrdinalIgnoreCase))
            {
                continue; // library.sqlite and its sidecars are compared as rows.
            }
            // Both carry the same device prefix, so the relative part is a plain suffix.
            string key = "fs:" + path[(root.Length + 1)..].Replace('\\', '/');
            FileAttributes attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                entries[key] = "link";
            }
            else if (attributes.HasFlag(FileAttributes.Directory))
            {
                entries[key] = "dir";
                Walk(root, path, database, entries);
            }
            else
            {
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                entries[key] = $"file {stream.Length} {Convert.ToHexStringLower(SHA256.HashData(stream))}";
            }
        }
    }
}

internal sealed record SnapshotDiff(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, IReadOnlyList<string> Changed)
{
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;

    public override string ToString() =>
        $"added: [{string.Join(", ", Added)}]\nremoved: [{string.Join(", ", Removed)}]\nchanged: [{string.Join(", ", Changed)}]";
}

internal static class SnapshotAssert
{
    public static void Unchanged(LibrarySnapshot before, LibrarySnapshot after)
    {
        SnapshotDiff diff = LibrarySnapshot.Diff(before, after);
        Assert.True(diff.IsEmpty, "Expected no change, got\n" + diff);
    }

    public static void Exactly(
        SnapshotDiff actual, IEnumerable<string> added, IEnumerable<string> removed, IEnumerable<string>? changed = null)
    {
        SnapshotDiff expected = new(
            added.Order(StringComparer.Ordinal).ToArray(),
            removed.Order(StringComparer.Ordinal).ToArray(),
            (changed ?? []).Order(StringComparer.Ordinal).ToArray());
        bool same = expected.Added.SequenceEqual(actual.Added) &&
                    expected.Removed.SequenceEqual(actual.Removed) &&
                    expected.Changed.SequenceEqual(actual.Changed);
        Assert.True(same, $"Expected\n{expected}\nGot\n{actual}");
    }
}
