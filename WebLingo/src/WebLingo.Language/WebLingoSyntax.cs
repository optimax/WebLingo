using WebLingo.Core;
namespace WebLingo.Language;

// The syntax tree of M0.6's language subset (specification, section 8.4, "Parser"). Every node carries the span of
// source it was parsed from.

public abstract record WebLingoSyntaxNode(WebLingoSourceSpan Span);

public sealed record WebLingoCompilationUnitSyntax(IReadOnlyList<WebLingoStatementSyntax> Statements, WebLingoSourceSpan Span)
    : WebLingoSyntaxNode(Span);

public abstract record WebLingoStatementSyntax(WebLingoSourceSpan Span) : WebLingoSyntaxNode(Span);

/// <summary>What declares a name: <c>var</c>, <c>const</c>, a function declaration, or a function's parameter.</summary>
public enum WebLingoDeclarationKind { Var, Const, Function, Parameter, Class }

/// <summary>
/// <c>var x = 1;</c> or <c>const x = 1;</c>; the span includes the semicolon. <paramref name="Initializer"/> is null
/// only in a tree with errors (a declaration without a value is WL1006).
/// </summary>
public sealed record WebLingoVariableDeclarationSyntax(
    WebLingoDeclarationKind Kind, string Name, WebLingoSourceSpan NameSpan, WebLingoExpressionSyntax? Initializer, WebLingoSourceSpan Span)
    : WebLingoStatementSyntax(Span);

/// <summary>An expression followed by a semicolon, which the span includes.</summary>
public sealed record WebLingoExpressionStatementSyntax(WebLingoExpressionSyntax Expression, WebLingoSourceSpan Span)
    : WebLingoStatementSyntax(Span);

/// <summary>A block, <c>{ statements }</c>, which opens a scope (specification, section 3.5).</summary>
public sealed record WebLingoBlockSyntax(IReadOnlyList<WebLingoStatementSyntax> Statements, WebLingoSourceSpan Span) : WebLingoStatementSyntax(Span);

/// <summary><c>if (condition) then</c>, with an optional <c>else</c> (specification, section 3.5).</summary>
public sealed record WebLingoIfSyntax(WebLingoExpressionSyntax Condition, WebLingoStatementSyntax Then, WebLingoStatementSyntax? Else, WebLingoSourceSpan Span)
    : WebLingoStatementSyntax(Span);

public sealed record WebLingoWhileSyntax(WebLingoExpressionSyntax Condition, WebLingoStatementSyntax Body, WebLingoSourceSpan Span)
    : WebLingoStatementSyntax(Span);

public sealed record WebLingoDoWhileSyntax(WebLingoStatementSyntax Body, WebLingoExpressionSyntax Condition, WebLingoSourceSpan Span)
    : WebLingoStatementSyntax(Span);

/// <summary>
/// <c>for (init; condition; step) body</c>; each part may be missing. <paramref name="Init"/> is a declaration or an
/// expression statement.
/// </summary>
public sealed record WebLingoForSyntax(
    WebLingoStatementSyntax? Init, WebLingoExpressionSyntax? Condition, WebLingoExpressionSyntax? Step, WebLingoStatementSyntax Body, WebLingoSourceSpan Span)
    : WebLingoStatementSyntax(Span);

/// <summary>
/// <c>foreach (var item in collection) body</c>, or <c>foreach (var (key, value) in map) body</c>, whose
/// <paramref name="ValueName"/> is then the value's (specification, sections 3.5 and 3.6).
/// </summary>
public sealed record WebLingoForEachSyntax(
    string ItemName, WebLingoSourceSpan ItemSpan, string? ValueName, WebLingoSourceSpan ValueSpan, WebLingoExpressionSyntax Collection,
    WebLingoStatementSyntax Body, WebLingoSourceSpan Span)
    : WebLingoStatementSyntax(Span);

