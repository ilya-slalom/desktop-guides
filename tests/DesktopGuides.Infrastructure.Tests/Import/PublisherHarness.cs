using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Import;

internal sealed class PublisherHarness : IAsyncDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "desktop-guides-publish-" + Guid.NewGuid().ToString("N"));

    private PublisherHarness() => Paths = new ManagedPathResolver(Path.Combine(root, "app-data"));

    public ManagedPathResolver Paths { get; }
    public SqliteLibraryRepository Repository { get; private set; } = null!;
    public Game Game { get; private set; } = null!;
    public ImportTestDirectory Sources { get; } = new();
    public GuideImportValidator Validator { get; } = new(null, new FakeMapDecoder(decodes: true));
    public Guid OperationId { get; private set; }
    public Guid GuideId { get; private set; }

    public static async Task<PublisherHarness> CreateAsync()
    {
        PublisherHarness harness = new();
        harness.Repository = new SqliteLibraryRepository(harness.Paths);
        await harness.Repository.InitializeAsync();
        harness.Game = await harness.Repository.AddGameAsync("Publish Game", "PC", null);
        return harness;
    }

    /// <summary>Opens a fresh repository on the same files, as the next app start does.</summary>
    public async Task RestartAsync()
    {
        await Repository.DisposeAsync();
        Repository = new SqliteLibraryRepository(Paths);
        await Repository.InitializeAsync();
    }

    /// <summary>A publisher whose next import uses fresh, known IDs.</summary>
    public GuideImportPublisher Publisher(
        Action<ImportCheckpoint>? checkpoint = null,
        Func<string, Stream>? createStagedFile = null,
        Action<IImportJournal, Guid>? rollBack = null,
        Action<ReplaceCheckpoint>? replaceCheckpoint = null,
        Action<IImportJournal, Guid>? replaceRollBack = null)
    {
        OperationId = Guid.NewGuid();
        GuideId = Guid.NewGuid();
        Queue<Guid> ids = new([OperationId, GuideId]);
        return new GuideImportPublisher(
            Repository, Paths, null,
            () => ids.Count > 0 ? ids.Dequeue() : Guid.NewGuid(),
            createStagedFile ?? (path => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true)),
            checkpoint ?? (_ => { }),
            rollBack ?? ((journal, operationId) => journal.RollBack(operationId)),
            replaceCheckpoint ?? (_ => { }),
            replaceRollBack ?? ((journal, operationId) => journal.RollBackReplace(operationId)));
    }

    public async Task<ImportManifest> InspectAsync(string path, int? codePage = null, string? password = null)
    {
        ImportInspection inspection = await Validator.InspectAsync(path, CancellationToken.None);
        return inspection switch
        {
            ImportReady ready => ready.Manifest,
            ImportNeedsTxtEncoding needs => await Validator.ResolveTxtEncodingAsync(
                needs, codePage ?? throw new InvalidOperationException("A code page is required."), CancellationToken.None),
            ImportNeedsPdfPassword needs => await Validator.ResolvePdfPasswordAsync(
                needs, password ?? throw new InvalidOperationException("A password is required."), CancellationToken.None),
            _ => throw new InvalidOperationException(),
        };
    }

    public Task<Guid> PublishAsync(
        GuideImportPublisher publisher, ImportManifest manifest,
        IProgress<ImportProgress>? progress = null, CancellationToken token = default,
        bool allowDuplicate = false) =>
        publisher.PublishAsync(manifest, Game.Id, "Imported Guide", allowDuplicate, progress, token);

    public long Count(string table)
    {
        using SqliteConnection connection = new($"Data Source={Paths.DatabasePath};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>Every row the query returns, each as its columns joined by '|', one per line.</summary>
    public string Rows(string sql)
    {
        using SqliteConnection connection = new($"Data Source={Paths.DatabasePath};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        List<string> rows = [];
        while (reader.Read())
        {
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(index =>
                reader.IsDBNull(index) ? "null" : Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture))));
        }
        return string.Join("\n", rows);
    }

    public void Execute(string sql, Guid id)
    {
        using SqliteConnection connection = new($"Data Source={Paths.DatabasePath};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; " + sql;
        command.Parameters.AddWithValue("$id", id.ToString("N"));
        command.ExecuteNonQuery();
    }

    /// <summary>No guide, no operation row, and nothing staged or managed.</summary>
    public void AssertNothingLeft()
    {
        Assert.Equal(0, Count("Guides"));
        Assert.Equal(0, Count("FileOperations"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Paths.StagingRoot));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Paths.ContentRoot));
    }

    public async ValueTask DisposeAsync()
    {
        await Repository.DisposeAsync();
        Sources.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

/// <summary>A staged file on a full disk.</summary>
internal sealed class DiskFullStream : MemoryStream
{
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new IOException("There is not enough space on the disk.", unchecked((int)0x80070070));

    public override void Write(ReadOnlySpan<byte> buffer) => Write([], 0, 0);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default) =>
        ValueTask.FromException(new IOException("There is not enough space on the disk.", unchecked((int)0x80070070)));
}
