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
        // A program that imports others: each file's function, whose closure the program registers, before its own
        // instance and its imports (section 3.9).
        var units = new List<WebLingoIrUnit>();
        var prologue = new List<WebLingoIrStatement>();
        for (var i = 0; i < program.Units.Count; i++)
        {
            var unit = program.Units[i];
            var function = unit.Function is null ? -1 : lowerer.LowerUnit(unit, i);
            var exports = new List<string>();
            foreach (var (name, _) in unit.Exports) exports.Add(name);
            foreach (var builtin in unit.Builtins ?? []) exports.Add(builtin.Name);
            units.Add(new WebLingoIrUnit(unit.Name, function, exports) { Parameters = unit.Parameters });
            if (function >= 0) prologue.Add(new WebLingoIrRegisterUnit(i, new WebLingoIrMakeClosure(function, default), default));
        }
        if (program.Units.Count > 0) prologue.Add(MakeModule(program.Units[0], 0));
        for (var i = 1; i < program.Units.Count; i++)
        {
            // A standard module, made with the program's own instance, before any import (section 3.9).
            if (program.Units[i].Builtins is { } builtins) prologue.Add(MakeModule(program.Units[i], i));
        }
        var statements = lowerer.LowerBlock(program.Statements, prologue);
        return new WebLingoIrProgram(statements, Names(program.Locals), program.Builtins)
        {
            Functions = lowerer._functions!,
            Units = units,
            Classes = lowerer._classes,
            ErrorClass = lowerer._errorClass,
            ObjectClass = lowerer._objectClass,
            ValueClasses = lowerer._valueClasses,
            ErrorClasses = lowerer._errorClasses,
            IsAsync = program.IsAsync,
            IsImport = program.IsImport,
            Parameters = program.Parameters
        };
    }

    /// <summary>A file's instance, made where its function begins, its exports its cells (section 3.9).</summary>
    private static WebLingoIrStatement MakeModule(WebLingoBoundUnit unit, int index)
    {
        var values = new List<WebLingoIrExpression>();
        foreach (var (_, local) in unit.Exports) values.Add(new WebLingoIrLoadLocal(local.Index, default)); // Its cell, as it is.
        foreach (var builtin in unit.Builtins ?? []) values.Add(new WebLingoIrLoadBuiltin(builtin.Index, default));
        return Initialize(unit.Instance, new WebLingoIrMakeModule(index, values, default), default);
    }

    /// <summary>
    /// A file's function (section 3.9): its instance, then its imports, then its top level, giving its instance, which
    /// its importers await.
    /// </summary>
    private int LowerUnit(WebLingoBoundUnit unit, int index)
    {
        var function = unit.Function!;
        var functionIndex = _functions.Count;
        _functions.Add(null);
        var body = LowerBlock(function.Body, [MakeModule(unit, index)]);
        body.Add(new WebLingoIrReturn(LowerExpression(new WebLingoBoundLocalReference(unit.Instance, default)), default));
        var captures = new List<WebLingoIrCapture>();
        foreach (var capture in function.Captures) captures.Add(new WebLingoIrCapture(capture.FromLocal, capture.Index));
        _functions[functionIndex] = new WebLingoIrFunction(function.Name, function.ParameterCount, Names(function.Locals), captures, body,
            function.Span)
        {
            IsAsync = true,
            Source = function.Source
        };
        return functionIndex;
    }

    private static string[] Names(IReadOnlyList<WebLingoLocalSymbol> locals)
    {
        var names = new string[locals.Count];
        foreach (var local in locals) names[local.Index] = local.Name;
        return names;
    }

    /// <summary>A block's statements, after the cells of its captured locals and the closures of its functions.</summary>
    private List<WebLingoIrStatement> LowerBlock(IReadOnlyList<WebLingoBoundStatement> block,
        IReadOnlyList<WebLingoIrStatement>? prologue = null)
    {
        var lowered = new List<WebLingoIrStatement>();
        foreach (var statement in block)
        {
            var local = statement switch
            {
                WebLingoBoundVariableDeclaration declaration => declaration.Local,
                WebLingoBoundFunctionDeclaration function => function.Local,
                WebLingoBoundClassDeclaration declaration => declaration.Local,
                WebLingoBoundImport import => import.Local,
                WebLingoBoundModuleParameter parameter => parameter.Local,
                _ => null
            };
            if (local is { IsCaptured: true }) lowered.Add(new WebLingoIrNewCell(local.Index, statement.Span));
        }
        // A file's instance, and its imports, each awaited, before its functions and classes, whose bases they may be;
        // and before those, the built-in classes, which the files it imports use (section 3.9).
        var madeFirst = new HashSet<WebLingoBoundClassDeclaration>();
        if (prologue is not null)
        {
            foreach (var statement in block)
            {
                if (statement is not WebLingoBoundClassDeclaration declaration || !IsBuiltin(declaration.Class)) continue;
                lowered.Add(LowerClassDeclaration(declaration));
                madeFirst.Add(declaration);
            }
            lowered.AddRange(prologue);
        }
        foreach (var statement in block)
        {
            // A module's parameters, before its imports, which may give them on (section 3.9).
            if (statement is WebLingoBoundModuleParameter parameter) lowered.Add(LowerModuleParameter(parameter));
        }
        foreach (var statement in block)
        {
            if (statement is not WebLingoBoundImport import) continue;
            var imported = new WebLingoIrImport(import.Unit, import.Span) { Values = import.Values is { } values ? LowerExpression(values) : null };
            lowered.Add(Initialize(import.Local, new WebLingoIrAwait(imported, import.Span), import.Span));
        }
        foreach (var statement in block)
        {
            if (statement is WebLingoBoundFunctionDeclaration function)
            {
                lowered.Add(Initialize(function.Local, new WebLingoIrMakeClosure(LowerFunction(function.Function), function.Span),
                    function.Span));
            }
            if (statement is WebLingoBoundClassDeclaration declaration && !madeFirst.Contains(declaration))
            {
                lowered.Add(LowerClassDeclaration(declaration));
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
                lowered.Add(SetStatic(target, name, value, span));
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
                lowered.Add(SetStatic(LowerExpression(extension.Target), name, value, span));
            }
        }
        foreach (var statement in block)
        {
            if (statement is not (WebLingoBoundFunctionDeclaration or WebLingoBoundClassDeclaration or WebLingoBoundExtension
                or WebLingoBoundImport or WebLingoBoundModuleParameter))
            {
                lowered.Add(LowerStatement(statement));
            }
        }
        return lowered;
    }

    /// <summary>
    /// A module's parameter: its value in the map the module's function is given, which its import checked, or its default
    /// (section 3.9).
    /// </summary>
    private WebLingoIrStatement LowerModuleParameter(WebLingoBoundModuleParameter parameter)
    {
        var span = parameter.Span;
        var values = LowerExpression(new WebLingoBoundLocalReference(parameter.Values, span));
        var name = new WebLingoIrStringConstant(parameter.Name, span);
        var given = Initialize(parameter.Local, new WebLingoIrIndex(values, name, span), span);
        var otherwise = Initialize(parameter.Local, parameter.Default is { } value ? LowerExpression(value) : new WebLingoIrNullConstant(span), span);
        return new WebLingoIrIf(new WebLingoIrCallMethod(values, "has", [name], span), given, otherwise, span);
    }

    /// <summary>
    /// A class's static field set where its block begins, or a static constant set by the class's built-in
    /// <c>setConstant</c>, under a name no program can declare, so that no static member of a program's hides it.
    /// </summary>
    private WebLingoIrStatement SetStatic(WebLingoIrExpression target, string name, WebLingoBoundExpression value, WebLingoSourceSpan span) =>
        new WebLingoIrEvaluate(value is WebLingoBoundStaticConstant constant
            ? new WebLingoIrCallMethod(target, "static:const", [new WebLingoIrStringConstant(name, span), LowerExpression(constant.Value)], span)
            : new WebLingoIrStoreMember(target, name, LowerExpression(value), span), span);

    /// <summary>A class made where its block begins, a built-in one remembered by its role (section 3.7).</summary>
    private WebLingoIrStatement LowerClassDeclaration(WebLingoBoundClassDeclaration declaration)
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
        return Initialize(declaration.Local, made, declaration.Span);
    }

    private bool IsBuiltin(WebLingoBoundClass bound)
    {
        foreach (var (_, builtin) in _builtinBound)
        {
            if (ReferenceEquals(bound, builtin)) return true;
        }
        return false;
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
            IsAsync = function.IsAsync,
            Source = function.Source
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
            case WebLingoBoundImportDynamic imported:
                return new WebLingoIrImportDynamic(LowerExpression(imported.Path), imported.Values is { } values ? LowerExpression(values) : null,
                    imported.Span);
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
