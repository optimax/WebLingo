using WebLingo.Core;
namespace WebLingo.Language;

// The syntax tree of M0.6's language subset (specification, section 8.4, "Parser"). Every node carries the span of
// source it was parsed from.

public abstract record WsSyntaxNode(WsSourceSpan Span);

public sealed record WsCompilationUnitSyntax(IReadOnlyList<WsStatementSyntax> Statements, WsSourceSpan Span)
    : WsSyntaxNode(Span);

public abstract record WsStatementSyntax(WsSourceSpan Span) : WsSyntaxNode(Span);

public enum WsDeclarationKind { Var, Const }

/// <summary>
/// <c>var x = 1;</c> or <c>const x = 1;</c>; the span includes the semicolon. <paramref name="Initializer"/> is null
/// only in a tree with errors (a declaration without a value is WS1006).
/// </summary>
public sealed record WsVariableDeclarationSyntax(
    WsDeclarationKind Kind, string Name, WsSourceSpan NameSpan, WsExpressionSyntax? Initializer, WsSourceSpan Span)
    : WsStatementSyntax(Span);

/// <summary>An expression followed by a semicolon, which the span includes.</summary>
public sealed record WsExpressionStatementSyntax(WsExpressionSyntax Expression, WsSourceSpan Span)
    : WsStatementSyntax(Span);

public abstract record WsExpressionSyntax(WsSourceSpan Span) : WsSyntaxNode(Span);

public sealed record WsIntegerLiteralSyntax(long Value, WsSourceSpan Span) : WsExpressionSyntax(Span);

public sealed record WsNameSyntax(string Name, WsSourceSpan Span) : WsExpressionSyntax(Span);

public enum WsUnaryOperator { Negate, Plus }

public sealed record WsUnaryExpressionSyntax(WsUnaryOperator Operator, WsExpressionSyntax Operand, WsSourceSpan Span)
    : WsExpressionSyntax(Span);

public enum WsBinaryOperator { Add, Subtract, Multiply, Divide }

public sealed record WsBinaryExpressionSyntax(
    WsBinaryOperator Operator, WsExpressionSyntax Left, WsExpressionSyntax Right, WsSourceSpan OperatorSpan,
    WsSourceSpan Span)
    : WsExpressionSyntax(Span);

/// <summary>An expression in parentheses; the span includes them.</summary>
public sealed record WsParenthesizedExpressionSyntax(WsExpressionSyntax Expression, WsSourceSpan Span)
    : WsExpressionSyntax(Span);

public sealed record WsAssignmentExpressionSyntax(WsNameSyntax Target, WsExpressionSyntax Value, WsSourceSpan Span)
    : WsExpressionSyntax(Span);

public sealed record WsCallExpressionSyntax(
    WsExpressionSyntax Callee, IReadOnlyList<WsExpressionSyntax> Arguments, WsSourceSpan Span)
    : WsExpressionSyntax(Span);

/// <summary>Where an expression should have been and could not be parsed; its diagnostic is already reported.</summary>
public sealed record WsErrorExpressionSyntax(WsSourceSpan Span) : WsExpressionSyntax(Span);