/// <summary>
/// <c>using (var name = value) body</c>: <paramref name="Name"/>, in scope in the body only, is disposed when the body
/// ends, by any way out (section 3.7).
/// </summary>
public sealed record WebLingoUsingSyntax(string Name, WebLingoSourceSpan NameSpan, WebLingoExpressionSyntax Value, WebLingoStatementSyntax Body, WebLingoSourceSpan Span)
    : WebLingoStatementSyntax(Span);

/// <summary>
/// <c>try { body } catch (name) { … } finally { … }</c>, with a <c>catch</c>, a <c>finally</c>, or both (section 3.7).
/// </summary>
public sealed record WebLingoTrySyntax(
    WebLingoBlockSyntax Body, IReadOnlyList<WebLingoCatchSyntax> Catches, WebLingoBlockSyntax? Finally, WebLingoSourceSpan Span) : WebLingoStatementSyntax(Span);

/// <summary>
/// A <c>catch</c>: the class it catches, a name or a dotted name, or null for any exception; the variable it declares;
/// and its body (section 3.7, "Typed catches").
/// </summary>
public sealed record WebLingoCatchSyntax(WebLingoExpressionSyntax? Class, string Name, WebLingoSourceSpan NameSpan, WebLingoBlockSyntax Body,
    WebLingoSourceSpan Span) : WebLingoSyntaxNode(Span);

/// <summary><c>value is Class</c>, the class a name or a dotted name (section 3.7).</summary>
public sealed record WebLingoIsExpressionSyntax(WebLingoExpressionSyntax Value, WebLingoExpressionSyntax Class, WebLingoSourceSpan Span)
    : WebLingoExpressionSyntax(Span);

/// <summary><c>throw value;</c>, or <c>throw;</c>, which throws the exception its <c>catch</c> caught again (section 3.7).</summary>
public sealed record WebLingoThrowSyntax(WebLingoExpressionSyntax? Value, WebLingoSourceSpan Span) : WebLingoStatementSyntax(Span);

public sealed record WebLingoBreakSyntax(WebLingoSourceSpan Span) : WebLingoStatementSyntax(Span);

public sealed record WebLingoContinueSyntax(WebLingoSourceSpan Span) : WebLingoStatementSyntax(Span);

/// <summary>A function's parameter, by name.</summary>
public sealed record WebLingoParameterSyntax(string Name, WebLingoSourceSpan Span) : WebLingoSyntaxNode(Span)
{
    /// <summary>Its default value, <c>b = 1</c>, which makes it optional, or null (section 3.5).</summary>
    public WebLingoExpressionSyntax? Default { get; init; }
}

/// <summary><c>function name(parameters) { body }</c>, in scope throughout its block (specification, section 3.5).</summary>
public sealed record WebLingoFunctionDeclarationSyntax(
    string Name, WebLingoSourceSpan NameSpan, IReadOnlyList<WebLingoParameterSyntax> Parameters, WebLingoBlockSyntax Body, WebLingoSourceSpan Span)
    : WebLingoStatementSyntax(Span)
{
    /// <summary>Whether it is <c>async</c>: its call gives a task, and it may <c>await</c> (section 3.8).</summary>
    public bool IsAsync { get; init; }
}

/// <summary>A method of a class, or its <c>constructor</c>: a name, parameters and a body (section 3.7).</summary>
public sealed record WebLingoMethodSyntax(
    string Name, WebLingoSourceSpan NameSpan, IReadOnlyList<WebLingoParameterSyntax> Parameters, WebLingoBlockSyntax Body, WebLingoSourceSpan Span)
    : WebLingoSyntaxNode(Span)
{
    /// <summary>Whether its body calls <c>super(…)</c> anywhere, a function inside it included.</summary>
    public bool CallsSuper { get; init; }

    /// <summary>Whether it is <c>static</c>: a method of the class itself, without <c>this</c> (section 3.7).</summary>
    public bool IsStatic { get; init; }

    /// <summary>Whether it is <c>async</c> (section 3.8).</summary>
    public bool IsAsync { get; init; }
}

