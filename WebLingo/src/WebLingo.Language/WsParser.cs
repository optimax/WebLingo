using WebLingo.Core;
namespace WebLingo.Language;

/// <summary>
/// The M0.6 parser (specification, section 8.4, "Parser"; its choices are in section 15.2): variable declarations,
/// expression statements, integer literals, names, unary <c>-</c> and <c>+</c>, <c>+ - * /</c>, parentheses, assignment and calls,
/// each simple statement ended by a mandatory semicolon. It recovers from an error at the next semicolon or
/// declaration, so that one pass reports several errors, and reports no error twice: a token the lexer already
/// reported as bad gives no further diagnostic.
/// </summary>
public sealed class WsParser : IWsParser
{
    /// <summary>How deeply parsing may recurse (parentheses, unary operators, assignments, arguments).</summary>
    public const int MaxRecursionDepth = 256;
    /// <summary>How deep a parsed expression may be, so that every later stage can walk it recursively.</summary>
    public const int MaxExpressionDepth = 1000;

    private readonly WsSourceText _source;
    private readonly IReadOnlyList<WsToken> _tokens;
    private readonly List<WsDiagnostic> _diagnostics = [];
    private int _index;
    private int _previousEnd;
    private int _recursionDepth;
    private bool _statementHasError;

    public WsParser(string source, string name = "") : this(new WsSourceText(source, name)) { }

    public WsParser(WsSourceText source)
    {
        _source = source;
        _tokens = WsLexer.Tokenize(source.Text, out var lexerDiagnostics);
        _diagnostics.AddRange(lexerDiagnostics);
    }

    private WsToken Current => _tokens[_index];

    public WsSyntaxTree ParseCompilationUnit()
    {
        var statements = new List<WsStatementSyntax>();
        while (Current.Kind != WsTokenKind.EndOfFile)
        {
            var before = _index;
            statements.Add(ParseStatement());
            if (_index == before) Advance(); // Never loop without consuming a token.
        }
        var root = new WsCompilationUnitSyntax(statements, new WsSourceSpan(0, _source.Text.Length));
        CheckDepth(root);
        return new WsSyntaxTree(_source, root, InSourceOrder(_diagnostics));
    }

    /// <summary>The lexer's and the parser's diagnostics merged by position; same-place diagnostics keep their order.</summary>
    private static WsDiagnostic[] InSourceOrder(List<WsDiagnostic> diagnostics)
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

    private WsStatementSyntax ParseStatement()
    {
        _statementHasError = false;
        var start = Current.Span.Start;
        if (Current.Kind is WsTokenKind.Var or WsTokenKind.Const or WsTokenKind.Let) return ParseVariableDeclaration(start);
        if (Current.Kind == WsTokenKind.OpenBrace) return ParseBlock(start);
        if (Current.Kind == WsTokenKind.If) return ParseIf(start);
        if (Current.Kind == WsTokenKind.While) return ParseWhile(start);
        if (Current.Kind == WsTokenKind.Do) return ParseDoWhile(start);
        if (Current.Kind == WsTokenKind.For) return ParseFor(start);
        if (Current.Kind == WsTokenKind.Foreach) return ParseForEach(start);
        if (Current.Kind == WsTokenKind.Function && Peek(1).Kind == WsTokenKind.Identifier) return ParseFunctionDeclaration(start);
        if (Current.Kind == WsTokenKind.Return)
        {
            Advance();
            var value = Current.Kind == WsTokenKind.Semicolon ? null : ParseExpression();
            ExpectSemicolon();
            return new WsReturnSyntax(value, SpanFrom(start));
        }
        if (Current.Kind is WsTokenKind.Break or WsTokenKind.Continue)
        {
            var keyword = Advance();
            ExpectSemicolon();
            return keyword.Kind == WsTokenKind.Break ? new WsBreakSyntax(SpanFrom(start)) : new WsContinueSyntax(SpanFrom(start));
        }
        var expression = ParseExpression();
        ExpectSemicolon();
        return new WsExpressionStatementSyntax(expression, SpanFrom(start));
    }

