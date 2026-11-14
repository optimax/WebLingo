using System.Text;
namespace WebLingo.Bytecode;

/// <summary>
/// Shows a module as text, one instruction per line, for tests and for looking at what the compiler made. Registers
/// are <c>r0</c>, <c>r1</c>, …; locals are named where the module knows their names. The format is for people, not a
/// file format.
/// </summary>
public static class WsDisassembler
{
    /// <summary>The module's entry point, then each of its other functions, with its index.</summary>
    public static string Format(WsCompiledModule module)
    {
        var text = new StringBuilder();
        Append(text, module, module.EntryPoint, "");
        for (var i = 0; i < module.Functions.Count; i++) Append(text.Append('\n'), module, module.Functions[i], $"function{i} ");
        return text.ToString();
    }

    private static void Append(StringBuilder text, WsCompiledModule module, WsFunction function, string label)
    {
        text.Append("function ").Append(label).Append(function.Name).Append(" (");
        if (function.ParameterCount > 0) text.Append(function.ParameterCount).Append(" parameters, ");
        if (function.Captures.Count > 0) text.Append(function.Captures.Count).Append(" captures, ");
        text.Append(function.LocalCount).Append(" locals, ").Append(function.RegisterCount).Append(" registers)\n");
        for (var i = 0; i < function.Code.Count; i++)
        {
            text.Append(i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)).Append("  ")
                .Append(Format(module, function, function.Code[i])).Append('\n');
        }
        foreach (var handler in function.Handlers)
        {
            text.Append("handler ").Append(handler.Start).Append(" to ").Append(handler.End - 1).Append(": ")
                .Append(handler.Target).Append(", exception ").Append(Register(function, handler.Value)).Append(", origin ")
                .Append(Register(function, handler.Origin)).Append('\n');
        }
    }

    /// <summary>An instruction of the module's entry point.</summary>
    public static string Format(WsCompiledModule module, WsInstruction instruction) =>
        Format(module, module.EntryPoint, instruction);

    public static string Format(WsCompiledModule module, WsFunction function, WsInstruction instruction)
    {
        var (a, b, c, d) = (instruction.A, instruction.B, instruction.C, instruction.D);
        return instruction.OpCode switch
        {
            WsOpCode.LoadConstant => $"LoadConstant {Register(function, a)}, {Constant(module, b)}",
            WsOpCode.Move => $"Move {Register(function, a)}, {Register(function, b)}",
            WsOpCode.Negate => $"Negate {Register(function, a)}, {Register(function, b)}",
            WsOpCode.Plus => $"Plus {Register(function, a)}, {Register(function, b)}",
            WsOpCode.Not => $"Not {Register(function, a)}, {Register(function, b)}",
            WsOpCode.LoadNull => $"LoadNull {Register(function, a)}",
            WsOpCode.LoadBoolean => $"LoadBoolean {Register(function, a)}, {(b == 0 ? "false" : "true")}",
            WsOpCode.Equal or WsOpCode.NotEqual or WsOpCode.Less or WsOpCode.LessOrEqual or WsOpCode.Greater
                or WsOpCode.GreaterOrEqual =>
                $"{instruction.OpCode} {Register(function, a)}, {Register(function, b)}, {Register(function, c)}",
            WsOpCode.JumpIfFalse or WsOpCode.JumpIfTrue => $"{instruction.OpCode} {Register(function, a)}, {b:D2}",
            WsOpCode.CheckBoolean => $"CheckBoolean {Register(function, a)}",
            WsOpCode.NewCell or WsOpCode.BoxRegister or WsOpCode.CopyCell or WsOpCode.ReturnValue =>
                $"{instruction.OpCode} {Register(function, a)}",
            WsOpCode.LoadCell or WsOpCode.StoreCell => $"{instruction.OpCode} {Register(function, a)}, {Register(function, b)}",
            WsOpCode.LoadCapture => $"LoadCapture {Register(function, a)}, capture{b}",
            WsOpCode.StoreCapture => $"StoreCapture capture{a}, {Register(function, b)}",
            WsOpCode.MakeClosure => $"MakeClosure {Register(function, a)}, function{b}",
            WsOpCode.LoadBuiltin => $"LoadBuiltin {Register(function, a)}, {Import(module, b)}",
            WsOpCode.Call => $"Call {Register(function, a)}, {Register(function, b)}({Arguments(function, c, d)})",
            WsOpCode.Add or WsOpCode.Subtract or WsOpCode.Multiply or WsOpCode.Divide =>
                $"{instruction.OpCode} {Register(function, a)}, {Register(function, b)}, {Register(function, c)}",
            WsOpCode.CallBuiltin => $"CallBuiltin {Register(function, a)}, {Import(module, b)}({Arguments(function, c, d)})",
            WsOpCode.Jump => $"Jump {a}",
            WsOpCode.Return => "Return",
            WsOpCode.GetMember => $"GetMember {Register(function, a)}, {Register(function, b)}{(d == 0 ? "." : "?.")}{Constant(module, c)}",
            WsOpCode.SetMember => $"SetMember {Register(function, a)}.{Constant(module, b)}, {Register(function, c)}",
            WsOpCode.Dispose => $"Dispose {Register(function, a)}",
            WsOpCode.Throw => $"Throw {Register(function, a)}",
            WsOpCode.ToText => $"ToText {Register(function, a)}, {Register(function, b)}",
            WsOpCode.Rethrow => $"Rethrow {Register(function, a)}, {Register(function, b)}",
            WsOpCode.MakeClass => $"MakeClass {Register(function, a)}, class{b}" + (d == 1 ? $" extends {Register(function, c)}" : ""),
            WsOpCode.GetSuper => $"GetSuper {Register(function, a)}, {Register(function, b)}, {Register(function, c)}, {Constant(module, d)}",
            WsOpCode.New => $"New {Register(function, a)}, {Register(function, b)}({Arguments(function, c, d)})",
            WsOpCode.CallMethod => $"CallMethod {Register(function, a)}, {Register(function, c)}.{Constant(module, b)}" +
                                   $"({Arguments(function, c + 1, d)})",
            WsOpCode.NewArray => $"NewArray {Register(function, a)}, [{Arguments(function, b, c)}]",
            WsOpCode.NewSet => $"NewSet {Register(function, a)}, #{{{Arguments(function, b, c)}}}",
            WsOpCode.NewMap => $"NewMap {Register(function, a)}, {{{Arguments(function, b, 2 * c)}}}",
            WsOpCode.ForEachNext => $"ForEachNext {Register(function, b)}, {Register(function, b + 1)}, " +
                                    $"{Arguments(function, b + 2, c)} else {a:D2}",
            WsOpCode.GetIndex => $"GetIndex {Register(function, a)}, {Register(function, b)}{(d == 0 ? "[" : "?[")}{Register(function, c)}]",
            WsOpCode.SetIndex => $"SetIndex {Register(function, a)}[{Register(function, b)}], {Register(function, c)}",
            _ => $"?{(int)instruction.OpCode} {a}, {b}, {c}, {d}"
        };
    }

    private static string Register(WsFunction function, int register)
    {
        var name = function.LocalNames is { } names && register >= 0 && register < names.Count ? $"({names[register]})" : "";
        return $"r{register}{name}";
    }

    private static string Constant(WsCompiledModule module, int index)
    {
        if (index < 0 || index >= module.Constants.Count) return $"#{index}?";
        var constant = module.Constants[index];
        return constant.Kind switch
        {
            WsConstantKind.Int64 => constant.Bits.ToString(System.Globalization.CultureInfo.InvariantCulture),
            WsConstantKind.String => "\"" + constant.Text + "\"",
            _ => BitConverter.Int64BitsToDouble(constant.Bits).ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "d"
        };
    }

    private static string Import(WsCompiledModule module, int index) =>
        index >= 0 && index < module.Imports.Count ? module.Imports[index].Name : $"import{index}?";

    private static string Arguments(WsFunction function, int first, int count)
    {
        var parts = new string[Math.Max(0, count)];
        for (var i = 0; i < parts.Length; i++) parts[i] = Register(function, first + i);
        return string.Join(", ", parts);
    }
}
