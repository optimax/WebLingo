using System.Text;
using WebLingo.Compiler;
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
/// <para>
/// <c>include "path";</c> in a block's code is textual: the template the path names, found by the host's resolver, is
/// read in its place, as if its text stood there, so that it sees the variables there. Each file's positions are its own
/// among the program's (<see cref="WebLingoTemplateFiles"/>).
/// </para>
/// </summary>
public sealed class WebLingoTemplateParser
{
    /// <summary>How deep includes may nest (WL5004); arbitrary, and easy to change.</summary>
    public const int MaxIncludeDepth = 64;
    /// <summary>How much text, in UTF-16 code units, a page's includes may read in all, each include counted (WL5004); arbitrary.</summary>
    public const long MaxIncludedLength = 16L * 1024 * 1024;

    private const string Open = "<%";
    private const string Close = "%>";
    private const string Include = "include";

    private enum BlockKind { Statements, Escaped, Raw, Comment }

    /// <summary>
    /// A piece of a file: text without a line break; a line break (CR LF, LF or CR); or a block, with the span of its
    /// opening (<c>&lt;%</c> and its mark), its code's tokens, and the span of its <c>%&gt;</c>, empty where it has none.
    /// Spans are the file's own.
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

    private readonly IWebLingoSourceResolver? _resolver;
    private readonly WebLingoTemplateFiles _files;
    private readonly List<WebLingoDiagnostic> _diagnostics = [];
    private readonly List<WebLingoToken> _tokens = [];
    private readonly Dictionary<string, List<Piece>> _read = new(StringComparer.Ordinal);
    private readonly List<string> _including = [];
    private long _includedLength;

    private WebLingoTemplateParser(WebLingoSourceText page, IWebLingoSourceResolver? resolver)
    {
        _resolver = resolver;
        _files = new WebLingoTemplateFiles(page);
    }

    /// <summary>A page that includes nothing.</summary>
    public static WebLingoSyntaxTree Parse(WebLingoSourceText page) => Parse(page, null, out _);

    /// <summary>
    /// A page, the templates it includes found by <paramref name="resolver"/>: from the name of the file that includes one
    /// and its path, the template's name, which identifies it, and its text; with a null resolver, an include is WL5002.
    /// The tree's positions are the program's: <paramref name="files"/> says which file each is in.
    /// </summary>
    public static WebLingoSyntaxTree Parse(WebLingoSourceText page, IWebLingoSourceResolver? resolver, out WebLingoTemplateFiles files)
    {
        var parser = new WebLingoTemplateParser(page, resolver);
        parser._including.Add(page.Name);
        parser.Emit(page.Name, page.Text, parser.Read(page.Text, 0), 0);
        parser._tokens.Add(new WebLingoToken(WebLingoTokenKind.EndOfFile, "", new WebLingoSourceSpan(page.Text.Length, 0)));
        files = parser._files;
        return new WebLingoParser(page, parser._tokens, parser._diagnostics).ParseCompilationUnit();
    }