    /// <summary>A block; the statements in it recover as at the top level, and a missing '}' is reported at the end.</summary>
    private WsStatementSyntax ParseBlock(int start)
    {
        var open = Advance();
        var statements = new List<WsStatementSyntax>();
        if (!EnterRecursion()) return new WsBlockSyntax(statements, SpanFrom(start));
        try
        {
            while (Current.Kind is not (WsTokenKind.CloseBrace or WsTokenKind.EndOfFile))
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
        if (Current.Kind == WsTokenKind.CloseBrace)
        {
            Advance();
        }
        else
        {
            _statementHasError = false;
            Report(WsDiagnosticCodes.UnexpectedToken, "This '{' has no matching '}'.", open.Span);
        }
        return new WsBlockSyntax(statements, SpanFrom(start));
    }

    private WsStatementSyntax ParseIf(int start)
    {
        Advance();
        var condition = ParseCondition();
        if (!EnterRecursion()) return new WsExpressionStatementSyntax(condition, SpanFrom(start));
        try
        {
            var then = ParseStatement();
            WsStatementSyntax? otherwise = null;
            if (Current.Kind == WsTokenKind.Else)
            {
                Advance();
                otherwise = ParseStatement();
            }
            return new WsIfSyntax(condition, then, otherwise, SpanFrom(start));
        }
        finally
        {
            _recursionDepth--;
        }
    }

    private WsStatementSyntax ParseWhile(int start)
    {
        Advance();
        var condition = ParseCondition();
        var body = ParseNested();
        return body is null ? new WsExpressionStatementSyntax(condition, SpanFrom(start))
            : new WsWhileSyntax(condition, body, SpanFrom(start));
    }

    private WsStatementSyntax ParseDoWhile(int start)
    {
        Advance();
        var body = ParseNested();
        if (body is null) return new WsBlockSyntax([], SpanFrom(start));
        _statementHasError = false;
        Expect(WsTokenKind.While, "'while'");
        var condition = ParseCondition();
        ExpectSemicolon();
        return new WsDoWhileSyntax(body, condition, SpanFrom(start));
    }

    /// <summary>
    /// <c>foreach (var item in collection) body</c> or <c>foreach (var (key, value) in map) body</c>; the variables are
    /// declared with <c>var</c> only, and <c>in</c> is a keyword only there (sections 3.5 and 3.6).
    /// </summary>
    private WsStatementSyntax ParseForEach(int start)
    {
        Advance();
        _statementHasError = false;
        Expect(WsTokenKind.OpenParen, "'('");
        if (Current.Kind != WsTokenKind.Var && !_statementHasError) ReportUnexpected("'var'");
        if (Current.Kind is WsTokenKind.Var or WsTokenKind.Const or WsTokenKind.Let) Advance(); // 'const' read as 'var' to recover.
        string? valueName = null;
        var valueSpan = default(WsSourceSpan);
        (string Name, WsSourceSpan Span) item;
        if (Current.Kind == WsTokenKind.OpenParen)
        {
            Advance();
            item = ExpectName();
            Expect(WsTokenKind.Comma, "','");
            (valueName, valueSpan) = ExpectName();
            Expect(WsTokenKind.CloseParen, "')'");
        }
        else if (IsIn(Current) && !IsIn(Peek(1)))
        {
            // 'foreach (var in …)': the name is missing, and 'in' is where it should be.
            ReportUnexpected("a name");
            item = ("", new WsSourceSpan(Current.Span.Start, 0));
        }
        else
        {
            item = ExpectName();
        }
        if (Current.Kind == WsTokenKind.Identifier && !IsIn(Current) && !_statementHasError) ReportUnexpected("'in'");
        if (Current.Kind == WsTokenKind.Identifier) Advance(); // Another name, such as 'of', read as 'in' to recover.
        else if (!_statementHasError) ReportUnexpected("'in'");
        var collection = ParseExpression();
        Expect(WsTokenKind.CloseParen, "')'");
        var body = ParseNested();
        return body is null
            ? new WsExpressionStatementSyntax(collection, SpanFrom(start))
            : new WsForEachSyntax(item.Name, item.Span, valueName, valueSpan, collection, body, SpanFrom(start));
    }

    private static bool IsIn(WsToken token) => token.Kind == WsTokenKind.Identifier && token.Text == "in";

    /// <summary>A name being declared; an empty one, reported, if there is none.</summary>
    private (string Name, WsSourceSpan Span) ExpectName()
    {
        var token = Current;
        if (token.Kind == WsTokenKind.Identifier)
        {
            Advance();
            return (token.Text, token.Span);
        }
        if (!_statementHasError) ReportUnexpected("a name");
        return ("", new WsSourceSpan(token.Span.Start, 0));
    }

    private WsStatementSyntax ParseFor(int start)
    {
        Advance();
        _statementHasError = false;
        Expect(WsTokenKind.OpenParen, "'('");
        WsStatementSyntax? init = null;
        if (Current.Kind is WsTokenKind.Var or WsTokenKind.Const or WsTokenKind.Let)
        {
            init = ParseVariableDeclaration(Current.Span.Start); // Ends with its ';'.
        }
        else if (Current.Kind == WsTokenKind.Semicolon)
        {
            Advance();
        }
        else
        {
            var initStart = Current.Span.Start;
            var expression = ParseExpression();
            init = new WsExpressionStatementSyntax(expression, SpanFrom(initStart));
            Expect(WsTokenKind.Semicolon, "';'");
        }
        WsExpressionSyntax? condition = Current.Kind == WsTokenKind.Semicolon ? null : ParseExpression();
        Expect(WsTokenKind.Semicolon, "';'");
        WsExpressionSyntax? step = Current.Kind == WsTokenKind.CloseParen ? null : ParseExpression();
        Expect(WsTokenKind.CloseParen, "')'");
        var body = ParseNested();
        return body is null ? new WsBlockSyntax([], SpanFrom(start))
            : new WsForSyntax(init, condition, step, body, SpanFrom(start));
    }

    private WsStatementSyntax ParseFunctionDeclaration(int start)
    {
        Advance();
        var name = Advance();
        var (parameters, body) = ParseFunctionRest();
        return new WsFunctionDeclarationSyntax(name.Text, name.Span, parameters, body, SpanFrom(start));
    }

    /// <summary>A function's parameters and body, after <c>function</c> and its name, if it has one.</summary>
    private (IReadOnlyList<WsParameterSyntax> Parameters, WsBlockSyntax Body) ParseFunctionRest()
    {
        _statementHasError = false;
        var parameters = new List<WsParameterSyntax>();
        Expect(WsTokenKind.OpenParen, "'('");
        if (Current.Kind != WsTokenKind.CloseParen)
        {
            while (true)
            {
                if (Current.Kind != WsTokenKind.Identifier)
                {
                    ReportUnexpected("a parameter name");
                    break;
                }
                var parameter = Advance();
                parameters.Add(new WsParameterSyntax(parameter.Text, parameter.Span));
                if (Current.Kind != WsTokenKind.Comma) break;
                Advance();
            }
        }
        Expect(WsTokenKind.CloseParen, "')'");
        var bodyStart = Current.Span.Start;
        if (Current.Kind != WsTokenKind.OpenBrace)
        {
            if (!_statementHasError) ReportUnexpected("'{', which begins the function's body");
            return (parameters, new WsBlockSyntax([], new WsSourceSpan(bodyStart, 0)));
        }
        var body = ParseBlock(bodyStart);
        return (parameters, body as WsBlockSyntax ?? new WsBlockSyntax([], body.Span));
    }

    /// <summary>A statement nested in another, within the parser's recursion limit; null beyond it.</summary>
    private WsStatementSyntax? ParseNested()
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
    private WsExpressionSyntax ParseCondition()
    {
        _statementHasError = false;
        Expect(WsTokenKind.OpenParen, "'('");
        var condition = ParseExpression();
        Expect(WsTokenKind.CloseParen, "')'");
        return condition;
    }

    private WsVariableDeclarationSyntax ParseVariableDeclaration(int start)
    {
        var keyword = Advance();
        if (keyword.Kind == WsTokenKind.Let)
        {
            Report(WsDiagnosticCodes.UnexpectedToken,
                "'let' is not a declaration keyword in WebLingo: use 'var' for a variable, or 'const' for a constant.",
                keyword.Span);
        }
        var kind = keyword.Kind == WsTokenKind.Const ? WsDeclarationKind.Const : WsDeclarationKind.Var;

        var name = "";
        var nameSpan = new WsSourceSpan(Current.Span.Start, 0);
        if (Current.Kind == WsTokenKind.Identifier)
        {
            var token = Advance();
            (name, nameSpan) = (token.Text, token.Span);
        }
        else
        {
            ReportUnexpected($"a name after '{keyword.Text}'");
        }

        WsExpressionSyntax? initializer = null;
        if (Current.Kind == WsTokenKind.Equals)
        {
            Advance();
            initializer = ParseExpression();
        }
        else if (!_statementHasError)
        {
            // Every declaration has a value (specification, section 3.1).
            var keywordText = kind == WsDeclarationKind.Const ? "const" : "var";
            Report(WsDiagnosticCodes.MissingInitializer,
                $"'{name}' needs a value where it is declared: '{keywordText} {name} = …;'.", nameSpan);
        }
        ExpectSemicolon();
        return new WsVariableDeclarationSyntax(kind, name, nameSpan, initializer, SpanFrom(start));
    }

    private void ExpectSemicolon()
    {
        if (Current.Kind == WsTokenKind.Semicolon)
        {
            Advance();
            return;
        }
        if (Current.Kind == WsTokenKind.Bad) _statementHasError = true;
        if (_statementHasError)
        {
            // This statement already has an error: skip to where the next one should start, reporting nothing more.
            while (Current.Kind is not (WsTokenKind.EndOfFile or WsTokenKind.Semicolon or WsTokenKind.Var
                   or WsTokenKind.Const or WsTokenKind.Let or WsTokenKind.OpenBrace or WsTokenKind.CloseBrace))
            {
                Advance();
            }
            if (Current.Kind == WsTokenKind.Semicolon) Advance();
            return;
        }
        Report(WsDiagnosticCodes.MissingSemicolon, "A ';' is missing here.", new WsSourceSpan(_previousEnd, 0));
    }

    private WsExpressionSyntax ParseExpression()
    {
        if (!EnterRecursion()) return new WsErrorExpressionSyntax(new WsSourceSpan(Current.Span.Start, 0));
        try
        {
            return ParseAssignment();
        }
        finally
        {
            _recursionDepth--;
        }
    }

    private WsExpressionSyntax ParseAssignment()
    {
        var left = ParseOr();
        if (Current.Kind != WsTokenKind.Equals) return left;
        var equals = Advance();
        var value = ParseExpression();
        var span = Between(left.Span, value.Span);
        if (left is WsNameSyntax target) return new WsAssignmentExpressionSyntax(target, value, span);
        if (left is WsIndexExpressionSyntax element) return new WsIndexAssignmentSyntax(element, value, span);
        if (left is not WsErrorExpressionSyntax)
        {
            Report(WsDiagnosticCodes.InvalidAssignmentTarget,
                "Only a variable, or an element or entry ('x[i]'), can be assigned a value with '='.",
                left.Span.Length > 0 ? left.Span : equals.Span);
        }
        return new WsErrorExpressionSyntax(span);
    }

    private WsExpressionSyntax ParseOr() => ParseLeftAssociative(ParseAnd, WsTokenKind.PipePipe);

    private WsExpressionSyntax ParseAnd() => ParseLeftAssociative(ParseEquality, WsTokenKind.AmpAmp);

    private WsExpressionSyntax ParseEquality() =>
        ParseLeftAssociative(ParseRelational, WsTokenKind.EqualEqual, WsTokenKind.BangEqual);

    private WsExpressionSyntax ParseRelational() =>
        ParseLeftAssociative(ParseAdditive, WsTokenKind.Less, WsTokenKind.LessEqual, WsTokenKind.Greater,
            WsTokenKind.GreaterEqual);

    /// <summary>One level of left-associative binary operators (specification, section 3.5, "Precedence").</summary>
    private WsExpressionSyntax ParseLeftAssociative(Func<WsExpressionSyntax> operand, params WsTokenKind[] operators)
    {
        var left = operand();
        while (Array.IndexOf(operators, Current.Kind) >= 0)
        {
            var op = Advance();
            var right = operand();
            left = new WsBinaryExpressionSyntax(BinaryOperatorOf(op.Kind), left, right, op.Span, Between(left.Span, right.Span));
        }
        return left;
    }

    private static WsBinaryOperator BinaryOperatorOf(WsTokenKind kind) => kind switch
    {
        WsTokenKind.PipePipe => WsBinaryOperator.Or,
        WsTokenKind.AmpAmp => WsBinaryOperator.And,
        WsTokenKind.EqualEqual => WsBinaryOperator.Equal,
        WsTokenKind.BangEqual => WsBinaryOperator.NotEqual,
        WsTokenKind.Less => WsBinaryOperator.Less,
        WsTokenKind.LessEqual => WsBinaryOperator.LessOrEqual,
        WsTokenKind.Greater => WsBinaryOperator.Greater,
        _ => WsBinaryOperator.GreaterOrEqual
    };

    private WsExpressionSyntax ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (Current.Kind is WsTokenKind.Plus or WsTokenKind.Minus)
        {
            var op = Advance();
            var right = ParseMultiplicative();
            var kind = op.Kind == WsTokenKind.Plus ? WsBinaryOperator.Add : WsBinaryOperator.Subtract;
            left = new WsBinaryExpressionSyntax(kind, left, right, op.Span, Between(left.Span, right.Span));
        }
        return left;
    }

