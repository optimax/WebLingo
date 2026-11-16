using Xunit;
namespace WebLingo.Runtime.Tests;

/// <summary>
/// The portable core's own text codec (<see cref="WebLingoTextCodec"/>): UTF-8, UTF-16, US-ASCII and ISO-8859-1, found by
/// the web's names, decoding without failing, what is no character U+FFFD, and encoding without failing.
/// </summary>
public sealed class TextCodecTests
{
    [Theory]
    [InlineData("utf-8", WebLingoCharset.Utf8)]
    [InlineData("UTF8", WebLingoCharset.Utf8)]
    [InlineData("utf-16", WebLingoCharset.Utf16LittleEndian)]
    [InlineData("UTF-16LE", WebLingoCharset.Utf16LittleEndian)]
    [InlineData("utf-16be", WebLingoCharset.Utf16BigEndian)]
    [InlineData("US-ASCII", WebLingoCharset.Ascii)]
    [InlineData("ISO-8859-1", WebLingoCharset.Latin1)]
    [InlineData("latin1", WebLingoCharset.Latin1)]
    public void ACharsetIsFoundByItsNames(string name, WebLingoCharset charset)
    {
        Assert.True(WebLingoTextCodec.TryFind(name, out var found));
        Assert.Equal(charset, found);
    }

    [Theory]
    [InlineData("klingon")]
    [InlineData("windows-1252")]
    [InlineData("")]
    public void AnUnknownCharsetIsNotFound(string name) => Assert.False(WebLingoTextCodec.TryFind(name, out _));

    [Theory]
    [InlineData(WebLingoCharset.Utf8)]
    [InlineData(WebLingoCharset.Utf16LittleEndian)]
    [InlineData(WebLingoCharset.Utf16BigEndian)]
    public void UnicodeTextGoesThereAndBack(WebLingoCharset charset)
    {
        const string text = "tea, thé, 茶, 🍵";
        Assert.Equal(text, WebLingoTextCodec.Decode(WebLingoTextCodec.Encode(text, charset), charset));
    }

    [Fact]
    public void Utf8IsTheWebsUtf8()
    {
        Assert.Equal([0x63, 0x61, 0x66, 0xC3, 0xA9, 0xF0, 0x9F, 0x8D, 0xB5], WebLingoTextCodec.Encode("café🍵", WebLingoCharset.Utf8));
        // A byte order mark is dropped; a byte no sequence begins, a sequence cut short, an overlong form, a surrogate, and a
        // value beyond U+10FFFF are each U+FFFD.
        Assert.Equal("a", WebLingoTextCodec.Decode([0xEF, 0xBB, 0xBF, 0x61], WebLingoCharset.Utf8));
        Assert.Equal("a\uFFFDb", WebLingoTextCodec.Decode([0x61, 0x80, 0x62], WebLingoCharset.Utf8));
        Assert.Equal("a\uFFFDb", WebLingoTextCodec.Decode([0x61, 0xE2, 0x82, 0x62], WebLingoCharset.Utf8));
        Assert.Equal("\uFFFD\uFFFD", WebLingoTextCodec.Decode([0xC0, 0xAF], WebLingoCharset.Utf8));
        Assert.Equal("\uFFFD\uFFFD\uFFFD", WebLingoTextCodec.Decode([0xED, 0xA0, 0x80], WebLingoCharset.Utf8));
        Assert.Equal("\uFFFD\uFFFD\uFFFD\uFFFD", WebLingoTextCodec.Decode([0xF4, 0x90, 0x80, 0x80], WebLingoCharset.Utf8));
        Assert.Equal("\uFFFD", WebLingoTextCodec.Decode([0xE2, 0x82], WebLingoCharset.Utf8));
        // A lone surrogate is encoded as U+FFFD.
        Assert.Equal([0xEF, 0xBF, 0xBD], WebLingoTextCodec.Encode("\uD800", WebLingoCharset.Utf8));
    }

    [Fact]
    public void Utf16HonoursItsByteOrderMark()
    {
        Assert.Equal([0x61, 0x00, 0x3C, 0xD8, 0x75, 0xDF], WebLingoTextCodec.Encode("a🍵", WebLingoCharset.Utf16LittleEndian));
        Assert.Equal([0x00, 0x61], WebLingoTextCodec.Encode("a", WebLingoCharset.Utf16BigEndian));
        Assert.Equal("a", WebLingoTextCodec.Decode([0xFE, 0xFF, 0x00, 0x61], WebLingoCharset.Utf16LittleEndian));
        Assert.Equal("a\uFFFD", WebLingoTextCodec.Decode([0x61, 0x00, 0x62], WebLingoCharset.Utf16LittleEndian));
        Assert.Equal("\uFFFDa", WebLingoTextCodec.Decode([0x3D, 0xD8, 0x61, 0x00], WebLingoCharset.Utf16LittleEndian));
    }

    [Fact]
    public void AsciiAndLatin1HoldWhatTheyCan()
    {
        Assert.Equal("café", WebLingoTextCodec.Decode([0x63, 0x61, 0x66, 0xE9], WebLingoCharset.Latin1));
        Assert.Equal("caf\uFFFD", WebLingoTextCodec.Decode([0x63, 0x61, 0x66, 0xE9], WebLingoCharset.Ascii));
        Assert.Equal([0x63, 0xE9, 0x3F], WebLingoTextCodec.Encode("cé茶", WebLingoCharset.Latin1));
        Assert.Equal([0x63, 0x3F, 0x3F], WebLingoTextCodec.Encode("cé🍵", WebLingoCharset.Ascii));
    }
}
