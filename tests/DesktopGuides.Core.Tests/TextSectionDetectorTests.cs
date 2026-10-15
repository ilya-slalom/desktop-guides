using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class TextSectionDetectorTests
{
    private static string Filler(int lines) =>
        string.Concat(Enumerable.Range(1, lines).Select(n => $"Step {n:000} - keep walking along the path.\n"));

    private static IReadOnlyList<DetectedSection> Detect(string text) =>
        TextSectionDetector.Detect(text, CancellationToken.None);

    private static string Boxed(string title, string code) =>
        $"==========================================\n {title,-30} [{code}]\n==========================================\n";

    private const string Contents =
        "GUIDE TITLE\n\nCONTENTS\n" +
        "  Introduction ............ [INT00]\n" +
        "  Forest .................. [WLK01]\n" +
        "  Castle .................. [WLK02]\n" +
        "  Bosses .................. [BOS00]\n\n";

    private static string CodedGuide() =>
        Contents +
        Boxed("Introduction", "INT00") + Filler(40) +
        Boxed("Forest", "WLK01") + Filler(40) +
        Boxed("Castle", "WLK02") + Filler(40) +
        Boxed("Bosses", "BOS00") + Filler(40);

    [Fact]
    public void SearchCodesFindTheSectionsAfterTheContents()
    {
        string text = CodedGuide();

        IReadOnlyList<DetectedSection> sections = Detect(text);

        Assert.Equal(["Introduction", "Forest", "Castle", "Bosses"], sections.Select(s => s.Title));
        Assert.All(sections, s => Assert.Equal(1, s.Level));
        Assert.Equal(text.IndexOf(" Castle", text.IndexOf("[WLK02]", StringComparison.Ordinal) + 1, StringComparison.Ordinal),
            sections[2].Offset);
    }

    [Fact]
    public void ACrossReferenceBeforeTheHeadingIsSkipped()
    {
        string text = Contents +
            Boxed("Introduction", "INT00") + "If you get lost, see [WLK02] for the castle.\n" + Filler(40) +
            Boxed("Forest", "WLK01") + Filler(40) +
            Boxed("Castle", "WLK02") + Filler(40) +
            Boxed("Bosses", "BOS00") + Filler(40);

        DetectedSection castle = Detect(text).Single(s => s.Title == "Castle");

        Assert.Equal(text.LastIndexOf(" Castle", StringComparison.Ordinal), castle.Offset);
    }

    [Fact]
    public void DottedSearchCodesNestByDepth()
    {
        string text = "CONTENTS\n  Basics [1.0]\n  Controls [1.1]\n  Walkthrough [2.0]\n\n" +
            Boxed("Basics", "1.0") + Filler(30) + Boxed("Controls", "1.1") + Filler(30) +
            Boxed("Walkthrough", "2.0") + Filler(30);

        Assert.Equal([1, 2, 1], Detect(text).Select(s => s.Level));
    }

    [Fact]
    public void UnderlinedHeadingsAreFound()
    {
        string text = "Intro text.\n" + Filler(5) +
            "Walkthrough\n===========\n" + Filler(30) +
            "The forest\n----------\n" + Filler(30) +
            "Bosses\n======\n" + Filler(30);

        IReadOnlyList<DetectedSection> sections = Detect(text);

        Assert.Equal(["Walkthrough", "The forest", "Bosses"], sections.Select(s => s.Title));
        Assert.Equal([1, 2, 1], sections.Select(s => s.Level));
        Assert.Equal(text.IndexOf("The forest", StringComparison.Ordinal), sections[1].Offset);
    }

    [Fact]
    public void BoxedHeadingsAreFoundWhateverTheRuleLength()
    {
        string rule = new string('=', 70) + "\n";
        string text = Filler(5) + rule + " WALKTHROUGH\n" + rule + Filler(30) +
            rule + " BOSSES\n" + rule + Filler(30) + rule + " SECRETS\n" + rule + Filler(30);

        Assert.Equal(["WALKTHROUGH", "BOSSES", "SECRETS"], Detect(text).Select(s => s.Title));
    }

    [Fact]
    public void NumberedHeadingsNestAndSkipTheContentsCopy()
    {
        string text = "CONTENTS\n1.0 Basics ........ 3\n1.1 Controls ...... 4\n2.0 Walkthrough ... 9\n\n" +
            Filler(10) + "1.0 Basics\n" + Filler(30) + "1.1 Controls\n" + Filler(30) + "2.0 Walkthrough\n" + Filler(30);

        IReadOnlyList<DetectedSection> sections = Detect(text);

        Assert.Equal(["Basics", "Controls", "Walkthrough"], sections.Select(s => s.Title));
        Assert.Equal([1, 2, 1], sections.Select(s => s.Level));
        Assert.Equal(text.LastIndexOf("1.0 Basics", StringComparison.Ordinal), sections[0].Offset);
    }

    [Fact]
    public void SearchCodesWinOverHeadings()
    {
        string text = CodedGuide() + "Appendix\n========\n" + Filler(5);

        Assert.DoesNotContain(Detect(text), s => s.Title == "Appendix");
    }

    [Fact]
    public void AsciiArtIsNotAnOutline() =>
        Assert.Empty(Detect(Filler(20) +
            "+-------+-------+\n|  Hall |  Keep |\n+-------+-------+\n|  Well |  Gate |\n+-------+-------+\n" +
            "   /\\\n  /  \\\n /____\\\n" + Filler(40)));

    [Fact]
    public void TwoSectionsAreNotEnough() =>
        Assert.Empty(Detect("Walkthrough\n===========\n" + Filler(30) + "Bosses\n======\n" + Filler(30)));

    [Fact]
    public void NumberedProseIsNotAnOutline() =>
        Assert.Empty(Detect(Filler(10) +
            "1. Go to the door and open it.\n" + Filler(20) +
            "2. Take the key from the table.\n" + Filler(20) +
            "3. Return to the hall and use it.\n" + Filler(20)));

    [Fact]
    public void AContentsBlockWithNoBodyIsNotAnOutline() =>
        Assert.Empty(Detect(Contents + Filler(200)));

    [Fact]
    public void SectionsOnlyInTheFirstTenthAreNotAnOutline() =>
        Assert.Empty(Detect("Aaa\n===\n" + "Bbb\n===\n" + "Ccc\n===\n" + "Ddd\n===\n" + Filler(400)));

    [Fact]
    public void ACancelledDetectionFindsNothing()
    {
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Empty(TextSectionDetector.Detect(CodedGuide(), cancelled.Token));
    }
}