    private WsExpressionSyntax ParseMultiplicative()
    {
        var left = ParseUnary();
        while (Current.Kind is WsTokenKind.Star or WsTokenKind.Slash)
        {
            var op = Advance();
            var right = ParseUnary();
            var kind = op.Kind == WsTokenKind.Star ? WsBinaryOperator.Multiply : WsBinaryOperator.Divide;
            left = new WsBinaryExpressionSyntax(kind, left, right, op.Span, Between(left.Span, right.Span));
        }
        return left;
    }

    private WsExpressionSyntax ParseUnary()
    {
        if (Current.Kind is not (WsTokenKind.Minus or WsTokenKind.Plus or WsTokenKind.Bang)) return ParseCall();
        if (!EnterRecursion()) return new WsErrorExpressionSyntax(new WsSourceSpan(Current.Span.Start, 0));
        try
        {
            var sign = Advance();
            var operand = ParseUnary();
            var kind = sign.Kind switch
            {
                WsTokenKind.Minus => WsUnaryOperator.Negate,
                WsTokenKind.Plus => WsUnaryOperator.Plus,
                _ => WsUnaryOperator.Not
            };
            return new WsUnaryExpressionSyntax(kind, operand, Between(sign.Span, operand.Span));
        }
        finally
        {
            _recursionDepth--;
        }
    }