    /// <summary>
    /// A file as pieces: its text, split at its line breaks, and its blocks, each block's code read by the lexer, with the
    /// lines that write nothing marked. Its diagnostics are reported at the file's positions in the program.
    /// </summary>
    private List<Piece> Read(string text, int shift)
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
                foreach (var diagnostic in lexer.Diagnostics) _diagnostics.Add(diagnostic with { Span = Shift(diagnostic.Span, shift) });
                end = token.Span.Start + token.Span.Length;
                block = new Piece(open, end) { Block = kind, Opening = opening, Closing = token.Span };
                block.Tokens.AddRange(code);
            }
            if (block.Closing.Length == 0)
            {
                Report(WebLingoDiagnosticCodes.UnterminatedTemplateBlock, $"This block has no closing '{Close}'.", Shift(opening, shift));
            }
            pieces.Add(block);
            position = end;
        }
        DropSilentLines(text, pieces);
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

    /// <summary>
    /// The tokens of the file <paramref name="name"/>, at its positions in the program, <paramref name="shift"/> after its
    /// own: each run of its text a write of it as it stands, and each block its code, an include's template's in its
    /// place, or a write of it.
    /// </summary>
    private void Emit(string name, string text, List<Piece> pieces, int shift)
    {
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
                AddText(text, run, runStart, runEnd, shift);
                runStart = -1;
            }
            switch (kind)
            {
                case BlockKind.Statements:
                    AddCode(name, piece.Tokens, shift);
                    break;
                case BlockKind.Escaped or BlockKind.Raw when piece.Tokens.Count == 0:
                    if (piece.Closing.Length > 0) Report(WebLingoDiagnosticCodes.UnexpectedToken, $"Expected an expression, found '{Close}'.",
                        Shift(piece.Closing, shift));
                    break;
                case BlockKind.Escaped or BlockKind.Raw:
                    var writer = kind == BlockKind.Escaped ? WebLingoTemplateWriters.Escaped : WebLingoTemplateWriters.Raw;
                    var expression = new List<WebLingoToken>(piece.Tokens.Count);
                    foreach (var token in piece.Tokens) expression.Add(token with { Span = Shift(token.Span, shift) });
                    AddWrite(writer, Shift(piece.Opening, shift), expression, Shift(piece.Closing, shift));
                    break;
            }
        }
        if (runStart >= 0) AddText(text, run, runStart, runEnd, shift);
    }

    /// <summary>A run of a file's text, written as it stands.</summary>
    private void AddText(string text, StringBuilder run, int start, int end, int shift)
    {
        var span = new WebLingoSourceSpan(start + shift, end - start);
        var literal = new WebLingoToken(WebLingoTokenKind.StringLiteral, text[start..end], span, StringValue: run.ToString());
        AddWrite(WebLingoTemplateWriters.Raw, span, [literal], span);
        run.Clear();
    }

    /// <summary>A statement block's code, each <c>include "path";</c> in it replaced by the template it names.</summary>
    private void AddCode(string name, List<WebLingoToken> code, int shift)
    {
        for (var i = 0; i < code.Count; i++)
        {
            var token = code[i];
            var isInclude = token.Kind == WebLingoTokenKind.Identifier && token.Text == Include
                && i + 1 < code.Count && code[i + 1].Kind == WebLingoTokenKind.StringLiteral
                && !(i > 0 && code[i - 1].Kind is WebLingoTokenKind.Dot or WebLingoTokenKind.QuestionDot);
            if (!isInclude)
            {
                _tokens.Add(token with { Span = Shift(token.Span, shift) });
                continue;
            }
            var path = code[++i];
            var pathSpan = Shift(path.Span, shift);
            if (i + 1 < code.Count && code[i + 1].Kind == WebLingoTokenKind.Semicolon)
            {
                i++;
            }
            else
            {
                Report(WebLingoDiagnosticCodes.MissingSemicolon, "A ';' is missing here.", new WebLingoSourceSpan(pathSpan.Start + pathSpan.Length, 0));
            }
            AddInclude(name, path.StringValue!, pathSpan);
        }
    }

    /// <summary>The template <paramref name="path"/> names, included by the file <paramref name="includer"/>, in its place.</summary>
    private void AddInclude(string includer, string path, WebLingoSourceSpan span)
    {
        string? error;
        if (_resolver is null)
        {
            error = "this host finds no templates";
        }
        else if (_resolver.TryResolve(includer, path, out var name, out var text, out error))
        {
            var cycle = _including.IndexOf(name);
            if (cycle >= 0)
            {
                var chain = string.Join(" includes ", _including.GetRange(cycle, _including.Count - cycle));
                Report(WebLingoDiagnosticCodes.TemplateCycle, $"Including '{path}' makes a cycle: {chain} includes {name}.", span);
                return;
            }
            if (_including.Count >= MaxIncludeDepth)
            {
                Report(WebLingoDiagnosticCodes.TemplateTooLarge, $"Includes nest too deeply here: at most {MaxIncludeDepth}.", span);
                return;
            }
            _includedLength += text.Length;
            if (_includedLength > MaxIncludedLength)
            {
                Report(WebLingoDiagnosticCodes.TemplateTooLarge,
                    $"The page's includes read too much text: at most {MaxIncludedLength} characters in all.", span);
                return;
            }
            var template = new WebLingoSourceText(text, name);
            var shift = _files.StartOf(template);
            if (!_read.TryGetValue(name, out var pieces))
            {
                pieces = Read(text, shift);
                _read.Add(name, pieces);
            }
            _including.Add(name);
            Emit(name, text, pieces, shift);
            _including.RemoveAt(_including.Count - 1);
            return;
        }
        Report(WebLingoDiagnosticCodes.TemplateNotFound, $"The template '{path}' cannot be included: {error}.", span);
    }

    /// <summary><c>writer(expression);</c>, the call at <paramref name="opening"/>, and its <c>)</c> and <c>;</c> at <paramref name="closing"/>.</summary>
    private void AddWrite(string writer, WebLingoSourceSpan opening, IReadOnlyList<WebLingoToken> expression, WebLingoSourceSpan closing)
    {
        _tokens.Add(new WebLingoToken(WebLingoTokenKind.Identifier, writer, opening));
        _tokens.Add(new WebLingoToken(WebLingoTokenKind.OpenParen, "(", opening));
        _tokens.AddRange(expression);
        _tokens.Add(new WebLingoToken(WebLingoTokenKind.CloseParen, ")", closing));
        _tokens.Add(new WebLingoToken(WebLingoTokenKind.Semicolon, ";", closing));
    }

    private void Report(string code, string message, WebLingoSourceSpan span) =>
        _diagnostics.Add(new WebLingoDiagnostic(code, WebLingoDiagnosticSeverity.Error, message, span));

    private static WebLingoSourceSpan Shift(WebLingoSourceSpan span, int shift) => new(span.Start + shift, span.Length);
}
