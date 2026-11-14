using WebLingo.Core;
using Xunit;
namespace WebLingo.Language.Tests;

public sealed class SourceTextTests
{
    [Theory]
    [InlineData("ab\ncd")]
    [InlineData("ab\r\ncd")]
    [InlineData("ab\rcd")]
    public void EveryLineEndingStartsALine(string text)
    {
        var source = new WebLingoSourceText(text);
        Assert.Equal(2, source.LineCount);
        Assert.Equal(new WebLingoSourceLocation(0, 1, 1), source.GetLocation(0));
        Assert.Equal(new WebLingoSourceLocation(1, 1, 2), source.GetLocation(1));
        var d = text.IndexOf('d');
        Assert.Equal(new WebLingoSourceLocation(d, 2, 2), source.GetLocation(d));
    }

    [Fact]
    public void TheLineEndingBelongsToItsLine()
    {
        var source = new WebLingoSourceText("ab\r\ncd");
        Assert.Equal((1, 3), Line(source, 2));
        Assert.Equal((1, 4), Line(source, 3));
        Assert.Equal((2, 1), Line(source, 4));
    }

    [Fact]
    public void ATextEndingWithALineEndingHasAnEmptyLastLine()
    {
        var source = new WebLingoSourceText("a\n");
        Assert.Equal(2, source.LineCount);
        Assert.Equal((2, 1), Line(source, 2));
    }

    [Fact]
    public void AColumnCountsScalarValuesNotCodeUnits()
    {
        var source = new WebLingoSourceText("\U0001F600x");
        Assert.Equal((1, 2), Line(source, 2));
    }

    [Fact]
    public void ATabIsOneColumn()
    {
        Assert.Equal((1, 2), Line(new WebLingoSourceText("\tx"), 1));
    }

    [Fact]
    public void PositionsOutsideTheTextAreClamped()
    {
        var source = new WebLingoSourceText("ab");
        Assert.Equal(new WebLingoSourceLocation(0, 1, 1), source.GetLocation(-5));
        Assert.Equal(new WebLingoSourceLocation(2, 1, 3), source.GetLocation(99));
    }

    [Fact]
    public void AnEmptyTextHasOneLine()
    {
        var source = new WebLingoSourceText("");
        Assert.Equal(1, source.LineCount);
        Assert.Equal(new WebLingoSourceLocation(0, 1, 1), source.GetLocation(0));
    }

    [Fact]
    public void LineStartsAreKnown()
    {
        var source = new WebLingoSourceText("a\nbc\r\nd");
        Assert.Equal(new[] { 0, 2, 6 }, Enumerable.Range(1, source.LineCount).Select(source.GetLineStart));
    }

    private static (int Line, int Column) Line(WebLingoSourceText source, int offset)
    {
        var location = source.GetLocation(offset);
        return (location.Line, location.Column);
    }
}