    /// <summary>A primary expression followed by any calls, <c>(…)</c>, members, <c>.name</c>, and indexes, <c>[…]</c>.</summary>
    private WsExpressionSyntax ParseCall()
    {
        var expression = ParsePrimary();
        while (true)
        {
            switch (Current.Kind)
            {
                case WsTokenKind.OpenParen:
                {
                    Advance();
                    var arguments = new List<WsExpressionSyntax>();
                    if (Current.Kind != WsTokenKind.CloseParen)
                    {
                        arguments.Add(ParseExpression());
                        while (Current.Kind == WsTokenKind.Comma)
                        {
                            Advance();
                            arguments.Add(ParseExpression());
                        }
                    }
                    Expect(WsTokenKind.CloseParen, "')'");
                    expression = new WsCallExpressionSyntax(expression, arguments, SpanFrom(expression.Span.Start));
                    continue;
                }
                case WsTokenKind.Dot:
                {
                    Advance();
                    if (Current.Kind != WsTokenKind.Identifier)
                    {
                        ReportUnexpected("a member's name");
                        return new WsErrorExpressionSyntax(SpanFrom(expression.Span.Start));
                    }
                    var name = Advance();
                    expression = new WsMemberAccessSyntax(expression, name.Text, name.Span, SpanFrom(expression.Span.Start));
                    continue;
                }
                case WsTokenKind.OpenBracket:
                {
                    Advance();
                    var index = ParseExpression();
                    Expect(WsTokenKind.CloseBracket, "']'");
                    expression = new WsIndexExpressionSyntax(expression, index, SpanFrom(expression.Span.Start));
                    continue;
                }
                default:
                    return expression;
            }
        }
    }