/// <summary><c>static var name = value;</c>, a field of a class itself, set when the class is made (section 3.7).</summary>
public sealed record WebLingoStaticFieldSyntax(string Name, WebLingoSourceSpan NameSpan, WebLingoExpressionSyntax Value, WebLingoSourceSpan Span)
    : WebLingoSyntaxNode(Span)
{
    /// <summary>Whether it is <c>static const</c>, a constant, which cannot be assigned (section 3.7).</summary>
    public bool IsConstant { get; init; }
}

/// <summary><c>class Name { members }</c>, in scope throughout its block, as a function is (section 3.7).</summary>
public sealed record WebLingoClassDeclarationSyntax(
    string Name, WebLingoSourceSpan NameSpan, IReadOnlyList<WebLingoMethodSyntax> Methods, WebLingoSourceSpan Span)
    : WebLingoStatementSyntax(Span)
{
    /// <summary>The class it extends, <c>extends Base</c>, or null (section 3.7).</summary>
    public WebLingoExpressionSyntax? Base { get; init; }

    /// <summary>Its static fields, in their order; its static methods are among <see cref="Methods"/>.</summary>
    public IReadOnlyList<WebLingoStaticFieldSyntax> StaticFields { get; init; } = [];
}

/// <summary>
/// <c>extend Name { members }</c>: methods and static members given to a class that exists, a program's or a kind of
/// value's, hoisted with its block's classes (section 3.7).
/// </summary>
public sealed record WebLingoExtendSyntax(WebLingoExpressionSyntax Target, IReadOnlyList<WebLingoMethodSyntax> Methods, WebLingoSourceSpan Span)
    : WebLingoStatementSyntax(Span)
{
    /// <summary>Its static fields, in their order; its static methods are among <see cref="Methods"/>.</summary>
    public IReadOnlyList<WebLingoStaticFieldSyntax> StaticFields { get; init; } = [];
}

/// <summary>
/// <c>import db from "lib/db.wl";</c>, <c>import { f, g } from "lib/util.wl";</c>, or both, <c>import db, { f } from …;</c>:
/// a module's instance, or names it exports, at a module's top level (section 3.9).
/// </summary>
public sealed record WebLingoImportSyntax(string? Name, WebLingoSourceSpan NameSpan, IReadOnlyList<WebLingoImportedNameSyntax> Names,
    string Path, WebLingoSourceSpan PathSpan, WebLingoSourceSpan Span) : WebLingoStatementSyntax(Span)
{
    /// <summary>The values it gives the module's parameters, <c>{ size: 10 }</c> after its path; null for none (section 3.9).</summary>
    public IReadOnlyList<WebLingoImportValueSyntax>? Values { get; init; }
}

/// <summary>A value an import gives a module's parameter, <c>size: 10</c>.</summary>
public sealed record WebLingoImportValueSyntax(string Name, WebLingoSourceSpan NameSpan, WebLingoExpressionSyntax Value);

/// <summary>
/// <c>param name;</c> or <c>param size = 10;</c>: a module's parameter, a constant of it, which its import gives, at its
/// top level, before its other statements (section 3.9).
/// </summary>
public sealed record WebLingoParamSyntax(string Name, WebLingoSourceSpan NameSpan, WebLingoExpressionSyntax? Default, WebLingoSourceSpan Span)
    : WebLingoStatementSyntax(Span);

/// <summary>A name an import takes from a module's exports.</summary>
public sealed record WebLingoImportedNameSyntax(string Name, WebLingoSourceSpan Span);

/// <summary><c>export</c> before a <c>var</c>, <c>const</c>, <c>function</c> or <c>class</c> at a module's top level (section 3.9).</summary>
public sealed record WebLingoExportSyntax(WebLingoStatementSyntax Declaration, WebLingoSourceSpan Span) : WebLingoStatementSyntax(Span);

