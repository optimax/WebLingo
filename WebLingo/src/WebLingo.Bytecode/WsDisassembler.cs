using System.Text;
namespace WebLingo.Bytecode;

/// <summary>
/// Shows a module as text, one instruction per line, for tests and for looking at what the compiler made. Registers
/// are <c>r0</c>, <c>r1</c>, …; locals are named where the module knows their names. The format is for people, not a
/// file format.
/// </summary>
public static class WsDisassembler
{
    public static string Format(WsCompiledModule module)
    {
        var text = new StringBuilder();
        var function = module.EntryPoint;
        text.Append("function ").Append(function.Name).Append(" (").Append(function.LocalCount).Append(" locals, ")
            .Append(function.RegisterCount).Append(" registers)\n");
        for (var i = 0; i < function.Code.Count; i++)
        {
            text.Append(i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)).Append("  ")
                .Append(Format(module, function.Code[i])).Append('\n');
        }
        return text.ToString();
    }

    public static string Format(WsCompiledModule module, WsInstruction instruction)
    {
        var function = module.EntryPoint;
        var (a, b, c, d) = (instruction.A, instruction.B, instruction.C, instruction.D);
        return instruction.OpCode switch
        {
            WsOpCode.LoadConstant => $"LoadConstant {Register(function, a)}, {Constant(module, b)}",
            WsOpCode.Move => $"Move {Register(function, a)}, {Register(function, b)}",
            WsOpCode.Negate => $"Negate {Register(function, a)}, {Register(function, b)}",
            WsOpCode.Add or WsOpCode.Subtract or WsOpCode.Multiply or WsOpCode.Divide =>
                $"{instruction.OpCode} {Register(function, a)}, {Register(function, b)}, {Register(function, c)}",
            WsOpCode.CheckInitialized or WsOpCode.CheckDeclared or WsOpCode.DeclareUnassigned =>
                $"{instruction.OpCode} {Register(function, a)}",
            WsOpCode.CallBuiltin => $"CallBuiltin {Register(function, a)}, {Import(module, b)}({Arguments(function, c, d)})",
            WsOpCode.Jump => $"Jump {a}",
            WsOpCode.Return => "Return",
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
        return constant.Kind == WsConstantKind.Int64
            ? constant.Bits.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : BitConverter.Int64BitsToDouble(constant.Bits).ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "d";
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
