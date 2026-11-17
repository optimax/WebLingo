using System.Globalization;
using System.Text;
namespace WebLingo.Bytecode;

/// <summary>
/// Shows a stack module as text, one instruction per line, as <see cref="WebLingoDisassembler"/> shows a register module:
/// locals are <c>l0</c>, <c>l1</c>, …, named where the module knows their names. The format is for people, not a file
/// format.
/// </summary>
public static class WebLingoStackDisassembler
{
    /// <summary>The module's entry point, then each of its other functions, with its index.</summary>
    public static string Format(WebLingoStackModule module)
    {
        var text = new StringBuilder();
        Append(text, module, module.EntryPoint, "");
        for (var i = 0; i < module.Functions.Count; i++) Append(text.Append('\n'), module, module.Functions[i], $"function{i} ");
        return text.ToString();
    }

    private static void Append(StringBuilder text, WebLingoStackModule module, WebLingoStackFunction function, string label)
    {
        text.Append("function ").Append(label).Append(function.Name).Append(" (");
        if (function.ParameterCount > 0) text.Append(function.ParameterCount).Append(" parameters, ");
        if (function.Captures.Count > 0) text.Append(function.Captures.Count).Append(" captures, ");
        text.Append(function.LocalCount).Append(" locals, ").Append(function.MaxStack).Append(" stack)\n");
        for (var i = 0; i < function.Code.Count; i++)
        {
            text.Append(i.ToString("D2", CultureInfo.InvariantCulture)).Append("  ")
                .Append(Format(module, function, function.Code[i])).Append('\n');
        }
        foreach (var handler in function.Handlers)
        {
            text.Append("handler ").Append(handler.Start).Append(" to ").Append(handler.End - 1).Append(": ")
                .Append(handler.Target).Append(", exception ").Append(Local(function, handler.Value)).Append(", origin ")
                .Append(Local(function, handler.Origin)).Append(", depth ").Append(handler.Depth).Append('\n');
        }
    }

    public static string Format(WebLingoStackModule module, WebLingoStackFunction function, WebLingoStackInstruction instruction)
    {
        var (a, b) = (instruction.A, instruction.B);
        var name = instruction.OpCode.ToString();
        return instruction.OpCode switch
        {
            WebLingoStackOpCode.LoadConstant => $"{name} {Constant(module, a)}",
            WebLingoStackOpCode.LoadBoolean => $"{name} {(a == 0 ? "false" : "true")}",
            WebLingoStackOpCode.LoadLocal or WebLingoStackOpCode.StoreLocal or WebLingoStackOpCode.NewCell or WebLingoStackOpCode.BoxLocal
                or WebLingoStackOpCode.CopyCell or WebLingoStackOpCode.LoadCell or WebLingoStackOpCode.StoreCell => $"{name} {Local(function, a)}",
            WebLingoStackOpCode.Jump or WebLingoStackOpCode.JumpIfFalse or WebLingoStackOpCode.JumpIfTrue => $"{name} {Target(a)}",
            WebLingoStackOpCode.JumpIfGiven => $"{name} {Local(function, b)}, {Target(a)}",
            WebLingoStackOpCode.LoadCapture or WebLingoStackOpCode.StoreCapture => $"{name} capture{a}",
            WebLingoStackOpCode.MakeClosure => $"{name} function{a}",
            WebLingoStackOpCode.LoadBuiltin => $"{name} {Import(module, a)}",
            WebLingoStackOpCode.Call or WebLingoStackOpCode.New => $"{name} ({a} arguments)",
            WebLingoStackOpCode.CallBuiltin => $"{name} {Import(module, a)} ({b} arguments)",
            WebLingoStackOpCode.GetMember => $"{name} {(b == 0 ? "." : "?.")}{Constant(module, a)}",
            WebLingoStackOpCode.CallMethod => $"{name} .{Constant(module, a)} ({b} arguments)",
            WebLingoStackOpCode.GetIndex => a == 0 ? $"{name} []" : $"{name} ?[]",
            WebLingoStackOpCode.StoreIndex => a == 0 ? name : $"{name}, kept",
            WebLingoStackOpCode.SetMember => $"{name} .{Constant(module, a)}" + (b == 0 ? "" : ", kept"),
            WebLingoStackOpCode.NewArray or WebLingoStackOpCode.NewSet => $"{name} ({a} values)",
            WebLingoStackOpCode.NewMap => $"{name} ({a} entries)",
            WebLingoStackOpCode.ForEachNext or WebLingoStackOpCode.ForEachNextEntry =>
                $"{name} {Local(function, b)}, {Local(function, b + 1)} else {Target(a)}",
            WebLingoStackOpCode.ForEachStart or WebLingoStackOpCode.ForEachVisits => $"{name} {Local(function, b)} visited {Target(a)}",
            WebLingoStackOpCode.MakeClass => $"{name} class{a}" + (b == 1 ? " extends" : ""),
            WebLingoStackOpCode.Extend => $"{name} class{a}",
            WebLingoStackOpCode.GetSuper => $"{name} {Constant(module, a)}",
            WebLingoStackOpCode.Import or WebLingoStackOpCode.RegisterUnit => $"{name} file{a}",
            WebLingoStackOpCode.MakeModule => $"{name} file{a} ({b} cells)",
            WebLingoStackOpCode.ImportDynamic => name + ((a & 1) == 1 ? " with values" : "") + (a >= 2 ? ", anew" : ""),
            _ when Enum.IsDefined(instruction.OpCode) => name,
            _ => $"?{(int)instruction.OpCode} {a}, {b}"
        };
    }

    private static string Target(int index) => index.ToString("D2", CultureInfo.InvariantCulture);

    private static string Local(WebLingoStackFunction function, int local)
    {
        var name = function.LocalNames is { } names && local >= 0 && local < names.Count ? $"({names[local]})" : "";
        return $"l{local}{name}";
    }

    private static string Constant(WebLingoStackModule module, int index)
    {
        if (index < 0 || index >= module.Constants.Count) return $"#{index}?";
        var constant = module.Constants[index];
        return constant.Kind switch
        {
            WebLingoConstantKind.Int64 => constant.Bits.ToString(CultureInfo.InvariantCulture),
            WebLingoConstantKind.String => "\"" + constant.Text + "\"",
            WebLingoConstantKind.Decimal => constant.Text + "m",
            _ => BitConverter.Int64BitsToDouble(constant.Bits).ToString("R", CultureInfo.InvariantCulture) + "d"
        };
    }

    private static string Import(WebLingoStackModule module, int index) =>
        index >= 0 && index < module.Imports.Count ? module.Imports[index].Name : $"import{index}?";
}
