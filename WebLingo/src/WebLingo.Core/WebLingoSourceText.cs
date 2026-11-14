namespace WebLingo.Core;

/// <summary>
/// A source text and its line map, which turns positions into the one-based lines and columns diagnostics
/// report. Positions are .NET string indices (UTF-16 code units). A line ends at CRLF, LF, or CR, and a column
/// counts the Unicode scalar values from the start of its line. Both choices are proposed (specification,
/// section 15.2), and this class is the one place that makes them.
/// </summary>
public sealed class WebLingoSourceText
{
    private readonly int[] _lineStarts;

    public WebLingoSourceText(string text, string name = "")
    {
        Text = text;
        Name = name;
        _lineStarts = ComputeLineStarts(text);
    }

    public string Text { get; }
    public string Name { get; }
    public int LineCount => _lineStarts.Length;

    /// <summary>The position where a one-based line starts.</summary>
    public int GetLineStart(int line) => _lineStarts[line - 1];

    /// <summary>The line and column of a position; a position outside the text is clamped to it.</summary>
    public WebLingoSourceLocation GetLocation(int offset)
    {
        if (offset < 0) offset = 0;
        if (offset > Text.Length) offset = Text.Length;
        var lineIndex = FindLineIndex(offset);
        var column = 1;
        for (var i = _lineStarts[lineIndex]; i < offset; i++)
        {
            // A surrogate pair is one scalar value: count it at its high surrogate only.
            if (char.IsLowSurrogate(Text[i]) && i > _lineStarts[lineIndex] && char.IsHighSurrogate(Text[i - 1])) continue;
            column++;
        }
        return new WebLingoSourceLocation(offset, lineIndex + 1, column);
    }

    private int FindLineIndex(int offset)
    {
        var low = 0;
        var high = _lineStarts.Length - 1;
        while (low < high)
        {
            var middle = low + (high - low + 1) / 2;
            if (_lineStarts[middle] <= offset) low = middle;
            else high = middle - 1;
        }
        return low;
    }

    private static int[] ComputeLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                starts.Add(i + 1);
            }
            else if (c == '\n')
            {
                starts.Add(i + 1);
            }
        }
        return starts.ToArray();
    }
}
