using System.Text;
namespace WebLingo.Bytecode;

/// <summary>
/// Shows a module as text, one instruction per line, for tests and for looking at what the compiler made. Registers
/// are <c>r0</c>, <c>r1</c>, …; locals are named where the module knows their names, and the functions, classes and files
/// instructions refer to by their tables' names. A function with the debugger's metadata (<c>weblingo compile --debug</c>)
/// has its captures named too, a line <c>; 6:21</c> before each statement's first instruction, and its variables after its
/// code, their lines and columns from the file's tables (<see cref="WebLingoDisassemblyNames"/>). The format is for
/// people, not a file format.
/// </summary>
public static class WebLingoDisassembler
{
    /// <summary>The module's entry point, then each of its other functions, with its index.</summary>
    public static string Format(WebLingoCompiledModule module) => Format(module, []);

    /// <summary>
    /// The module's entry point, then each of its other functions, with its index; <paramref name="positions"/> the file's
    /// line and column tables, which a function's statements and variables are shown at.
    /// </summary>
    public static string Format(WebLingoCompiledModule module, IReadOnlyList<WebLingoSourcePositions> positions)
    {
        var text = new StringBuilder();
        Append(text, module, module.EntryPoint,
            WebLingoDisassemblyNames.EntryHeading(module.EntryPoint.Name, module.Name, module.Units), positions);
        for (var i = 0; i < module.Functions.Count; i++)
        {
            Append(text.Append('\n'), module, module.Functions[i],
                WebLingoDisassemblyNames.Heading(i, module.Functions[i].Name, module.Units), positions);
        }
        return text.ToString();
    }

