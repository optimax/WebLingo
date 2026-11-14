using WebLingo.Core;
namespace WebLingo.Language;

/// <summary>
/// The M0.6 parser (specification, section 8.4, "Parser"; its choices are in section 15.2): variable declarations,
/// expression statements, integer literals, names, unary <c>-</c> and <c>+</c>, <c>+ - * /</c>, parentheses, assignment and calls,
/// each simple statement ended by a mandatory semicolon. It recovers from an error at the next semicolon or
/// declaration, so that one pass reports several errors, and reports no error twice: a token the lexer already
/// reported as bad gives no further diagnostic.
/// </summary>
public sealed class WebLingoParser : IWebLingoParser
{
    /// <summary>How deeply parsing may recurse (parentheses, unary operators, assignments, arguments).</summary>
    public const int MaxRecursionDepth = 256;
    /// <summary>How deep a parsed expression may be, so that every later stage can walk it recursively.</summary>
    public const int MaxExpressionDepth = 1000;

    private readonly WebLingoSourceText _source;
    private readonly IReadOnlyList<WebLingoToken> _tokens;
    private readonly List<WebLingoDiagnostic> _diagnostics = [];
    private int _index;
    private int _previousEnd;
    private int _recursionDepth;
    private bool _statementHasError;
    private int _superCalls; // How many super(…) calls have been parsed: whether a constructor calls its base's.

    public WebLingoParser(string source, string name = "") : this(new WebLingoSourceText(source, name)) { }

    public WebLingoParser(WebLingoSourceText source)
    {
        _source = source;
        _tokens = WebLingoLexer.Tokenize(source.Text, out var lexerDiagnostics);
        _diagnostics.AddRange(lexerDiagnostics);
    }

    private WebLingoToken Current => _tokens[_index];

    public WebLingoSyntaxTree ParseCompilationUnit()
    {
        var statements = new List<WebLingoStatementSyntax>();
        while (Current.Kind != WebLingoTokenKind.EndOfFile)
        {
            var before = _index;
            statements.Add(ParseStatement());
            if (_index == before) Advance(); // Never loop without consuming a token.
        }
        var root = new WebLingoCompilationUnitSyntax(statements, new WebLingoSourceSpan(0, _source.Text.Length));
        CheckDepth(root);
        return new WebLingoSyntaxTree(_source, root, InSourceOrder(_diagnostics));
    }

    /// <summary>The lexer's and the parser's diagnostics merged by position; same-place diagnostics keep their order.</summary>
    private static WebLingoDiagnostic[] InSourceOrder(List<WebLingoDiagnostic> diagnostics)
    {
        var sorted = diagnostics.ToArray();
        for (var i = 1; i < sorted.Length; i++)
        {
            var item = sorted[i];
            var j = i - 1;
            while (j >= 0 && sorted[j].Span.Start > item.Span.Start)
            {
                sorted[j + 1] = sorted[j];
                j--;
            }
            sorted[j + 1] = item;
        }
        return sorted;
    }

    private WebLingoStatementSyntax ParseStatement()
    {
        _statementHasError = false;
        var start = Current.Span.Start;
        if (Current.Kind is WebLingoTokenKind.Var or WebLingoTokenKind.Const or WebLingoTokenKind.Let) return ParseVariableDeclaration(start);
        if (Current.Kind == WebLingoTokenKind.OpenBrace) return ParseBlock(start);
        if (Current.Kind == WebLingoTokenKind.If) return ParseIf(start);
        if (Current.Kind == WebLingoTokenKind.While) return ParseWhile(start);
        if (Current.Kind == WebLingoTokenKind.Do) return ParseDoWhile(start);
        if (Current.Kind == WebLingoTokenKind.For) return ParseFor(start);
        if (Current.Kind == WebLingoTokenKind.Foreach) return ParseForEach(start);
        if (Current.Kind is WebLingoTokenKind.Class or WebLingoTokenKind.Extend) return ParseClass(start);
        if (Current.Kind == WebLingoTokenKind.Import) return ParseImport(start);
        if (Current.Kind == WebLingoTokenKind.Param) return ParseParam(start);
        if (Current.Kind == WebLingoTokenKind.Export) return ParseExport(start);
        if (Current.Kind == WebLingoTokenKind.Using) return ParseUsing(start);
        if (Current.Kind == WebLingoTokenKind.Try) return ParseTry(start);
        if (Current.Kind == WebLingoTokenKind.Throw)
        {
            Advance();
            var thrown = Current.Kind == WebLingoTokenKind.Semicolon ? null : ParseExpression();
            ExpectSemicolon();
            return new WebLingoThrowSyntax(thrown, SpanFrom(start));
        }
        if (Current.Kind == WebLingoTokenKind.Function && Peek(1).Kind == WebLingoTokenKind.Identifier) return ParseFunctionDeclaration(start);
        if (Current.Kind == WebLingoTokenKind.Async && Peek(1).Kind == WebLingoTokenKind.Function && Peek(2).Kind == WebLingoTokenKind.Identifier)
        {
            Advance();
            return ParseFunctionDeclaration(start, isAsync: true);
        }
        if (Current.Kind == WebLingoTokenKind.Return)
        {
            Advance();
            var value = Current.Kind == WebLingoTokenKind.Semicolon ? null : ParseExpression();
            ExpectSemicolon();
            return new WebLingoReturnSyntax(value, SpanFrom(start));
        }
        if (Current.Kind is WebLingoTokenKind.Break or WebLingoTokenKind.Continue)
        {
            var keyword = Advance();
            ExpectSemicolon();
            return keyword.Kind == WebLingoTokenKind.Break ? new WebLingoBreakSyntax(SpanFrom(start)) : new WebLingoContinueSyntax(SpanFrom(start));
        }
        var expression = ParseExpression();
        ExpectSemicolon();
        return new WebLingoExpressionStatementSyntax(expression, SpanFrom(start));
    }

    /// <summary>A block; the statements in it recover as at the top level, and a missing '}' is reported at the end.</summary>
    private WebLingoStatementSyntax ParseBlock(int start)
    {
        var open = Advance();
        var statements = new List<WebLingoStatementSyntax>();
        if (!EnterRecursion()) return new WebLingoBlockSyntax(statements, SpanFrom(start));
        try
        {
            while (Current.Kind is not (WebLingoTokenKind.CloseBrace or WebLingoTokenKind.EndOfFile))
            {
                var before = _index;
                statements.Add(ParseStatement());
                if (_index == before) Advance(); // Never loop without consuming a token.
            }
        }
        finally
        {
            _recursionDepth--;
        }
        if (Current.Kind == WebLingoTokenKind.CloseBrace)
        {
            Advance();
        }
        else
        {
            _statementHasError = false;
            Report(WebLingoDiagnosticCodes.UnexpectedToken, "This '{' has no matching '}'.", open.Span);
        }
        return new WebLingoBlockSyntax(statements, SpanFrom(start));
    }

    private WebLingoStatementSyntax ParseIf(int start)
    {
        Advance();
        var condition = ParseCondition();
        if (!EnterRecursion()) return new WebLingoExpressionStatementSyntax(condition, SpanFrom(start));
        try
        {
            var then = ParseStatement();
            WebLingoStatementSyntax? otherwise = null;
            if (Current.Kind == WebLingoTokenKind.Else)
            {
                Advance();
                otherwise = ParseStatement();
            }
            return new WebLingoIfSyntax(condition, then, otherwise, SpanFrom(start));
        }
        finally
        {
            _recursionDepth--;
        }
    }

    private WebLingoStatementSyntax ParseWhile(int start)
    {
        Advance();
        var condition = ParseCondition();
        var body = ParseNested();
        return body is null ? new WebLingoExpressionStatementSyntax(condition, SpanFrom(start))
            : new WebLingoWhileSyntax(condition, body, SpanFrom(start));
    }

