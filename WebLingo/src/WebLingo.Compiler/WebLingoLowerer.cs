using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Compiler;

/// <summary>
/// Lowers a bound program to the IR (<see cref="WebLingoIrProgram"/>), reducing the bound tree to explicit operations.
/// A local that a closure captures lives in a cell, which every closure of it shares (specification, section 3.5):
/// its block gives it a new cell when it begins (so that a function declared in the block, whose closure is made
/// then too, can capture it), and its declaration initializes the cell. A function declaration's closure is made
/// when its block begins, since the function is in scope throughout. Every function the program declares, at any
/// depth, is lowered into one list.
/// </summary>
public sealed class WebLingoLowerer
{
    private readonly List<WebLingoIrFunction?> _functions = [];
    private readonly List<WebLingoIrClass> _classes = [];
    private WebLingoBoundClass? _errorBound;
    private int _errorClass = -1;
    private WebLingoBoundClass? _objectBound;
    private int _objectClass = -1;
    private IReadOnlyList<(string Name, WebLingoBoundClass Class)> _builtinBound = [];
    private int[] _valueClasses = [];
    private readonly List<(string Code, int Class)> _errorClasses = [];

    private WebLingoLowerer() { }

    public static WebLingoIrProgram Lower(WebLingoBoundProgram program)
    {
        var lowerer = new WebLingoLowerer
        {
            _errorBound = program.ErrorClass,
            _objectBound = program.ObjectClass,
            _builtinBound = program.BuiltinClasses
        };
        var statements = lowerer.LowerBlock(program.Statements);
        return new WebLingoIrProgram(statements, Names(program.Locals), program.Builtins)
        {
            Functions = lowerer._functions!,
            Classes = lowerer._classes,
            ErrorClass = lowerer._errorClass,
            ObjectClass = lowerer._objectClass,
            ValueClasses = lowerer._valueClasses,
            ErrorClasses = lowerer._errorClasses,
            IsAsync = program.IsAsync
        };
    }

    private static string[] Names(IReadOnlyList<WebLingoLocalSymbol> locals)
    {
        var names = new string[locals.Count];
        foreach (var local in locals) names[local.Index] = local.Name;
        return names;
    }

    /// <summary>A block's statements, after the cells of its captured locals and the closures of its functions.</summary>
    private List<WebLingoIrStatement> LowerBlock(IReadOnlyList<WebLingoBoundStatement> block)
    {
        var lowered = new List<WebLingoIrStatement>();
        foreach (var statement in block)
        {
            var local = statement switch
            {
                WebLingoBoundVariableDeclaration declaration => declaration.Local,
                WebLingoBoundFunctionDeclaration function => function.Local,
                WebLingoBoundClassDeclaration declaration => declaration.Local,
                _ => null
            };
            if (local is { IsCaptured: true }) lowered.Add(new WebLingoIrNewCell(local.Index, statement.Span));
        }
        foreach (var statement in block)
        {
            if (statement is WebLingoBoundFunctionDeclaration function)
            {
                lowered.Add(Initialize(function.Local, new WebLingoIrMakeClosure(LowerFunction(function.Function), function.Span),
                    function.Span));
            }
            if (statement is WebLingoBoundClassDeclaration declaration)
            {
                var index = LowerClass(declaration.Class);
                if (ReferenceEquals(declaration.Class, _errorBound)) _errorClass = index;
                if (ReferenceEquals(declaration.Class, _objectBound)) _objectClass = index;
                foreach (var (name, builtin) in _builtinBound)
                {
                    if (ReferenceEquals(declaration.Class, builtin)) Remember(name, index);
                }
                var made = new WebLingoIrMakeClass(index, declaration.Span)
                {
                    Base = declaration.Class.Base is null ? null : LowerExpression(declaration.Class.Base)
                };
                lowered.Add(Initialize(declaration.Local, made, declaration.Span));
            }
        }
        foreach (var statement in block)
        {
            // A class's static fields, set in their order once the block's classes are made (section 3.7).
            if (statement is not WebLingoBoundClassDeclaration { Class.StaticFields.Count: > 0 } declaration) continue;
            var local = declaration.Local;
            foreach (var (name, value, span) in declaration.Class.StaticFields)
            {
                WebLingoIrExpression target = local.IsCaptured ? new WebLingoIrLoadCell(local.Index, span) : new WebLingoIrLoadLocal(local.Index, span);
                lowered.Add(new WebLingoIrEvaluate(new WebLingoIrStoreMember(target, name, LowerExpression(value), span), span));
            }
        }
        foreach (var statement in block)
        {
            // An extension, in its order once the block's classes are made and their static fields set, then its own
            // static fields, set in their order (section 3.7).
            if (statement is not WebLingoBoundExtension extension) continue;
            lowered.Add(new WebLingoIrExtend(LowerExpression(extension.Target), LowerClass(extension.Class), extension.Span));
            foreach (var (name, value, span) in extension.Class.StaticFields)
            {
                lowered.Add(new WebLingoIrEvaluate(new WebLingoIrStoreMember(LowerExpression(extension.Target), name, LowerExpression(value), span),
                    span));
            }
        }
        foreach (var statement in block)
        {
            if (statement is not (WebLingoBoundFunctionDeclaration or WebLingoBoundClassDeclaration or WebLingoBoundExtension))
            {
                lowered.Add(LowerStatement(statement));
            }
        }
        return lowered;
    }

