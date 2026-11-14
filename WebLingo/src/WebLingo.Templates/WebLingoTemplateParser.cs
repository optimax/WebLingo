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
/// <para>
/// <c>layout "path", { name: value, … };</c>, at most once in a page's own text, after its parameters and imports and
/// before anything else, makes the rest of the page its body: the page imports the layout, a template of its own scope,
/// read as <see cref="WebLingoTemplateKind.Layout"/> (<see cref="LayoutScheme"/> before its path), giving it the
/// values as its parameters' and the body as a function, which the layout's <c>renderBody()</c> awaits, so that the body
/// runs, and streams, where the layout has it. The layouts a page's leads to are followed here, so that a cycle of them,
/// or a layout's parameters given wrong, is an error at compile time.
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
    private const string Layout = "layout";
    private const string RenderBody = "renderBody";

    /// <summary>What an import's path begins with when it names a page's layout (section 13).</summary>
    public const string LayoutScheme = "layout:";

    /// <summary>A layout's parameter that holds the page's body, and the page's import of its layout: names no program can write.</summary>
    private const string Body = "<%body%>";
    private const string LayoutImport = "<%layout%>";

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
    private readonly WebLingoTemplateKind _kind;
    private long _includedLength;

    /// <summary>The page's layout statement, once read: its keyword's span, its path, and its values' entries.</summary>
    private (WebLingoSourceSpan At, WebLingoToken Path, List<WebLingoToken> Entries)? _layout;
    /// <summary>Where the page's body, after its layout statement, begins among the tokens.</summary>
    private int _bodyStart;

    private WebLingoTemplateParser(WebLingoSourceText page, IWebLingoSourceResolver? resolver, WebLingoTemplateKind kind)
    {
        _resolver = resolver;
        _kind = kind;
        _files = new WebLingoTemplateFiles(page);
    }

    /// <summary>A page that includes nothing.</summary>
    public static WebLingoSyntaxTree Parse(WebLingoSourceText page) => Parse(page, null, out _);

    /// <summary>
    /// A page, the templates it includes found by <paramref name="resolver"/>: from the name of the file that includes one
    /// and its path, the template's name, which identifies it, and its text; with a null resolver, an include is WL5002.
    /// The tree's positions are the program's: <paramref name="files"/> says which file each is in.
    /// </summary>
    public static WebLingoSyntaxTree Parse(WebLingoSourceText page, IWebLingoSourceResolver? resolver, out WebLingoTemplateFiles files,
        WebLingoTemplateKind kind = WebLingoTemplateKind.Page)
    {
        var parser = new WebLingoTemplateParser(page, resolver, kind);
        parser._including.Add(page.Name);
        if (kind == WebLingoTemplateKind.Layout)
        {
            // A layout's first parameter, which its page gives: the page's body, a function.
            var start = new WebLingoSourceSpan(0, 0);
            parser._tokens.Add(new WebLingoToken(WebLingoTokenKind.Param, "param", start));
            parser._tokens.Add(new WebLingoToken(WebLingoTokenKind.Identifier, Body, start));
            parser._tokens.Add(new WebLingoToken(WebLingoTokenKind.Semicolon, ";", start));
        }
        parser.Emit(page.Name, page.Text, Read(page.Text, 0, parser._diagnostics), 0);
        parser.AddLayoutImport();
        parser._tokens.Add(new WebLingoToken(WebLingoTokenKind.EndOfFile, "", new WebLingoSourceSpan(page.Text.Length, 0)));
        files = parser._files;
        return new WebLingoParser(page, parser._tokens, parser._diagnostics).ParseCompilationUnit();
    }

    /// <summary>
    /// A file as pieces: its text, split at its line breaks, and its blocks, each block's code read by the lexer, with the
    /// lines that write nothing marked. Its diagnostics go to <paramref name="diagnostics"/>, at the file's positions in the
    /// program.
    /// </summary>
    private static List<Piece> Read(string text, int shift, List<WebLingoDiagnostic> diagnostics)
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
                foreach (var diagnostic in lexer.Diagnostics) diagnostics.Add(diagnostic with { Span = Shift(diagnostic.Span, shift) });
                end = token.Span.Start + token.Span.Length;
                block = new Piece(open, end) { Block = kind, Opening = opening, Closing = token.Span };
                block.Tokens.AddRange(code);
            }
            if (block.Closing.Length == 0)
            {
                diagnostics.Add(new WebLingoDiagnostic(WebLingoDiagnosticCodes.UnterminatedTemplateBlock, WebLingoDiagnosticSeverity.Error,
                    $"This block has no closing '{Close}'.", Shift(opening, shift)));
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
                    for (var i = 0; i < piece.Tokens.Count; i++) AddToken(expression, piece.Tokens, i, shift);
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

    /// <summary>
    /// A statement block's code, each <c>include "path";</c> in it replaced by the template it names, and its
    /// <c>layout "path", values;</c> read.
    /// </summary>
    private void AddCode(string name, List<WebLingoToken> code, int shift)
    {
        for (var i = 0; i < code.Count; i++)
        {
            var token = code[i];
            var directive = token.Kind == WebLingoTokenKind.Identifier && token.Text is Include or Layout
                && i + 1 < code.Count && code[i + 1].Kind == WebLingoTokenKind.StringLiteral && !AfterDot(code, i);
            if (directive && token.Text == Layout)
            {
                i = AddLayout(code, i, shift);
                continue;
            }
            if (!directive)
            {
                AddToken(_tokens, code, i, shift);
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
                pieces = Read(text, shift, _diagnostics);
                _read.Add(name, pieces);
            }
            _including.Add(name);
            Emit(name, text, pieces, shift);
            _including.RemoveAt(_including.Count - 1);
            return;
        }
        Report(WebLingoDiagnosticCodes.TemplateNotFound, $"The template '{path}' cannot be included: {error}.", span);
    }

    private static bool AfterDot(List<WebLingoToken> code, int i) =>
        i > 0 && code[i - 1].Kind is WebLingoTokenKind.Dot or WebLingoTokenKind.QuestionDot;

    /// <summary>A token of a block's code, at its position in the program; in a layout, <c>renderBody</c> awaiting the body.</summary>
    private void AddToken(List<WebLingoToken> into, List<WebLingoToken> code, int i, int shift)
    {
        var token = code[i] with { Span = Shift(code[i].Span, shift) };
        if (_kind == WebLingoTemplateKind.Layout && token.Kind == WebLingoTokenKind.Identifier && token.Text == RenderBody
            && !AfterDot(code, i))
        {
            into.Add(new WebLingoToken(WebLingoTokenKind.Await, "await", token.Span));
            into.Add(new WebLingoToken(WebLingoTokenKind.Identifier, Body, token.Span));
            return;
        }
        into.Add(token);
    }

    /// <summary>
    /// <c>layout "path", { … };</c> at <paramref name="at"/>: checked, and kept for <see cref="AddLayoutImport"/>, the
    /// tokens after it being the page's body. The index of its last token.
    /// </summary>
    private int AddLayout(List<WebLingoToken> code, int at, int shift)
    {
        var keyword = Shift(code[at].Span, shift);
        var path = code[at + 1] with { Span = Shift(code[at + 1].Span, shift) };
        var entries = new List<WebLingoToken>();
        var next = at + 2;
        var end = path.Span.Start + path.Span.Length;
        if (next < code.Count && code[next].Kind == WebLingoTokenKind.Comma)
        {
            next++;
            var close = next < code.Count && code[next].Kind == WebLingoTokenKind.OpenBrace ? Matching(code, next) : -1;
            if (close < 0)
            {
                var found = next < code.Count ? $"'{code[next].Text}'" : $"'{Close}'";
                var span = next < code.Count ? Shift(code[next].Span, shift) : new WebLingoSourceSpan(end, 0);
                Report(WebLingoDiagnosticCodes.UnexpectedToken, $"Expected a layout's values, a map literal, found {found}.", span);
                return code.Count - 1;
            }
            for (var i = next + 1; i < close; i++) entries.Add(code[i] with { Span = Shift(code[i].Span, shift) });
            if (entries.Count > 0 && entries[^1].Kind == WebLingoTokenKind.Comma) entries.RemoveAt(entries.Count - 1);
            end = Shift(code[close].Span, shift).Start + 1;
            next = close + 1;
        }
        if (next < code.Count && code[next].Kind == WebLingoTokenKind.Semicolon)
        {
            next++;
        }
        else
        {
            Report(WebLingoDiagnosticCodes.MissingSemicolon, "A ';' is missing here.", new WebLingoSourceSpan(end, 0));
        }
        if (_including.Count > 1)
        {
            Report(WebLingoDiagnosticCodes.MisplacedLayout, "A layout is a page's own: a template it includes cannot have one.", keyword);
        }
        else if (_layout is not null)
        {
            Report(WebLingoDiagnosticCodes.MisplacedLayout, "A page has at most one layout.", keyword);
        }
        else if (!OnlyParametersAndImports())
        {
            Report(WebLingoDiagnosticCodes.MisplacedLayout,
                "A page's layout comes before anything else the page does or writes, after its parameters and imports.", keyword);
        }
        else
        {
            CheckLayout(_including[0], path, entries);
            _layout = (keyword, path, entries);
            _bodyStart = _tokens.Count;
        }
        return next - 1;
    }

    /// <summary>The index of the token that closes the bracket at <paramref name="open"/>, or -1.</summary>
    private static int Matching(List<WebLingoToken> code, int open)
    {
        var depth = 0;
        for (var i = open; i < code.Count; i++)
        {
            depth += Depth(code[i].Kind);
            if (depth == 0) return i;
        }
        return -1;
    }

    private static int Depth(WebLingoTokenKind kind) => kind switch
    {
        WebLingoTokenKind.OpenParen or WebLingoTokenKind.OpenBrace or WebLingoTokenKind.OpenBracket or WebLingoTokenKind.HashBrace
            or WebLingoTokenKind.QuestionBracket => 1,
        WebLingoTokenKind.CloseParen or WebLingoTokenKind.CloseBrace or WebLingoTokenKind.CloseBracket => -1,
        _ => 0
    };

    /// <summary>Whether the program so far is only statements that begin with <c>param</c> or <c>import</c>.</summary>
    private bool OnlyParametersAndImports()
    {
        var start = true;
        var depth = 0;
        foreach (var token in _tokens)
        {
            if (start && token.Kind is not (WebLingoTokenKind.Param or WebLingoTokenKind.Import)) return false;
            start = false;
            depth += Depth(token.Kind);
            if (depth == 0 && token.Kind == WebLingoTokenKind.Semicolon) start = true;
        }
        return true;
    }

    /// <summary>
    /// The layouts the page's leads to, followed from <paramref name="page"/>: one that cannot be found is WL5002, and a
    /// cycle WL5003; and the page's layout's parameters, against the values given (WL5006).
    /// </summary>
    private void CheckLayout(string page, WebLingoToken path, List<WebLingoToken> entries)
    {
        var text = path.StringValue!;
        if (_resolver is null)
        {
            Report(WebLingoDiagnosticCodes.TemplateNotFound, $"The layout '{text}' cannot be used: this host finds no templates.", path.Span);
            return;
        }
        var chain = new List<string> { page };
        var (from, next) = (page, text);
        while (true)
        {
            if (!_resolver.TryResolve(from, next, out var name, out var source, out var error))
            {
                // A layout's own layout that cannot be found is reported where that layout is read.
                if (chain.Count == 1) Report(WebLingoDiagnosticCodes.TemplateNotFound, $"The layout '{text}' cannot be used: {error}.", path.Span);
                return;
            }
            if (chain.Contains(name))
            {
                Report(WebLingoDiagnosticCodes.TemplateCycle,
                    $"Using the layout '{text}' makes a cycle: {string.Join(" uses ", chain)} uses {name}.", path.Span);
                return;
            }
            if (chain.Count > MaxIncludeDepth)
            {
                Report(WebLingoDiagnosticCodes.TemplateTooLarge, $"Layouts nest too deeply here: at most {MaxIncludeDepth}.", path.Span);
                return;
            }
            chain.Add(name);
            var code = Code(source);
            if (chain.Count == 2) CheckValues(name, ParametersOf(code), path.Span, entries);
            if (LayoutOf(code) is not { } layout) return;
            (from, next) = (name, layout);
        }
    }

    /// <summary>The tokens of a template's statement blocks, in order, read for its layout and its parameters only.</summary>
    private static List<WebLingoToken> Code(string text)
    {
        var code = new List<WebLingoToken>();
        foreach (var piece in Read(text, 0, []))
        {
            if (piece.Block == BlockKind.Statements) code.AddRange(piece.Tokens);
        }
        return code;
    }

    /// <summary>The path a template's layout statement gives, if it has one.</summary>
    private static string? LayoutOf(List<WebLingoToken> code)
    {
        for (var i = 0; i + 1 < code.Count; i++)
        {
            if (code[i].Kind == WebLingoTokenKind.Identifier && code[i].Text == Layout && code[i + 1].Kind == WebLingoTokenKind.StringLiteral
                && !AfterDot(code, i))
            {
                return code[i + 1].StringValue;
            }
        }
        return null;
    }

    /// <summary>A template's parameters, each a name and whether it has a default, as its <c>param</c> statements declare them.</summary>
    private static List<(string Name, bool HasDefault)> ParametersOf(List<WebLingoToken> code)
    {
        var parameters = new List<(string, bool)>();
        for (var i = 0; i + 1 < code.Count; i++)
        {
            if (code[i].Kind != WebLingoTokenKind.Param || code[i + 1].Kind != WebLingoTokenKind.Identifier) continue;
            parameters.Add((code[i + 1].Text, i + 2 < code.Count && code[i + 2].Kind == WebLingoTokenKind.Equals));
        }
        return parameters;
    }

    /// <summary>
    /// The values a page gives its layout, a map literal's entries, against the layout's parameters: each key one of them,
    /// and each parameter without a default given (WL5006 otherwise).
    /// </summary>
    private void CheckValues(string layout, List<(string Name, bool HasDefault)> parameters, WebLingoSourceSpan at,
        List<WebLingoToken> entries)
    {
        var given = new HashSet<string>(StringComparer.Ordinal);
        var start = true;
        var depth = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            var token = entries[i];
            if (start && depth == 0 && token.Kind is WebLingoTokenKind.Identifier or WebLingoTokenKind.StringLiteral
                && i + 1 < entries.Count && entries[i + 1].Kind == WebLingoTokenKind.Colon)
            {
                var key = token.Kind == WebLingoTokenKind.Identifier ? token.Text : token.StringValue!;
                given.Add(key);
                var declared = false;
                foreach (var parameter in parameters) declared |= parameter.Name == key;
                if (!declared) Report(WebLingoDiagnosticCodes.ComponentParameter, $"'{key}' is not a parameter of '{layout}'.", token.Span);
            }
            start = false;
            depth += Depth(token.Kind);
            if (depth == 0 && token.Kind == WebLingoTokenKind.Comma) start = true;
        }
        foreach (var (name, hasDefault) in parameters)
        {
            if (!hasDefault && !given.Contains(name))
            {
                Report(WebLingoDiagnosticCodes.ComponentParameter, $"'{layout}''s parameter '{name}' is not given.", at);
            }
        }
    }

    /// <summary>
    /// The page's import of its layout, in place of its body: <c>import &lt;%layout%&gt; from "layout:path" { values,
    /// &lt;%body%&gt;: async function () { body } };</c>.
    /// </summary>
    private void AddLayoutImport()
    {
        if (_layout is not var (at, path, entries)) return;
        var body = _tokens.GetRange(_bodyStart, _tokens.Count - _bodyStart);
        _tokens.RemoveRange(_bodyStart, body.Count);
        _tokens.Add(new WebLingoToken(WebLingoTokenKind.Import, "import", at));
        _tokens.Add(new WebLingoToken(WebLingoTokenKind.Identifier, LayoutImport, at));
        _tokens.Add(new WebLingoToken(WebLingoTokenKind.Identifier, "from", at));
        _tokens.Add(path with { StringValue = LayoutScheme + path.StringValue });
        _tokens.Add(new WebLingoToken(WebLingoTokenKind.OpenBrace, "{", at));
        _tokens.AddRange(entries);
        if (entries.Count > 0) _tokens.Add(new WebLingoToken(WebLingoTokenKind.Comma, ",", at));
        _tokens.Add(new WebLingoToken(WebLingoTokenKind.Identifier, Body, at));
        _tokens.Add(new WebLingoToken(WebLingoTokenKind.Colon, ":", at));
        _tokens.Add(new WebLingoToken(WebLingoTokenKind.Async, "async", at));
        _tokens.Add(new WebLingoToken(WebLingoTokenKind.Function, "function", at));
        _tokens.Add(new WebLingoToken(WebLingoTokenKind.OpenParen, "(", at));
        _tokens.Add(new WebLingoToken(WebLingoTokenKind.CloseParen, ")", at));
        _tokens.Add(new WebLingoToken(WebLingoTokenKind.OpenBrace, "{", at));
        _tokens.AddRange(body);
        _tokens.Add(new WebLingoToken(WebLingoTokenKind.CloseBrace, "}", at));
        _tokens.Add(new WebLingoToken(WebLingoTokenKind.CloseBrace, "}", at));
        _tokens.Add(new WebLingoToken(WebLingoTokenKind.Semicolon, ";", at));
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