    private WebLingoStatementSyntax ParseDoWhile(int start)
    {
        Advance();
        var body = ParseNested();
        if (body is null) return new WebLingoBlockSyntax([], SpanFrom(start));
        _statementHasError = false;
        Expect(WebLingoTokenKind.While, "'while'");
        var condition = ParseCondition();
        ExpectSemicolon();
        return new WebLingoDoWhileSyntax(body, condition, SpanFrom(start));
    }

    /// <summary>
    /// <c>foreach (var item in collection) body</c> or <c>foreach (var (key, value) in map) body</c>; the variables are
    /// declared with <c>var</c> only, and <c>in</c> is a keyword only there (sections 3.5 and 3.6).
    /// </summary>
    private WebLingoStatementSyntax ParseForEach(int start)
    {
        Advance();
        _statementHasError = false;
        Expect(WebLingoTokenKind.OpenParen, "'('");
        if (Current.Kind != WebLingoTokenKind.Var && !_statementHasError) ReportUnexpected("'var'");
        if (Current.Kind is WebLingoTokenKind.Var or WebLingoTokenKind.Const or WebLingoTokenKind.Let) Advance(); // 'const' read as 'var' to recover.
        string? valueName = null;
        var valueSpan = default(WebLingoSourceSpan);
        (string Name, WebLingoSourceSpan Span) item;
        if (Current.Kind == WebLingoTokenKind.OpenParen)
        {
            Advance();
            item = ExpectName();
            Expect(WebLingoTokenKind.Comma, "','");
            (valueName, valueSpan) = ExpectName();
            Expect(WebLingoTokenKind.CloseParen, "')'");
        }
        else if (IsIn(Current) && !IsIn(Peek(1)))
        {
            // 'foreach (var in …)': the name is missing, and 'in' is where it should be.
            ReportUnexpected("a name");
            item = ("", new WebLingoSourceSpan(Current.Span.Start, 0));
        }
        else
        {
            item = ExpectName();
        }
        if (Current.Kind == WebLingoTokenKind.Identifier && !IsIn(Current) && !_statementHasError) ReportUnexpected("'in'");
        if (Current.Kind == WebLingoTokenKind.Identifier) Advance(); // Another name, such as 'of', read as 'in' to recover.
        else if (!_statementHasError) ReportUnexpected("'in'");
        var collection = ParseExpression();
        Expect(WebLingoTokenKind.CloseParen, "')'");
        var body = ParseNested();
        return body is null
            ? new WebLingoExpressionStatementSyntax(collection, SpanFrom(start))
            : new WebLingoForEachSyntax(item.Name, item.Span, valueName, valueSpan, collection, body, SpanFrom(start));
    }

    private static bool IsIn(WebLingoToken token) => token.Kind == WebLingoTokenKind.Identifier && token.Text == "in";

    /// <summary>
    /// <c>class Name { members }</c>, each member a method, or the <c>constructor</c>: a name, with or without
    /// <c>function</c> before it, its parameters and its body; or, after <c>static</c>, a method of the class itself, or
    /// a field of it, <c>static var name = value;</c> (section 3.7).
    /// </summary>
    private WebLingoStatementSyntax ParseClass(int start)
    {
        // class Name { … }, or extend Name { … }, whose members are a class's but for a constructor (section 3.7).
        var isExtension = Advance().Kind == WebLingoTokenKind.Extend;
        var (name, nameSpan) = isExtension ? ("", default) : ExpectName();
        WebLingoExpressionSyntax? baseClass = null;
        if (isExtension || Current.Kind == WebLingoTokenKind.Extends)
        {
            // extends Name, or extend Name, or a member of one (section 3.7).
            if (!isExtension) Advance();
            baseClass = ParsePrimary();
            while (Current.Kind == WebLingoTokenKind.Dot && Peek(1).Kind == WebLingoTokenKind.Identifier)
            {
                Advance();
                var member = Advance();
                baseClass = new WebLingoMemberAccessSyntax(baseClass, member.Text, member.Span, SpanFrom(baseClass.Span.Start));
            }
        }
        Expect(WebLingoTokenKind.OpenBrace, "'{'");
        var methods = new List<WebLingoMethodSyntax>();
        var staticFields = new List<WebLingoStaticFieldSyntax>();
        WebLingoStatementSyntax Made() => isExtension
            ? new WebLingoExtendSyntax(baseClass!, methods, SpanFrom(start)) { StaticFields = staticFields }
            : new WebLingoClassDeclarationSyntax(name, nameSpan, methods, SpanFrom(start)) { Base = baseClass, StaticFields = staticFields };
        while (Current.Kind is not (WebLingoTokenKind.CloseBrace or WebLingoTokenKind.EndOfFile))
        {
            var memberStart = Current.Span.Start;
            var isStatic = Current.Kind == WebLingoTokenKind.Static;
            if (isStatic)
            {
                Advance();
                if (Current.Kind is WebLingoTokenKind.Var or WebLingoTokenKind.Const or WebLingoTokenKind.Let)
                {
                    if (ParseStaticField(memberStart) is { } field) staticFields.Add(field);
                    continue;
                }
            }
            if (Current is { Kind: WebLingoTokenKind.Identifier, Text: "property" } && Peek(1).Kind == WebLingoTokenKind.Identifier
                && Peek(2).Kind == WebLingoTokenKind.OpenBrace)
            {
                // property name { get { … } set(value) { … } }: a getter and a setter, methods named get:name and
                // set:name, which no program can name otherwise (section 3.7).
                Advance();
                var property = Advance();
                Advance();
                var accessors = 0;
                while (Current is { Kind: WebLingoTokenKind.Identifier, Text: "get" or "set" })
                {
                    var accessor = Advance();
                    accessors++;
                    if (!EnterRecursion()) return Made();
                    try
                    {
                        IReadOnlyList<WebLingoParameterSyntax> accessorParameters = [];
                        WebLingoBlockSyntax? accessorBody;
                        if (accessor.Text == "set")
                        {
                            (accessorParameters, accessorBody) = ParseFunctionRest();
                            if (accessorParameters.Count != 1 && !_statementHasError)
                            {
                                Report(WebLingoDiagnosticCodes.UnexpectedToken, "A setter takes one parameter, the value assigned.",
                                    accessor.Span);
                            }
                        }
                        else
                        {
                            accessorBody = ParseRequiredBlock("'{', which begins the getter");
                        }
                        if (accessorBody is null) return Made();
                        methods.Add(new WebLingoMethodSyntax(accessor.Text + ":" + property.Text, property.Span, accessorParameters,
                            accessorBody, SpanFrom(memberStart)) { IsStatic = isStatic });
                    }
                    finally
                    {
                        _recursionDepth--;
                    }
                }
                if (accessors == 0 && !_statementHasError) ReportUnexpected("'get' or 'set'");
                Expect(WebLingoTokenKind.CloseBrace, "'}'");
                continue;
            }
            if (isStatic && Current is { Kind: WebLingoTokenKind.Identifier, Text: "operator" } && IsOperator(Peek(1).Kind))
            {
                // static operator +(a, b) { … }: what an operator does with the class's values (section 3.7).
                Advance();
                var symbol = Advance();
                if (!EnterRecursion()) return Made();
                try
                {
                    var (operands, operatorBody) = ParseFunctionRest();
                    if (operands.Count != 2 && !_statementHasError)
                    {
                        Report(WebLingoDiagnosticCodes.UnexpectedToken, "An operator takes two parameters, its operands.", symbol.Span);
                    }
                    methods.Add(new WebLingoMethodSyntax("operator" + symbol.Text, symbol.Span, operands, operatorBody, SpanFrom(memberStart))
                    {
                        IsStatic = true
                    });
                }
                finally
                {
                    _recursionDepth--;
                }
                continue;
            }
            var isAsync = Current.Kind == WebLingoTokenKind.Async && Peek(1).Kind is WebLingoTokenKind.Identifier or WebLingoTokenKind.Function;
            if (isAsync) Advance();
            if (Current.Kind == WebLingoTokenKind.Function) Advance();
            if (Current.Kind != WebLingoTokenKind.Identifier)
            {
                if (!_statementHasError) ReportUnexpected(isStatic ? "a static method or 'var'" : "a method");
                SkipToClosingBrace();
                return Made();
            }
            var methodName = Advance();
            if (isStatic && methodName.Text == "constructor" && !_statementHasError)
            {
                Report(WebLingoDiagnosticCodes.UnexpectedToken, "A constructor cannot be static.", methodName.Span);
            }
            if (isAsync && methodName.Text == "constructor" && !_statementHasError)
            {
                Report(WebLingoDiagnosticCodes.UnexpectedToken, "A constructor cannot be async.", methodName.Span);
            }
            if (isExtension && methodName.Text == "constructor" && !_statementHasError)
            {
                Report(WebLingoDiagnosticCodes.UnexpectedToken, "An extension cannot have a constructor.", methodName.Span);
            }
            if (!EnterRecursion()) return Made();
            try
            {
                var superCalls = _superCalls;
                var (parameters, body) = ParseFunctionRest();
                methods.Add(new WebLingoMethodSyntax(methodName.Text, methodName.Span, parameters, body, SpanFrom(memberStart))
                {
                    CallsSuper = _superCalls != superCalls,
                    IsStatic = isStatic,
                    IsAsync = isAsync
                });
            }
            finally
            {
                _recursionDepth--;
            }
        }
        Expect(WebLingoTokenKind.CloseBrace, "'}'");
        return Made();
    }

