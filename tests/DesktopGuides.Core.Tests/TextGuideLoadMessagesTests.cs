using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class TextGuideLoadMessagesTests
{
    [Theory]
    [InlineData(TextGuideLoadError.Missing, "This guide's file is missing from the library.")]
    [InlineData(TextGuideLoadError.TooLarge, "This guide is larger than the 64 MB limit for text files.")]
    [InlineData(TextGuideLoadError.Unreadable, "This guide's file can't be opened. Close any app that's using it, then open the guide again.")]
    [InlineData(TextGuideLoadError.InvalidMetadata, "This guide's saved details are damaged, so it can't be opened.")]
    [InlineData(TextGuideLoadError.NotUtf8, "This guide isn't valid UTF-8 text, so it can't be opened.")]
    [InlineData(TextGuideLoadError.Undecodable, "This guide can't be read with its saved encoding.")]
    public void EachErrorHasOneSentence(TextGuideLoadError error, string message)
    {
        Assert.Equal(message, TextGuideLoadMessages.For(error));
    }

    [Fact]
    public void EveryErrorIsCovered()
    {
        Assert.All(Enum.GetValues<TextGuideLoadError>(), error =>
            Assert.False(string.IsNullOrWhiteSpace(TextGuideLoadMessages.For(error))));
    }

    [Fact]
    public void AnUnknownErrorThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TextGuideLoadMessages.For((TextGuideLoadError)99));
    }
}