/// <summary><c>return value;</c>, or <c>return;</c>, which gives <c>null</c>.</summary>
public sealed record WebLingoReturnSyntax(WebLingoExpressionSyntax? Value, WebLingoSourceSpan Span) : WebLingoStatementSyntax(Span);

public abstract record WebLingoExpressionSyntax(WebLingoSourceSpan Span) : WebLingoSyntaxNode(Span);

/// <summary><c>function (parameters) { body }</c>, a function value.</summary>
public sealed record WebLingoFunctionExpressionSyntax(IReadOnlyList<WebLingoParameterSyntax> Parameters, WebLingoBlockSyntax Body, WebLingoSourceSpan Span)
    : WebLingoExpressionSyntax(Span)
{
    /// <summary>Whether it is <c>async</c> (section 3.8).</summary>
    public bool IsAsync { get; init; }
}

/// <summary><c>await value</c>: the value, or a task's result once it completes (section 3.8).</summary>
public sealed record WebLingoAwaitExpressionSyntax(WebLingoExpressionSyntax Operand, WebLingoSourceSpan Span) : WebLingoExpressionSyntax(Span);

public sealed record WebLingoIntegerLiteralSyntax(long Value, WebLingoSourceSpan Span) : WebLingoExpressionSyntax(Span);

/// <summary>A binary64 literal: <c>1.5</c>, <c>1e21</c>, or the keyword <c>Infinity</c> or <c>NaN</c>.</summary>
public sealed record WebLingoBinary64LiteralSyntax(double Value, WebLingoSourceSpan Span) : WebLingoExpressionSyntax(Span);

/// <summary><c>true</c> or <c>false</c>.</summary>
public sealed record WebLingoBooleanLiteralSyntax(bool Value, WebLingoSourceSpan Span) : WebLingoExpressionSyntax(Span);

public sealed record WebLingoNullLiteralSyntax(WebLingoSourceSpan Span) : WebLingoExpressionSyntax(Span);

/// <summary>A string literal; <paramref name="Value"/> is its text, its escapes decoded.</summary>
public sealed record WebLingoStringLiteralSyntax(string Value, WebLingoSourceSpan Span) : WebLingoExpressionSyntax(Span);

public sealed record WebLingoNameSyntax(string Name, WebLingoSourceSpan Span) : WebLingoExpressionSyntax(Span);

public enum WebLingoUnaryOperator { Negate, Plus, Not }

public sealed record WebLingoUnaryExpressionSyntax(WebLingoUnaryOperator Operator, WebLingoExpressionSyntax Operand, WebLingoSourceSpan Span)
    : WebLingoExpressionSyntax(Span);

public enum WebLingoBinaryOperator
{
    Add, Subtract, Multiply, Divide, Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual,
    /// <summary><c>&amp;&amp;</c>, which evaluates its right operand only when the left one is true.</summary>
    And,
    /// <summary><c>||</c>, which evaluates its right operand only when the left one is false.</summary>
    Or
}

public sealed record WebLingoBinaryExpressionSyntax(
    WebLingoBinaryOperator Operator, WebLingoExpressionSyntax Left, WebLingoExpressionSyntax Right, WebLingoSourceSpan OperatorSpan,
    WebLingoSourceSpan Span)
    : WebLingoExpressionSyntax(Span);

/// <summary>An expression in parentheses; the span includes them.</summary>
public sealed record WebLingoParenthesizedExpressionSyntax(WebLingoExpressionSyntax Expression, WebLingoSourceSpan Span)
    : WebLingoExpressionSyntax(Span);

public sealed record WebLingoAssignmentExpressionSyntax(WebLingoNameSyntax Target, WebLingoExpressionSyntax Value, WebLingoSourceSpan Span)
    : WebLingoExpressionSyntax(Span);

/// <summary><c>[a, b, c]</c>, a new array of those values (section 3.6).</summary>
public sealed record WebLingoArrayLiteralSyntax(IReadOnlyList<WebLingoExpressionSyntax> Elements, WebLingoSourceSpan Span) : WebLingoExpressionSyntax(Span);