    /// <summary>
    /// <c>import db from "path";</c>, <c>import { f, g } from "path";</c>, or <c>import db, { f } from "path";</c>
    /// (section 3.9): <c>from</c> is a keyword only here.
    /// </summary>
    private WebLingoStatementSyntax ParseImport(int start)
    {
        Advance();
        string? name = null;
        var nameSpan = default(WebLingoSourceSpan);
        var names = new List<WebLingoImportedNameSyntax>();
        if (Current.Kind == WebLingoTokenKind.Identifier && !(Current.Text == "from" && Peek(1).Kind == WebLingoTokenKind.StringLiteral))
        {
            (name, nameSpan) = ExpectName();
            if (Current.Kind == WebLingoTokenKind.Comma && Peek(1).Kind == WebLingoTokenKind.OpenBrace) Advance();
        }
        if (Current.Kind == WebLingoTokenKind.OpenBrace)
        {
            Advance();
            while (Current.Kind == WebLingoTokenKind.Identifier)
            {
                var imported = Advance();
                names.Add(new WebLingoImportedNameSyntax(imported.Text, imported.Span));
                if (Current.Kind != WebLingoTokenKind.Comma) break;
                Advance();
            }
            Expect(WebLingoTokenKind.CloseBrace, "'}'");
        }
        if (name is null && names.Count == 0 && !_statementHasError) ReportUnexpected("a name, or '{' and the names to import");
        if (Current is { Kind: WebLingoTokenKind.Identifier, Text: "from" }) Advance();
        else if (!_statementHasError) ReportUnexpected("'from'");
        var path = "";
        var pathSpan = Current.Span;
        if (Current.Kind == WebLingoTokenKind.StringLiteral) path = Advance().StringValue!;
        else if (!_statementHasError) ReportUnexpected("the module's path, a string");
        List<WebLingoImportValueSyntax>? values = null;
        if (Current.Kind == WebLingoTokenKind.OpenBrace)
        {
            // The values it gives the module's parameters, a map literal's entries, each a name or a string (section 3.9).
            Advance();
            values = [];
            while (Current.Kind is WebLingoTokenKind.Identifier or WebLingoTokenKind.StringLiteral)
            {
                var key = Advance();
                Expect(WebLingoTokenKind.Colon, "':'");
                values.Add(new WebLingoImportValueSyntax(key.StringValue ?? key.Text, key.Span, ParseExpression()));
                if (Current.Kind != WebLingoTokenKind.Comma) break;
                Advance();
            }
            Expect(WebLingoTokenKind.CloseBrace, "'}'");
        }
        ExpectSemicolon();
        return new WebLingoImportSyntax(name, nameSpan, names, path, pathSpan, SpanFrom(start)) { Values = values };
    }

    /// <summary><c>param name;</c> or <c>param size = 10;</c>: a module's parameter (section 3.9).</summary>
    private WebLingoStatementSyntax ParseParam(int start)
    {
        Advance();
        var (name, nameSpan) = ExpectName();
        WebLingoExpressionSyntax? value = null;
        if (Current.Kind == WebLingoTokenKind.Equals)
        {
            Advance();
            value = ParseExpression();
        }
        ExpectSemicolon();
        return new WebLingoParamSyntax(name, nameSpan, value, SpanFrom(start));
    }

    /// <summary><c>export</c> before a <c>var</c>, <c>const</c>, <c>function</c> or <c>class</c> declaration (section 3.9).</summary>
    private WebLingoStatementSyntax ParseExport(int start)
    {
        var keyword = Advance();
        var declares = Current.Kind is WebLingoTokenKind.Var or WebLingoTokenKind.Const or WebLingoTokenKind.Let
            or WebLingoTokenKind.Function or WebLingoTokenKind.Class
            || (Current.Kind == WebLingoTokenKind.Async && Peek(1).Kind == WebLingoTokenKind.Function);
        if (!declares && !_statementHasError)
        {
            Report(WebLingoDiagnosticCodes.UnexpectedToken,
                "'export' stands before a declaration: 'var', 'const', 'function' or 'class'.", keyword.Span);
        }
        var declaration = ParseStatement();
        return declares ? new WebLingoExportSyntax(declaration, SpanFrom(start)) : declaration;
    }

    /// <summary>The operators a class may declare: arithmetic's and the orderings, not <c>==</c> (section 3.7).</summary>
    private static bool IsOperator(WebLingoTokenKind kind) => kind is WebLingoTokenKind.Plus or WebLingoTokenKind.Minus or WebLingoTokenKind.Star
        or WebLingoTokenKind.Slash or WebLingoTokenKind.Less or WebLingoTokenKind.LessEqual or WebLingoTokenKind.Greater or WebLingoTokenKind.GreaterEqual;

    /// <summary>
    /// <c>static var name = value;</c>, after its <c>static</c>: a field of a class itself, or with <c>const</c> a constant
    /// (section 3.7). <c>let</c> in place of <c>var</c> is reported, and read as <c>var</c>; a field without a value is
    /// WL1006, and none.
    /// </summary>
    private WebLingoStaticFieldSyntax? ParseStaticField(int start)
    {
        var isConstant = Current.Kind == WebLingoTokenKind.Const;
        if (Current.Kind == WebLingoTokenKind.Let && !_statementHasError) ReportUnexpected("'var' or 'const'");
        Advance();
        var (name, nameSpan) = ExpectName();
        WebLingoExpressionSyntax? value = null;
        if (Current.Kind == WebLingoTokenKind.Equals)
        {
            Advance();
            value = ParseExpression();
        }
        else if (!_statementHasError)
        {
            Report(WebLingoDiagnosticCodes.MissingInitializer,
                $"'{name}' needs a value where it is declared: 'static {(isConstant ? "const" : "var")} {name} = …;'.", nameSpan);
        }
        ExpectSemicolon();
        return value is null || name.Length == 0
            ? null
            : new WebLingoStaticFieldSyntax(name, nameSpan, value, SpanFrom(start)) { IsConstant = isConstant };
    }

