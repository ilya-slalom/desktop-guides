using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class DiagnosticFileTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("dg-diagnostic-").FullName;

    // A directory in the file's place makes the replace fail on every OS,
    // as a test's open handle does on Windows. It goes before the first
    // retry, so no timing decides whether the reader let go in time.
    [Fact]
    public void AReplaceBlockedForAMomentStillLands()
    {
        string path = Path.Combine(folder, "position.json");
        Directory.CreateDirectory(path);
        int retries = 0;

        DiagnosticFile.Write(path, "{\"a\":2}", beforeRetry: () =>
        {
            if (retries++ == 0) Directory.Delete(path);
        });

        Assert.Equal(1, retries);
        Assert.Equal("{\"a\":2}", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void AReplaceThatStaysBlockedThrows()
    {
        string path = Path.Combine(folder, "position.json");
        Directory.CreateDirectory(path);

        Exception error = Assert.ThrowsAny<Exception>(() => DiagnosticFile.Write(path, "{}"));

        Assert.True(error is IOException or UnauthorizedAccessException, error.GetType().Name);
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);
}
