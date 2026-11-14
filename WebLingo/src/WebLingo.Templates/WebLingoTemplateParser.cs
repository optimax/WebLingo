using System.Text;
using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Templates;

/// <summary>
/// A page (a <c>.wlp</c>, specification, section 13, "Contracts") read as the WebLingo parser's tokens, and parsed into a
/// program that writes it: its text, each piece between blocks, as a call of <see cref="WebLingoTemplateWriters.Raw"/>
/// with the text as it stands; <c>&lt;% … %&gt;</c> as its statements, a statement spanning several blocks with the text
/// between them in its body; <c>&lt;%= … %&gt;</c> and <c>&lt;%- … %&gt;</c> as a call of
/// <see cref="WebLingoTemplateWriters.Escaped"/> or <see cref="WebLingoTemplateWriters.Raw"/> with their expression; and
/// <c>&lt;%# … %&gt;</c> as nothing. A block's code is read by WebLingo's lexer, so that <c>%&gt;</c> in a string or a
/// comment does not end it (approved 14 November 2026); a <c>&lt;%#</c> block, which holds no code, ends at the first
/// <c>%&gt;</c>. A line that holds only <c>&lt;% … %&gt;</c> and <c>&lt;%# … %&gt;</c> blocks, and spaces, writes
/// nothing, not even its line break.
/// </summary>
public static class WebLingoTemplateParser
{
    private const string Open = "<%";
    private const string Close = "%>";

    private enum BlockKind { Statements, Escaped, Raw, Comment }

    /// <summary>
    /// A piece of the page: text without a line break; a line break (CR LF, LF or CR); or a block, with the span of its
    /// opening (<c>&lt;%</c> and its mark), its code's tokens, and the span of its <c>%&gt;</c>, empty where it has none.
    /// </summary>
    private sealed class Piece(int start, int end)
    {
        public int Start { get; } = start;
        public int End { get; } = end;
        public bool IsBreak { get; init; }
        public BlockKind? Block { get; init; }
        public WebLingoSourceSpan Opening { get; init; }
        public WebLingoSourceSpan Closing { get; init; }
        public List<WebLingoToken> Tokens { get; } = [];
        public bool Dropped { get; set; }
    }

    public static WebLingoSyntaxTree Parse(WebLingoSourceText page)
    {
        var diagnostics = new List<WebLingoDiagnostic>();
        var pieces = Read(page.Text, diagnostics);
        DropSilentLines(page.Text, pieces);
        var tokens = Tokens(page.Text, pieces, diagnostics);
        return new WebLingoParser(page, tokens, diagnostics).ParseCompilationUnit();
    }

    /// <summary>The page as pieces: its text, split at its line breaks, and its blocks, each block's code read by the lexer.</summary>
    private static List<Piece> Read(string text, List<WebLingoDiagnostic> diagnostics)
    {
        var pieces = new List<Piece>();
        var position = 0;
        while (position < text.Length)
        {
            var open = text.IndexOf(Open, position, StringComparison.Ordinal);
            AddText(text, position, open < 0 ? text.Length : open, pieces);
            if (open < 0) break;

            var codeStart = open + Open.Length;
            var kind = codeStart < text.Length
                ? text[codeStart] switch { '=' => BlockKind.Escaped, '-' => BlockKind.Raw, '#' => BlockKind.Comment, _ => BlockKind.Statements }
                : BlockKind.Statements;
            if (kind != BlockKind.Statements) codeStart++;
            var opening = new WebLingoSourceSpan(open, codeStart - open);

            int end;
            Piece block;
            if (kind == BlockKind.Comment)
            {
                // No code: the first %> ends it.
                var close = text.IndexOf(Close, codeStart, StringComparison.Ordinal);
                end = close < 0 ? text.Length : close + Close.Length;
                block = new Piece(open, end)
                {
                    Block = kind, Opening = opening, Closing = new WebLingoSourceSpan(close < 0 ? end : close, close < 0 ? 0 : Close.Length)
                };
            }
            else
            {
                var lexer = new WebLingoLexer(text, codeStart, Close);
                var code = new List<WebLingoToken>();
                WebLingoToken token;
                while ((token = lexer.NextToken()).Kind != WebLingoTokenKind.EndOfFile) code.Add(token);
                diagnostics.AddRange(lexer.Diagnostics);
                end = token.Span.Start + token.Span.Length;
                block = new Piece(open, end) { Block = kind, Opening = opening, Closing = token.Span };
                block.Tokens.AddRange(code);
            }
            if (block.Closing.Length == 0)
            {
                diagnostics.Add(new WebLingoDiagnostic(WebLingoDiagnosticCodes.UnterminatedTemplateBlock, WebLingoDiagnosticSeverity.Error,
                    $"This block has no closing '{Close}'.", opening));
            }
            pieces.Add(block);
            position = end;
        }
        return pieces;
    }