    /// <summary>
    /// <c>try { … } catch (name) { … }</c>, with <c>finally { … }</c> after its <c>catch</c> or in its place (section 3.7):
    /// each part a block.
    /// </summary>
    private WebLingoStatementSyntax ParseTry(int start)
    {
        Advance();
        var body = ParseRequiredBlock("'{', which begins the 'try' block");
        if (body is null) return new WebLingoBlockSyntax([], SpanFrom(start));
        _statementHasError = false;
        var catches = new List<WebLingoCatchSyntax>();
        var catchesAny = false;
        while (Current.Kind == WebLingoTokenKind.Catch)
        {
            // catch (e), or catch (Class e), the class a name or a dotted name (section 3.7).
            var catchStart = Current.Span.Start;
            var keyword = Advance();
            if (catchesAny && !_statementHasError)
            {
                Report(WebLingoDiagnosticCodes.UnexpectedToken, "A 'catch' that catches every exception must be the last.", keyword.Span);
            }
            Expect(WebLingoTokenKind.OpenParen, "'('");
            WebLingoExpressionSyntax? caught = null;
            if (Current.Kind == WebLingoTokenKind.Identifier && Peek(1).Kind is WebLingoTokenKind.Identifier or WebLingoTokenKind.Dot)
            {
                caught = ParseDottedName();
            }
            catchesAny |= caught is null;
            var (catchName, catchNameSpan) = ExpectName();
            Expect(WebLingoTokenKind.CloseParen, "')'");
            var catchBody = ParseRequiredBlock("'{', which begins the 'catch' block");
            if (catchBody is null) return body;
            catches.Add(new WebLingoCatchSyntax(caught, catchName, catchNameSpan, catchBody, SpanFrom(catchStart)));
            _statementHasError = false;
        }
        WebLingoBlockSyntax? finallyBody = null;
        if (Current.Kind == WebLingoTokenKind.Finally)
        {
            Advance();
            finallyBody = ParseRequiredBlock("'{', which begins the 'finally' block");
            if (finallyBody is null) return body;
        }
        if (catches.Count == 0 && finallyBody is null)
        {
            ReportUnexpected("'catch' or 'finally'");
            return body;
        }
        return new WebLingoTrySyntax(body, catches, finallyBody, SpanFrom(start));
    }

    /// <summary>A name, or a dotted name, <c>a.b.C</c>: how a typed catch and <c>is</c> name a class (section 3.7).</summary>
    private WebLingoExpressionSyntax ParseDottedName()
    {
        var first = Current;
        if (first.Kind != WebLingoTokenKind.Identifier)
        {
            if (!_statementHasError) ReportUnexpected("a class's name");
            return new WebLingoErrorExpressionSyntax(new WebLingoSourceSpan(first.Span.Start, 0));
        }
        Advance();
        WebLingoExpressionSyntax name = new WebLingoNameSyntax(first.Text, first.Span);
        while (Current.Kind == WebLingoTokenKind.Dot && Peek(1).Kind == WebLingoTokenKind.Identifier)
        {
            Advance();
            var member = Advance();
            name = new WebLingoMemberAccessSyntax(name, member.Text, member.Span, SpanFrom(first.Span.Start));
        }
        return name;
    }

    /// <summary>A block that a statement needs, which must begin here; null, reported, if it does not.</summary>
    private WebLingoBlockSyntax? ParseRequiredBlock(string expected)
    {
        if (Current.Kind != WebLingoTokenKind.OpenBrace)
        {
            if (!_statementHasError) ReportUnexpected(expected);
            return null;
        }
        return ParseBlock(Current.Span.Start) as WebLingoBlockSyntax;
    }

    /// <summary><c>using (var name = value) body</c>, one resource, declared with <c>var</c> (section 3.7).</summary>
    private WebLingoStatementSyntax ParseUsing(int start)
    {
        Advance();
        _statementHasError = false;
        Expect(WebLingoTokenKind.OpenParen, "'('");
        if (Current.Kind != WebLingoTokenKind.Var && !_statementHasError) ReportUnexpected("'var'");
        if (Current.Kind is WebLingoTokenKind.Var or WebLingoTokenKind.Const or WebLingoTokenKind.Let) Advance(); // 'const' read as 'var' to recover.
        var (name, nameSpan) = ExpectName();
        Expect(WebLingoTokenKind.Equals, "'='");
        var value = ParseExpression();
        Expect(WebLingoTokenKind.CloseParen, "')'");
        var body = ParseNested();
        return body is null
            ? new WebLingoExpressionStatementSyntax(value, SpanFrom(start))
            : new WebLingoUsingSyntax(name, nameSpan, value, body, SpanFrom(start));
    }

    /// <summary>Parameters' default values, as statements of their own, so that each expression's depth is checked.</summary>
    private static void PushDefaults(IReadOnlyList<WebLingoParameterSyntax> parameters, Stack<WebLingoStatementSyntax> statements)
    {
        foreach (var parameter in parameters)
        {
            if (parameter.Default is { } value) statements.Push(new WebLingoExpressionStatementSyntax(value, value.Span));
        }
    }

    /// <summary>A name being declared; an empty one, reported, if there is none.</summary>
    private (string Name, WebLingoSourceSpan Span) ExpectName()
    {
        var token = Current;
        if (token.Kind == WebLingoTokenKind.Identifier)
        {
            Advance();
            return (token.Text, token.Span);
        }
        if (!_statementHasError) ReportUnexpected("a name");
        return ("", new WebLingoSourceSpan(token.Span.Start, 0));
    }

    private WebLingoStatementSyntax ParseFor(int start)
    {
        Advance();
        _statementHasError = false;
        Expect(WebLingoTokenKind.OpenParen, "'('");
        WebLingoStatementSyntax? init = null;
        if (Current.Kind is WebLingoTokenKind.Var or WebLingoTokenKind.Const or WebLingoTokenKind.Let)
        {
            init = ParseVariableDeclaration(Current.Span.Start); // Ends with its ';'.
        }
        else if (Current.Kind == WebLingoTokenKind.Semicolon)
        {
            Advance();
        }
        else
        {
            var initStart = Current.Span.Start;
            var expression = ParseExpression();
            init = new WebLingoExpressionStatementSyntax(expression, SpanFrom(initStart));
            Expect(WebLingoTokenKind.Semicolon, "';'");
        }
        WebLingoExpressionSyntax? condition = Current.Kind == WebLingoTokenKind.Semicolon ? null : ParseExpression();
        Expect(WebLingoTokenKind.Semicolon, "';'");
        WebLingoExpressionSyntax? step = Current.Kind == WebLingoTokenKind.CloseParen ? null : ParseExpression();
        Expect(WebLingoTokenKind.CloseParen, "')'");
        var body = ParseNested();
        return body is null ? new WebLingoBlockSyntax([], SpanFrom(start))
            : new WebLingoForSyntax(init, condition, step, body, SpanFrom(start));
    }

    private WebLingoStatementSyntax ParseFunctionDeclaration(int start, bool isAsync = false)
    {
        Advance();
        var name = Advance();
        var (parameters, body) = ParseFunctionRest(receiver: true);
        return new WebLingoFunctionDeclarationSyntax(name.Text, name.Span, parameters, body, SpanFrom(start)) { IsAsync = isAsync };
    }

