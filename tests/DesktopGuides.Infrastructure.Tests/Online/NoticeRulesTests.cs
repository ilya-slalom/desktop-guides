using DesktopGuides.Infrastructure.Online;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Online;

public sealed class NoticeRulesTests
{
    [Theory]
    [InlineData("This guide cannot be distributed in printed or electronic media in any way.", NoticeKind.Restrictive)]
    [InlineData("You may print, view, or reproduce this guide for your personal use only.", NoticeKind.Permissive)]
    [InlineData("May not be reproduced in any form, except for personal, private use.", NoticeKind.Permissive)]
    [InlineData("This guide may not be stored on any computer without permission.", NoticeKind.Restrictive)]
    [InlineData("(c) 2003 Example Author", NoticeKind.Unknown)]
    [InlineData("This FAQ may not be posted on any other web site.", NoticeKind.Unknown)]
    [InlineData(null, NoticeKind.Unknown)]
    public void ClassifiesNotices(string? notice, NoticeKind expected) =>
        Assert.Equal(expected, NoticeRules.Classify(notice));

    [Fact]
    public void FindsTheFirstCopyrightBlock()
    {
        string text = "CONTENTS\n1. Start\n2. Copyright\n\nSTART\nGo north.\n\n" +
            "COPYRIGHT\n(c) 2003 Example Author.\nAll rights reserved.\n\nTHE END\n";
        Assert.Equal("COPYRIGHT\n(c) 2003 Example Author.\nAll rights reserved.", NoticeRules.Find(text));
        Assert.Null(NoticeRules.Find("No notice here.\nJust text."));
    }

    [Fact]
    public void ALongBlockIsCutAt30Lines()
    {
        string text = "(c) 2003 Example Author\n" + string.Join("\n", Enumerable.Range(1, 50).Select(n => $"Line {n}"));
        Assert.Equal(30, NoticeRules.Find(text)!.Split('\n').Length);
    }
}