    private static void Append(StringBuilder text, WebLingoCompiledModule module, WebLingoFunction function, string heading,
        IReadOnlyList<WebLingoSourcePositions> positions)
    {
        text.Append("function ").Append(heading).Append(" (");
        if (function.ParameterCount > 0) text.Append(function.ParameterCount).Append(" parameters, ");
        if (function.HasRest) text.Append("a rest parameter, ");
        if (function.Captures.Count > 0) text.Append(function.Captures.Count).Append(" captures, ");
        text.Append(function.LocalCount).Append(" locals, ").Append(function.RegisterCount).Append(" registers)\n");
        var table = WebLingoDisassemblyNames.TableOf(positions, function.Source);
        for (var i = 0; i < function.Code.Count; i++)
        {
            WebLingoDisassemblyNames.AppendStatement(text, function.DebugInfo, table, i);
            text.Append(i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)).Append("  ")
                .Append(Format(module, function, function.Code[i])).Append('\n');
        }
        foreach (var handler in function.Handlers)
        {
            text.Append("handler ").Append(handler.Start).Append(" to ").Append(handler.End - 1).Append(": ")
                .Append(handler.Target).Append(", exception ").Append(Register(function, handler.Value)).Append(", origin ")
                .Append(Register(function, handler.Origin)).Append('\n');
        }
        WebLingoDisassemblyNames.AppendVariables(text, function.DebugInfo, table, register => $"r{register}",
            register => Register(function, register));
    }

    /// <summary>An instruction of the module's entry point.</summary>
    public static string Format(WebLingoCompiledModule module, WebLingoInstruction instruction) =>
        Format(module, module.EntryPoint, instruction);

    public static string Format(WebLingoCompiledModule module, WebLingoFunction function, WebLingoInstruction instruction)
    {
        var (a, b, c, d) = (instruction.A, instruction.B, instruction.C, instruction.D);
        return instruction.OpCode switch
        {
            WebLingoOpCode.LoadConstant => $"LoadConstant {Register(function, a)}, {Constant(module, b)}",
            WebLingoOpCode.Move => $"Move {Register(function, a)}, {Register(function, b)}",
            WebLingoOpCode.Negate => $"Negate {Register(function, a)}, {Register(function, b)}",
            WebLingoOpCode.Plus => $"Plus {Register(function, a)}, {Register(function, b)}",
            WebLingoOpCode.Not => $"Not {Register(function, a)}, {Register(function, b)}",
            WebLingoOpCode.LoadNull => $"LoadNull {Register(function, a)}",
            WebLingoOpCode.LoadBoolean => $"LoadBoolean {Register(function, a)}, {(b == 0 ? "false" : "true")}",
            WebLingoOpCode.Equal or WebLingoOpCode.NotEqual or WebLingoOpCode.Less or WebLingoOpCode.LessOrEqual or WebLingoOpCode.Greater
                or WebLingoOpCode.GreaterOrEqual =>
                $"{instruction.OpCode} {Register(function, a)}, {Register(function, b)}, {Register(function, c)}",
            WebLingoOpCode.JumpIfFalse or WebLingoOpCode.JumpIfTrue or WebLingoOpCode.JumpIfGiven => $"{instruction.OpCode} {Register(function, a)}, {b:D2}",
            WebLingoOpCode.CheckBoolean => $"CheckBoolean {Register(function, a)}",
            WebLingoOpCode.NewCell or WebLingoOpCode.BoxRegister or WebLingoOpCode.CopyCell or WebLingoOpCode.ReturnValue
                or WebLingoOpCode.TakeRest =>
                $"{instruction.OpCode} {Register(function, a)}",
            WebLingoOpCode.LoadCell or WebLingoOpCode.StoreCell => $"{instruction.OpCode} {Register(function, a)}, {Register(function, b)}",
            WebLingoOpCode.LoadCapture => $"LoadCapture {Register(function, a)}, {CaptureAt(function, b)}",
            WebLingoOpCode.StoreCapture => $"StoreCapture {CaptureAt(function, a)}, {Register(function, b)}",
            WebLingoOpCode.MakeClosure => $"MakeClosure {Register(function, a)}, {FunctionAt(module, b)}",
            WebLingoOpCode.LoadBuiltin => $"LoadBuiltin {Register(function, a)}, {Import(module, b)}",
            WebLingoOpCode.Call => $"Call {Register(function, a)}, {Register(function, b)}({Arguments(function, c, d)})",
            WebLingoOpCode.AddConstant
                or WebLingoOpCode.SubtractConstant
                or WebLingoOpCode.MultiplyConstant
                or WebLingoOpCode.RemainderConstant
                or WebLingoOpCode.EqualConstant
                or WebLingoOpCode.NotEqualConstant
                or WebLingoOpCode.LessConstant
                or WebLingoOpCode.LessOrEqualConstant
                or WebLingoOpCode.GreaterConstant
                or WebLingoOpCode.GreaterOrEqualConstant =>
                $"{instruction.OpCode} {Register(function, a)}, {Register(function, b)}, {Constant(module, c)}",
            WebLingoOpCode.Add or WebLingoOpCode.Subtract or WebLingoOpCode.Multiply or WebLingoOpCode.Divide or WebLingoOpCode.Remainder
                or WebLingoOpCode.IntegerDivide =>
                $"{instruction.OpCode} {Register(function, a)}, {Register(function, b)}, {Register(function, c)}",
            WebLingoOpCode.CallBuiltin => $"CallBuiltin {Register(function, a)}, {Import(module, b)}({Arguments(function, c, d)})",
            WebLingoOpCode.Jump => $"Jump {a}",
            WebLingoOpCode.Return => "Return",
            WebLingoOpCode.GetMember => $"GetMember {Register(function, a)}, {Register(function, b)}{(d == 0 ? "." : "?.")}{Constant(module, c)}",
            WebLingoOpCode.SetMember => $"SetMember {Register(function, a)}.{Constant(module, b)}, {Register(function, c)}",
            WebLingoOpCode.GetField => $"GetField {Register(function, a)}, {Register(function, b)}.{Constant(module, c)}@{d}",
            WebLingoOpCode.SetField => $"SetField {Register(function, a)}.{Constant(module, b)}@{d}, {Register(function, c)}",
            WebLingoOpCode.Dispose => $"Dispose {Register(function, a)}",
            WebLingoOpCode.Throw => $"Throw {Register(function, a)}",
            WebLingoOpCode.ToText => $"ToText {Register(function, a)}, {Register(function, b)}" + (c == 1 ? ", written" : ""),
            WebLingoOpCode.ToJson => $"ToJson {Register(function, a)}, {Register(function, b)}, {Register(function, c)}",
            WebLingoOpCode.Await => $"Await {Register(function, a)}, {Register(function, b)}",
            WebLingoOpCode.Extend => $"Extend {Register(function, a)}, {WebLingoDisassemblyNames.Class(b, module.Classes)}",
            WebLingoOpCode.Hold => $"Hold {Register(function, a)}, {Register(function, b)}, {Register(function, c)}",
            WebLingoOpCode.ImportDynamic => ((d & 1) == 1
                ? $"ImportDynamic {Register(function, a)}, {Register(function, b)}, {Register(function, c)}"
                : $"ImportDynamic {Register(function, a)}, {Register(function, b)}") + (d >= 2 ? ", anew" : ""),
            WebLingoOpCode.Import => d == 1 ? $"Import {Register(function, a)}, {FileAt(module, b)}, {Register(function, c)}"
                : $"Import {Register(function, a)}, {FileAt(module, b)}",
            WebLingoOpCode.MakeModule => $"MakeModule {Register(function, a)}, {FileAt(module, b)}, {Register(function, c)}+{d}",
            WebLingoOpCode.RegisterUnit => $"RegisterUnit {FileAt(module, a)}, {Register(function, b)}",
            WebLingoOpCode.IsInstance => $"IsInstance {Register(function, a)}, {Register(function, b)}, {Register(function, c)}",
            WebLingoOpCode.Rethrow => $"Rethrow {Register(function, a)}, {Register(function, b)}",
            WebLingoOpCode.MakeClass => $"MakeClass {Register(function, a)}, {WebLingoDisassemblyNames.Class(b, module.Classes)}"
                                        + (d == 1 ? $" extends {Register(function, c)}" : ""),
            WebLingoOpCode.GetSuper => $"GetSuper {Register(function, a)}, {Register(function, b)}, {Register(function, c)}, {Constant(module, d)}",
            WebLingoOpCode.New => $"New {Register(function, a)}, {Register(function, b)}({Arguments(function, c, d)})",
            WebLingoOpCode.CallSpread or WebLingoOpCode.NewSpread =>
                $"{instruction.OpCode} {Register(function, a)}, {Register(function, b)}(...{Register(function, c)})",
            WebLingoOpCode.CallMethodSpread =>
                $"CallMethodSpread {Register(function, a)}, {Register(function, c)}.{Constant(module, b)}(...{Register(function, d)})",
            WebLingoOpCode.CallMethod => $"CallMethod {Register(function, a)}, {Register(function, c)}.{Constant(module, b)}" +
                                   $"({Arguments(function, c + 1, d)})",
            WebLingoOpCode.NewArray => $"NewArray {Register(function, a)}, [{Arguments(function, b, c)}]",
            WebLingoOpCode.NewSet => $"NewSet {Register(function, a)}, #{{{Arguments(function, b, c)}}}",
            WebLingoOpCode.NewMap => $"NewMap {Register(function, a)}, {{{Arguments(function, b, 2 * c)}}}",
            WebLingoOpCode.ForEachNext => $"ForEachNext {Register(function, b)}, {Register(function, b + 1)}, " +
                                    $"{Arguments(function, b + 2, c)} else {a:D2}",
            WebLingoOpCode.GetIndex => $"GetIndex {Register(function, a)}, {Register(function, b)}{(d == 0 ? "[" : "?[")}{Register(function, c)}]",
            WebLingoOpCode.SetIndex => $"SetIndex {Register(function, a)}[{Register(function, b)}], {Register(function, c)}",
            WebLingoOpCode.ForEachStart => $"ForEachStart {Register(function, b)} visited {a:D2}",
            WebLingoOpCode.ForEachVisits => $"ForEachVisits {Register(function, b)} visited {a:D2}",
            _ => $"?{(int)instruction.OpCode} {a}, {b}, {c}, {d}"
        };
    }

    private static string FunctionAt(WebLingoCompiledModule module, int index) =>
        WebLingoDisassemblyNames.Function(index, module.Functions, function => function.Name, module.Units);

    private static string FileAt(WebLingoCompiledModule module, int index) => WebLingoDisassemblyNames.File(index, module.Units);

    private static string CaptureAt(WebLingoFunction function, int index) => WebLingoDisassemblyNames.Capture(index, function.DebugInfo);

    private static string Register(WebLingoFunction function, int register)
    {
        var name = function.LocalNames is { } names && register >= 0 && register < names.Count ? $"({names[register]})" : "";
        return $"r{register}{name}";
    }

    private static string Constant(WebLingoCompiledModule module, int index)
    {
        if (index < 0 || index >= module.Constants.Count) return $"#{index}?";
        var constant = module.Constants[index];
        return constant.Kind switch
        {
            WebLingoConstantKind.Int64 => constant.Bits.ToString(System.Globalization.CultureInfo.InvariantCulture),
            WebLingoConstantKind.String => "\"" + constant.Text + "\"",
            WebLingoConstantKind.Decimal => constant.Text + "m",
            _ => BitConverter.Int64BitsToDouble(constant.Bits).ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "d"
        };
    }

    private static string Import(WebLingoCompiledModule module, int index) =>
        index >= 0 && index < module.Imports.Count ? module.Imports[index].Name : $"import{index}?";

    private static string Arguments(WebLingoFunction function, int first, int count)
    {
        var parts = new string[Math.Max(0, count)];
        for (var i = 0; i < parts.Length; i++) parts[i] = Register(function, first + i);
        return string.Join(", ", parts);
    }
}
