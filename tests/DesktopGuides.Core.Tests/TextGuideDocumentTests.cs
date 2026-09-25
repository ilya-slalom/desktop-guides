using System.Text;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class TextGuideDocumentTests
{
    [Fact]
    public void DecodesBomAndNormalizesNewlinesWithoutChangingWhitespace()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(" A  \r\n\tB\rC\n")];

        TextGuideDocument document = TextGuideDocument.Decode(bytes);

        Assert.Equal(" A  \n\tB\nC\n", document.Text);
        Assert.Equal("utf-8", document.EncodingName);
        Assert.Equal([0, 5, 8, 10], document.LineStarts);
    }

    [Fact]
    public void InvalidUtf8RequiresAnExplicitEncoding()
    {
        byte[] bytes = [0x48, 0x82, 0x0A];

        Assert.Throws<EncodingSelectionRequiredException>(() => TextGuideDocument.Decode(bytes));
    }

    [Fact]
    public void DecodesSelectedCp437WithoutChangingAsciiLayout()
    {
        byte[] bytes = [0x41, 0x20, 0x82, 0x0D, 0x0A, 0x20, 0x42];

        TextGuideDocument document = TextGuideDocument.Decode(bytes, 437);

        Assert.Equal("A é\n B", document.Text);
        Assert.Equal("ibm437", document.EncodingName);
    }

    [Fact]
    public void IndexesLogicalLinesByNormalizedCharacterOffset()
    {
        TextGuideDocument document = TextGuideDocument.Decode(Encoding.UTF8.GetBytes("A\nBC\n"));

        Assert.Equal([0, 2, 5], document.LineStarts);
        Assert.Equal(0, document.LineAtOffset(0));
        Assert.Equal(1, document.LineAtOffset(3));
        Assert.Equal(2, document.LineAtOffset(5));
    }

    [Fact]
    public void RestoresExactOffsetForUnchangedContent()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("alpha\nbeta\ngamma\n");
        TextLocation location = TextGuideDocument.Decode(bytes).Capture(8);

        TextRestoreResult result = TextGuideDocument.Decode(bytes).Restore(location);

        Assert.Equal(8, result.CharacterOffset);
        Assert.Equal(TextRestoreKind.Exact, result.Kind);
    }

    [Fact]
    public void FindsNearbyContextAfterContentChanges()
    {
        TextGuideDocument original = TextGuideDocument.Decode(
            Encoding.UTF8.GetBytes("first line\nunique middle marker\nlast line\n"));
        TextLocation location = original.Capture(20);
        TextGuideDocument revised = TextGuideDocument.Decode(
            Encoding.UTF8.GetBytes("new introduction\nfirst line\nunique middle marker\nlast line\n"));

        TextRestoreResult result = revised.Restore(location);

        Assert.Equal(37, result.CharacterOffset);
        Assert.Equal(TextRestoreKind.Context, result.Kind);
    }

    [Fact]
    public void ChoosesTheNearestRepeatedContextAfterContentChanges()
    {
        TextGuideDocument document = TextGuideDocument.Decode(
            Encoding.UTF8.GetBytes("target\n" + new string('x', 60) + "target\n"));
        TextLocation location = new(1, "different", "utf-8", 70, "target", 0, 0.5);

        TextRestoreResult result = document.Restore(location);

        Assert.Equal(67, result.CharacterOffset);
        Assert.Equal(TextRestoreKind.Context, result.Kind);
    }

    [Fact]
    public void FallsBackToFractionWhenContextCannotBeFound()
    {
        TextGuideDocument document = TextGuideDocument.Decode(Encoding.UTF8.GetBytes("one\ntwo\n"));
        TextLocation location = new(1, "different", "utf-8", 4, "missing", 0, 0.5);

        TextRestoreResult result = document.Restore(location);

        Assert.Equal(4, result.CharacterOffset);
        Assert.Equal(TextRestoreKind.Fraction, result.Kind);
    }
}
