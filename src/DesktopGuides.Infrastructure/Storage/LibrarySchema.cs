namespace DesktopGuides.Infrastructure.Storage;

internal static class LibrarySchema
{
    public const string Version1 = """
        CREATE TABLE Games (
            Id TEXT PRIMARY KEY CHECK (length(Id) = 32),
            Title TEXT NOT NULL CHECK (length(Title) BETWEEN 1 AND 160),
            Platform TEXT CHECK (Platform IS NULL OR length(Platform) <= 80),
            Notes TEXT CHECK (Notes IS NULL OR length(Notes) <= 2000),
            CreatedUtcMs INTEGER NOT NULL CHECK (CreatedUtcMs >= 0),
            UpdatedUtcMs INTEGER NOT NULL CHECK (UpdatedUtcMs >= 0)
        );

        CREATE TABLE Guides (
            Id TEXT PRIMARY KEY CHECK (length(Id) = 32),
            GameId TEXT NOT NULL REFERENCES Games(Id) ON DELETE CASCADE,
            Title TEXT NOT NULL CHECK (length(Title) BETWEEN 1 AND 200),
            Format TEXT NOT NULL CHECK (Format IN ('Txt', 'Html', 'Pdf')),
            ManagedRelativeRoot TEXT NOT NULL UNIQUE,
            PrimaryRelativePath TEXT NOT NULL,
            ContentSha256 TEXT NOT NULL CHECK (length(ContentSha256) = 64),
            ContentBytes INTEGER NOT NULL CHECK (ContentBytes >= 0),
            SourceLabel TEXT CHECK (SourceLabel IS NULL OR length(SourceLabel) <= 255),
            TextCodePage INTEGER CHECK (
                TextCodePage IS NULL OR (Format = 'Txt' AND TextCodePage IN (437, 1252))
            ),
            ImportedUtcMs INTEGER NOT NULL CHECK (ImportedUtcMs >= 0),
            UpdatedUtcMs INTEGER NOT NULL CHECK (UpdatedUtcMs >= 0)
        );

        CREATE INDEX IX_Guides_GameId ON Guides(GameId);

        CREATE TABLE ReadingStates (
            GuideId TEXT PRIMARY KEY REFERENCES Guides(Id) ON DELETE CASCADE,
            LocatorJson TEXT CHECK (LocatorJson IS NULL OR length(LocatorJson) <= 4096),
            EstimatedFraction REAL CHECK (
                EstimatedFraction IS NULL OR EstimatedFraction BETWEEN 0 AND 1
            ),
            LastOpenedUtcMs INTEGER CHECK (LastOpenedUtcMs IS NULL OR LastOpenedUtcMs >= 0),
            CompletedUtcMs INTEGER CHECK (CompletedUtcMs IS NULL OR CompletedUtcMs >= 0)
        );

        CREATE TABLE ReaderPreferences (
            GuideId TEXT PRIMARY KEY REFERENCES Guides(Id) ON DELETE CASCADE,
            TextScale REAL CHECK (TextScale IS NULL OR TextScale BETWEEN 0.75 AND 2)
        );

        CREATE TABLE Settings (
            Key TEXT PRIMARY KEY,
            Value TEXT NOT NULL
        );

        CREATE TABLE FileOperations (
            Id TEXT PRIMARY KEY CHECK (length(Id) = 32),
            Kind TEXT NOT NULL CHECK (Kind IN ('Import', 'DeleteGuide', 'DeleteGame')),
            Phase TEXT NOT NULL CHECK (Phase IN ('Prepared', 'Committed')),
            ManifestJson TEXT NOT NULL,
            CreatedUtcMs INTEGER NOT NULL CHECK (CreatedUtcMs >= 0)
        );

        PRAGMA user_version = 1;
        """;
}