/// <summary><c>#{a, b, c}</c>, a new set of those values (section 3.6).</summary>
public sealed record WebLingoSetLiteralSyntax(IReadOnlyList<WebLingoExpressionSyntax> Elements, WebLingoSourceSpan Span) : WebLingoExpressionSyntax(Span);

/// <summary>One entry of a map literal: its key, a string, written as a name or as a string literal, and its value.</summary>
public sealed record WebLingoMapEntrySyntax(string Key, WebLingoSourceSpan KeySpan, WebLingoExpressionSyntax Value);

/// <summary><c>{ key: value, … }</c>, a new map (section 3.6); at the start of a statement, <c>{</c> opens a block.</summary>
public sealed record WebLingoMapLiteralSyntax(IReadOnlyList<WebLingoMapEntrySyntax> Entries, WebLingoSourceSpan Span) : WebLingoExpressionSyntax(Span);

/// <summary><c>target[index]</c>: an element of an array, an entry of a map, or a character of a string (section 3.6).</summary>
public sealed record WebLingoIndexExpressionSyntax(WebLingoExpressionSyntax Target, WebLingoExpressionSyntax Index, WebLingoSourceSpan Span)
    : WebLingoExpressionSyntax(Span)
{
    /// <summary><c>target?[index]</c>, which gives <c>null</c> for an absent entry, or a <c>null</c> target (section 3.6).</summary>
    public bool IsOptional { get; init; }
}

/// <summary><c>target.name</c>: a member of a value (section 3.6); called, <c>target.name(…)</c>, a method.</summary>
public sealed record WebLingoMemberAccessSyntax(WebLingoExpressionSyntax Target, string Name, WebLingoSourceSpan NameSpan, WebLingoSourceSpan Span)
    : WebLingoExpressionSyntax(Span)
{
    /// <summary><c>target?.name</c>, which gives <c>null</c> for a <c>null</c> target or a missing member (section 3.7).</summary>
    public bool IsOptional { get; init; }
}

/// <summary><c>target.name = value</c>: a field assigned (section 3.7).</summary>
public sealed record WebLingoMemberAssignmentSyntax(WebLingoMemberAccessSyntax Target, WebLingoExpressionSyntax Value, WebLingoSourceSpan Span)
    : WebLingoExpressionSyntax(Span);

/// <summary><c>new Class(arguments)</c> (section 3.7).</summary>
public sealed record WebLingoNewExpressionSyntax(WebLingoExpressionSyntax Class, IReadOnlyList<WebLingoExpressionSyntax> Arguments, WebLingoSourceSpan Span)
    : WebLingoExpressionSyntax(Span);

/// <summary><c>super</c>, called, <c>super(…)</c>, or naming a member, <c>super.name</c> (section 3.7).</summary>
public sealed record WebLingoSuperSyntax(WebLingoSourceSpan Span) : WebLingoExpressionSyntax(Span);

/// <summary><c>this</c>, the instance a method runs on (section 3.7).</summary>
public sealed record WebLingoThisSyntax(WebLingoSourceSpan Span) : WebLingoExpressionSyntax(Span);

/// <summary><c>target[index] = value</c>: an element or an entry assigned (section 3.6).</summary>
public sealed record WebLingoIndexAssignmentSyntax(WebLingoIndexExpressionSyntax Target, WebLingoExpressionSyntax Value, WebLingoSourceSpan Span)
    : WebLingoExpressionSyntax(Span);

public sealed record WebLingoCallExpressionSyntax(
    WebLingoExpressionSyntax Callee, IReadOnlyList<WebLingoExpressionSyntax> Arguments, WebLingoSourceSpan Span)
    : WebLingoExpressionSyntax(Span);

/// <summary>Where an expression should have been and could not be parsed; its diagnostic is already reported.</summary>
public sealed record WebLingoErrorExpressionSyntax(WebLingoSourceSpan Span) : WebLingoExpressionSyntax(Span);
