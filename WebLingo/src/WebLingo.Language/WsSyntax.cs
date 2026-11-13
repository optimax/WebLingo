using WebLingo.Core;
namespace WebLingo.Language;

// The syntax tree of M0.6's language subset (specification, section 8.4, "Parser"). Every node carries the span of
// source it was parsed from.

public abstract record WsSyntaxNode(WsSourceSpan Span);

public sealed record WsCompilationUnitSyntax(IReadOnlyList<WsStatementSyntax> Statements, WsSourceSpan Span)
    : WsSyntaxNode(Span);

public abstract record WsStatementSyntax(WsSourceSpan Span) : WsSyntaxNode(Span);

/// <summary>What declares a name: <c>var</c>, <c>const</c>, a function declaration, or a function's parameter.</summary>
public enum WsDeclarationKind { Var, Const, Function, Parameter }

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

/// <summary>A block, <c>{ statements }</c>, which opens a scope (specification, section 3.5).</summary>
public sealed record WsBlockSyntax(IReadOnlyList<WsStatementSyntax> Statements, WsSourceSpan Span) : WsStatementSyntax(Span);

/// <summary><c>if (condition) then</c>, with an optional <c>else</c> (specification, section 3.5).</summary>
public sealed record WsIfSyntax(WsExpressionSyntax Condition, WsStatementSyntax Then, WsStatementSyntax? Else, WsSourceSpan Span)
    : WsStatementSyntax(Span);

public sealed record WsWhileSyntax(WsExpressionSyntax Condition, WsStatementSyntax Body, WsSourceSpan Span)
    : WsStatementSyntax(Span);

public sealed record WsDoWhileSyntax(WsStatementSyntax Body, WsExpressionSyntax Condition, WsSourceSpan Span)
    : WsStatementSyntax(Span);

/// <summary>
/// <c>for (init; condition; step) body</c>; each part may be missing. <paramref name="Init"/> is a declaration or an
/// expression statement.
/// </summary>
public sealed record WsForSyntax(
    WsStatementSyntax? Init, WsExpressionSyntax? Condition, WsExpressionSyntax? Step, WsStatementSyntax Body, WsSourceSpan Span)
    : WsStatementSyntax(Span);

public sealed record WsBreakSyntax(WsSourceSpan Span) : WsStatementSyntax(Span);

public sealed record WsContinueSyntax(WsSourceSpan Span) : WsStatementSyntax(Span);

/// <summary>A function's parameter, by name.</summary>
public sealed record WsParameterSyntax(string Name, WsSourceSpan Span) : WsSyntaxNode(Span);

/// <summary><c>function name(parameters) { body }</c>, in scope throughout its block (specification, section 3.5).</summary>
public sealed record WsFunctionDeclarationSyntax(
    string Name, WsSourceSpan NameSpan, IReadOnlyList<WsParameterSyntax> Parameters, WsBlockSyntax Body, WsSourceSpan Span)
    : WsStatementSyntax(Span);

/// <summary><c>return value;</c>, or <c>return;</c>, which gives <c>null</c>.</summary>
public sealed record WsReturnSyntax(WsExpressionSyntax? Value, WsSourceSpan Span) : WsStatementSyntax(Span);

public abstract record WsExpressionSyntax(WsSourceSpan Span) : WsSyntaxNode(Span);

/// <summary><c>function (parameters) { body }</c>, a function value.</summary>
public sealed record WsFunctionExpressionSyntax(IReadOnlyList<WsParameterSyntax> Parameters, WsBlockSyntax Body, WsSourceSpan Span)
    : WsExpressionSyntax(Span);

public sealed record WsIntegerLiteralSyntax(long Value, WsSourceSpan Span) : WsExpressionSyntax(Span);

/// <summary>A binary64 literal: <c>1.5</c>, <c>1e21</c>, or the keyword <c>Infinity</c> or <c>NaN</c>.</summary>
public sealed record WsBinary64LiteralSyntax(double Value, WsSourceSpan Span) : WsExpressionSyntax(Span);