    /// <summary>The text from <paramref name="start"/> to <paramref name="end"/>, as pieces of text and line breaks.</summary>
    private static void AddText(string text, int start, int end, List<Piece> pieces)
    {
        var from = start;
        for (var i = start; i < end; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            if (i > from) pieces.Add(new Piece(from, i));
            var breakEnd = text[i] == '\r' && i + 1 < end && text[i + 1] == '\n' ? i + 2 : i + 1;
            pieces.Add(new Piece(i, breakEnd) { IsBreak = true });
            i = breakEnd - 1;
            from = breakEnd;
        }
        if (end > from) pieces.Add(new Piece(from, end));
    }

    /// <summary>
    /// Drops the text and the line break of each line that holds only statement and comment blocks, and spaces and tabs,
    /// so that statements leave no blank lines. A block that spans lines is one line's.
    /// </summary>
    private static void DropSilentLines(string text, List<Piece> pieces)
    {
        var lineStart = 0;
        for (var i = 0; i <= pieces.Count; i++)
        {
            if (i < pieces.Count && !pieces[i].IsBreak) continue;
            var silent = false;
            for (var j = lineStart; j < i; j++)
            {
                var piece = pieces[j];
                if (piece.Block is { } kind)
                {
                    silent = kind is BlockKind.Statements or BlockKind.Comment;
                    if (!silent) break;
                }
                else if (!IsBlank(text, piece))
                {
                    silent = false;
                    break;
                }
            }
            if (silent)
            {
                for (var j = lineStart; j < i; j++)
                {
                    if (pieces[j].Block is null) pieces[j].Dropped = true;
                }
                if (i < pieces.Count) pieces[i].Dropped = true;
            }
            lineStart = i + 1;
        }
    }

    private static bool IsBlank(string text, Piece piece)
    {
        for (var i = piece.Start; i < piece.End; i++)
        {
            if (text[i] is not (' ' or '\t')) return false;
        }
        return true;
    }

    /// <summary>The program's tokens: each run of text a write of it as it stands, and each block its code, or a write of it.</summary>
    private static List<WebLingoToken> Tokens(string text, List<Piece> pieces, List<WebLingoDiagnostic> diagnostics)
    {
        var tokens = new List<WebLingoToken>();
        var run = new StringBuilder();
        var runStart = -1;
        var runEnd = -1;
        foreach (var piece in pieces)
        {
            if (piece.Block is not { } kind)
            {
                if (piece.Dropped) continue;
                if (runStart < 0) runStart = piece.Start;
                run.Append(text, piece.Start, piece.End - piece.Start);
                runEnd = piece.End;
                continue;
            }
            if (runStart >= 0)
            {
                var span = new WebLingoSourceSpan(runStart, runEnd - runStart);
                var literal = new WebLingoToken(WebLingoTokenKind.StringLiteral, text[runStart..runEnd], span, StringValue: run.ToString());
                AddWrite(tokens, WebLingoTemplateWriters.Raw, span, [literal], span);
                run.Clear();
                runStart = -1;
            }
            switch (kind)
            {
                case BlockKind.Statements:
                    tokens.AddRange(piece.Tokens);
                    break;
                case BlockKind.Escaped or BlockKind.Raw when piece.Tokens.Count == 0:
                    if (piece.Closing.Length > 0)
                    {
                        diagnostics.Add(new WebLingoDiagnostic(WebLingoDiagnosticCodes.UnexpectedToken, WebLingoDiagnosticSeverity.Error,
                            $"Expected an expression, found '{Close}'.", piece.Closing));
                    }
                    break;
                case BlockKind.Escaped or BlockKind.Raw:
                    var writer = kind == BlockKind.Escaped ? WebLingoTemplateWriters.Escaped : WebLingoTemplateWriters.Raw;
                    AddWrite(tokens, writer, piece.Opening, piece.Tokens, piece.Closing);
                    break;
            }
        }
        if (runStart >= 0)
        {
            var span = new WebLingoSourceSpan(runStart, runEnd - runStart);
            var literal = new WebLingoToken(WebLingoTokenKind.StringLiteral, text[runStart..runEnd], span, StringValue: run.ToString());
            AddWrite(tokens, WebLingoTemplateWriters.Raw, span, [literal], span);
        }
        tokens.Add(new WebLingoToken(WebLingoTokenKind.EndOfFile, "", new WebLingoSourceSpan(text.Length, 0)));
        return tokens;
    }

    /// <summary><c>writer(expression);</c>, the call at <paramref name="opening"/>, and its <c>)</c> and <c>;</c> at <paramref name="closing"/>.</summary>
    private static void AddWrite(List<WebLingoToken> tokens, string writer, WebLingoSourceSpan opening, IReadOnlyList<WebLingoToken> expression,
        WebLingoSourceSpan closing)
    {
        tokens.Add(new WebLingoToken(WebLingoTokenKind.Identifier, writer, opening));
        tokens.Add(new WebLingoToken(WebLingoTokenKind.OpenParen, "(", opening));
        tokens.AddRange(expression);
        tokens.Add(new WebLingoToken(WebLingoTokenKind.CloseParen, ")", closing));
        tokens.Add(new WebLingoToken(WebLingoTokenKind.Semicolon, ";", closing));
    }
}
