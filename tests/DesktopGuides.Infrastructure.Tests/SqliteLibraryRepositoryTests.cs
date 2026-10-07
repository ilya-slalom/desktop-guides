using System.Diagnostics;
using System.Globalization;
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Artwork;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class SqliteLibraryRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PersistsTwoIndependentGuideStatesAndSettingsAcrossReopen()
    {
        using TestLibrary directory = new();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        Guid gameId;
        await using (SqliteLibraryRepository repository =
            new(directory.Paths, new FixedTimeProvider(Now)))
        {
            await repository.InitializeAsync();
            Game game = await repository.AddGameAsync("  Space Quest  ", null, null);
            gameId = game.Id;
            Assert.Equal("Space Quest", game.Title);
            Assert.Equal(Now, game.CreatedUtc);
            InsertGuide(directory.Paths.DatabasePath, first, gameId);
            InsertGuide(directory.Paths.DatabasePath, second, gameId);
            SetCompleted(directory.Paths.DatabasePath, second);

            await repository.SaveReadingLocationAsync(first, "{\"one\":1}", 0.25);
            await repository.SaveReadingLocationAsync(second, "{\"two\":2}", 0.75);
            await repository.SaveReaderPreferencesAsync(first, 1.25);
            await repository.SaveSettingsAsync(new AppSettings(ThemePreference.Dark, second));
        }

        await using SqliteLibraryRepository reopened = new(directory.Paths);
        await reopened.InitializeAsync();

        Assert.Equal("Space Quest", (await reopened.GetGameAsync(gameId))?.Title);
        Assert.Single(await reopened.ListGamesAsync());
        Assert.Equal(2, (await reopened.ListGuidesAsync(gameId)).Count);
        Assert.Equal(first, (await reopened.GetGuideAsync(first))?.Id);
        Assert.Equal("{\"one\":1}", (await reopened.GetReadingStateAsync(first))?.LocatorJson);
        Assert.Equal(0.25, (await reopened.GetReadingStateAsync(first))?.EstimatedFraction);
        Assert.Equal("{\"two\":2}", (await reopened.GetReadingStateAsync(second))?.LocatorJson);
        Assert.Equal(0.75, (await reopened.GetReadingStateAsync(second))?.EstimatedFraction);
        Assert.Null((await reopened.GetReadingStateAsync(first))?.CompletedUtc);
        Assert.Equal(Now, (await reopened.GetReadingStateAsync(second))?.CompletedUtc);
        Assert.Equal(Now, (await reopened.GetGuideAsync(first))?.ImportedUtc);
        Assert.Equal(1.25, (await reopened.GetReaderPreferencesAsync(first))?.TextScale);
        Assert.Null((await reopened.GetReaderPreferencesAsync(second))?.TextScale);
        Assert.Equal(new AppSettings(ThemePreference.Dark, second), await reopened.GetSettingsAsync());

        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        Assert.Equal(4L, (long)version.ExecuteScalar()!);
        using SqliteCommand index = connection.CreateCommand();
        index.CommandText = """
            SELECT name FROM sqlite_schema
            WHERE type = 'index' AND name = 'IX_ReadingStates_LastOpenedUtcMs'
            """;
        Assert.Equal("IX_ReadingStates_LastOpenedUtcMs", index.ExecuteScalar());
        using SqliteCommand orphan = connection.CreateCommand();
        orphan.CommandText = "INSERT INTO ReadingStates (GuideId) VALUES ($id)";
        orphan.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        Assert.Throws<SqliteException>(() => orphan.ExecuteNonQuery());
    }

    [Fact]
    public async Task ProgressCoordinatorSavesEachGuidesLocatorAcrossReopen()
    {
        using TestLibrary directory = new();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        StoreSession firstSession = new(120);
        StoreSession secondSession = new(9000);
        await using (SqliteLibraryRepository repository =
            new(directory.Paths, new FixedTimeProvider(Now)))
        {
            await repository.InitializeAsync();
            Game game = await repository.AddGameAsync("Progress", null, null);
            InsertGuide(directory.Paths.DatabasePath, first, game.Id);
            InsertGuide(directory.Paths.DatabasePath, second, game.Id);
            ProgressCoordinator coordinator = new(repository, new FixedTimeProvider(Now.AddHours(2)));

            await using (IProgressTracking tracking = coordinator.Track(first, firstSession, null))
            {
                firstSession.Move();
            }
            await using (IProgressTracking tracking = coordinator.Track(second, secondSession, null))
            {
                secondSession.Move();
            }
            Assert.Equal(new ProgressCounts(2, 0, 0, 2), coordinator.Counts);
        }

        await using SqliteLibraryRepository reopened = new(directory.Paths);
        await reopened.InitializeAsync();
        foreach ((Guid guideId, StoreSession session) in new[] { (first, firstSession), (second, secondSession) })
        {
            ReadingState? state = await reopened.GetReadingStateAsync(guideId);
            Assert.Equal(0.4, state?.EstimatedFraction);
            LocationDecodeResult decoded = ReaderLocationCodec.Deserialize(
                state?.LocatorJson, GuideFormat.Txt, StoreSession.Hash);
            Assert.Equal(LocationDecodeStatus.Valid, decoded.Status);
            Assert.Equal(session.Current, decoded.Location);
            Assert.Equal(Now.AddHours(2), state?.LastOpenedUtc);
            Assert.Null(state?.CompletedUtc);
        }
        Game listed = Assert.Single(await reopened.ListGamesAsync());
        IReadOnlyList<GuideSummary> summaries = await reopened.ListGuideSummariesAsync(listed.Id);
        Assert.Equal(2, summaries.Count);
        Assert.All(summaries, summary => Assert.Equal(0.4, summary.State?.EstimatedFraction));
        Assert.All(summaries, summary => Assert.Equal(Now.AddHours(2), summary.State?.LastOpenedUtc));
    }

    [Fact]
    public async Task SaveAndOpenKeepACompletedGuideCompleted()
    {
        using TestLibrary directory = new();
        Guid guide = Guid.NewGuid();
        StoreSession session = new(120);
        await using SqliteLibraryRepository repository =
            new(directory.Paths, new FixedTimeProvider(Now));
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Progress", null, null);
        InsertGuide(directory.Paths.DatabasePath, guide, game.Id);
        SetCompleted(directory.Paths.DatabasePath, guide);
        ProgressCoordinator coordinator = new(repository, new FixedTimeProvider(Now.AddHours(2)));

        await using (IProgressTracking tracking = coordinator.Track(guide, session, null))
        {
            session.Move();
        }

        GuideSummary summary = Assert.Single(await repository.ListGuideSummariesAsync(game.Id));
        Assert.Equal(Now, summary.State?.CompletedUtc);
        Assert.Equal(0.4, summary.State?.EstimatedFraction);
        Assert.Equal(Now.AddHours(2), summary.State?.LastOpenedUtc);
        IReadOnlyList<CatalogFact> facts = CatalogPresentation.GuideFacts(
            summary, new FixedTimeProvider(Now.AddHours(3)), CultureInfo.InvariantCulture);
        Assert.Equal("Completed", facts[1].Label);
    }

    [Fact]
    public async Task RecordGuideOpenedRejectsAMissingRowAndANegativeTime()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Guid unknown = Guid.NewGuid();

        ReadingStateMissingException missing = await Assert.ThrowsAsync<ReadingStateMissingException>(() =>
            repository.RecordGuideOpenedAsync(unknown, Now));
        Assert.Equal(unknown, missing.GuideId);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repository.RecordGuideOpenedAsync(unknown, DateTimeOffset.UnixEpoch.AddMilliseconds(-1)));
    }

    private const string PlaceJson = """{"place":1}""";

    [Fact]
    public async Task CompletionTogglesTwiceAndKeepsTheReadingPlace()
    {
        using TestLibrary directory = new();
        Guid guide = Guid.NewGuid();
        await using SqliteLibraryRepository repository =
            new(directory.Paths, new FixedTimeProvider(Now));
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Completion", null, null);
        InsertGuide(directory.Paths.DatabasePath, guide, game.Id);
        await repository.SaveReadingLocationAsync(guide, PlaceJson, 0.4);
        await repository.RecordGuideOpenedAsync(guide, Now.AddHours(1));

        async Task AssertState(DateTimeOffset? completed)
        {
            ReadingState? state = await repository.GetReadingStateAsync(guide);
            Assert.Equal(PlaceJson, state?.LocatorJson);
            Assert.Equal(0.4, state?.EstimatedFraction);
            Assert.Equal(Now.AddHours(1), state?.LastOpenedUtc);
            Assert.Equal(completed, state?.CompletedUtc);
        }

        Assert.Equal(Now.AddHours(2), await repository.SetGuideCompletionAsync(guide, Now.AddHours(2)));
        await AssertState(Now.AddHours(2));
        Assert.Null(await repository.SetGuideCompletionAsync(guide, null));
        await AssertState(null);
        Assert.Equal(Now.AddHours(3), await repository.SetGuideCompletionAsync(guide, Now.AddHours(3)));
        await AssertState(Now.AddHours(3));
        Assert.Null(await repository.SetGuideCompletionAsync(guide, null));
        await AssertState(null);
    }

    [Fact]
    public async Task RepeatingAnActionKeepsTheCommittedState()
    {
        using TestLibrary directory = new();
        Guid guide = Guid.NewGuid();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Completion", null, null);
        InsertGuide(directory.Paths.DatabasePath, guide, game.Id);

        Assert.Null(await repository.SetGuideCompletionAsync(guide, null));
        Assert.Equal(Now, await repository.SetGuideCompletionAsync(guide, Now));
        Assert.Equal(Now, await repository.SetGuideCompletionAsync(guide, Now.AddHours(5)));
        Assert.Equal(Now, (await repository.GetReadingStateAsync(guide))?.CompletedUtc);
        Assert.Null(await repository.SetGuideCompletionAsync(guide, null));
        Assert.Null(await repository.SetGuideCompletionAsync(guide, null));
        Assert.Null((await repository.GetReadingStateAsync(guide))?.CompletedUtc);
    }

    [Fact]
    public async Task CompletionSurvivesReopen()
    {
        using TestLibrary directory = new();
        Guid guide = Guid.NewGuid();
        Guid gameId;
        FixedTimeProvider later = new(Now.AddHours(3));
        await using (SqliteLibraryRepository repository =
            new(directory.Paths, new FixedTimeProvider(Now)))
        {
            await repository.InitializeAsync();
            Game game = await repository.AddGameAsync("Completion", null, null);
            gameId = game.Id;
            InsertGuide(directory.Paths.DatabasePath, guide, gameId);
            await repository.SaveReadingLocationAsync(guide, PlaceJson, 0.4);
            await repository.SetGuideCompletionAsync(guide, Now.AddHours(2));
        }

        await using (SqliteLibraryRepository reopened = new(directory.Paths))
        {
            await reopened.InitializeAsync();
            GuideSummary summary = Assert.Single(await reopened.ListGuideSummariesAsync(gameId));
            Assert.Equal(Now.AddHours(2), summary.State?.CompletedUtc);
            Assert.Equal("Completed", CatalogPresentation.GuideFacts(
                summary, later, CultureInfo.InvariantCulture)[1].Label);
            Assert.Null(await reopened.SetGuideCompletionAsync(guide, null));
        }

        await using SqliteLibraryRepository again = new(directory.Paths);
        await again.InitializeAsync();
        GuideSummary returned = Assert.Single(await again.ListGuideSummariesAsync(gameId));
        Assert.Null(returned.State?.CompletedUtc);
        Assert.Equal(PlaceJson, returned.State?.LocatorJson);
        Assert.Equal("~40%", CatalogPresentation.GuideFacts(
            returned, later, CultureInfo.InvariantCulture)[1].Label);
    }

    [Fact]
    public async Task AFullEstimateIsNotCompletion()
    {
        // TR13.1: reading to 100% never creates a completion time.
        using TestLibrary directory = new();
        Guid guide = Guid.NewGuid();
        Guid gameId;
        await using (SqliteLibraryRepository repository =
            new(directory.Paths, new FixedTimeProvider(Now)))
        {
            await repository.InitializeAsync();
            Game game = await repository.AddGameAsync("Completion", null, null);
            gameId = game.Id;
            InsertGuide(directory.Paths.DatabasePath, guide, gameId);
            await repository.SaveReadingLocationAsync(guide, PlaceJson, 1.0);
            await repository.RecordGuideOpenedAsync(guide, Now.AddHours(1));
        }

        await using SqliteLibraryRepository reopened = new(directory.Paths);
        await reopened.InitializeAsync();
        GuideSummary summary = Assert.Single(await reopened.ListGuideSummariesAsync(gameId));
        Assert.Equal(1.0, summary.State?.EstimatedFraction);
        Assert.Null(summary.State?.CompletedUtc);
        Assert.Equal("~100%", CatalogPresentation.GuideFacts(
            summary, new FixedTimeProvider(Now.AddHours(2)), CultureInfo.InvariantCulture)[1].Label);
    }

    [Fact]
    public async Task SetGuideCompletionRejectsAMissingRowAndANegativeTime()
    {
        using TestLibrary directory = new();
        Guid guide = Guid.NewGuid();
        Guid unknown = Guid.NewGuid();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Completion", null, null);
        InsertGuide(directory.Paths.DatabasePath, guide, game.Id);

        ReadingStateMissingException missing = await Assert.ThrowsAsync<ReadingStateMissingException>(() =>
            repository.SetGuideCompletionAsync(unknown, Now));
        Assert.Equal(unknown, missing.GuideId);
        missing = await Assert.ThrowsAsync<ReadingStateMissingException>(() =>
            repository.SetGuideCompletionAsync(unknown, null));
        Assert.Equal(unknown, missing.GuideId);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repository.SetGuideCompletionAsync(guide, DateTimeOffset.UnixEpoch.AddMilliseconds(-1)));
        Assert.Null((await repository.GetReadingStateAsync(guide))?.CompletedUtc);
    }

    [Fact]
    public async Task ConcurrentWritesKeepEveryColumn()
    {
        using TestLibrary directory = new();
        Guid guide = Guid.NewGuid();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Completion", null, null);
        InsertGuide(directory.Paths.DatabasePath, guide, game.Id);

        await Task.WhenAll(
            repository.SaveReadingLocationAsync(guide, PlaceJson, 0.4),
            repository.SetGuideCompletionAsync(guide, Now.AddHours(2)),
            repository.RecordGuideOpenedAsync(guide, Now.AddHours(1)));

        ReadingState? state = await repository.GetReadingStateAsync(guide);
        Assert.Equal(PlaceJson, state?.LocatorJson);
        Assert.Equal(0.4, state?.EstimatedFraction);
        Assert.Equal(Now.AddHours(1), state?.LastOpenedUtc);
        Assert.Equal(Now.AddHours(2), state?.CompletedUtc);
    }

    [Fact]
    public async Task CompletionTimeIsCommittedInUtcMilliseconds()
    {
        using TestLibrary directory = new();
        Guid guide = Guid.NewGuid();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Completion", null, null);
        InsertGuide(directory.Paths.DatabasePath, guide, game.Id);
        DateTimeOffset local = new(2026, 10, 5, 17, 0, 0, TimeSpan.FromHours(9));

        DateTimeOffset? committed = await repository.SetGuideCompletionAsync(guide, local.AddTicks(5));

        Assert.Equal(local, committed);
        Assert.Equal(TimeSpan.Zero, committed?.Offset);
        Assert.Equal(committed, (await repository.GetReadingStateAsync(guide))?.CompletedUtc);
    }

    [Fact]
    public async Task CanceledCompletionWritesNothing()
    {
        using TestLibrary directory = new();
        Guid guide = Guid.NewGuid();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Completion", null, null);
        InsertGuide(directory.Paths.DatabasePath, guide, game.Id);
        using CancellationTokenSource canceled = new();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.SetGuideCompletionAsync(guide, Now, canceled.Token));

        Assert.Null((await repository.GetReadingStateAsync(guide))?.CompletedUtc);
    }

    [Fact]
    public async Task RejectsInvalidMetadataAndUnknownGuidePosition()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.AddGameAsync("  ", null, null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repository.SaveReadingLocationAsync(Guid.NewGuid(), "{}", 1.1));
        ReadingStateMissingException missing = await Assert.ThrowsAsync<ReadingStateMissingException>(() =>
            repository.SaveReadingLocationAsync(Guid.NewGuid(), "{}", 0.5));
        Assert.NotEqual(Guid.Empty, missing.GuideId);
    }

    [Fact]
    public async Task EditingOneDuplicateNamedGameKeepsItsIdAndGuideState()
    {
        using TestLibrary directory = new();
        DateTimeOffset later = Now.AddMinutes(12);
        AdjustableTimeProvider clock = new(Now);
        Guid editedId;
        Guid otherId;
        Guid guideId = Guid.NewGuid();
        await using (SqliteLibraryRepository repository = new(directory.Paths, clock))
        {
            await repository.InitializeAsync();
            Game first = await repository.AddGameAsync(
                " 大航海時代 ", " PC ", " First note ");
            Game other = await repository.AddGameAsync("大航海時代", null, null);
            editedId = first.Id;
            otherId = other.Id;
            InsertGuide(directory.Paths.DatabasePath, guideId, editedId);
            await repository.SaveReadingLocationAsync(
                guideId, "{\"line\":12}", 0.2);
            clock.Now = later;

            Game changed = await repository.UpdateGameAsync(
                editedId, " 大航海時代 II ", " Windows ", " ");
            Assert.Equal(editedId, changed.Id);
            Assert.Equal("大航海時代 II", changed.Title);
            Assert.Equal("Windows", changed.Platform);
            Assert.Null(changed.Notes);
            Assert.Equal(Now, changed.CreatedUtc);
            Assert.Equal(later, changed.UpdatedUtc);
            Assert.Equal("大航海時代", (await repository.GetGameAsync(otherId))?.Title);
            Assert.Equal(guideId, Assert.Single(
                await repository.ListGuidesAsync(editedId)).Id);
            Assert.Equal("{\"line\":12}",
                (await repository.GetReadingStateAsync(guideId))?.LocatorJson);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                repository.UpdateGameAsync(editedId, " ", null, null));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.UpdateGameAsync(Guid.NewGuid(), "Missing", null, null));
            Assert.Equal("大航海時代 II",
                (await repository.GetGameAsync(editedId))?.Title);
        }

        await using SqliteLibraryRepository reopened = new(directory.Paths);
        await reopened.InitializeAsync();
        Assert.Equal("大航海時代 II", (await reopened.GetGameAsync(editedId))?.Title);
        Assert.Equal(later, (await reopened.GetGameAsync(editedId))?.UpdatedUtc);
        Assert.Equal("{\"line\":12}",
            (await reopened.GetReadingStateAsync(guideId))?.LocatorJson);
    }

    [Fact]
    public async Task RejectsGuideRootThatIsNotBoundToItsId()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Example", null, null);

        Assert.Throws<SqliteException>(() =>
            InsertGuide(directory.Paths.DatabasePath, Guid.NewGuid(), game.Id,
                "content/wrong-guide"));
        Assert.Empty(await repository.ListGuidesAsync(game.Id));
    }

    [Fact]
    public async Task RecoversEmptyVersionZeroDatabaseAfterInterruptedSchemaCreation()
    {
        using TestLibrary directory = new();
        directory.Paths.EnsureCreated();
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        {
            using SqliteCommand journal = connection.CreateCommand();
            journal.CommandText = "PRAGMA journal_mode=WAL";
            journal.ExecuteNonQuery();
            using SqliteTransaction transaction = connection.BeginTransaction();
            using SqliteCommand abandoned = connection.CreateCommand();
            abandoned.Transaction = transaction;
            abandoned.CommandText = "CREATE TABLE Abandoned (Id INTEGER)";
            abandoned.ExecuteNonQuery();
            transaction.Rollback();
        }

        Assert.True(File.Exists(directory.Paths.DatabasePath));
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Recovered", null, null);
        Assert.Equal("Recovered", (await repository.GetGameAsync(game.Id))?.Title);
    }

    [Fact]
    public async Task PreservesPopulatedUnknownVersionZeroDatabase()
    {
        using TestLibrary directory = new();
        directory.Paths.EnsureCreated();
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE UnknownData (Value TEXT NOT NULL);
                INSERT INTO UnknownData (Value) VALUES ('keep this');
                """;
            command.ExecuteNonQuery();
        }

        await using SqliteLibraryRepository repository = new(directory.Paths);
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.InitializeAsync());
        using SqliteConnection reopened = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand verify = reopened.CreateCommand();
        verify.CommandText = "SELECT Value FROM UnknownData";
        Assert.Equal("keep this", verify.ExecuteScalar());
    }

    [Fact]
    public async Task PreservesEmptyVersionZeroDatabaseWithAnotherApplicationId()
    {
        using TestLibrary directory = new();
        directory.Paths.EnsureCreated();
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA application_id = 4242";
            command.ExecuteNonQuery();
        }

        await using SqliteLibraryRepository repository = new(directory.Paths);
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.InitializeAsync());
        using SqliteConnection reopened = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand verify = reopened.CreateCommand();
        verify.CommandText = "PRAGMA application_id";
        Assert.Equal(4242L, (long)verify.ExecuteScalar()!);
    }

    [Fact]
    public async Task UpgradesPopulatedVersionOneWithConsistentRecoveryCopy()
    {
        using TestLibrary directory = new();
        Guid gameId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        CreatePopulatedVersionOne(directory, gameId, guideId);

        // Keep a writer open with a recently committed WAL change during backup.
        using SqliteConnection active = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using (SqliteCommand latest = active.CreateCommand())
        {
            latest.CommandText = "UPDATE Games SET Notes = 'latest WAL note' WHERE Id = $id";
            latest.Parameters.AddWithValue("$id", gameId.ToString("N"));
            Assert.Equal(1, latest.ExecuteNonQuery());
        }
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();

        Assert.Equal("Older game", (await repository.GetGameAsync(gameId))?.Title);
        Assert.Equal("latest WAL note", (await repository.GetGameAsync(gameId))?.Notes);
        Assert.Equal(guideId, (await repository.GetGuideAsync(guideId))?.Id);
        Assert.Equal("{\"old\":true}",
            (await repository.GetReadingStateAsync(guideId))?.LocatorJson);
        Assert.Equal(Now, (await repository.GetReadingStateAsync(guideId))?.CompletedUtc);
        Assert.Equal(1.25, (await repository.GetReaderPreferencesAsync(guideId))?.TextScale);
        Assert.Equal(new AppSettings(ThemePreference.Dark, guideId),
            await repository.GetSettingsAsync());

        using SqliteCommand version = active.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        Assert.Equal(4L, (long)version.ExecuteScalar()!);
        using SqliteCommand index = active.CreateCommand();
        index.CommandText = """
            SELECT name FROM sqlite_schema
            WHERE type = 'index' AND name = 'IX_ReadingStates_LastOpenedUtcMs'
            """;
        Assert.Equal("IX_ReadingStates_LastOpenedUtcMs", index.ExecuteScalar());

        string backupPath = Assert.Single(
            Directory.GetFiles(directory.Paths.RecoveryRoot, "*.sqlite"));
        using SqliteConnection backup = OpenWithForeignKeys(backupPath);
        using SqliteCommand backupVersion = backup.CreateCommand();
        backupVersion.CommandText = "PRAGMA user_version";
        Assert.Equal(1L, (long)backupVersion.ExecuteScalar()!);
        using SqliteCommand backupGuide = backup.CreateCommand();
        backupGuide.CommandText = "SELECT Id FROM Guides WHERE Id = $id";
        backupGuide.Parameters.AddWithValue("$id", guideId.ToString("N"));
        Assert.Equal(guideId.ToString("N"), backupGuide.ExecuteScalar());
        using SqliteCommand backupNote = backup.CreateCommand();
        backupNote.CommandText = "SELECT Notes FROM Games WHERE Id = $id";
        backupNote.Parameters.AddWithValue("$id", gameId.ToString("N"));
        Assert.Equal("latest WAL note", backupNote.ExecuteScalar());

        await repository.InitializeAsync();
        Assert.Single(Directory.GetFiles(directory.Paths.RecoveryRoot, "*.sqlite"));
    }

    [Fact]
    public async Task FailedMigrationRetainsVersionOneDataAndRecoveryCopy()
    {
        using TestLibrary directory = new();
        Guid gameId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        CreatePopulatedVersionOne(directory, gameId, guideId);

        await using (SqliteLibraryRepository failing = new(
            directory.Paths, null, version =>
            {
                if (version == 2)
                {
                    throw new IOException("Injected after the v2 index was created.");
                }
            }))
        {
            InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
                () => failing.InitializeAsync());
            Assert.Contains("recovery copy:", error.Message);
            Assert.IsType<IOException>(error.InnerException);
        }

        using (SqliteConnection original = OpenWithForeignKeys(directory.Paths.DatabasePath))
        {
            using SqliteCommand version = original.CreateCommand();
            version.CommandText = "PRAGMA user_version";
            Assert.Equal(1L, (long)version.ExecuteScalar()!);
            using SqliteCommand index = original.CreateCommand();
            index.CommandText = """
                SELECT name FROM sqlite_schema
                WHERE type = 'index' AND name = 'IX_ReadingStates_LastOpenedUtcMs'
                """;
            Assert.Null(index.ExecuteScalar());
            using SqliteCommand game = original.CreateCommand();
            game.CommandText = "SELECT Title FROM Games WHERE Id = $id";
            game.Parameters.AddWithValue("$id", gameId.ToString("N"));
            Assert.Equal("Older game", game.ExecuteScalar());
        }

        string backupPath = Assert.Single(
            Directory.GetFiles(directory.Paths.RecoveryRoot, "*.sqlite"));
        using (SqliteConnection backup = OpenWithForeignKeys(backupPath))
        {
            using SqliteCommand version = backup.CreateCommand();
            version.CommandText = "PRAGMA user_version";
            Assert.Equal(1L, (long)version.ExecuteScalar()!);
            using SqliteCommand guide = backup.CreateCommand();
            guide.CommandText = "SELECT Id FROM Guides WHERE Id = $id";
            guide.Parameters.AddWithValue("$id", guideId.ToString("N"));
            Assert.Equal(guideId.ToString("N"), guide.ExecuteScalar());
        }

        await using SqliteLibraryRepository retry = new(directory.Paths);
        await retry.InitializeAsync();
        Assert.Equal(guideId, (await retry.GetGuideAsync(guideId))?.Id);
    }

    [Fact]
    public async Task RejectsNewerSchemaWithoutReplacingItsData()
    {
        using TestLibrary directory = new();
        directory.Paths.EnsureCreated();
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        {
            using SqliteCommand data = connection.CreateCommand();
            data.CommandText = """
                CREATE TABLE FutureData (Value TEXT NOT NULL);
                INSERT INTO FutureData (Value) VALUES ('keep this');
                PRAGMA user_version = 99;
                """;
            data.ExecuteNonQuery();
        }

        await using SqliteLibraryRepository repository = new(directory.Paths);
        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
            () => repository.InitializeAsync());
        Assert.Contains("Update Desktop Guides", error.Message);
        using SqliteConnection original = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand verify = original.CreateCommand();
        verify.CommandText = "SELECT Value FROM FutureData";
        Assert.Equal("keep this", verify.ExecuteScalar());
        Assert.Empty(Directory.GetFiles(directory.Paths.RecoveryRoot));
    }

    [Fact]
    public async Task RejectsOrphanedVersionOneRowsBeforeMigration()
    {
        using TestLibrary directory = new();
        Guid gameId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        CreatePopulatedVersionOne(directory, gameId, guideId);
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        {
            using SqliteCommand foreignKeys = connection.CreateCommand();
            foreignKeys.CommandText = "PRAGMA foreign_keys=OFF";
            foreignKeys.ExecuteNonQuery();
            using SqliteCommand orphan = connection.CreateCommand();
            orphan.CommandText = "INSERT INTO ReadingStates (GuideId) VALUES ($id)";
            orphan.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            orphan.ExecuteNonQuery();
        }

        await using SqliteLibraryRepository repository = new(directory.Paths);
        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
            () => repository.InitializeAsync());
        Assert.Contains("orphaned records", error.Message);
        using SqliteConnection original = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand version = original.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        Assert.Equal(1L, (long)version.ExecuteScalar()!);
        Assert.Empty(Directory.GetFiles(directory.Paths.RecoveryRoot));
    }

    [Fact]
    public async Task RejectsIncompleteCurrentSchemaWithoutReinitializing()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Keep", null, null);
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        {
            using SqliteCommand drop = connection.CreateCommand();
            drop.CommandText = "DROP INDEX IX_ReadingStates_LastOpenedUtcMs";
            drop.ExecuteNonQuery();
        }

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
            () => repository.InitializeAsync());
        Assert.Contains("schema is incomplete", error.Message);
        Assert.Equal("Keep", (await repository.GetGameAsync(game.Id))?.Title);
    }

    [Fact]
    public async Task RejectsLinkedDatabaseBeforeOpeningExternalVersionOneData()
    {
        using TestLibrary directory = new();
        directory.Paths.EnsureCreated();
        string outside = Path.Combine(directory.Root, "outside.sqlite");
        using (SqliteConnection connection = OpenWithForeignKeys(outside))
        {
            using SqliteCommand schema = connection.CreateCommand();
            schema.CommandText = LibrarySchema.Version1;
            schema.ExecuteNonQuery();
        }

        File.CreateSymbolicLink(directory.Paths.DatabasePath, outside);
        try
        {
            await using SqliteLibraryRepository repository = new(directory.Paths);
            await Assert.ThrowsAsync<InvalidDataException>(() => repository.InitializeAsync());
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                repository.GetGameAsync(Guid.NewGuid()));
            Assert.Empty(Directory.GetFiles(directory.Paths.RecoveryRoot));
        }
        finally
        {
            File.Delete(directory.Paths.DatabasePath);
        }

        using SqliteConnection original = OpenWithForeignKeys(outside);
        using SqliteCommand version = original.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        Assert.Equal(1L, (long)version.ExecuteScalar()!);
    }

    [Fact]
    public async Task RejectsLinkedSharedMemoryFileBeforeModifyingItsTarget()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        await repository.AddGameAsync("Keep", null, null);

        string sharedMemory = directory.Paths.DatabasePath + "-shm";
        if (File.Exists(sharedMemory))
        {
            File.Delete(sharedMemory);
        }
        string outside = Path.Combine(directory.Root, "outside-shm");
        byte[] sentinel = Enumerable.Repeat((byte)0x61, 32768).ToArray();
        File.WriteAllBytes(outside, sentinel);
        File.CreateSymbolicLink(sharedMemory, outside);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => repository.InitializeAsync());
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                repository.GetGameAsync(Guid.NewGuid()));
            Assert.Equal(sentinel, File.ReadAllBytes(outside));
        }
        finally
        {
            File.Delete(sharedMemory);
        }
    }

    [Fact]
    public async Task RejectsHardLinkedDatabaseBeforeUpgradingExternalData()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TestLibrary directory = new();
        directory.Paths.EnsureCreated();
        string outside = Path.Combine(directory.Root, "outside.sqlite");
        using (SqliteConnection connection = OpenWithForeignKeys(outside))
        {
            using SqliteCommand schema = connection.CreateCommand();
            schema.CommandText = LibrarySchema.Version1;
            schema.ExecuteNonQuery();
        }

        ProcessStartInfo start = new(
            "cmd.exe", $"/c mklink /H \"{directory.Paths.DatabasePath}\" \"{outside}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using (Process process = Process.Start(start)!)
        {
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        }
        Assert.True((File.GetAttributes(directory.Paths.DatabasePath) &
                     FileAttributes.ReparsePoint) == 0);

        await using SqliteLibraryRepository repository = new(directory.Paths);
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.InitializeAsync());
        Assert.Empty(Directory.GetFiles(directory.Paths.RecoveryRoot));
        using SqliteConnection original = OpenWithForeignKeys(outside);
        using SqliteCommand version = original.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        Assert.Equal(1L, (long)version.ExecuteScalar()!);
    }

    [Fact]
    public async Task RejectsAlteredVersionOneConstraintsBeforeBackup()
    {
        using TestLibrary directory = new();
        directory.Paths.EnsureCreated();
        string alteredSchema = LibrarySchema.Version1.Replace(
            "length(Title) BETWEEN 1 AND 160", "length(Title) <= 160",
            StringComparison.Ordinal);
        Assert.NotEqual(LibrarySchema.Version1, alteredSchema);
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        {
            using SqliteCommand schema = connection.CreateCommand();
            schema.CommandText = alteredSchema;
            schema.ExecuteNonQuery();
            using SqliteCommand game = connection.CreateCommand();
            game.CommandText = """
                INSERT INTO Games (Id, Title, CreatedUtcMs, UpdatedUtcMs)
                VALUES ($id, 'Keep', $now, $now)
                """;
            game.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            game.Parameters.AddWithValue("$now", Now.ToUnixTimeMilliseconds());
            game.ExecuteNonQuery();
        }

        await using SqliteLibraryRepository repository = new(directory.Paths);
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.InitializeAsync());
        Assert.Empty(Directory.GetFiles(directory.Paths.RecoveryRoot));

        using SqliteConnection original = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand verify = original.CreateCommand();
        verify.CommandText = "SELECT Title FROM Games";
        Assert.Equal("Keep", verify.ExecuteScalar());
        verify.CommandText = "PRAGMA user_version";
        Assert.Equal(1L, (long)verify.ExecuteScalar()!);
    }

    [Fact]
    public async Task RejectsRenamedVersionTwoColumnBeforeRepositoryReads()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        await repository.AddGameAsync("Keep", null, "Note");
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        {
            using SqliteCommand alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Games RENAME COLUMN Notes TO Memo";
            alter.ExecuteNonQuery();
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.InitializeAsync());
        using SqliteConnection original = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand verify = original.CreateCommand();
        verify.CommandText = "SELECT Title FROM Games";
        Assert.Equal("Keep", verify.ExecuteScalar());
        Assert.Empty(Directory.GetFiles(directory.Paths.RecoveryRoot));
    }

    [Fact]
    public async Task PersistsWindowMaterialAcrossReopen()
    {
        using TestLibrary directory = new();
        await using (SqliteLibraryRepository repository = new(directory.Paths))
        {
            await repository.InitializeAsync();
            Assert.Equal(WindowMaterial.Mica, (await repository.GetSettingsAsync()).WindowMaterial);
            await repository.SaveSettingsAsync(
                new AppSettings(ThemePreference.System, null, WindowMaterial.Acrylic));
        }

        await using SqliteLibraryRepository reopened = new(directory.Paths);
        await reopened.InitializeAsync();
        Assert.Equal(
            new AppSettings(ThemePreference.System, null, WindowMaterial.Acrylic),
            await reopened.GetSettingsAsync());
    }

    [Theory]
    [InlineData("Glass")]
    [InlineData("acrylic")]
    [InlineData("1")]
    public async Task ReadsUnknownStoredWindowMaterialAsMica(string value)
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO Settings (Key, Value) VALUES ('WindowMaterial', $value)";
            command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        }

        // A newer build may store a material this one does not know; the next save replaces it.
        Assert.Equal(WindowMaterial.Mica, (await repository.GetSettingsAsync()).WindowMaterial);
        await repository.UpdateSettingsAsync(s => s with { WindowMaterial = WindowMaterial.Solid });
        Assert.Equal(WindowMaterial.Solid, (await repository.GetSettingsAsync()).WindowMaterial);
    }

    [Fact]
    public async Task RejectsUndefinedWindowMaterialOnSave()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.SaveSettingsAsync(
            new AppSettings(ThemePreference.System, null, (WindowMaterial)42)));
    }

    [Fact]
    public async Task ConcurrentSettingsUpdatesKeepBothChanges()
    {
        using TestLibrary directory = new();
        Guid guide = Guid.NewGuid();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();

        Task<AppSettings>[] updates = Enumerable.Range(0, 20)
            .Select(i => i % 2 == 0
                ? repository.UpdateSettingsAsync(s => s with { WindowMaterial = WindowMaterial.Solid })
                : repository.UpdateSettingsAsync(s => s with { LastActiveGuideId = guide }))
            .ToArray();
        await Task.WhenAll(updates);

        Assert.Equal(
            new AppSettings(ThemePreference.System, guide, WindowMaterial.Solid),
            await repository.GetSettingsAsync());
    }

    [Fact]
    public async Task UpdateSettingsRejectsUndefinedValuesWithoutWriting()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        await repository.SaveSettingsAsync(
            new AppSettings(ThemePreference.Dark, null, WindowMaterial.Acrylic));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repository.UpdateSettingsAsync(s => s with { WindowMaterial = (WindowMaterial)42 }));

        Assert.Equal(
            new AppSettings(ThemePreference.Dark, null, WindowMaterial.Acrylic),
            await repository.GetSettingsAsync());
    }

    [Fact]
    public async Task IgnoresUnknownSettingsKeysFromOtherVersions()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO Settings (Key, Value) VALUES ('FutureSetting', 'x')";
            command.ExecuteNonQuery();
        }

        Assert.Equal(new AppSettings(ThemePreference.System, null), await repository.GetSettingsAsync());
    }

    [Fact]
    public async Task UpgradesPopulatedVersionTwoAndKeepsGames()
    {
        using TestLibrary directory = new();
        Guid gameId = Guid.NewGuid();
        CreatePopulatedVersionTwo(directory, gameId);

        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();

        Game game = Assert.Single(await repository.ListGamesAsync());
        Assert.Equal(gameId, game.Id);
        Assert.Null(game.Link);
        Assert.Equal(4L, ReadUserVersion(directory.Paths.DatabasePath));
        Assert.Single(Directory.GetFiles(directory.Paths.RecoveryRoot, "*.sqlite"));
    }

    [Fact]
    public async Task FailedVersionThreeMigrationStaysAtVersionTwo()
    {
        using TestLibrary directory = new();
        Guid gameId = Guid.NewGuid();
        CreatePopulatedVersionTwo(directory, gameId);

        await using (SqliteLibraryRepository failing = new(directory.Paths, null, version =>
        {
            if (version == 3) throw new IOException("Injected after v3 columns were added.");
        }))
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => failing.InitializeAsync());
        }

        Assert.Equal(2L, ReadUserVersion(directory.Paths.DatabasePath));
        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand columns = connection.CreateCommand();
        columns.CommandText = "SELECT count(*) FROM pragma_table_info('Games') WHERE name = 'ProviderName'";
        Assert.Equal(0L, (long)columns.ExecuteScalar()!);
    }

    [Fact]
    public async Task ManyUnlinkedGamesAreAllowedButADuplicateLinkIsRejected()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);

        InsertRawGame(connection, Guid.NewGuid(), null, null, null);
        InsertRawGame(connection, Guid.NewGuid(), null, null, null);
        InsertRawGame(connection, Guid.NewGuid(), "igdb", "1942", null);
        SqliteException duplicate = Assert.Throws<SqliteException>(
            () => InsertRawGame(connection, Guid.NewGuid(), "igdb", "1942", null));
        Assert.Equal(2067, duplicate.SqliteExtendedErrorCode);
    }

    [Theory]
    [InlineData("steam", "1942", null)]
    [InlineData("igdb", null, null)]
    [InlineData(null, "1942", null)]
    [InlineData("igdb", "123456789012345678901", null)]
    [InlineData(null, null, "artwork/other/a.png")]
    [InlineData(null, null, "content/a.png")]
    public async Task ProviderPairingAndArtworkPathChecksRejectBadRows(
        string? provider, string? externalId, string? artwork)
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);

        SqliteException error = Assert.Throws<SqliteException>(
            () => InsertRawGame(connection, Guid.NewGuid(), provider, externalId, artwork));
        Assert.Equal(275, error.SqliteExtendedErrorCode); // SQLITE_CONSTRAINT_CHECK
    }

    [Fact]
    public async Task ArtworkPathUnderTheGamesOwnFolderIsAccepted()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
        Guid id = Guid.NewGuid();

        InsertRawGame(connection, id, "igdb", "7", $"artwork/{id:N}/{new string('a', 64)}.png");
    }

    private static NewLinkedGame Linked(Guid id, string externalId = "70", string? artwork = null) => new(
        id, "Half-Life", "PC (Microsoft Windows)",
        new ProviderGameLink(ProviderGameLink.Igdb, externalId, Now),
        GameMetadataJsonTests.Sample(), artwork);

    [Fact]
    public async Task AddsFindsAndReopensALinkedGame()
    {
        using TestLibrary directory = new();
        Guid id = Guid.NewGuid();
        string artwork = $"artwork/{id:N}/{new string('b', 64)}.jpg";
        await using (SqliteLibraryRepository repository = new(directory.Paths, new FixedTimeProvider(Now)))
        {
            await repository.InitializeAsync();
            Game added = await repository.AddLinkedGameAsync(Linked(id, artwork: artwork));
            Assert.Equal(id, added.Id);
            Assert.Equal(new ProviderGameLink("igdb", "70", Now), added.Link);
        }

        await using SqliteLibraryRepository reopened = new(directory.Paths);
        await reopened.InitializeAsync();
        Game found = (await reopened.FindLinkedGameAsync("igdb", "70"))!;
        Assert.Equal(id, found.Id);
        Assert.Equal("Half-Life", found.Title);
        Assert.Equal("A summary.", found.Metadata!.Summary);
        Assert.Equal(artwork, found.ArtworkRelativePath);
        Assert.Null(await reopened.FindLinkedGameAsync("igdb", "71"));
    }

    [Fact]
    public async Task DuplicateLinkReportsTheExistingGame()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game first = await repository.AddLinkedGameAsync(Linked(Guid.NewGuid()));

        DuplicateProviderLinkException error = await Assert.ThrowsAsync<DuplicateProviderLinkException>(
            () => repository.AddLinkedGameAsync(Linked(Guid.NewGuid())));
        Assert.Equal(first.Id, error.ExistingGameId);
        Assert.Single(await repository.ListGamesAsync());
    }

    [Fact]
    public async Task LinkIsFreeAgainAfterGameRowIsDeleted()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game first = await repository.AddLinkedGameAsync(Linked(Guid.NewGuid()));
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        using (SqliteCommand delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM Games WHERE Id = $id";
            delete.Parameters.AddWithValue("$id", first.Id.ToString("N"));
            delete.ExecuteNonQuery();
        }

        Game second = await repository.AddLinkedGameAsync(Linked(Guid.NewGuid()));
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task UpdateMetadataKeepsLocalFieldsAndReplacesSnapshotAndArtwork()
    {
        using TestLibrary directory = new();
        AdjustableTimeProvider clock = new(Now);
        await using SqliteLibraryRepository repository = new(directory.Paths, clock);
        await repository.InitializeAsync();
        Guid id = Guid.NewGuid();
        await repository.AddLinkedGameAsync(Linked(id));
        await repository.UpdateGameAsync(id, "My title", "Steam Deck", "My notes");
        clock.Now = Now.AddDays(1);
        string artwork = $"artwork/{id:N}/{new string('c', 64)}.png";

        Game updated = await repository.UpdateGameMetadataAsync(
            id, GameMetadataJsonTests.Sample() with { Summary = "New." }, Now.AddDays(1), artwork);

        Assert.Equal(("My title", "Steam Deck", "My notes"), (updated.Title, updated.Platform, updated.Notes));
        Assert.Equal("New.", updated.Metadata!.Summary);
        Assert.Equal(Now.AddDays(1), updated.Link!.RetrievedUtc);
        Assert.Equal(artwork, updated.ArtworkRelativePath);
        Assert.Equal(Now.AddDays(1), updated.UpdatedUtc);
    }

    [Fact]
    public async Task UpdateMetadataRejectsMissingAndUnlinkedGames()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game manual = await repository.AddGameAsync("Manual", null, null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => repository.UpdateGameMetadataAsync(
            Guid.NewGuid(), GameMetadataJsonTests.Sample(), Now, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpdateGameMetadataAsync(
            manual.Id, GameMetadataJsonTests.Sample(), Now, null));
    }

    [Fact]
    public async Task CorruptMetadataJsonLoadsGameWithoutMetadata()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Guid id = Guid.NewGuid();
        await repository.AddLinkedGameAsync(Linked(id));
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        using (SqliteCommand corrupt = connection.CreateCommand())
        {
            corrupt.CommandText = "UPDATE Games SET MetadataJson = '{\"schemaVersion\":9}'";
            corrupt.ExecuteNonQuery();
        }

        Game game = Assert.Single(await repository.ListGamesAsync());
        Assert.Null(game.Metadata);
        Assert.NotNull(game.Link);
        Assert.Equal(id, (await repository.GetGameAsync(id))!.Id);
    }

    [Fact]
    public async Task UpgradesPopulatedVersionThreeWithAnEmptyAssetTable()
    {
        using TestLibrary directory = new();
        Guid gameId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        CreatePopulatedVersionThree(directory, gameId, guideId);

        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();

        Assert.Equal(4L, ReadUserVersion(directory.Paths.DatabasePath));
        Assert.NotNull(await repository.GetGuideAsync(guideId));
        Assert.Empty(await repository.GetGuideAssetsAsync(guideId));
    }

    [Fact]
    public async Task FailedVersionFourMigrationStaysAtVersionThree()
    {
        using TestLibrary directory = new();
        CreatePopulatedVersionThree(directory, Guid.NewGuid(), Guid.NewGuid());

        await using (SqliteLibraryRepository failing = new(directory.Paths, null, version =>
        {
            if (version == 4) throw new IOException("Injected after the v4 table was created.");
        }))
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => failing.InitializeAsync());
        }

        Assert.Equal(3L, ReadUserVersion(directory.Paths.DatabasePath));
        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand table = connection.CreateCommand();
        table.CommandText = "SELECT count(*) FROM sqlite_master WHERE name = 'GuideAssets'";
        Assert.Equal(0L, (long)table.ExecuteScalar()!);
    }

    [Fact]
    public async Task GuideAssetsRoundTripInOrdinalOrderAndCascadeWithTheGuide()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Assets", null, null);
        Guid guideId = Guid.NewGuid();
        InsertGuide(directory.Paths.DatabasePath, guideId, game.Id);
        InsertAsset(directory.Paths.DatabasePath, guideId, "styles/main.css", "StyleSheet");
        InsertAsset(directory.Paths.DatabasePath, guideId, "Images/map.png", "Image");
        InsertAsset(directory.Paths.DatabasePath, guideId, "guide.html", "EntryHtml");

        IReadOnlyList<GuideAsset> assets = await repository.GetGuideAssetsAsync(guideId);

        Assert.Equal(["Images/map.png", "guide.html", "styles/main.css"], assets.Select(a => a.RequestPath));
        Assert.Equal(
            new GuideAsset("guide.html", "guide.html", GuideAssetKind.EntryHtml, 1, new string('b', 64)),
            assets[1]);

        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        using (SqliteCommand delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM Guides WHERE Id = $id";
            delete.Parameters.AddWithValue("$id", guideId.ToString("N"));
            delete.ExecuteNonQuery();
        }
        Assert.Empty(await repository.GetGuideAssetsAsync(guideId));
    }

    [Theory]
    [InlineData("Other", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("Image", "abc")]
    public async Task GuideAssetChecksRejectBadKindsAndHashes(string kind, string hash)
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Checks", null, null);
        Guid guideId = Guid.NewGuid();
        InsertGuide(directory.Paths.DatabasePath, guideId, game.Id);

        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO GuideAssets (GuideId, RequestPath, RelativePath, Kind, ByteCount, Sha256)
            VALUES ($guide, 'a.png', 'a.png', $kind, 1, $hash)
            """;
        command.Parameters.AddWithValue("$guide", guideId.ToString("N"));
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$hash", hash);
        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
    }

    private static void CreatePopulatedVersionThree(TestLibrary directory, Guid gameId, Guid guideId)
    {
        CreatePopulatedVersionTwo(directory, gameId);
        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = LibrarySchema.Version3;
        command.ExecuteNonQuery();
        connection.Close();
        InsertGuide(directory.Paths.DatabasePath, guideId, gameId);
    }

    private static void InsertAsset(string databasePath, Guid guideId, string requestPath, string kind)
    {
        using SqliteConnection connection = OpenWithForeignKeys(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO GuideAssets (GuideId, RequestPath, RelativePath, Kind, ByteCount, Sha256)
            VALUES ($guide, $request, $request, $kind, 1, $hash)
            """;
        command.Parameters.AddWithValue("$guide", guideId.ToString("N"));
        command.Parameters.AddWithValue("$request", requestPath);
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$hash", new string('b', 64));
        command.ExecuteNonQuery();
    }

    private static void CreatePopulatedVersionTwo(TestLibrary directory, Guid gameId)
    {
        directory.Paths.EnsureCreated();
        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL";
        command.ExecuteNonQuery();
        command.CommandText = LibrarySchema.Version1;
        command.ExecuteNonQuery();
        command.CommandText = LibrarySchema.Version2;
        command.ExecuteNonQuery();
        command.CommandText = """
            INSERT INTO Games (Id, Title, CreatedUtcMs, UpdatedUtcMs)
            VALUES ($id, 'Version two game', $now, $now)
            """;
        command.Parameters.AddWithValue("$id", gameId.ToString("N"));
        command.Parameters.AddWithValue("$now", Now.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }

    private static void InsertRawGame(
        SqliteConnection connection, Guid id, string? provider, string? externalId, string? artwork)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Games (Id, Title, CreatedUtcMs, UpdatedUtcMs,
                ProviderName, ProviderGameId, ArtworkRelativePath)
            VALUES ($id, 'Raw', 0, 0, $provider, $externalId, $artwork)
            """;
        command.Parameters.AddWithValue("$id", id.ToString("N"));
        command.Parameters.AddWithValue("$provider", (object?)provider ?? DBNull.Value);
        command.Parameters.AddWithValue("$externalId", (object?)externalId ?? DBNull.Value);
        command.Parameters.AddWithValue("$artwork", (object?)artwork ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static long ReadUserVersion(string databasePath)
    {
        using SqliteConnection connection = OpenWithForeignKeys(databasePath);
        using SqliteCommand version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        return (long)version.ExecuteScalar()!;
    }

    private static void InsertGuide(
        string databasePath, Guid guideId, Guid gameId, string? managedRoot = null)
    {
        using SqliteConnection connection = OpenWithForeignKeys(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Guides (
                Id, GameId, Title, Format, ManagedRelativeRoot, PrimaryRelativePath,
                ContentSha256, ContentBytes, SourceLabel, TextCodePage,
                ImportedUtcMs, UpdatedUtcMs
            ) VALUES (
                $id, $game, 'Walkthrough', 'Txt', $root, 'guide.txt',
                $hash, 100, NULL, NULL, $now, $now
            );
            INSERT INTO ReadingStates (GuideId) VALUES ($id);
            INSERT INTO ReaderPreferences (GuideId) VALUES ($id);
            """;
        command.Parameters.AddWithValue("$id", guideId.ToString("N"));
        command.Parameters.AddWithValue("$game", gameId.ToString("N"));
        command.Parameters.AddWithValue(
            "$root", managedRoot ?? "content/" + guideId.ToString("N"));
        command.Parameters.AddWithValue("$hash", new string('a', 64));
        command.Parameters.AddWithValue("$now", Now.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }

    private static void CreatePopulatedVersionOne(
        TestLibrary directory, Guid gameId, Guid guideId)
    {
        directory.Paths.EnsureCreated();
        using (SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath))
        {
            using SqliteCommand journal = connection.CreateCommand();
            journal.CommandText = "PRAGMA journal_mode=WAL";
            journal.ExecuteNonQuery();
            using SqliteCommand schema = connection.CreateCommand();
            schema.CommandText = LibrarySchema.Version1;
            schema.ExecuteNonQuery();
            using SqliteCommand game = connection.CreateCommand();
            game.CommandText = """
                INSERT INTO Games (Id, Title, CreatedUtcMs, UpdatedUtcMs)
                VALUES ($id, 'Older game', $now, $now)
                """;
            game.Parameters.AddWithValue("$id", gameId.ToString("N"));
            game.Parameters.AddWithValue("$now", Now.ToUnixTimeMilliseconds());
            game.ExecuteNonQuery();
        }

        InsertGuide(directory.Paths.DatabasePath, guideId, gameId);
        using SqliteConnection reopened = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand state = reopened.CreateCommand();
        state.CommandText = """
            UPDATE ReadingStates
            SET LocatorJson = '{"old":true}', CompletedUtcMs = $now
            WHERE GuideId = $id;
            UPDATE ReaderPreferences SET TextScale = 1.25 WHERE GuideId = $id;
            INSERT INTO Settings (Key, Value) VALUES
                ('Theme', 'Dark'), ('LastActiveGuideId', $id);
            """;
        state.Parameters.AddWithValue("$id", guideId.ToString("N"));
        state.Parameters.AddWithValue("$now", Now.ToUnixTimeMilliseconds());
        state.ExecuteNonQuery();
    }

    [Fact]
    public async Task SweepRemovesArtworkOfDeletedGame()
    {
        using TestLibrary directory = new();
        Guid id = Guid.NewGuid();
        StoredArtwork stored;
        await using (SqliteLibraryRepository repository = new(directory.Paths))
        {
            await repository.InitializeAsync();
            stored = await new ManagedArtworkStore(directory.Paths)
                .StoreAsync(id, Artwork.TestImages.Png(4, 4), default);
            await repository.AddLinkedGameAsync(Linked(id, artwork: stored.RelativePath));
            using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
            using SqliteCommand delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM Games";
            delete.ExecuteNonQuery();
        }

        await using SqliteLibraryRepository reopened = new(directory.Paths);
        await reopened.InitializeAsync();
        Assert.Null(new ManagedArtworkStore(directory.Paths).ResolveFile(stored.RelativePath));
        Assert.Equal(0, reopened.LastStartupReconciliation!.ReviewOrphanCount);
        Assert.Equal(0, reopened.LastStartupReconciliation.ArtworkReviewCount);
    }

    [Fact]
    public async Task StartupKeepsArtworkOfLinkedGame()
    {
        using TestLibrary directory = new();
        Guid id = Guid.NewGuid();
        StoredArtwork stored;
        await using (SqliteLibraryRepository repository = new(directory.Paths))
        {
            await repository.InitializeAsync();
            stored = await new ManagedArtworkStore(directory.Paths)
                .StoreAsync(id, Artwork.TestImages.Png(5, 5), default);
            await repository.AddLinkedGameAsync(Linked(id, artwork: stored.RelativePath));
        }

        await using SqliteLibraryRepository reopened = new(directory.Paths);
        await reopened.InitializeAsync();
        Assert.NotNull(new ManagedArtworkStore(directory.Paths).ResolveFile(stored.RelativePath));
    }

    [Fact]
    public async Task StartupLeavesAnEmptyGameFolderItCannotDelete()
    {
        if (!OperatingSystem.IsWindows()) return;

        using TestLibrary directory = new();
        directory.Paths.EnsureCreated();
        string folder = Path.Combine(directory.Paths.ArtworkRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        // Windows refuses to remove a read-only directory.
        File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.ReadOnly);
        try
        {
            await using SqliteLibraryRepository repository = new(directory.Paths);
            await repository.InitializeAsync();
            Assert.True(Directory.Exists(folder));
            Assert.Empty(await repository.ListGamesAsync());
        }
        finally
        {
            File.SetAttributes(folder, FileAttributes.Directory);
        }
    }

    private static SqliteConnection OpenWithForeignKeys(string databasePath)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString());
        connection.Open();
        using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static void SetCompleted(string databasePath, Guid guideId)
    {
        using SqliteConnection connection = OpenWithForeignKeys(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ReadingStates SET CompletedUtcMs = $now WHERE GuideId = $id
            """;
        command.Parameters.AddWithValue("$now", Now.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$id", guideId.ToString("N"));
        Assert.Equal(1, command.ExecuteNonQuery());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StoreSession(int offset) : IReaderSession
    {
        public static readonly string Hash = new('a', 64);
        private int offset = offset;

        public GuideFormat Format => GuideFormat.Txt;
        public ReaderCapabilities Capabilities => ReaderCapabilities.Scroll;
        public event EventHandler? CapabilitiesChanged { add { } remove { } }
        public event EventHandler<AppearanceRestoredEventArgs>? AppearanceRestored { add { } remove { } }
        public event EventHandler<LocationChangedEventArgs>? LocationChanged;

        public ReaderLocation Current => new(
            GuideFormat.Txt, ReaderLocationCodec.CurrentVersion, Hash,
            new TextPosition(offset, $"line {offset}"), 0.4);

        public void Move()
        {
            offset++;
            LocationChanged?.Invoke(this, new LocationChangedEventArgs());
        }

        public Task<ReaderLocation> GetLocationAsync(CancellationToken token) => Task.FromResult(Current);
        public Task OpenAsync(ManagedGuideSource source, CancellationToken token) =>
            throw new NotSupportedException();
        public Task<RestoreOutcome> RestoreLocationAsync(ReaderLocation location, CancellationToken token) =>
            throw new NotSupportedException();
        public Task ApplyAppearanceAsync(ReaderAppearance appearance, CancellationToken token) =>
            throw new NotSupportedException();
        public Task ExecuteAsync(ReaderAction action, CancellationToken token) =>
            throw new NotSupportedException();
        public ValueTask DisposeAsync() => default;
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TestLibrary : IDisposable
    {
        public TestLibrary()
        {
            Root = Path.Combine(Path.GetTempPath(), "desktop-guides-db-" + Guid.NewGuid());
            Paths = new ManagedPathResolver(Path.Combine(Root, "app-data"));
        }

        public string Root { get; }
        public ManagedPathResolver Paths { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
    }
}
