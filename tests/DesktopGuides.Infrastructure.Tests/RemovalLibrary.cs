using System.Diagnostics;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

/// <summary>A real library on disk for the deletion tests.</summary>
internal sealed class RemovalLibrary : IAsyncDisposable
{
    private RemovalLibrary(string root)
    {
        Root = root;
        Paths = new ManagedPathResolver(Path.Combine(root, "app-data"));
        Repository = new SqliteLibraryRepository(Paths);
    }

    public string Root { get; }
    public ManagedPathResolver Paths { get; }
    public SqliteLibraryRepository Repository { get; private set; }

    public static async Task<RemovalLibrary> CreateAsync()
    {
        RemovalLibrary library = new(Path.Combine(
            Path.GetTempPath(), "desktop-guides-removal-" + Guid.NewGuid().ToString("N")));
        await library.Repository.InitializeAsync();
        return library;
    }

    /// <summary>Opens a fresh repository on the same files, as the next app start does.</summary>
    public async Task RestartAsync()
    {
        await Repository.DisposeAsync();
        Repository = new SqliteLibraryRepository(Paths);
        await Repository.InitializeAsync();
    }

    public async Task<Guid> AddGameAsync(string title) =>
        (await Repository.AddGameAsync(title, null, null)).Id;

    public async Task<Guid> AddGuideAsync(Guid gameId, string title)
    {
        Guid operation = Guid.NewGuid();
        Guid id = Guid.NewGuid();
        await Repository.RunImportAsync<bool>((journal, _) =>
        {
            journal.Prepare(operation, id);
            journal.Publish(
                new NewImportedGuide(operation, id, gameId, title, GuideFormat.Txt, "guide.txt",
                    new string('a', 64), 20, null, null),
                () => { });
            return Task.FromResult(true);
        }, CancellationToken.None);
        return id;
    }

    public Task RunDeletion(Action<IDeletionJournal> work) =>
        Repository.RunDeletionAsync(journal =>
        {
            work(journal);
            return true;
        }, CancellationToken.None);

    public static void WriteFile(string directory, string relative, string text)
    {
        string path = Path.Combine(directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public static void CreateJunction(string link, string target)
    {
        Directory.CreateDirectory(target);
        using Process process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    public string Scalar(string sql)
    {
        using SqliteConnection connection = new($"Data Source={Paths.DatabasePath};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar())!;
    }

    public void Execute(string sql)
    {
        using SqliteConnection connection = new($"Data Source={Paths.DatabasePath};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public string RowsFor(Guid guideId)
    {
        string id = guideId.ToString("N");
        return Scalar($"""
            SELECT (SELECT COUNT(*) FROM Guides WHERE Id = '{id}') || '|' ||
                   (SELECT COUNT(*) FROM ReadingStates WHERE GuideId = '{id}') || '|' ||
                   (SELECT COUNT(*) FROM ReaderPreferences WHERE GuideId = '{id}')
            """);
    }

    public async ValueTask DisposeAsync()
    {
        await Repository.DisposeAsync();
        // Tests that create a junction delete it in a finally block first.
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}
