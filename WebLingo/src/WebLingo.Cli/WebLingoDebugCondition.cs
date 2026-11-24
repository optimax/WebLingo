using WebLingo.Core;
using WebLingo.Language;
using WebLingo.Runtime;
using WebLingo.VM;
namespace WebLingo.Cli;

/// <summary>
/// A breakpoint's condition as the debugger reads it where it is set (specification, section 5.7, "Debugging WASP pages,
/// parts", part 3; the owner's answers, 24 November 2026): a WebLingo expression without calls of the program's code,
/// names, literals, members, indexing, operators, and the built-in classes' methods that only read, of strings, arrays,
/// maps and sets; anything else, or a syntax error, refused, its message saying why, the names it reads given, for the
/// debugger to check against the scope of its line.
/// </summary>
internal static class WebLingoDebugCondition
{
    private const string Holds = "A condition holds names, literals, members, indexing, operators and the methods of strings, arrays, maps and "
                                 + "sets that only read, and does not run the program's code";

    public static bool TryRead(string text, out WebLingoDebugExpression? condition, out HashSet<string> names, out string? error)
    {
        (condition, names, error) = (null, new HashSet<string>(StringComparer.Ordinal), null);
        // In parentheses, a line break before the last, so that a comment at its end ends there.
        var tree = new WebLingoParser("(" + text + "\n);", "condition").ParseCompilationUnit();
        foreach (var diagnostic in tree.Diagnostics)
        {
            if (diagnostic.Severity != WebLingoDiagnosticSeverity.Error) continue;
            error = $"The condition does not parse: {diagnostic.Message}";
            return false;
        }
        if (tree.Root.Statements is not [WebLingoExpressionStatementSyntax { Expression: var expression }])
        {
            error = "The condition is not one expression.";
            return false;
        }
        condition = Read(expression, names, ref error);
        return error is null;
    }

