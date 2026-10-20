using DesktopGuides.Infrastructure.Tests.Import;

namespace DesktopGuides.Infrastructure.Tests.Online;

// S22: synthetic source pages, written for the tests with the sites'
// structure and invented text.
internal static class P3Fixtures
{
    public static string Read(string relative) => File.ReadAllText(Path.GetFullPath(
        Path.Combine(P0Fixtures.Root, "..", "p3", relative.Replace('/', Path.DirectorySeparatorChar))));
}
