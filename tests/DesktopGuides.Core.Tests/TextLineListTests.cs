using System.Collections;
using System.Text;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class TextLineListTests
{
    private static TextLineList Lines(string text) =>
        new(TextGuideDocument.Decode(Encoding.UTF8.GetBytes(text)));

    [Theory]
    [InlineData("a", 1)]
    [InlineData("a\nb", 2)]
    [InlineData("a\nb\n", 2)]
    [InlineData("", 0)]
    [InlineData("\n", 1)]
    [InlineData("a\n\n", 2)]
    public void CountExcludesOnlyTheEmptyLineAfterAFinalNewline(string text, int count)
    {
        TextLineList lines = Lines(text);

        Assert.Equal(count, lines.Count);
        Assert.Equal(count, ((ICollection)lines).Count);
    }

    [Fact]
    public void ItemsCarryTheirIndexAndDisplayText()
    {
        TextLineList lines = Lines("a\tb\r\n\fc\n\n");

        Assert.Equal(
            [new TextLineItem(0, "a       b"), new TextLineItem(1, " c"), new TextLineItem(2, "")],
            lines.ToArray());
    }

    [Fact]
    public void TheIndexerBuildsAFreshItemEachTime()
    {
        TextLineList lines = Lines("one\ntwo");

        TextLineItem first = lines[1];
        TextLineItem second = lines[1];

        Assert.Equal(first, second);
        Assert.NotSame(first, second);
        Assert.Equal(first, ((IList)lines)[1]);
    }

    [Fact]
    public void IndexOfUsesTheItemIndex()
    {
        IList lines = Lines("a\nb\nc");

        Assert.Equal(1, lines.IndexOf(new TextLineItem(1, "anything")));
        Assert.True(lines.Contains(new TextLineItem(2, "c")));
    }

    [Fact]
    public void IndexOfRejectsOtherObjectsAndIndexesOutsideTheList()
    {
        IList lines = Lines("a\nb\n");

        Assert.Equal(-1, lines.IndexOf("b"));
        Assert.Equal(-1, lines.IndexOf(null));
        Assert.Equal(-1, lines.IndexOf(new TextLineItem(2, "")));
        Assert.Equal(-1, lines.IndexOf(new TextLineItem(-1, "a")));
        Assert.False(lines.Contains(new TextLineItem(5, "a")));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void AnIndexOutsideTheListThrows(int index)
    {
        TextLineList lines = Lines("a\nb\n");

        Assert.Throws<ArgumentOutOfRangeException>(() => lines[index]);
        Assert.Throws<ArgumentOutOfRangeException>(() => ((IList)lines)[index]);
    }

    [Fact]
    public void TheListCantBeChanged()
    {
        IList lines = Lines("a\nb");
        TextLineItem item = new(0, "a");

        Assert.True(lines.IsReadOnly);
        Assert.True(lines.IsFixedSize);
        Assert.Throws<NotSupportedException>(() => lines.Add(item));
        Assert.Throws<NotSupportedException>(() => lines.Clear());
        Assert.Throws<NotSupportedException>(() => lines.Insert(0, item));
        Assert.Throws<NotSupportedException>(() => lines.Remove(item));
        Assert.Throws<NotSupportedException>(() => lines.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => lines[0] = item);
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void EnumerationYieldsEveryRowInOrder()
    {
        IEnumerable lines = Lines("x\ny\nz\n");

        Assert.Equal(["x", "y", "z"], lines.Cast<TextLineItem>().Select(item => item.Text));
    }

    [Fact]
    public void CopyToFillsTheArrayFromTheIndex()
    {
        ICollection lines = Lines("x\ny");
        object[] target = new object[3];

        lines.CopyTo(target, 1);

        Assert.Null(target[0]);
        Assert.Equal(new TextLineItem(0, "x"), target[1]);
        Assert.Equal(new TextLineItem(1, "y"), target[2]);
    }
}