    /// <summary>
    /// Whether a token's text is a word, a name or a keyword: a member's name may be a keyword after a point, as in
    /// <c>Module.import(…)</c> (section 3.9).
    /// </summary>
    private static bool IsWord(string text)
    {
        if (text.Length == 0 || !(char.IsAsciiLetter(text[0]) || text[0] == '_')) return false;
        foreach (var c in text)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_') return false;
        }
        return true;
    }

    /// <summary>A function's parameters and body, after <c>function</c> and its name, if it has one.</summary>
    /// <remarks>
    /// A function that is not a method may take a receiver, <c>this</c> as its first parameter, without a default
    /// (<paramref name="receiver"/>; section 3.7).
    /// </remarks>
    private (IReadOnlyList<WebLingoParameterSyntax> Parameters, WebLingoBlockSyntax Body) ParseFunctionRest(bool receiver = false)
    {
        _statementHasError = false;
        var parameters = new List<WebLingoParameterSyntax>();
        Expect(WebLingoTokenKind.OpenParen, "'('");
        if (receiver && Current.Kind == WebLingoTokenKind.This)
        {
            parameters.Add(new WebLingoParameterSyntax("this", Advance().Span));
            if (Current.Kind == WebLingoTokenKind.Equals) ReportUnexpected("',' or ')': a receiver has no default value");
            else if (Current.Kind == WebLingoTokenKind.Comma) Advance();
        }
        if (Current.Kind != WebLingoTokenKind.CloseParen && !_statementHasError)
        {
            var optional = false;
            while (true)
            {
                if (Current.Kind != WebLingoTokenKind.Identifier)
                {
                    ReportUnexpected("a parameter name");
                    break;
                }
                var parameter = Advance();
                WebLingoExpressionSyntax? defaultValue = null;
                if (Current.Kind == WebLingoTokenKind.Equals)
                {
                    // A default value, which makes the parameter optional (section 3.5).
                    Advance();
                    defaultValue = ParseExpression();
                    optional = true;
                }
                else if (optional && !_statementHasError)
                {
                    Report(WebLingoDiagnosticCodes.UnexpectedToken,
                        "A parameter without a default value cannot follow one with a default value.", parameter.Span);
                }
                parameters.Add(new WebLingoParameterSyntax(parameter.Text, parameter.Span) { Default = defaultValue });
                if (Current.Kind != WebLingoTokenKind.Comma) break;
                Advance();
            }
        }
        Expect(WebLingoTokenKind.CloseParen, "')'");
        var bodyStart = Current.Span.Start;
        if (Current.Kind != WebLingoTokenKind.OpenBrace)
        {
            if (!_statementHasError) ReportUnexpected("'{', which begins the function's body");
            return (parameters, new WebLingoBlockSyntax([], new WebLingoSourceSpan(bodyStart, 0)));
        }
        var body = ParseBlock(bodyStart);
        return (parameters, body as WebLingoBlockSyntax ?? new WebLingoBlockSyntax([], body.Span));
    }

    /// <summary>A statement nested in another, within the parser's recursion limit; null beyond it.</summary>
    private WebLingoStatementSyntax? ParseNested()
    {
        if (!EnterRecursion()) return null;
        try
        {
            return ParseStatement();
        }
        finally
        {
            _recursionDepth--;
        }
    }

    /// <summary>A parenthesized condition, as <c>if</c> and the loops take it.</summary>
    private WebLingoExpressionSyntax ParseCondition()
    {
        _statementHasError = false;
        Expect(WebLingoTokenKind.OpenParen, "'('");
        var condition = ParseExpression();
        Expect(WebLingoTokenKind.CloseParen, "')'");
        return condition;
    }

    private WebLingoVariableDeclarationSyntax ParseVariableDeclaration(int start)
    {
        var keyword = Advance();
        if (keyword.Kind == WebLingoTokenKind.Let)
        {
            Report(WebLingoDiagnosticCodes.UnexpectedToken,
                "'let' is not a declaration keyword in WebLingo: use 'var' for a variable, or 'const' for a constant.",
                keyword.Span);
        }
        var kind = keyword.Kind == WebLingoTokenKind.Const ? WebLingoDeclarationKind.Const : WebLingoDeclarationKind.Var;

        var name = "";
        var nameSpan = new WebLingoSourceSpan(Current.Span.Start, 0);
        if (Current.Kind == WebLingoTokenKind.Identifier)
        {
            var token = Advance();
            (name, nameSpan) = (token.Text, token.Span);
        }
        else
        {
            ReportUnexpected($"a name after '{keyword.Text}'");
        }

        WebLingoExpressionSyntax? initializer = null;
        if (Current.Kind == WebLingoTokenKind.Equals)
        {
            Advance();
            initializer = ParseExpression();
        }
        else if (!_statementHasError)
        {
            // Every declaration has a value (specification, section 3.1).
            var keywordText = kind == WebLingoDeclarationKind.Const ? "const" : "var";
            Report(WebLingoDiagnosticCodes.MissingInitializer,
                $"'{name}' needs a value where it is declared: '{keywordText} {name} = …;'.", nameSpan);
        }
        ExpectSemicolon();
        return new WebLingoVariableDeclarationSyntax(kind, name, nameSpan, initializer, SpanFrom(start));
    }

    private void ExpectSemicolon()
    {
        if (Current.Kind == WebLingoTokenKind.Semicolon)
        {
            Advance();
            return;
        }
        if (Current.Kind == WebLingoTokenKind.Bad) _statementHasError = true;
        if (_statementHasError)
        {
            // This statement already has an error: skip to where the next one should start, reporting nothing more.
            while (Current.Kind is not (WebLingoTokenKind.EndOfFile or WebLingoTokenKind.Semicolon or WebLingoTokenKind.Var
                   or WebLingoTokenKind.Const or WebLingoTokenKind.Let or WebLingoTokenKind.OpenBrace or WebLingoTokenKind.CloseBrace))
            {
                Advance();
            }
            if (Current.Kind == WebLingoTokenKind.Semicolon) Advance();
            return;
        }
        Report(WebLingoDiagnosticCodes.MissingSemicolon, "A ';' is missing here.", new WebLingoSourceSpan(_previousEnd, 0));
    }

    private WebLingoExpressionSyntax ParseExpression()
    {
        if (!EnterRecursion()) return new WebLingoErrorExpressionSyntax(new WebLingoSourceSpan(Current.Span.Start, 0));
        try
        {
            return ParseAssignment();
        }
        finally
        {
            _recursionDepth--;
        }
    }

    private WebLingoExpressionSyntax ParseAssignment()
    {
        var left = ParseOr();
        if (Current.Kind != WebLingoTokenKind.Equals) return left;
        var equals = Advance();
        var value = ParseExpression();
        var span = Between(left.Span, value.Span);
        if (left is WebLingoNameSyntax target) return new WebLingoAssignmentExpressionSyntax(target, value, span);
        if (left is WebLingoIndexExpressionSyntax { IsOptional: false } element) return new WebLingoIndexAssignmentSyntax(element, value, span);
        if (left is WebLingoMemberAccessSyntax { IsOptional: false } member) return new WebLingoMemberAssignmentSyntax(member, value, span);
        if (left is not WebLingoErrorExpressionSyntax)
        {
            Report(WebLingoDiagnosticCodes.InvalidAssignmentTarget,
                "Only a variable, an element or entry ('x[i]'), or a field ('x.name') can be assigned a value with '='.",
                left.Span.Length > 0 ? left.Span : equals.Span);
        }
        return new WebLingoErrorExpressionSyntax(span);
    }

    private WebLingoExpressionSyntax ParseOr() => ParseLeftAssociative(ParseAnd, WebLingoTokenKind.PipePipe);

    private WebLingoExpressionSyntax ParseAnd() => ParseLeftAssociative(ParseEquality, WebLingoTokenKind.AmpAmp);

    private WebLingoExpressionSyntax ParseEquality() =>
        ParseLeftAssociative(ParseRelational, WebLingoTokenKind.EqualEqual, WebLingoTokenKind.BangEqual);

    /// <summary>The comparisons <c>&lt; &lt;= &gt; &gt;=</c>, and <c>is</c>, whose class is a name or a dotted name (section 3.7).</summary>
    private WebLingoExpressionSyntax ParseRelational()
    {
        var left = ParseAdditive();
        while (true)
        {
            if (Current.Kind == WebLingoTokenKind.Is)
            {
                Advance();
                var type = ParseDottedName();
                left = new WebLingoIsExpressionSyntax(left, type, Between(left.Span, type.Span));
                continue;
            }
            if (Current.Kind is not (WebLingoTokenKind.Less or WebLingoTokenKind.LessEqual or WebLingoTokenKind.Greater or WebLingoTokenKind.GreaterEqual))
            {
                return left;
            }
            var op = Advance();
            var right = ParseAdditive();
            left = new WebLingoBinaryExpressionSyntax(BinaryOperatorOf(op.Kind), left, right, op.Span, Between(left.Span, right.Span));
        }
    }

    /// <summary>One level of left-associative binary operators (specification, section 3.5, "Precedence").</summary>
    private WebLingoExpressionSyntax ParseLeftAssociative(Func<WebLingoExpressionSyntax> operand, params WebLingoTokenKind[] operators)
    {
        var left = operand();
        while (Array.IndexOf(operators, Current.Kind) >= 0)
        {
            var op = Advance();
            var right = operand();
            left = new WebLingoBinaryExpressionSyntax(BinaryOperatorOf(op.Kind), left, right, op.Span, Between(left.Span, right.Span));
        }
        return left;
    }

    private static WebLingoBinaryOperator BinaryOperatorOf(WebLingoTokenKind kind) => kind switch
    {
        WebLingoTokenKind.PipePipe => WebLingoBinaryOperator.Or,
        WebLingoTokenKind.AmpAmp => WebLingoBinaryOperator.And,
        WebLingoTokenKind.EqualEqual => WebLingoBinaryOperator.Equal,
        WebLingoTokenKind.BangEqual => WebLingoBinaryOperator.NotEqual,
        WebLingoTokenKind.Less => WebLingoBinaryOperator.Less,
        WebLingoTokenKind.LessEqual => WebLingoBinaryOperator.LessOrEqual,
        WebLingoTokenKind.Greater => WebLingoBinaryOperator.Greater,
        _ => WebLingoBinaryOperator.GreaterOrEqual
    };

    private WebLingoExpressionSyntax ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (Current.Kind is WebLingoTokenKind.Plus or WebLingoTokenKind.Minus)
        {
            var op = Advance();
            var right = ParseMultiplicative();
            var kind = op.Kind == WebLingoTokenKind.Plus ? WebLingoBinaryOperator.Add : WebLingoBinaryOperator.Subtract;
            left = new WebLingoBinaryExpressionSyntax(kind, left, right, op.Span, Between(left.Span, right.Span));
        }
        return left;
    }

    private WebLingoExpressionSyntax ParseMultiplicative()
    {
        var left = ParseUnary();
        while (Current.Kind is WebLingoTokenKind.Star or WebLingoTokenKind.Slash)
        {
            var op = Advance();
            var right = ParseUnary();
            var kind = op.Kind == WebLingoTokenKind.Star ? WebLingoBinaryOperator.Multiply : WebLingoBinaryOperator.Divide;
            left = new WebLingoBinaryExpressionSyntax(kind, left, right, op.Span, Between(left.Span, right.Span));
        }
        return left;
    }

    private WebLingoExpressionSyntax ParseUnary()
    {
        if (Current.Kind == WebLingoTokenKind.Await)
        {
            // await value, a prefix as the unary operators are (section 3.8).
            if (!EnterRecursion()) return new WebLingoErrorExpressionSyntax(new WebLingoSourceSpan(Current.Span.Start, 0));
            try
            {
                var keyword = Advance();
                var awaited = ParseUnary();
                return new WebLingoAwaitExpressionSyntax(awaited, Between(keyword.Span, awaited.Span));
            }
            finally
            {
                _recursionDepth--;
            }
        }
        if (Current.Kind is not (WebLingoTokenKind.Minus or WebLingoTokenKind.Plus or WebLingoTokenKind.Bang)) return ParseCall();
        if (!EnterRecursion()) return new WebLingoErrorExpressionSyntax(new WebLingoSourceSpan(Current.Span.Start, 0));
        try
        {
            var sign = Advance();
            var operand = ParseUnary();
            var kind = sign.Kind switch
            {
                WebLingoTokenKind.Minus => WebLingoUnaryOperator.Negate,
                WebLingoTokenKind.Plus => WebLingoUnaryOperator.Plus,
                _ => WebLingoUnaryOperator.Not
            };
            return new WebLingoUnaryExpressionSyntax(kind, operand, Between(sign.Span, operand.Span));
        }
        finally
        {
            _recursionDepth--;
        }
    }

    /// <summary>A primary expression followed by any calls, <c>(…)</c>, members, <c>.name</c>, and indexes, <c>[…]</c>.</summary>
    private WebLingoExpressionSyntax ParseCall()
    {
        var expression = ParsePrimary();
        while (true)
        {
            switch (Current.Kind)
            {
                case WebLingoTokenKind.OpenParen:
                {
                    Advance();
                    var arguments = new List<WebLingoExpressionSyntax>();
                    if (Current.Kind != WebLingoTokenKind.CloseParen)
                    {
                        arguments.Add(ParseExpression());
                        while (Current.Kind == WebLingoTokenKind.Comma)
                        {
                            Advance();
                            arguments.Add(ParseExpression());
                        }
                    }
                    Expect(WebLingoTokenKind.CloseParen, "')'");
                    if (expression is WebLingoSuperSyntax) _superCalls++;
                    expression = new WebLingoCallExpressionSyntax(expression, arguments, SpanFrom(expression.Span.Start));
                    continue;
                }
                case WebLingoTokenKind.Dot or WebLingoTokenKind.QuestionDot:
                {
                    var isOptional = Advance().Kind == WebLingoTokenKind.QuestionDot;
                    if (Current.Kind != WebLingoTokenKind.Identifier && !IsWord(Current.Text))
                    {
                        ReportUnexpected("a member's name");
                        return new WebLingoErrorExpressionSyntax(SpanFrom(expression.Span.Start));
                    }
                    var name = Advance();
                    expression = new WebLingoMemberAccessSyntax(expression, name.Text, name.Span, SpanFrom(expression.Span.Start))
                    {
                        IsOptional = isOptional
                    };
                    continue;
                }
                case WebLingoTokenKind.OpenBracket or WebLingoTokenKind.QuestionBracket:
                {
                    var isOptional = Advance().Kind == WebLingoTokenKind.QuestionBracket;
                    var index = ParseExpression();
                    Expect(WebLingoTokenKind.CloseBracket, "']'");
                    expression = new WebLingoIndexExpressionSyntax(expression, index, SpanFrom(expression.Span.Start))
                    {
                        IsOptional = isOptional
                    };
                    continue;
                }
                default:
                    return expression;
            }
        }
    }

    private WebLingoExpressionSyntax ParsePrimary()
    {
        var token = Current;
        switch (token.Kind)
        {
            case WebLingoTokenKind.IntegerLiteral:
                Advance();
                return new WebLingoIntegerLiteralSyntax(token.IntegerValue, token.Span);
            case WebLingoTokenKind.Binary64Literal:
                Advance();
                return new WebLingoBinary64LiteralSyntax(token.Binary64Value, token.Span);
            case WebLingoTokenKind.True or WebLingoTokenKind.False:
                Advance();
                return new WebLingoBooleanLiteralSyntax(token.Kind == WebLingoTokenKind.True, token.Span);
            case WebLingoTokenKind.Null:
                Advance();
                return new WebLingoNullLiteralSyntax(token.Span);
            case WebLingoTokenKind.StringLiteral:
                Advance();
                return new WebLingoStringLiteralSyntax(token.StringValue!, token.Span);
            case WebLingoTokenKind.Function:
            case WebLingoTokenKind.Async when Peek(1).Kind == WebLingoTokenKind.Function:
            {
                var isAsync = token.Kind == WebLingoTokenKind.Async;
                Advance();
                if (isAsync) Advance();
                if (!EnterRecursion()) return new WebLingoErrorExpressionSyntax(token.Span);
                try
                {
                    var (parameters, body) = ParseFunctionRest(receiver: true);
                    return new WebLingoFunctionExpressionSyntax(parameters, body, SpanFrom(token.Span.Start)) { IsAsync = isAsync };
                }
                finally
                {
                    _recursionDepth--;
                }
            }
            case WebLingoTokenKind.Infinity:
                Advance();
                return new WebLingoBinary64LiteralSyntax(double.PositiveInfinity, token.Span);
            case WebLingoTokenKind.NaN:
                Advance();
                return new WebLingoBinary64LiteralSyntax(double.NaN, token.Span);
            case WebLingoTokenKind.Identifier:
                Advance();
                return new WebLingoNameSyntax(token.Text, token.Span);
            case WebLingoTokenKind.OpenBracket:
            {
                // [a, b, c], or [], with no trailing comma, as in calls (section 3.6).
                Advance();
                var elements = ParseElements(WebLingoTokenKind.CloseBracket, "']'");
                return new WebLingoArrayLiteralSyntax(elements, SpanFrom(token.Span.Start));
            }
            case WebLingoTokenKind.HashBrace:
            {
                // #{a, b, c}, or #{} (section 3.6).
                Advance();
                var elements = ParseElements(WebLingoTokenKind.CloseBrace, "'}'");
                return new WebLingoSetLiteralSyntax(elements, SpanFrom(token.Span.Start));
            }
            case WebLingoTokenKind.OpenBrace:
            {
                // { key: value, … }, or {}, a key written as a name or as a string literal (section 3.6).
                Advance();
                var entries = new List<WebLingoMapEntrySyntax>();
                if (Current.Kind != WebLingoTokenKind.CloseBrace)
                {
                    while (true)
                    {
                        var key = Current;
                        if (key.Kind is not (WebLingoTokenKind.Identifier or WebLingoTokenKind.StringLiteral))
                        {
                            ReportUnexpected("a key: a name or a string");
                            SkipToClosingBrace();
                            return new WebLingoErrorExpressionSyntax(SpanFrom(token.Span.Start));
                        }
                        Advance();
                        Expect(WebLingoTokenKind.Colon, "':'");
                        var value = ParseExpression();
                        entries.Add(new WebLingoMapEntrySyntax(key.Kind == WebLingoTokenKind.Identifier ? key.Text : key.StringValue!,
                            key.Span, value));
                        if (Current.Kind != WebLingoTokenKind.Comma) break;
                        Advance();
                    }
                }
                Expect(WebLingoTokenKind.CloseBrace, "'}'");
                return new WebLingoMapLiteralSyntax(entries, SpanFrom(token.Span.Start));
            }
            case WebLingoTokenKind.This:
                Advance();
                return new WebLingoThisSyntax(token.Span);
            case WebLingoTokenKind.Super:
                Advance();
                return new WebLingoSuperSyntax(token.Span);
            case WebLingoTokenKind.New:
            {
                // new Class(arguments): the class a name, or a member of one, then its arguments (section 3.7).
                Advance();
                var target = ParsePrimary();
                while (Current.Kind == WebLingoTokenKind.Dot && Peek(1).Kind == WebLingoTokenKind.Identifier)
                {
                    Advance();
                    var name = Advance();
                    target = new WebLingoMemberAccessSyntax(target, name.Text, name.Span, SpanFrom(target.Span.Start));
                }
                if (Current.Kind != WebLingoTokenKind.OpenParen)
                {
                    Expect(WebLingoTokenKind.OpenParen, "'('");
                    return new WebLingoNewExpressionSyntax(target, [], SpanFrom(token.Span.Start));
                }
                Advance();
                var arguments = ParseElements(WebLingoTokenKind.CloseParen, "')'");
                return new WebLingoNewExpressionSyntax(target, arguments, SpanFrom(token.Span.Start));
            }
            case WebLingoTokenKind.OpenParen:
                Advance();
                var inner = ParseExpression();
                Expect(WebLingoTokenKind.CloseParen, "')'");
                return new WebLingoParenthesizedExpressionSyntax(inner, SpanFrom(token.Span.Start));
            case WebLingoTokenKind.Bad:
                // Already reported by the lexer.
                Advance();
                _statementHasError = true;
                return new WebLingoErrorExpressionSyntax(token.Span);
            default:
                ReportUnexpected("an expression");
                // Consume the token unless the statement's recovery needs it to find where the next one starts.
                if (token.Kind is not (WebLingoTokenKind.EndOfFile or WebLingoTokenKind.Semicolon or WebLingoTokenKind.Var
                    or WebLingoTokenKind.Const or WebLingoTokenKind.Let or WebLingoTokenKind.OpenBrace or WebLingoTokenKind.CloseBrace))
                {
                    Advance();
                }
                return new WebLingoErrorExpressionSyntax(new WebLingoSourceSpan(token.Span.Start, 0));
        }
    }

    /// <summary>A literal's elements, separated by commas, with no trailing comma, up to and with its closing token.</summary>
    private List<WebLingoExpressionSyntax> ParseElements(WebLingoTokenKind close, string shown)
    {
        var elements = new List<WebLingoExpressionSyntax>();
        if (Current.Kind != close)
        {
            elements.Add(ParseExpression());
            while (Current.Kind == WebLingoTokenKind.Comma)
            {
                Advance();
                elements.Add(ParseExpression());
            }
        }
        Expect(close, shown);
        return elements;
    }

    /// <summary>
    /// Skips the rest of a map literal after an error in it: to its closing brace, which it consumes, or to the end of
    /// the statement, so that what is left of it is not reported again.
    /// </summary>
    private void SkipToClosingBrace()
    {
        var depth = 0;
        while (Current.Kind is not (WebLingoTokenKind.EndOfFile or WebLingoTokenKind.Semicolon))
        {
            if (Current.Kind == WebLingoTokenKind.OpenBrace) depth++;
            if (Current.Kind == WebLingoTokenKind.CloseBrace && depth-- == 0)
            {
                Advance();
                return;
            }
            Advance();
        }
    }

    private void Expect(WebLingoTokenKind kind, string shown)
    {
        if (Current.Kind == kind)
        {
            Advance();
            return;
        }
        if (Current.Kind == WebLingoTokenKind.Bad)
        {
            _statementHasError = true;
            return;
        }
        if (!_statementHasError) ReportUnexpected(shown);
    }

    private void ReportUnexpected(string expected)
    {
        if (Current.Kind == WebLingoTokenKind.Bad)
        {
            _statementHasError = true;
            return;
        }
        var found = Current.Kind == WebLingoTokenKind.EndOfFile ? "the end of the file" : $"'{Current.Text}'";
        Report(WebLingoDiagnosticCodes.UnexpectedToken, $"Expected {expected}, found {found}.", Current.Span);
    }

    private bool EnterRecursion()
    {
        if (_recursionDepth >= MaxRecursionDepth)
        {
            if (!_statementHasError)
            {
                Report(WebLingoDiagnosticCodes.NestingTooDeep,
                    $"The expression nests more than {MaxRecursionDepth} levels deep.", Current.Span);
            }
            // Skip the rest of the statement, so that unwinding does not report every level it leaves.
            _statementHasError = true;
            while (Current.Kind is not (WebLingoTokenKind.EndOfFile or WebLingoTokenKind.Semicolon or WebLingoTokenKind.CloseBrace)) Advance();
            return false;
        }
        _recursionDepth++;
        return true;
    }

    /// <summary>
    /// Reports an expression deeper than <see cref="MaxExpressionDepth"/>, such as a very long chain of additions,
    /// which parses in a loop but would overflow the stack of a recursive walk. This walk uses a stack of its own.
    /// </summary>
    private void CheckDepth(WebLingoCompilationUnitSyntax root)
    {
        var pending = new Stack<(WebLingoExpressionSyntax Expression, int Depth)>();
        var statements = new Stack<WebLingoStatementSyntax>();
        for (var i = root.Statements.Count - 1; i >= 0; i--) statements.Push(root.Statements[i]);
        while (statements.Count > 0)
        {
            var statement = statements.Pop();
            if (statement is WebLingoBlockSyntax block)
            {
                for (var i = block.Statements.Count - 1; i >= 0; i--) statements.Push(block.Statements[i]);
                continue;
            }
            if (statement is WebLingoIfSyntax conditional)
            {
                if (conditional.Else is { } otherwise) statements.Push(otherwise);
                statements.Push(conditional.Then);
            }
            if (statement is WebLingoWhileSyntax whileLoop) statements.Push(whileLoop.Body);
            if (statement is WebLingoFunctionDeclarationSyntax function)
            {
                statements.Push(function.Body);
                PushDefaults(function.Parameters, statements);
            }
            if (statement is WebLingoDoWhileSyntax doLoop) statements.Push(doLoop.Body);
            if (statement is WebLingoForEachSyntax forEachLoop) statements.Push(forEachLoop.Body);
            if (statement is WebLingoUsingSyntax usingStatement) statements.Push(usingStatement.Body);
            if (statement is WebLingoExportSyntax exported) statements.Push(exported.Declaration);
            if (statement is WebLingoTrySyntax tryStatement)
            {
                statements.Push(tryStatement.Body);
                foreach (var catchClause in tryStatement.Catches) statements.Push(catchClause.Body);
                if (tryStatement.Finally is { } finallyBlock) statements.Push(finallyBlock);
            }
            var (classMethods, classFields) = statement switch
            {
                WebLingoClassDeclarationSyntax declared => (declared.Methods, declared.StaticFields),
                WebLingoExtendSyntax extension => (extension.Methods, extension.StaticFields),
                _ => (null, null)
            };
            if (classMethods is not null)
            {
                foreach (var method in classMethods)
                {
                    statements.Push(method.Body);
                    PushDefaults(method.Parameters, statements);
                }
                foreach (var field in classFields!)
                {
                    statements.Push(new WebLingoExpressionStatementSyntax(field.Value, field.Value.Span));
                }
            }
            if (statement is WebLingoForSyntax forLoop)
            {
                // Its parts, as statements of their own, so that each expression's depth is checked.
                statements.Push(forLoop.Body);
                if (forLoop.Step is { } step) statements.Push(new WebLingoExpressionStatementSyntax(step, step.Span));
                if (forLoop.Condition is { } test) statements.Push(new WebLingoExpressionStatementSyntax(test, test.Span));
                if (forLoop.Init is { } init) statements.Push(init);
                continue;
            }
            var top = statement switch
            {
                WebLingoVariableDeclarationSyntax { Initializer: { } initializer } => initializer,
                WebLingoExpressionStatementSyntax expressionStatement => expressionStatement.Expression,
                WebLingoIfSyntax ifStatement => ifStatement.Condition,
                WebLingoWhileSyntax whileStatement => whileStatement.Condition,
                WebLingoDoWhileSyntax doStatement => doStatement.Condition,
                WebLingoForEachSyntax forEachStatement => forEachStatement.Collection,
                WebLingoUsingSyntax usingResource => usingResource.Value,
                WebLingoThrowSyntax { Value: { } thrown } => thrown,
                WebLingoReturnSyntax { Value: { } returned } => returned,
                _ => null
            };
            if (top is null) continue;
            pending.Push((top, 1));
            while (pending.Count > 0)
            {
                var (expression, depth) = pending.Pop();
                if (depth > MaxExpressionDepth)
                {
                    Report(WebLingoDiagnosticCodes.NestingTooDeep,
                        $"The expression is more than {MaxExpressionDepth} operations deep.", statement.Span);
                    pending.Clear();
                    break;
                }
                switch (expression)
                {
                    case WebLingoUnaryExpressionSyntax unary: pending.Push((unary.Operand, depth + 1)); break;
                    case WebLingoParenthesizedExpressionSyntax parenthesized:
                        pending.Push((parenthesized.Expression, depth + 1));
                        break;
                    case WebLingoBinaryExpressionSyntax binary:
                        pending.Push((binary.Left, depth + 1));
                        pending.Push((binary.Right, depth + 1));
                        break;
                    case WebLingoIsExpressionSyntax test: pending.Push((test.Value, depth + 1)); break;
                    case WebLingoAwaitExpressionSyntax awaited: pending.Push((awaited.Operand, depth + 1)); break;
                    case WebLingoAssignmentExpressionSyntax assignment: pending.Push((assignment.Value, depth + 1)); break;
                    case WebLingoFunctionExpressionSyntax functionExpression:
                        statements.Push(functionExpression.Body);
                        PushDefaults(functionExpression.Parameters, statements);
                        break;
                    case WebLingoCallExpressionSyntax call:
                        pending.Push((call.Callee, depth + 1));
                        foreach (var argument in call.Arguments) pending.Push((argument, depth + 1));
                        break;
                    case WebLingoIndexExpressionSyntax index:
                        pending.Push((index.Target, depth + 1));
                        pending.Push((index.Index, depth + 1));
                        break;
                    case WebLingoMemberAccessSyntax member: pending.Push((member.Target, depth + 1)); break;
                    case WebLingoArrayLiteralSyntax array:
                        foreach (var element in array.Elements) pending.Push((element, depth + 1));
                        break;
                    case WebLingoSetLiteralSyntax set:
                        foreach (var element in set.Elements) pending.Push((element, depth + 1));
                        break;
                    case WebLingoMapLiteralSyntax map:
                        foreach (var entry in map.Entries) pending.Push((entry.Value, depth + 1));
                        break;
                    case WebLingoMemberAssignmentSyntax field:
                        pending.Push((field.Target, depth + 1));
                        pending.Push((field.Value, depth + 1));
                        break;
                    case WebLingoNewExpressionSyntax created:
                        pending.Push((created.Class, depth + 1));
                        foreach (var argument in created.Arguments) pending.Push((argument, depth + 1));
                        break;
                    case WebLingoIndexAssignmentSyntax element:
                        pending.Push((element.Target, depth + 1));
                        pending.Push((element.Value, depth + 1));
                        break;
                }
            }
        }
    }

    private WebLingoToken Peek(int ahead) => _tokens[Math.Min(_index + ahead, _tokens.Count - 1)];

    private WebLingoToken Advance()
    {
        var token = Current;
        if (token.Kind != WebLingoTokenKind.EndOfFile)
        {
            _index++;
            _previousEnd = token.Span.Start + token.Span.Length;
        }
        return token;
    }

    private WebLingoSourceSpan SpanFrom(int start) => new(start, Math.Max(0, _previousEnd - start));

    private static WebLingoSourceSpan Between(WebLingoSourceSpan first, WebLingoSourceSpan last) =>
        new(first.Start, Math.Max(0, last.Start + last.Length - first.Start));

    private void Report(string code, string message, WebLingoSourceSpan span)
    {
        _statementHasError = true;
        _diagnostics.Add(new WebLingoDiagnostic(code, WebLingoDiagnosticSeverity.Error, message, span));
    }
}