    private WsExpressionSyntax ParsePrimary()
    {
        var token = Current;
        switch (token.Kind)
        {
            case WsTokenKind.IntegerLiteral:
                Advance();
                return new WsIntegerLiteralSyntax(token.IntegerValue, token.Span);
            case WsTokenKind.Binary64Literal:
                Advance();
                return new WsBinary64LiteralSyntax(token.Binary64Value, token.Span);
            case WsTokenKind.True or WsTokenKind.False:
                Advance();
                return new WsBooleanLiteralSyntax(token.Kind == WsTokenKind.True, token.Span);
            case WsTokenKind.Null:
                Advance();
                return new WsNullLiteralSyntax(token.Span);
            case WsTokenKind.StringLiteral:
                Advance();
                return new WsStringLiteralSyntax(token.StringValue!, token.Span);
            case WsTokenKind.Function:
            {
                Advance();
                if (!EnterRecursion()) return new WsErrorExpressionSyntax(token.Span);
                try
                {
                    var (parameters, body) = ParseFunctionRest();
                    return new WsFunctionExpressionSyntax(parameters, body, SpanFrom(token.Span.Start));
                }
                finally
                {
                    _recursionDepth--;
                }
            }
            case WsTokenKind.Infinity:
                Advance();
                return new WsBinary64LiteralSyntax(double.PositiveInfinity, token.Span);
            case WsTokenKind.NaN:
                Advance();
                return new WsBinary64LiteralSyntax(double.NaN, token.Span);
            case WsTokenKind.Identifier:
                Advance();
                return new WsNameSyntax(token.Text, token.Span);
            case WsTokenKind.OpenBracket:
            {
                // [a, b, c], or [], with no trailing comma, as in calls (section 3.6).
                Advance();
                var elements = new List<WsExpressionSyntax>();
                if (Current.Kind != WsTokenKind.CloseBracket)
                {
                    elements.Add(ParseExpression());
                    while (Current.Kind == WsTokenKind.Comma)
                    {
                        Advance();
                        elements.Add(ParseExpression());
                    }
                }
                Expect(WsTokenKind.CloseBracket, "']'");
                return new WsArrayLiteralSyntax(elements, SpanFrom(token.Span.Start));
            }
            case WsTokenKind.OpenBrace:
            {
                // { key: value, … }, or {}, a key written as a name or as a string literal (section 3.6).
                Advance();
                var entries = new List<WsMapEntrySyntax>();
                if (Current.Kind != WsTokenKind.CloseBrace)
                {
                    while (true)
                    {
                        var key = Current;
                        if (key.Kind is not (WsTokenKind.Identifier or WsTokenKind.StringLiteral))
                        {
                            ReportUnexpected("a key: a name or a string");
                            SkipToClosingBrace();
                            return new WsErrorExpressionSyntax(SpanFrom(token.Span.Start));
                        }
                        Advance();
                        Expect(WsTokenKind.Colon, "':'");
                        var value = ParseExpression();
                        entries.Add(new WsMapEntrySyntax(key.Kind == WsTokenKind.Identifier ? key.Text : key.StringValue!,
                            key.Span, value));
                        if (Current.Kind != WsTokenKind.Comma) break;
                        Advance();
                    }
                }
                Expect(WsTokenKind.CloseBrace, "'}'");
                return new WsMapLiteralSyntax(entries, SpanFrom(token.Span.Start));
            }
            case WsTokenKind.OpenParen:
                Advance();
                var inner = ParseExpression();
                Expect(WsTokenKind.CloseParen, "')'");
                return new WsParenthesizedExpressionSyntax(inner, SpanFrom(token.Span.Start));
            case WsTokenKind.Bad:
                // Already reported by the lexer.
                Advance();
                _statementHasError = true;
                return new WsErrorExpressionSyntax(token.Span);
            default:
                ReportUnexpected("an expression");
                // Consume the token unless the statement's recovery needs it to find where the next one starts.
                if (token.Kind is not (WsTokenKind.EndOfFile or WsTokenKind.Semicolon or WsTokenKind.Var
                    or WsTokenKind.Const or WsTokenKind.Let or WsTokenKind.OpenBrace or WsTokenKind.CloseBrace))
                {
                    Advance();
                }
                return new WsErrorExpressionSyntax(new WsSourceSpan(token.Span.Start, 0));
        }
    }

