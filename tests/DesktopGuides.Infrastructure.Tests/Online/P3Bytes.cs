using DesktopGuides.Infrastructure.Tests.Import;

namespace DesktopGuides.Infrastructure.Tests.Online;

// S22 PR 5: a P3 fixture's bytes, for pages in a code page other than UTF-8.
internal static class P3Bytes
{
    public static byte[] Read(string relative) => File.ReadAllBytes(Path.GetFullPath(
        Path.Combine(P0Fixtures.Root, "..", "p3", relative.Replace('/', Path.DirectorySeparatorChar))));
}
