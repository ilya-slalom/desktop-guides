using System.Text;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class TextLineViewTests
{
    private static TextGuideDocument Document(string text) =>
        TextGuideDocument.Decode(Encoding.UTF8.GetBytes(text));

    [Theory]
    [InlineData("\tx", "", 8)]
    [InlineData("abc\tx", "abc", 5)]
    [InlineData("abcdefg\tx", "abcdefg", 1)]
    [InlineData("abcdefgh\tx", "abcdefgh", 8)]
    [InlineData("0123456789\tx", "0123456789", 6)]
    public void ATabExpandsToTheNextEightColumnStop(string line, string before, int spaces)
    {
        Assert.Equal(before + new string(' ', spaces) + "x", TextLineView.DisplayText(Document(line), 0));
    }

    [Fact]
    public void ConsecutiveTabsEachReachTheNextStop()
    {
        Assert.Equal("a" + new string(' ', 15) + "b", TextLineView.DisplayText(Document("a\t\tb"), 0));
    }

    [Theory]
    [InlineData("a\fb")]
    [InlineData("a\u001Ab")]
    [InlineData("a\u001Bb")]
    [InlineData("a\u007Fb")]
    [InlineData("a\0b")]
    public void AControlCharacterShowsAsOneSpace(string line)
    {
        Assert.Equal("a b", TextLineView.DisplayText(Document(line), 0));
    }

    [Fact]
    public void SpacesAndOtherCharactersAreKept()
    {
        const string line = "  lead  mid é ─┼─  trail  ";

        Assert.Equal(line, TextLineView.DisplayText(Document(line), 0));
    }

    [Fact]
    public void ALineExcludesItsNewline()
    {
        TextGuideDocument document = Document("one\r\ntwo\n");

        Assert.Equal("one", TextLineView.DisplayText(document, 0));
        Assert.Equal("two", TextLineView.DisplayText(document, 1));
        Assert.Equal("", TextLineView.DisplayText(document, 2));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void ALineOutsideTheDocumentThrows(int line)
    {
        TextGuideDocument document = Document("one\ntwo\n");

        Assert.Throws<ArgumentOutOfRangeException>(() => TextLineView.DisplayText(document, line));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextLineView.DisplayColumns(document, line));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextLineView.SourceOffset(document, line, 0));
    }

    [Fact]
    public void DisplayColumnsIsTheDisplayTextLength()
    {
        TextGuideDocument document = Document("\tx\nab\t\tc\n\fz\u007F\n\nplain\n0123456789\t!");

        for (int line = 0; line < document.LineStarts.Count; line++)
        {
            Assert.Equal(TextLineView.DisplayText(document, line).Length, TextLineView.DisplayColumns(document, line));
        }
    }

    [Fact]
    public void SourceOffsetRoundTripsEachCharactersColumn()
    {
        // Line 1 is "ab\tc", starting at offset 2; its characters sit at columns 0, 1, 2 and 8.
        TextGuideDocument document = Document("x\nab\tc");

        Assert.Equal(2, TextLineView.SourceOffset(document, 1, 0));
        Assert.Equal(3, TextLineView.SourceOffset(document, 1, 1));
        Assert.Equal(4, TextLineView.SourceOffset(document, 1, 2));
        Assert.Equal(5, TextLineView.SourceOffset(document, 1, 8));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    public void AColumnInsideATabMapsToTheTab(int column)
    {
        Assert.Equal(4, TextLineView.SourceOffset(Document("x\nab\tc"), 1, column));
    }

    [Theory]
    [InlineData(-5, 2)]
    [InlineData(9, 6)]
    [InlineData(1000, 6)]
    public void AColumnOutsideTheLineClampsToIt(int column, int offset)
    {
        Assert.Equal(offset, TextLineView.SourceOffset(Document("x\nab\tc"), 1, column));
    }
}