    private static WebLingoIrStatement Initialize(WebLingoLocalSymbol local, WebLingoIrExpression value, WebLingoSourceSpan span) =>
        local.IsCaptured
            ? new WebLingoIrEvaluate(new WebLingoIrStoreCell(local.Index, value, span), span)
            : new WebLingoIrInitializeLocal(local.Index, value, span);

    /// <summary>A built-in class's index, if it is a kind of value's, or of runtime error's (section 3.7).</summary>
    private void Remember(string name, int index)
    {
        foreach (var (valueClass, kind) in WebLingoPrelude.ValueClasses)
        {
            if (valueClass != name) continue;
            if (_valueClasses.Length == 0)
            {
                _valueClasses = new int[WebLingoPrelude.ValueClasses.Length];
                for (var i = 0; i < _valueClasses.Length; i++) _valueClasses[i] = -1;
            }
            _valueClasses[(int)kind] = index;
        }
        foreach (var (errorClass, code) in WebLingoPrelude.ErrorClasses)
        {
            if (errorClass == name) _errorClasses.Add((code, index));
        }
    }

    private int LowerClass(WebLingoBoundClass declaration)
    {
        var constructor = declaration.Constructor is null ? -1 : LowerFunction(declaration.Constructor);
        var methods = new List<(string, int)>();
        foreach (var (name, function) in declaration.Methods) methods.Add((name, LowerFunction(function)));
        var staticMethods = new List<(string, int)>();
        foreach (var (name, function) in declaration.StaticMethods) staticMethods.Add((name, LowerFunction(function)));
        var staticFields = new List<string>();
        foreach (var (name, _, _) in declaration.StaticFields) staticFields.Add(name);
        _classes.Add(new WebLingoIrClass(declaration.Name, constructor, methods)
        {
            StaticMethods = staticMethods,
            StaticFields = staticFields,
            DefaultConstructor = declaration.DefaultConstructor
        });
        return _classes.Count - 1;
    }