    /// <summary>
    /// Skips the rest of a map literal after an error in it: to its closing brace, which it consumes, or to the end of
    /// the statement, so that what is left of it is not reported again.
    /// </summary>
    private void SkipToClosingBrace()
    {
        var depth = 0;
        while (Current.Kind is not (WsTokenKind.EndOfFile or WsTokenKind.Semicolon))
        {
            if (Current.Kind == WsTokenKind.OpenBrace) depth++;
            if (Current.Kind == WsTokenKind.CloseBrace && depth-- == 0)
            {
                Advance();
                return;
            }
            Advance();
        }
    }

    private void Expect(WsTokenKind kind, string shown)
    {
        if (Current.Kind == kind)
        {
            Advance();
            return;
        }
        if (Current.Kind == WsTokenKind.Bad)
        {
            _statementHasError = true;
            return;
        }
        if (!_statementHasError) ReportUnexpected(shown);
    }

    private void ReportUnexpected(string expected)
    {
        if (Current.Kind == WsTokenKind.Bad)
        {
            _statementHasError = true;
            return;
        }
        var found = Current.Kind == WsTokenKind.EndOfFile ? "the end of the file" : $"'{Current.Text}'";
        Report(WsDiagnosticCodes.UnexpectedToken, $"Expected {expected}, found {found}.", Current.Span);
    }