    private static WebLingoDebugExpression? Read(WebLingoExpressionSyntax syntax, HashSet<string> names, ref string? error)
    {
        if (error is not null) return null;
        switch (syntax)
        {
            case WebLingoParenthesizedExpressionSyntax parenthesized:
                return Read(parenthesized.Expression, names, ref error);
            case WebLingoIntegerLiteralSyntax integer:
                return new WebLingoDebugExpression.Literal(integer.Value);
            case WebLingoBinary64LiteralSyntax number:
                return new WebLingoDebugExpression.Literal(number.Value);
            case WebLingoDecimalLiteralSyntax exact:
                return new WebLingoDebugExpression.Literal(WebLingoValue.ParseDecimal(exact.Digits));
            case WebLingoBooleanLiteralSyntax flag:
                return new WebLingoDebugExpression.Literal(flag.Value);
            case WebLingoNullLiteralSyntax:
                return new WebLingoDebugExpression.Literal(null);
            case WebLingoStringLiteralSyntax text:
                return new WebLingoDebugExpression.Literal(text.Value);
            case WebLingoNameSyntax name:
                names.Add(name.Name);
                return new WebLingoDebugExpression.Name(name.Name);
            case WebLingoMemberAccessSyntax member:
                return Read(member.Target, names, ref error) is { } owner ? new WebLingoDebugExpression.Member(owner, member.Name, member.IsOptional) : null;
            case WebLingoIndexExpressionSyntax index:
            {
                var target = Read(index.Target, names, ref error);
                var key = Read(index.Index, names, ref error);
                return target is null || key is null ? null : new WebLingoDebugExpression.Index(target, key, index.IsOptional);
            }
            case WebLingoCallExpressionSyntax { Callee: WebLingoMemberAccessSyntax method } call:
            {
                if (!WebLingoDebugExpression.ReadsOnly(method.Name))
                {
                    error = $"{Holds}: '{method.Name}' is not such a method.";
                    return null;
                }
                var target = Read(method.Target, names, ref error);
                var arguments = new List<WebLingoDebugExpression>();
                foreach (var argument in call.Arguments)
                {
                    if (Read(argument, names, ref error) is { } read) arguments.Add(read);
                }
                return target is null || error is not null ? null : new WebLingoDebugExpression.Call(target, method.Name, arguments, method.IsOptional);
            }
            case WebLingoUnaryExpressionSyntax unary:
                return Read(unary.Operand, names, ref error) is { } operand
                    ? new WebLingoDebugExpression.Unary(unary.Operator switch
                    {
                        WebLingoUnaryOperator.Not => WebLingoDebugOperator.Not,
                        WebLingoUnaryOperator.Negate => WebLingoDebugOperator.Negate,
                        _ => WebLingoDebugOperator.Plus
                    }, operand)
                    : null;
            case WebLingoBinaryExpressionSyntax binary:
            {
                var left = Read(binary.Left, names, ref error);
                var right = Read(binary.Right, names, ref error);
                return left is null || right is null ? null : new WebLingoDebugExpression.Binary(binary.Operator switch
                {
                    WebLingoBinaryOperator.Add => WebLingoDebugOperator.Add,
                    WebLingoBinaryOperator.Subtract => WebLingoDebugOperator.Subtract,
                    WebLingoBinaryOperator.Multiply => WebLingoDebugOperator.Multiply,
                    WebLingoBinaryOperator.Divide => WebLingoDebugOperator.Divide,
                    WebLingoBinaryOperator.Remainder => WebLingoDebugOperator.Remainder,
                    WebLingoBinaryOperator.IntegerDivide => WebLingoDebugOperator.IntegerDivide,
                    WebLingoBinaryOperator.Equal => WebLingoDebugOperator.Equal,
                    WebLingoBinaryOperator.NotEqual => WebLingoDebugOperator.NotEqual,
                    WebLingoBinaryOperator.Less => WebLingoDebugOperator.Less,
                    WebLingoBinaryOperator.LessOrEqual => WebLingoDebugOperator.LessOrEqual,
                    WebLingoBinaryOperator.Greater => WebLingoDebugOperator.Greater,
                    WebLingoBinaryOperator.GreaterOrEqual => WebLingoDebugOperator.GreaterOrEqual,
                    WebLingoBinaryOperator.And => WebLingoDebugOperator.And,
                    WebLingoBinaryOperator.Or => WebLingoDebugOperator.Or,
                    _ => WebLingoDebugOperator.Coalesce
                }, left, right);
            }
            case WebLingoConditionalExpressionSyntax conditional:
            {
                var test = Read(conditional.Condition, names, ref error);
                var whenTrue = Read(conditional.WhenTrue, names, ref error);
                var whenFalse = Read(conditional.WhenFalse, names, ref error);
                return test is null || whenTrue is null || whenFalse is null ? null : new WebLingoDebugExpression.Conditional(test, whenTrue, whenFalse);
            }
            default:
                error = $"{Holds}: it cannot hold {What(syntax)}.";
                return null;
        }
    }

    /// <summary>What a condition cannot hold, as its message names it.</summary>
    private static string What(WebLingoExpressionSyntax syntax) => syntax switch
    {
        WebLingoCallExpressionSyntax => "a call of a function",
        WebLingoAssignmentExpressionSyntax or WebLingoMemberAssignmentSyntax or WebLingoIndexAssignmentSyntax
            or WebLingoCompoundAssignmentSyntax or WebLingoIncrementSyntax => "an assignment",
        WebLingoNewExpressionSyntax => "new",
        WebLingoAwaitExpressionSyntax => "await",
        WebLingoFunctionExpressionSyntax => "a function",
        WebLingoArrayLiteralSyntax or WebLingoSetLiteralSyntax or WebLingoMapLiteralSyntax => "a collection's literal",
        WebLingoThisSyntax => "this",
        WebLingoIsExpressionSyntax => "is",
        _ => "this expression"
    };
}
