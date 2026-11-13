using WebLingo.Core;
namespace WebLingo.Language;

/// <summary>
/// The M0.6 parser (specification, section 8.4, "Parser"; its choices are in section 15.2): variable declarations,
/// expression statements, integer literals, names, unary minus, <c>+ - * /</c>, parentheses, assignment and calls,
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
        var expression = ParseExpression();
        ExpectSemicolon();
        return new WsExpressionStatementSyntax(expression, SpanFrom(start));
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
                   or WsTokenKind.Const or WsTokenKind.Let))
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
        var left = ParseAdditive();
        if (Current.Kind != WsTokenKind.Equals) return left;
        var equals = Advance();
        var value = ParseExpression();
        var span = Between(left.Span, value.Span);
        if (left is WsNameSyntax target) return new WsAssignmentExpressionSyntax(target, value, span);
        if (left is not WsErrorExpressionSyntax)
        {
            Report(WsDiagnosticCodes.InvalidAssignmentTarget, "Only a variable can be assigned a value with '='.",
                left.Span.Length > 0 ? left.Span : equals.Span);
        }
        return new WsErrorExpressionSyntax(span);
    }

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
        if (Current.Kind != WsTokenKind.Minus) return ParseCall();
        if (!EnterRecursion()) return new WsErrorExpressionSyntax(new WsSourceSpan(Current.Span.Start, 0));
        try
        {
            var minus = Advance();
            var operand = ParseUnary();
            return new WsUnaryExpressionSyntax(WsUnaryOperator.Negate, operand, Between(minus.Span, operand.Span));
        }
        finally
        {
            _recursionDepth--;
        }
    }

    private WsExpressionSyntax ParseCall()
    {
        var expression = ParsePrimary();
        while (Current.Kind == WsTokenKind.OpenParen)
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
        }
        return expression;
    }

    private WsExpressionSyntax ParsePrimary()
    {
        var token = Current;
        switch (token.Kind)
        {
            case WsTokenKind.IntegerLiteral:
                Advance();
                return new WsIntegerLiteralSyntax(token.IntegerValue, token.Span);
            case WsTokenKind.Identifier:
                Advance();
                return new WsNameSyntax(token.Text, token.Span);
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
                    or WsTokenKind.Const or WsTokenKind.Let))
                {
                    Advance();
                }
                return new WsErrorExpressionSyntax(new WsSourceSpan(token.Span.Start, 0));
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
            while (Current.Kind is not (WsTokenKind.EndOfFile or WsTokenKind.Semicolon)) Advance();
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
        foreach (var statement in root.Statements)
        {
            var top = statement switch
            {
                WsVariableDeclarationSyntax { Initializer: { } initializer } => initializer,
                WsExpressionStatementSyntax expressionStatement => expressionStatement.Expression,
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
                    case WsCallExpressionSyntax call:
                        pending.Push((call.Callee, depth + 1));
                        foreach (var argument in call.Arguments) pending.Push((argument, depth + 1));
                        break;
                }
            }
        }
    }

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