    private bool EnterRecursion()
    {
        if (_recursionDepth >= MaxRecursionDepth)
        {
            if (!_statementHasError)
            {
                Report(WsDiagnosticCodes.NestingTooDeep,
                    $"The expression nests more than {MaxRecursionDepth} levels deep.", Current.Span);
            }
            // Skip the rest of the statement, so that unwinding does not report every level it leaves.
            _statementHasError = true;
            while (Current.Kind is not (WsTokenKind.EndOfFile or WsTokenKind.Semicolon or WsTokenKind.CloseBrace)) Advance();
            return false;
        }
        _recursionDepth++;
        return true;
    }

    /// <summary>
    /// Reports an expression deeper than <see cref="MaxExpressionDepth"/>, such as a very long chain of additions,
    /// which parses in a loop but would overflow the stack of a recursive walk. This walk uses a stack of its own.
    /// </summary>
    private void CheckDepth(WsCompilationUnitSyntax root)
    {
        var pending = new Stack<(WsExpressionSyntax Expression, int Depth)>();
        var statements = new Stack<WsStatementSyntax>();
        for (var i = root.Statements.Count - 1; i >= 0; i--) statements.Push(root.Statements[i]);
        while (statements.Count > 0)
        {
            var statement = statements.Pop();
            if (statement is WsBlockSyntax block)
            {
                for (var i = block.Statements.Count - 1; i >= 0; i--) statements.Push(block.Statements[i]);
                continue;
            }
            if (statement is WsIfSyntax conditional)
            {
                if (conditional.Else is { } otherwise) statements.Push(otherwise);
                statements.Push(conditional.Then);
            }
            if (statement is WsWhileSyntax whileLoop) statements.Push(whileLoop.Body);
            if (statement is WsFunctionDeclarationSyntax function) statements.Push(function.Body);
            if (statement is WsDoWhileSyntax doLoop) statements.Push(doLoop.Body);
            if (statement is WsForEachSyntax forEachLoop) statements.Push(forEachLoop.Body);
            if (statement is WsForSyntax forLoop)
            {
                // Its parts, as statements of their own, so that each expression's depth is checked.
                statements.Push(forLoop.Body);
                if (forLoop.Step is { } step) statements.Push(new WsExpressionStatementSyntax(step, step.Span));
                if (forLoop.Condition is { } test) statements.Push(new WsExpressionStatementSyntax(test, test.Span));
                if (forLoop.Init is { } init) statements.Push(init);
                continue;
            }
            var top = statement switch
            {
                WsVariableDeclarationSyntax { Initializer: { } initializer } => initializer,
                WsExpressionStatementSyntax expressionStatement => expressionStatement.Expression,
                WsIfSyntax ifStatement => ifStatement.Condition,
                WsWhileSyntax whileStatement => whileStatement.Condition,
                WsDoWhileSyntax doStatement => doStatement.Condition,
                WsForEachSyntax forEachStatement => forEachStatement.Collection,
                WsReturnSyntax { Value: { } returned } => returned,
                _ => null
            };
            if (top is null) continue;
            pending.Push((top, 1));
            while (pending.Count > 0)
            {
                var (expression, depth) = pending.Pop();
                if (depth > MaxExpressionDepth)
                {
                    Report(WsDiagnosticCodes.NestingTooDeep,
                        $"The expression is more than {MaxExpressionDepth} operations deep.", statement.Span);
                    pending.Clear();
                    break;
                }
                switch (expression)
                {
                    case WsUnaryExpressionSyntax unary: pending.Push((unary.Operand, depth + 1)); break;
                    case WsParenthesizedExpressionSyntax parenthesized:
                        pending.Push((parenthesized.Expression, depth + 1));
                        break;
                    case WsBinaryExpressionSyntax binary:
                        pending.Push((binary.Left, depth + 1));
                        pending.Push((binary.Right, depth + 1));
                        break;
                    case WsAssignmentExpressionSyntax assignment: pending.Push((assignment.Value, depth + 1)); break;
                    case WsFunctionExpressionSyntax functionExpression:
                        statements.Push(functionExpression.Body);
                        break;
                    case WsCallExpressionSyntax call:
                        pending.Push((call.Callee, depth + 1));
                        foreach (var argument in call.Arguments) pending.Push((argument, depth + 1));
                        break;
                    case WsIndexExpressionSyntax index:
                        pending.Push((index.Target, depth + 1));
                        pending.Push((index.Index, depth + 1));
                        break;
                    case WsMemberAccessSyntax member: pending.Push((member.Target, depth + 1)); break;
                    case WsArrayLiteralSyntax array:
                        foreach (var element in array.Elements) pending.Push((element, depth + 1));
                        break;
                    case WsMapLiteralSyntax map:
                        foreach (var entry in map.Entries) pending.Push((entry.Value, depth + 1));
                        break;
                    case WsIndexAssignmentSyntax element:
                        pending.Push((element.Target, depth + 1));
                        pending.Push((element.Value, depth + 1));
                        break;
                }
            }
        }
    }

    private WsToken Peek(int ahead) => _tokens[Math.Min(_index + ahead, _tokens.Count - 1)];

    private WsToken Advance()
    {
        var token = Current;
        if (token.Kind != WsTokenKind.EndOfFile)
        {
            _index++;
            _previousEnd = token.Span.Start + token.Span.Length;
        }
        return token;
    }

    private WsSourceSpan SpanFrom(int start) => new(start, Math.Max(0, _previousEnd - start));

    private static WsSourceSpan Between(WsSourceSpan first, WsSourceSpan last) =>
        new(first.Start, Math.Max(0, last.Start + last.Length - first.Start));

    private void Report(string code, string message, WsSourceSpan span)
    {
        _statementHasError = true;
        _diagnostics.Add(new WsDiagnostic(code, WsDiagnosticSeverity.Error, message, span));
    }
}