/// <summary><c>true</c> or <c>false</c>.</summary>
public sealed record WsBooleanLiteralSyntax(bool Value, WsSourceSpan Span) : WsExpressionSyntax(Span);

public sealed record WsNullLiteralSyntax(WsSourceSpan Span) : WsExpressionSyntax(Span);

/// <summary>A string literal; <paramref name="Value"/> is its text, its escapes decoded.</summary>
public sealed record WsStringLiteralSyntax(string Value, WsSourceSpan Span) : WsExpressionSyntax(Span);

public sealed record WsNameSyntax(string Name, WsSourceSpan Span) : WsExpressionSyntax(Span);

public enum WsUnaryOperator { Negate, Plus, Not }

public sealed record WsUnaryExpressionSyntax(WsUnaryOperator Operator, WsExpressionSyntax Operand, WsSourceSpan Span)
    : WsExpressionSyntax(Span);

public enum WsBinaryOperator
{
    Add, Subtract, Multiply, Divide, Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual,
    /// <summary><c>&amp;&amp;</c>, which evaluates its right operand only when the left one is true.</summary>
    And,
    /// <summary><c>||</c>, which evaluates its right operand only when the left one is false.</summary>
    Or
}

public sealed record WsBinaryExpressionSyntax(
    WsBinaryOperator Operator, WsExpressionSyntax Left, WsExpressionSyntax Right, WsSourceSpan OperatorSpan,
    WsSourceSpan Span)
    : WsExpressionSyntax(Span);

/// <summary>An expression in parentheses; the span includes them.</summary>
public sealed record WsParenthesizedExpressionSyntax(WsExpressionSyntax Expression, WsSourceSpan Span)
    : WsExpressionSyntax(Span);

public sealed record WsAssignmentExpressionSyntax(WsNameSyntax Target, WsExpressionSyntax Value, WsSourceSpan Span)
    : WsExpressionSyntax(Span);

/// <summary><c>[a, b, c]</c>, a new array of those values (section 3.6).</summary>
public sealed record WsArrayLiteralSyntax(IReadOnlyList<WsExpressionSyntax> Elements, WsSourceSpan Span) : WsExpressionSyntax(Span);

/// <summary>One entry of a map literal: its key, a string, written as a name or as a string literal, and its value.</summary>
public sealed record WsMapEntrySyntax(string Key, WsSourceSpan KeySpan, WsExpressionSyntax Value);

/// <summary><c>{ key: value, … }</c>, a new map (section 3.6); at the start of a statement, <c>{</c> opens a block.</summary>
public sealed record WsMapLiteralSyntax(IReadOnlyList<WsMapEntrySyntax> Entries, WsSourceSpan Span) : WsExpressionSyntax(Span);

/// <summary><c>target[index]</c>: an element of an array, an entry of a map, or a character of a string (section 3.6).</summary>
public sealed record WsIndexExpressionSyntax(WsExpressionSyntax Target, WsExpressionSyntax Index, WsSourceSpan Span)
    : WsExpressionSyntax(Span);

/// <summary><c>target.name</c>: a member of a value (section 3.6); called, <c>target.name(…)</c>, a method.</summary>
public sealed record WsMemberAccessSyntax(WsExpressionSyntax Target, string Name, WsSourceSpan NameSpan, WsSourceSpan Span)
    : WsExpressionSyntax(Span);

/// <summary><c>target[index] = value</c>: an element or an entry assigned (section 3.6).</summary>
public sealed record WsIndexAssignmentSyntax(WsIndexExpressionSyntax Target, WsExpressionSyntax Value, WsSourceSpan Span)
    : WsExpressionSyntax(Span);

public sealed record WsCallExpressionSyntax(
    WsExpressionSyntax Callee, IReadOnlyList<WsExpressionSyntax> Arguments, WsSourceSpan Span)
    : WsExpressionSyntax(Span);

/// <summary>Where an expression should have been and could not be parsed; its diagnostic is already reported.</summary>
public sealed record WsErrorExpressionSyntax(WsSourceSpan Span) : WsExpressionSyntax(Span);