    private int LowerFunction(WebLingoBoundFunction function)
    {
        var index = _functions.Count;
        _functions.Add(null); // Its place, so that the functions it declares come after it.
        var body = new List<WebLingoIrStatement>();
        // A captured parameter moves into a cell of its own when the function begins.
        for (var i = 0; i < function.ParameterCount; i++)
        {
            var parameter = function.Locals[i];
            if (!parameter.IsCaptured) continue;
            body.Add(new WebLingoIrBoxLocal(parameter.Index, parameter.DeclarationSpan));
        }
        // A parameter its caller left out gets its default value, after a captured one has moved into its cell.
        var offset = function.HasThis ? 1 : 0;
        var optional = 0;
        for (var i = 0; i < function.Defaults.Count; i++)
        {
            if (function.Defaults[i] is not { } value) continue;
            optional++;
            var parameter = function.Locals[offset + i];
            body.Add(new WebLingoIrDefaultParameter(parameter.Index, parameter.IsCaptured, LowerExpression(value), value.Span));
        }
        body.AddRange(LowerBlock(function.Body));
        var captures = new List<WebLingoIrCapture>();
        foreach (var capture in function.Captures) captures.Add(new WebLingoIrCapture(capture.FromLocal, capture.Index));
        _functions[index] = new WebLingoIrFunction(function.Name, function.ParameterCount, Names(function.Locals), captures, body,
            function.Span)
        {
            HasThis = function.HasThis,
            OptionalCount = optional,
            IsAsync = function.IsAsync
        };
        return index;
    }

    private WebLingoIrStatement LowerStatement(WebLingoBoundStatement statement)
    {
        switch (statement)
        {
            case WebLingoBoundVariableDeclaration declaration:
                return Initialize(declaration.Local, LowerExpression(declaration.Initializer), declaration.Span);
            case WebLingoBoundExpressionStatement expressionStatement:
                return new WebLingoIrEvaluate(LowerExpression(expressionStatement.Expression), statement.Span);
            case WebLingoBoundIf conditional:
                return new WebLingoIrIf(LowerExpression(conditional.Condition), LowerStatement(conditional.Then),
                    conditional.Else is null ? null : LowerStatement(conditional.Else), conditional.Span);
            case WebLingoBoundLoop loop:
            {
                var copies = new List<int>();
                foreach (var local in loop.PerIterationLocals)
                {
                    if (local.IsCaptured) copies.Add(local.Index);
                }
                return new WebLingoIrLoop(loop.Condition is null ? null : LowerExpression(loop.Condition), LowerStatement(loop.Body),
                    loop.Step is null ? null : LowerExpression(loop.Step), loop.TestFirst, loop.Span)
                {
                    PerIteration = copies.Count == 0 ? null : new WebLingoIrCopyCells(copies, loop.Span)
                };
            }
            case WebLingoBoundForEach forEach:
            {
                var captured = new List<int>();
                if (forEach.Item.IsCaptured) captured.Add(forEach.Item.Index);
                if (forEach.Value is { IsCaptured: true } value) captured.Add(value.Index);
                return new WebLingoIrForEach(forEach.Collection.Index, LowerExpression(forEach.CollectionValue),
                    forEach.Value is null ? 1 : 2, captured, LowerStatement(forEach.Body), forEach.Span);
            }
            case WebLingoBoundUsing usingStatement:
            {
                var local = usingStatement.Local;
                var initialize = new List<WebLingoIrStatement>();
                if (local.IsCaptured) initialize.Add(new WebLingoIrNewCell(local.Index, usingStatement.Span));
                initialize.Add(Initialize(local, LowerExpression(usingStatement.Value), usingStatement.Span));
                WebLingoIrExpression resource = local.IsCaptured
                    ? new WebLingoIrLoadCell(local.Index, usingStatement.Span)
                    : new WebLingoIrLoadLocal(local.Index, usingStatement.Span);
                return new WebLingoIrUsing(initialize, resource, LowerStatement(usingStatement.Body), usingStatement.ExceptionValue.Index,
                    usingStatement.ExceptionOrigin.Index, usingStatement.Span);
            }
            case WebLingoBoundTry tryStatement:
            {
                WebLingoIrCatch? handler = null;
                if (tryStatement.Catches.Count > 0)
                {
                    // The catches, tried in turn: each a test of its class, but one without a class, which catches
                    // anything, and a last one throwing again what none of them catches, after the finally (section 3.7).
                    var value = tryStatement.CatchValue!.Index;
                    var origin = tryStatement.CatchOrigin!.Index;
                    WebLingoIrStatement chain = new WebLingoIrRethrow(value, origin, tryStatement.Span);
                    for (var i = tryStatement.Catches.Count - 1; i >= 0; i--)
                    {
                        var catchPart = tryStatement.Catches[i];
                        var variable = catchPart.Variable;
                        var span = variable.DeclarationSpan;
                        var body = new List<WebLingoIrStatement>();
                        if (variable.IsCaptured) body.Add(new WebLingoIrNewCell(variable.Index, span));
                        body.Add(Initialize(variable, new WebLingoIrLoadLocal(value, span), span));
                        body.Add(LowerStatement(catchPart.Body));
                        var block = new WebLingoIrBlock(body, span);
                        chain = catchPart.Class is null
                            ? block
                            : new WebLingoIrIf(new WebLingoIrIs(new WebLingoIrLoadLocal(value, span), LowerExpression(catchPart.Class), span), block,
                                chain, span);
                    }
                    handler = new WebLingoIrCatch(value, origin, [chain]);
                }
                var cleanup = tryStatement.Finally is { } finallyPart
                    ? new WebLingoIrFinally(finallyPart.Value.Index, finallyPart.Origin.Index, LowerStatement(finallyPart.Body))
                    : null;
                return new WebLingoIrTry(LowerStatement(tryStatement.Body), handler, cleanup, tryStatement.Span);
            }
            case WebLingoBoundThrow throwStatement:
                return new WebLingoIrThrow(LowerExpression(throwStatement.Value), throwStatement.Span);
            case WebLingoBoundRethrow rethrow:
                return new WebLingoIrRethrow(rethrow.Value.Index, rethrow.Origin.Index, rethrow.Span);
            case WebLingoBoundBreak breakStatement:
                return new WebLingoIrBreak(breakStatement.Span);
            case WebLingoBoundContinue continueStatement:
                return new WebLingoIrContinue(continueStatement.Span);
            case WebLingoBoundReturn returnStatement:
                return new WebLingoIrReturn(returnStatement.Value is null ? null : LowerExpression(returnStatement.Value),
                    returnStatement.Span);
            case WebLingoBoundBlock block:
                return new WebLingoIrBlock(LowerBlock(block.Statements), block.Span);
            default:
                throw new InvalidOperationException($"Unknown statement {statement.GetType().Name}.");
        }
    }

    private WebLingoIrExpression LowerExpression(WebLingoBoundExpression expression)
    {
        switch (expression)
        {
            case WebLingoBoundIntegerLiteral literal:
                return new WebLingoIrInt64Constant(literal.Value, literal.Span);
            case WebLingoBoundBinary64Literal literal:
                return new WebLingoIrBinary64Constant(literal.Value, literal.Span);
            case WebLingoBoundBooleanLiteral literal:
                return new WebLingoIrBooleanConstant(literal.Value, literal.Span);
            case WebLingoBoundNullLiteral literal:
                return new WebLingoIrNullConstant(literal.Span);
            case WebLingoBoundStringLiteral literal:
                return new WebLingoIrStringConstant(literal.Value, literal.Span);
            case WebLingoBoundBinary { Operator: WebLingoBinaryOperator.And or WebLingoBinaryOperator.Or } logical:
                return new WebLingoIrLogical(logical.Operator == WebLingoBinaryOperator.And, LowerExpression(logical.Left),
                    LowerExpression(logical.Right), logical.Span);
            case WebLingoBoundLocalReference reference:
                return reference.Local.IsCaptured
                    ? new WebLingoIrLoadCell(reference.Local.Index, reference.Span)
                    : new WebLingoIrLoadLocal(reference.Local.Index, reference.Span);
            case WebLingoBoundCaptureReference capture:
                return new WebLingoIrLoadCapture(capture.Index, capture.Span);
            case WebLingoBoundUnary unary:
                return new WebLingoIrUnary(unary.Operator, LowerExpression(unary.Operand), unary.Span);
            case WebLingoBoundAwait awaited:
                return new WebLingoIrAwait(LowerExpression(awaited.Value), awaited.Span);
            case WebLingoBoundIs test:
                return new WebLingoIrIs(LowerExpression(test.Value), LowerExpression(test.Class), test.Span);
            case WebLingoBoundToText text:
                return new WebLingoIrToText(LowerExpression(text.Value), text.Span);
            case WebLingoBoundHold hold:
                return new WebLingoIrHold(LowerExpression(hold.Instance), LowerExpression(hold.Value), hold.Span);
            case WebLingoBoundBinary binary:
                var left = LowerExpression(binary.Left);
                var right = LowerExpression(binary.Right);
                return new WebLingoIrBinary(binary.Operator, left, right, binary.Span);
            case WebLingoBoundAssignment assignment:
                return assignment.Local.IsCaptured
                    ? new WebLingoIrStoreCell(assignment.Local.Index, LowerExpression(assignment.Value), assignment.Span)
                    : new WebLingoIrStoreLocal(assignment.Local.Index, LowerExpression(assignment.Value), assignment.Span);
            case WebLingoBoundCaptureAssignment assignment:
                return new WebLingoIrStoreCapture(assignment.Index, LowerExpression(assignment.Value), assignment.Span);
            case WebLingoBoundBuiltinReference builtin:
                return new WebLingoIrLoadBuiltin(builtin.Builtin.Index, builtin.Span);
            case WebLingoBoundFunctionExpression function:
                return new WebLingoIrMakeClosure(LowerFunction(function.Function), function.Span);
            case WebLingoBoundBuiltinCall call:
                return new WebLingoIrCallBuiltin(call.Builtin.Index, LowerArguments(call.Arguments), call.Span);
            case WebLingoBoundCall call:
                return new WebLingoIrCall(LowerExpression(call.Callee), LowerArguments(call.Arguments), call.Span);
            case WebLingoBoundArrayLiteral array:
                return new WebLingoIrNewArray(LowerArguments(array.Elements), array.Span);
            case WebLingoBoundSetLiteral set:
                return new WebLingoIrNewSet(LowerArguments(set.Elements), set.Span);
            case WebLingoBoundMapLiteral map:
            {
                var entries = new List<WebLingoIrMapEntry>();
                foreach (var (key, value) in map.Entries) entries.Add(new WebLingoIrMapEntry(key, LowerExpression(value)));
                return new WebLingoIrNewMap(entries, map.Span);
            }
            case WebLingoBoundIndex index:
                return new WebLingoIrIndex(LowerExpression(index.Target), LowerExpression(index.Index), index.Span)
                {
                    IsOptional = index.IsOptional
                };
            case WebLingoBoundIndexAssignment element:
                return new WebLingoIrStoreIndex(LowerExpression(element.Target), LowerExpression(element.Index),
                    LowerExpression(element.Value), element.Span);
            case WebLingoBoundMember member:
                return new WebLingoIrMember(LowerExpression(member.Target), member.Name, member.Span) { IsOptional = member.IsOptional };
            case WebLingoBoundMemberAssignment field:
                return new WebLingoIrStoreMember(LowerExpression(field.Target), field.Name, LowerExpression(field.Value), field.Span);
            case WebLingoBoundSuper super:
                return new WebLingoIrGetSuper(LowerExpression(super.Class), LowerExpression(super.This), super.Name, super.Span);
            case WebLingoBoundNew created:
                return new WebLingoIrNew(LowerExpression(created.Class), LowerArguments(created.Arguments), created.Span);
            case WebLingoBoundMethodCall call:
                return new WebLingoIrCallMethod(LowerExpression(call.Target), call.Name, LowerArguments(call.Arguments), call.Span);
            default:
                throw new InvalidOperationException($"Cannot lower {expression.GetType().Name}: binding had errors.");
        }
    }

    private List<WebLingoIrExpression> LowerArguments(IReadOnlyList<WebLingoBoundExpression> arguments)
    {
        var lowered = new List<WebLingoIrExpression>();
        foreach (var argument in arguments) lowered.Add(LowerExpression(argument));
        return lowered;
    }
}
