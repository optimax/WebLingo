using System.Diagnostics.CodeAnalysis;
using WebLingo.Core;
namespace WebLingo.Bytecode;

/// <summary>
/// WLBC, WebLingo ByteCode (specification, sections 5.1 and 5.3): a module of one VM's bytecode as a file's bytes, and
/// back. A file is a 32-byte header, a directory of 32-byte entries, one for each section, giving its kind, offset,
/// length and CRC-32, and the sections: the module, constants, functions, code, classes and units, handlers, source
/// positions, optional, and debugging, optional too, after the source positions, which a module compiled for a debugger
/// has (<c>weblingo compile --debug</c>; section 15.2, "WLBC's layout"). The same module is always written as the same
/// bytes. Reading checks the file and verifies the module with its VM's verifier (section 5.5); it never throws.
/// Nothing here is frozen: the format may change without a new version until the owner freezes it.
/// </summary>
public static class WebLingoBytecodeFile
{
    public const int MajorVersion = 0;
    public const int MinorVersion = 1;
    public const string RegisterExtension = ".wlbr";
    public const string StackExtension = ".wlbs";
    public const string LegacyExtension = ".wlbc";

    /// <summary>The VM named by a filename extension, if it names one. The file's header must still be checked.</summary>
    public static WebLingoBytecodeKind? KindForExtension(string extension)
    {
        if (extension.Equals(RegisterExtension, StringComparison.OrdinalIgnoreCase)) return WebLingoBytecodeKind.Register;
        if (extension.Equals(StackExtension, StringComparison.OrdinalIgnoreCase)) return WebLingoBytecodeKind.Stack;
        return null;
    }

    public static bool IsBytecodeExtension(string extension) =>
        KindForExtension(extension).HasValue || extension.Equals(LegacyExtension, StringComparison.OrdinalIgnoreCase);

    private const int HeaderSize = 32;
    private const int EntrySize = 32;
    private const int SectionCount = 7;
    private const int PositionsSection = 7;
    private const int DebuggingSection = 8;

    private static readonly byte[] Magic = [(byte)'W', (byte)'L', (byte)'B', (byte)'C'];

    private static readonly string[] SectionNames =
        ["", "module", "constants", "functions", "code", "classes and units", "handlers", "source positions", "debugging"];

    /// <summary>
    /// A register module as a file. <paramref name="textOf"/> gives a file's text, the program's own for null, or null
    /// for a file it does not have, from which the file's line and column table is made (section 5.3, "As built").
    /// </summary>
    public static byte[] Write(WebLingoCompiledModule module, Func<string?, WebLingoSourceText?>? textOf = null)
    {
        var functions = new List<WebLingoFunction> { module.EntryPoint };
        functions.AddRange(module.Functions);
        var heads = new List<FunctionHead>();
        foreach (var function in functions) heads.Add(HeadOf(function));
        var code = new WebLingoBytecodeWriter();
        var handlers = new WebLingoBytecodeWriter();
        foreach (var function in functions)
        {
            foreach (var instruction in function.Code)
            {
                code.Byte((int)instruction.OpCode);
                code.Signed(instruction.A);
                code.Signed(instruction.B);
                code.Signed(instruction.C);
                code.Signed(instruction.D);
            }
            handlers.Count(function.Handlers.Count);
            foreach (var handler in function.Handlers)
            {
                handlers.Signed(handler.Start);
                handlers.Signed(handler.End);
                handlers.Signed(handler.Target);
                handlers.Signed(handler.Value);
                handlers.Signed(handler.Origin);
            }
        }
        return Assemble(WebLingoBytecodeKind.Register, HeadOf(module), heads, code, handlers, textOf);
    }

    /// <summary>A stack module as a file, as <see cref="Write(WebLingoCompiledModule, Func{string?, WebLingoSourceText?}?)"/> writes a register module.</summary>
    public static byte[] Write(WebLingoStackModule module, Func<string?, WebLingoSourceText?>? textOf = null)
    {
        var functions = new List<WebLingoStackFunction> { module.EntryPoint };
        functions.AddRange(module.Functions);
        var heads = new List<FunctionHead>();
        foreach (var function in functions) heads.Add(HeadOf(function));
        var code = new WebLingoBytecodeWriter();
        var handlers = new WebLingoBytecodeWriter();
        foreach (var function in functions)
        {
            foreach (var instruction in function.Code)
            {
                code.Byte((int)instruction.OpCode);
                code.Signed(instruction.A);
                code.Signed(instruction.B);
            }
            handlers.Count(function.Handlers.Count);
            foreach (var handler in function.Handlers)
            {
                handlers.Signed(handler.Start);
                handlers.Signed(handler.End);
                handlers.Signed(handler.Target);
                handlers.Signed(handler.Value);
                handlers.Signed(handler.Origin);
                handlers.Signed(handler.Depth);
            }
        }
        return Assemble(WebLingoBytecodeKind.Stack, HeadOf(module), heads, code, handlers, textOf);
    }

    /// <summary>Which VM's bytecode a file holds, from its header; false, with why, for a file that is not WLBC this reader knows.</summary>
    public static bool TryReadKind(byte[] file, out WebLingoBytecodeKind kind, out string? error)
    {
        kind = default;
        error = CheckHeader(file);
        if (error is not null) return false;
        kind = (WebLingoBytecodeKind)Fixed(file, 8, 2);
        return true;
    }

    /// <summary>
    /// A file's register module, verified, and its files' source positions, the program's own first; false, with why (a
    /// WL4001), for a file that is damaged, of a version this reader does not know, the stack VM's, or fails verification.
    /// </summary>
    public static bool TryReadRegister(byte[] file, [NotNullWhen(true)] out WebLingoCompiledModule? module,
        out IReadOnlyList<WebLingoSourcePositions> positions, out string? error)
    {
        module = null;
        if (!TryOpen(file, WebLingoBytecodeKind.Register, out var parts, out positions, out error)) return false;
        var (head, heads, codeReader, handlersReader) = parts;
        var functions = new List<WebLingoFunction>();
        for (var f = 0; f < heads.Count; f++)
        {
            var h = heads[f];
            if (h.InstructionCount > codeReader.Left / 5) codeReader.Fail($"has fewer instructions than function {f} has");
            var code = new WebLingoInstruction[codeReader.Failed ? 0 : h.InstructionCount];
            for (var i = 0; i < code.Length; i++)
            {
                code[i] = new WebLingoInstruction((WebLingoOpCode)codeReader.Byte(), codeReader.Int(), codeReader.Int(),
                    codeReader.Int(), codeReader.Int());
            }
            var handlers = new WebLingoHandler[handlersReader.Count()];
            for (var i = 0; i < handlers.Length; i++)
            {
                handlers[i] = new WebLingoHandler(handlersReader.Int(), handlersReader.Int(), handlersReader.Int(), handlersReader.Int(),
                    handlersReader.Int());
            }
            functions.Add(new WebLingoFunction(h.Name, h.First, h.Second, code, h.Spans, h.LocalNames)
            {
                ParameterCount = h.ParameterCount,
                OptionalCount = h.OptionalCount,
                HasThis = h.HasThis,
                IsAsync = h.IsAsync,
                HasRest = h.HasRest,
                IsNative = h.IsNative,
                Source = h.Source,
                NativeBuiltin = h.NativeBuiltin,
                NativeResult = h.NativeResult,
                Captures = h.Captures,
                Handlers = handlers,
                DebugInfo = h.DebugInfo
            });
        }
        if (Ended(codeReader, handlersReader) is { } damaged)
        {
            error = damaged;
            return false;
        }
        var read = new WebLingoCompiledModule(head.Name, head.Constants, head.Imports, functions[0])
        {
            Functions = functions.GetRange(1, functions.Count - 1),
            Classes = head.Classes,
            Units = head.Units,
            ErrorClass = head.ErrorClass,
            ObjectClass = head.ObjectClass,
            ValueClasses = head.ValueClasses,
            ErrorClasses = head.ErrorClasses,
            IsImport = head.IsImport,
            Parameters = head.Parameters
        };
        var verification = new WebLingoBytecodeVerifier().Verify(read);
        if (!verification.IsValid)
        {
            error = "The file's bytecode fails verification: " + verification.Error;
            return false;
        }
        module = read;
        return true;
    }

    /// <summary>A file's stack module, verified, as <see cref="TryReadRegister"/> reads a register module.</summary>
    public static bool TryReadStack(byte[] file, [NotNullWhen(true)] out WebLingoStackModule? module,
        out IReadOnlyList<WebLingoSourcePositions> positions, out string? error)
    {
        module = null;
        if (!TryOpen(file, WebLingoBytecodeKind.Stack, out var parts, out positions, out error)) return false;
        var (head, heads, codeReader, handlersReader) = parts;
        var functions = new List<WebLingoStackFunction>();
        for (var f = 0; f < heads.Count; f++)
        {
            var h = heads[f];
            if (h.InstructionCount > codeReader.Left / 3) codeReader.Fail($"has fewer instructions than function {f} has");
            var code = new WebLingoStackInstruction[codeReader.Failed ? 0 : h.InstructionCount];
            for (var i = 0; i < code.Length; i++)
            {
                code[i] = new WebLingoStackInstruction((WebLingoStackOpCode)codeReader.Byte(), codeReader.Int(), codeReader.Int());
            }
            var handlers = new WebLingoStackHandler[handlersReader.Count()];
            for (var i = 0; i < handlers.Length; i++)
            {
                handlers[i] = new WebLingoStackHandler(handlersReader.Int(), handlersReader.Int(), handlersReader.Int(),
                    handlersReader.Int(), handlersReader.Int(), handlersReader.Int());
            }
            functions.Add(new WebLingoStackFunction(h.Name, h.First, h.Second, code, h.Spans, h.LocalNames)
            {
                ParameterCount = h.ParameterCount,
                OptionalCount = h.OptionalCount,
                HasThis = h.HasThis,
                IsAsync = h.IsAsync,
                HasRest = h.HasRest,
                IsNative = h.IsNative,
                Source = h.Source,
                NativeBuiltin = h.NativeBuiltin,
                NativeResult = h.NativeResult,
                Captures = h.Captures,
                Handlers = handlers,
                DebugInfo = h.DebugInfo
            });
        }
        if (Ended(codeReader, handlersReader) is { } damaged)
        {
            error = damaged;
            return false;
        }
        var read = new WebLingoStackModule(head.Name, head.Constants, head.Imports, functions[0])
        {
            Functions = functions.GetRange(1, functions.Count - 1),
            Classes = head.Classes,
            Units = head.Units,
            ErrorClass = head.ErrorClass,
            ObjectClass = head.ObjectClass,
            ValueClasses = head.ValueClasses,
            ErrorClasses = head.ErrorClasses,
            IsImport = head.IsImport,
            Parameters = head.Parameters
        };
        var verification = new WebLingoStackBytecodeVerifier().Verify(read);
        if (!verification.IsValid)
        {
            error = "The file's bytecode fails verification: " + verification.Error;
            return false;
        }
        module = read;
        return true;
    }

    /// <summary>What both VMs' modules have alike.</summary>
    private sealed record ModuleHead(string Name, bool IsImport, int ErrorClass, int ObjectClass, IReadOnlyList<int> ValueClasses,
        IReadOnlyList<WebLingoErrorClass> ErrorClasses, IReadOnlyList<WebLingoModuleParameter> Parameters,
        IReadOnlyList<WebLingoImport> Imports, IReadOnlyList<WebLingoConstant> Constants, IReadOnlyList<WebLingoClassInfo> Classes,
        IReadOnlyList<WebLingoUnitInfo> Units);

    /// <summary>
    /// What both VMs' functions have alike: <see cref="First"/> and <see cref="Second"/> are a register function's
    /// register and local counts, a stack function's local count and most stack values.
    /// </summary>
    private sealed record FunctionHead(string Name, int First, int Second, int ParameterCount, int OptionalCount, bool HasThis,
        bool IsAsync, bool IsNative, string? Source, int NativeBuiltin, WebLingoNativeResult NativeResult,
        IReadOnlyList<WebLingoCapture> Captures, int InstructionCount)
    {
        public IReadOnlyList<WebLingoSourceSpan>? Spans { get; set; }
        public IReadOnlyList<string>? LocalNames { get; set; }
        public WebLingoFunctionDebugInfo? DebugInfo { get; set; }
        public bool HasRest { get; init; }
    }

    private const int HasThisFlag = 1, IsAsyncFlag = 2, IsNativeFlag = 4, HasSourceFlag = 8, HasRestFlag = 16;
    private const int HasSpansFlag = 1, HasLocalNamesFlag = 2;

    private static ModuleHead HeadOf(WebLingoCompiledModule m) => new(m.Name, m.IsImport, m.ErrorClass, m.ObjectClass,
        m.ValueClasses, m.ErrorClasses, m.Parameters, m.Imports, m.Constants, m.Classes, m.Units);

    private static ModuleHead HeadOf(WebLingoStackModule m) => new(m.Name, m.IsImport, m.ErrorClass, m.ObjectClass,
        m.ValueClasses, m.ErrorClasses, m.Parameters, m.Imports, m.Constants, m.Classes, m.Units);

    private static FunctionHead HeadOf(WebLingoFunction f) => new(f.Name, f.RegisterCount, f.LocalCount, f.ParameterCount,
        f.OptionalCount, f.HasThis, f.IsAsync, f.IsNative, f.Source, f.NativeBuiltin, f.NativeResult, f.Captures, f.Code.Count)
    {
        Spans = f.Spans,
        LocalNames = f.LocalNames,
        DebugInfo = f.DebugInfo,
        HasRest = f.HasRest
    };

    private static FunctionHead HeadOf(WebLingoStackFunction f) => new(f.Name, f.LocalCount, f.MaxStack, f.ParameterCount,
        f.OptionalCount, f.HasThis, f.IsAsync, f.IsNative, f.Source, f.NativeBuiltin, f.NativeResult, f.Captures, f.Code.Count)
    {
        Spans = f.Spans,
        LocalNames = f.LocalNames,
        DebugInfo = f.DebugInfo,
        HasRest = f.HasRest
    };

    private static byte[] Assemble(WebLingoBytecodeKind kind, ModuleHead module, List<FunctionHead> functions,
        WebLingoBytecodeWriter code, WebLingoBytecodeWriter handlers, Func<string?, WebLingoSourceText?>? textOf)
    {
        List<WebLingoBytecodeWriter> listed =
        [
            WriteModule(module), WriteConstants(module.Constants), WriteFunctions(functions), code,
            WriteClassesAndUnits(module.Classes, module.Units), handlers, WritePositions(functions, textOf)
        ];
        // A module compiled for a debugger, its functions' debugging metadata after their source positions.
        if (functions.Exists(f => f.DebugInfo is not null)) listed.Add(WriteDebugging(functions));
        var sections = listed.ToArray();
        var total = HeaderSize + EntrySize * sections.Length;
        foreach (var section in sections) total += section.Length;
        var file = new WebLingoBytecodeWriter(total);
        foreach (var b in Magic) file.Byte(b);
        file.Fixed(MajorVersion, 2);
        file.Fixed(MinorVersion, 2);
        file.Fixed((ulong)kind, 2);
        file.Fixed(0, 2);
        file.Fixed((ulong)sections.Length, 4);
        file.Fixed(HeaderSize, 8);
        file.Fixed((ulong)total, 8);
        var offset = HeaderSize + EntrySize * sections.Length;
        for (var i = 0; i < sections.Length; i++)
        {
            file.Fixed((ulong)(i + 1), 4);
            file.Fixed(0, 4);
            file.Fixed((ulong)offset, 8);
            file.Fixed((ulong)sections[i].Length, 8);
            file.Fixed(sections[i].Crc32(), 4);
            file.Fixed(0, 4);
            offset += sections[i].Length;
        }
        var bytes = file.ToArray();
        Array.Resize(ref bytes, total);
        offset = HeaderSize + EntrySize * sections.Length;
        foreach (var section in sections)
        {
            section.CopyTo(bytes, offset);
            offset += section.Length;
        }
        return bytes;
    }

    private static WebLingoBytecodeWriter WriteModule(ModuleHead module)
    {
        var w = new WebLingoBytecodeWriter();
        w.Text(module.Name);
        w.Byte(module.IsImport ? 1 : 0);
        w.Signed(module.ErrorClass);
        w.Signed(module.ObjectClass);
        w.Count(module.ValueClasses.Count);
        foreach (var value in module.ValueClasses) w.Signed(value);
        w.Count(module.ErrorClasses.Count);
        foreach (var error in module.ErrorClasses)
        {
            w.Text(error.Code);
            w.Signed(error.Class);
        }
        WriteParameters(w, module.Parameters);
        w.Count(module.Imports.Count);
        foreach (var import in module.Imports)
        {
            w.Text(import.Name);
            w.Signed(import.Arity);
        }
        return w;
    }

    private static void WriteParameters(WebLingoBytecodeWriter w, IReadOnlyList<WebLingoModuleParameter> parameters)
    {
        w.Count(parameters.Count);
        foreach (var parameter in parameters)
        {
            w.Text(parameter.Name);
            w.Boolean(parameter.HasDefault);
        }
    }

    private static WebLingoBytecodeWriter WriteConstants(IReadOnlyList<WebLingoConstant> constants)
    {
        var w = new WebLingoBytecodeWriter();
        w.Count(constants.Count);
        foreach (var constant in constants)
        {
            w.Byte((int)constant.Kind);
            switch (constant.Kind)
            {
                case WebLingoConstantKind.Int64:
                    w.Signed(constant.Bits);
                    break;
                case WebLingoConstantKind.Binary64:
                    w.Fixed((ulong)constant.Bits, 8);
                    break;
                default:
                    w.Text(constant.Text ?? "");
                    break;
            }
        }
        return w;
    }

    private static WebLingoBytecodeWriter WriteFunctions(List<FunctionHead> functions)
    {
        var w = new WebLingoBytecodeWriter();
        w.Count(functions.Count);
        foreach (var f in functions)
        {
            w.Text(f.Name);
            w.Signed(f.First);
            w.Signed(f.Second);
            w.Signed(f.ParameterCount);
            w.Signed(f.OptionalCount);
            w.Byte((f.HasThis ? HasThisFlag : 0) | (f.IsAsync ? IsAsyncFlag : 0) | (f.IsNative ? IsNativeFlag : 0)
                   | (f.Source is not null ? HasSourceFlag : 0) | (f.HasRest ? HasRestFlag : 0));
            if (f.Source is not null) w.Text(f.Source);
            w.Signed(f.NativeBuiltin);
            w.Byte((int)f.NativeResult);
            w.Count(f.Captures.Count);
            foreach (var capture in f.Captures)
            {
                w.Boolean(capture.FromLocal);
                w.Signed(capture.Index);
            }
            w.Count(f.InstructionCount);
        }
        return w;
    }

    private static WebLingoBytecodeWriter WriteClassesAndUnits(IReadOnlyList<WebLingoClassInfo> classes, IReadOnlyList<WebLingoUnitInfo> units)
    {
        var w = new WebLingoBytecodeWriter();
        w.Count(classes.Count);
        foreach (var info in classes)
        {
            w.Text(info.Name);
            w.Signed(info.Constructor);
            WriteMethods(w, info.Methods);
            WriteMethods(w, info.StaticMethods);
            WriteNames(w, info.StaticFields);
            w.Byte(info.DefaultConstructor ? 1 : 0);
        }
        w.Count(units.Count);
        foreach (var unit in units)
        {
            w.Text(unit.Name);
            w.Signed(unit.Function);
            WriteNames(w, unit.Exports);
            WriteParameters(w, unit.Parameters);
            w.Byte(unit.IsNative ? 1 : 0);
        }
        return w;
    }

    private static void WriteMethods(WebLingoBytecodeWriter w, IReadOnlyList<WebLingoClassMethod> methods)
    {
        w.Count(methods.Count);
        foreach (var method in methods)
        {
            w.Text(method.Name);
            w.Signed(method.Function);
        }
    }

    private static void WriteNames(WebLingoBytecodeWriter w, IReadOnlyList<string> names)
    {
        w.Count(names.Count);
        foreach (var name in names) w.Text(name);
    }

    /// <summary>
    /// Each function's spans and its locals' names, then, for each file its spans are in whose text
    /// <paramref name="textOf"/> gives, the program's own first, the line and column of each position a span starts at.
    /// </summary>
    private static WebLingoBytecodeWriter WritePositions(List<FunctionHead> functions, Func<string?, WebLingoSourceText?>? textOf)
    {
        var w = new WebLingoBytecodeWriter();
        var own = new HashSet<int>();
        var files = new List<string>();
        var starts = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (var f in functions)
        {
            w.Byte((f.Spans is not null ? HasSpansFlag : 0) | (f.LocalNames is not null ? HasLocalNamesFlag : 0));
            if (f.Spans is not null)
            {
                w.Count(f.Spans.Count);
                var set = own;
                if (f.Source is { } source && !starts.TryGetValue(source, out set))
                {
                    set = [];
                    starts.Add(source, set);
                    files.Add(source);
                }
                foreach (var span in f.Spans)
                {
                    w.Signed(span.Start);
                    w.Signed(span.Length);
                    set!.Add(span.Start);
                }
                // The positions the debugging section names, where it is written, so that each has its own line and column.
                if (f.DebugInfo is { } info)
                {
                    foreach (var point in info.Points.Values) set!.Add(point.Start);
                    foreach (var local in info.Locals)
                    {
                        set!.Add(local.VisibleFrom);
                        if (local.VisibleUntil != int.MaxValue) set.Add(local.VisibleUntil);
                    }
                }
            }
            if (f.LocalNames is not null) WriteNames(w, f.LocalNames);
        }
        var tables = new List<(string? Source, WebLingoSourceText Text, HashSet<int> Starts)>();
        if (textOf?.Invoke(null) is { } ownText) tables.Add((null, ownText, own));
        foreach (var file in files)
        {
            if (textOf?.Invoke(file) is { } text) tables.Add((file, text, starts[file]));
        }
        w.Count(tables.Count);
        foreach (var (source, text, set) in tables)
        {
            w.Boolean(source is not null);
            if (source is not null) w.Text(source);
            var offsets = new List<int>(set);
            offsets.Sort();
            w.Count(offsets.Count);
            foreach (var offset in offsets)
            {
                var location = text.GetLocation(offset);
                w.Signed(offset);
                w.Signed(location.Line);
                w.Signed(location.Column);
            }
        }
        return w;
    }

    /// <summary>
    /// The debugging section: for each function, whether it has the debugger's metadata, and if so its statements' first
    /// instructions with their spans, in order, its variables, each with its name, its local, the span of its source in which
    /// it is visible, and the export it reads for a named import, and its captures' names. Its file is not written: a
    /// function's is its own <see cref="FunctionHead.Source"/>, or the module's, which the reader gives it.
    /// </summary>
    private static WebLingoBytecodeWriter WriteDebugging(List<FunctionHead> functions)
    {
        var w = new WebLingoBytecodeWriter();
        foreach (var f in functions)
        {
            w.Boolean(f.DebugInfo is not null);
            if (f.DebugInfo is not { } info) continue;
            var points = new List<KeyValuePair<int, WebLingoSourceSpan>>(info.Points);
            points.Sort((x, y) => x.Key.CompareTo(y.Key));
            w.Count(points.Count);
            foreach (var (instruction, span) in points)
            {
                w.Signed(instruction);
                w.Signed(span.Start);
                w.Signed(span.Length);
            }
            w.Count(info.Locals.Count);
            foreach (var local in info.Locals)
            {
                w.Text(local.Name);
                w.Signed(local.Register);
                w.Signed(local.VisibleFrom);
                w.Signed(local.VisibleUntil);
                w.Boolean(local.ExportName is not null);
                if (local.ExportName is not null) w.Text(local.ExportName);
            }
            WriteNames(w, info.Captures);
        }
        return w;
    }

    /// <summary>Why a file's header is wrong, or null: not WLBC, a version this reader does not know, an unknown VM or flags.</summary>
    private static string? CheckHeader(byte[] file)
    {
        if (file.Length < HeaderSize || file[0] != Magic[0] || file[1] != Magic[1] || file[2] != Magic[2] || file[3] != Magic[3])
        {
            return "The file is not WLBC: it does not start with 'WLBC'.";
        }
        var (major, minor) = (Fixed(file, 4, 2), Fixed(file, 6, 2));
        if (major != MajorVersion || minor != MinorVersion)
        {
            return $"The file is WLBC {major}.{minor}, which this reader does not know: it reads WLBC {MajorVersion}.{MinorVersion}.";
        }
        if (Fixed(file, 8, 2) is not ((ulong)WebLingoBytecodeKind.Register or (ulong)WebLingoBytecodeKind.Stack))
        {
            return $"The file is damaged: its header names VM {Fixed(file, 8, 2)}, which is neither 1, the register VM, nor 2, the stack VM.";
        }
        if (Fixed(file, 10, 2) != 0) return "The file is damaged: its header has an unknown flag.";
        return null;
    }

    private static ulong Fixed(byte[] file, int at, int size)
    {
        ulong value = 0;
        for (var i = 0; i < size; i++) value |= (ulong)file[at + i] << (8 * i);
        return value;
    }

    /// <summary>
    /// Checks a file's header and directory, each section's bounds and CRC-32, and reads every section but the code and the
    /// handlers, which <paramref name="parts"/> gives readers of, with what the others hold.
    /// </summary>
    private static bool TryOpen(byte[] file, WebLingoBytecodeKind kind,
        out (ModuleHead Head, List<FunctionHead> Functions, WebLingoBytecodeReader Code, WebLingoBytecodeReader Handlers) parts,
        out IReadOnlyList<WebLingoSourcePositions> positions, out string? error)
    {
        parts = default;
        positions = [];
        if (!TryReadKind(file, out var found, out error)) return false;
        if (found != kind)
        {
            error = found == WebLingoBytecodeKind.Stack
                ? "The file holds the stack VM's bytecode, which the register VM does not run."
                : "The file holds the register VM's bytecode, which the stack VM does not run.";
            return false;
        }
        var (count, directory, length) = (Fixed(file, 12, 4), Fixed(file, 16, 8), Fixed(file, 24, 8));
        if (length != (ulong)file.Length)
        {
            error = $"The file is damaged: its header says it is {length} bytes long, and it is {file.Length}.";
            return false;
        }
        if (count is not (SectionCount - 1 or SectionCount or DebuggingSection) || directory < HeaderSize || directory > length
            || count * EntrySize > length - directory)
        {
            error = "The file is damaged: its directory is not where its header says, or does not have six, seven or eight sections.";
            return false;
        }
        var bounds = new (int Start, int End)[DebuggingSection + 1];
        for (var i = 0; i < (int)count; i++)
        {
            var at = (int)directory + i * EntrySize;
            var (section, offset, size) = (Fixed(file, at, 4), Fixed(file, at + 8, 8), Fixed(file, at + 16, 8));
            if (section != (ulong)(i + 1) || Fixed(file, at + 4, 4) != 0 || Fixed(file, at + 28, 4) != 0)
            {
                error = $"The file is damaged: its directory's entry {i + 1} is not section {i + 1}'s.";
                return false;
            }
            if (offset > length || size > length - offset)
            {
                error = $"The file is damaged: its {SectionNames[i + 1]} section is outside the file.";
                return false;
            }
            if (WebLingoCrc32.Compute(file, (int)offset, (int)size) != (uint)Fixed(file, at + 24, 4))
            {
                error = $"The file is damaged: its {SectionNames[i + 1]} section does not match its CRC-32.";
                return false;
            }
            bounds[i + 1] = ((int)offset, (int)(offset + size));
        }

        WebLingoBytecodeReader Reader(int section) =>
            new(file, bounds[section].Start, bounds[section].End, SectionNames[section]);

        var moduleReader = Reader(1);
        var constantsReader = Reader(2);
        var functionsReader = Reader(3);
        var classesReader = Reader(5);
        var head = ReadModule(moduleReader, ReadConstants(constantsReader), ReadClasses(classesReader), ReadUnits(classesReader));
        var functions = ReadFunctions(functionsReader);
        var positionsReader = count >= SectionCount ? Reader(PositionsSection) : null;
        if (positionsReader is not null) positions = ReadPositions(positionsReader, functions);
        var debuggingReader = count == DebuggingSection ? Reader(DebuggingSection) : null;
        if (debuggingReader is not null)
        {
            var source = head.IsImport && head.Name.StartsWith("import ", StringComparison.Ordinal) ? head.Name[7..] : head.Name;
            ReadDebugging(debuggingReader, functions, kind, source);
        }
        error = Ended(moduleReader, constantsReader, functionsReader, classesReader)
                ?? (positionsReader is null ? null : Ended(positionsReader))
                ?? (debuggingReader is null ? null : Ended(debuggingReader));
        if (error is not null) return false;
        if (functions.Count == 0)
        {
            error = "The file is damaged: its functions section has no entry point.";
            return false;
        }
        parts = (head, functions, Reader(4), Reader(6));
        return true;
    }

    /// <summary>The first section's error, each having been read to its end; null if every one was.</summary>
    private static string? Ended(params WebLingoBytecodeReader[] readers)
    {
        foreach (var reader in readers)
        {
            reader.End();
            if (reader.Error is { } error) return error;
        }
        return null;
    }

    private static ModuleHead ReadModule(WebLingoBytecodeReader r, IReadOnlyList<WebLingoConstant> constants,
        IReadOnlyList<WebLingoClassInfo> classes, IReadOnlyList<WebLingoUnitInfo> units)
    {
        var name = r.Text();
        var isImport = r.Flags(1) == 1;
        var (errorClass, objectClass) = (r.Int(), r.Int());
        var valueClasses = new int[r.Count()];
        for (var i = 0; i < valueClasses.Length; i++) valueClasses[i] = r.Int();
        var errorClasses = new WebLingoErrorClass[r.Count()];
        for (var i = 0; i < errorClasses.Length; i++) errorClasses[i] = new WebLingoErrorClass(r.Text(), r.Int());
        var parameters = ReadParameters(r);
        var imports = new WebLingoImport[r.Count()];
        for (var i = 0; i < imports.Length; i++) imports[i] = new WebLingoImport(r.Text(), r.Int());
        return new ModuleHead(name, isImport, errorClass, objectClass, valueClasses, errorClasses, parameters, imports, constants, classes,
            units);
    }

    private static WebLingoModuleParameter[] ReadParameters(WebLingoBytecodeReader r)
    {
        var parameters = new WebLingoModuleParameter[r.Count()];
        for (var i = 0; i < parameters.Length; i++) parameters[i] = new WebLingoModuleParameter(r.Text(), r.Boolean());
        return parameters;
    }

    private static WebLingoConstant[] ReadConstants(WebLingoBytecodeReader r)
    {
        var constants = new WebLingoConstant[r.Count()];
        for (var i = 0; i < constants.Length; i++)
        {
            constants[i] = r.Byte() switch
            {
                (int)WebLingoConstantKind.Int64 => WebLingoConstant.Int64(r.Signed()),
                (int)WebLingoConstantKind.Binary64 => new WebLingoConstant(WebLingoConstantKind.Binary64, r.Fixed64()),
                (int)WebLingoConstantKind.String => WebLingoConstant.String(r.Text()),
                (int)WebLingoConstantKind.Decimal => WebLingoConstant.Decimal(r.Text()),
                _ => Unknown(r, "has a constant of an unknown kind")
            };
        }
        return constants;
    }

    private static WebLingoConstant Unknown(WebLingoBytecodeReader r, string why)
    {
        r.Fail(why);
        return WebLingoConstant.Int64(0);
    }

    private static List<FunctionHead> ReadFunctions(WebLingoBytecodeReader r)
    {
        var functions = new List<FunctionHead>();
        for (var count = r.Count(); count > 0 && !r.Failed; count--)
        {
            var name = r.Text();
            var (first, second, parameters, optional) = (r.Int(), r.Int(), r.Int(), r.Int());
            var flags = r.Flags(HasThisFlag | IsAsyncFlag | IsNativeFlag | HasSourceFlag | HasRestFlag);
            var source = (flags & HasSourceFlag) != 0 ? r.Text() : null;
            var native = r.Int();
            var result = r.Byte();
            if (result > (int)WebLingoNativeResult.Nothing) r.Fail("has a native call's result of an unknown kind");
            var captures = new WebLingoCapture[r.Count()];
            for (var i = 0; i < captures.Length; i++) captures[i] = new WebLingoCapture(r.Boolean(), r.Int());
            functions.Add(new FunctionHead(name, first, second, parameters, optional, (flags & HasThisFlag) != 0, (flags & IsAsyncFlag) != 0,
                (flags & IsNativeFlag) != 0, source, native, (WebLingoNativeResult)result, captures, r.Number())
            {
                HasRest = (flags & HasRestFlag) != 0
            });
        }
        return functions;
    }

    private static WebLingoClassInfo[] ReadClasses(WebLingoBytecodeReader r)
    {
        var classes = new WebLingoClassInfo[r.Count()];
        for (var i = 0; i < classes.Length; i++)
        {
            var (name, constructor, methods) = (r.Text(), r.Int(), ReadMethods(r));
            classes[i] = new WebLingoClassInfo(name, constructor, methods)
            {
                StaticMethods = ReadMethods(r),
                StaticFields = ReadNames(r),
                DefaultConstructor = r.Flags(1) == 1
            };
        }
        return classes;
    }

    private static WebLingoUnitInfo[] ReadUnits(WebLingoBytecodeReader r)
    {
        var units = new WebLingoUnitInfo[r.Count()];
        for (var i = 0; i < units.Length; i++)
        {
            var (name, function, exports) = (r.Text(), r.Int(), ReadNames(r));
            units[i] = new WebLingoUnitInfo(name, function, exports) { Parameters = ReadParameters(r), IsNative = r.Flags(1) == 1 };
        }
        return units;
    }

    private static WebLingoClassMethod[] ReadMethods(WebLingoBytecodeReader r)
    {
        var methods = new WebLingoClassMethod[r.Count()];
        for (var i = 0; i < methods.Length; i++) methods[i] = new WebLingoClassMethod(r.Text(), r.Int());
        return methods;
    }

    private static string[] ReadNames(WebLingoBytecodeReader r)
    {
        var names = new string[r.Count()];
        for (var i = 0; i < names.Length; i++) names[i] = r.Text();
        return names;
    }

    /// <summary>
    /// Gives each function the debugger's metadata the debugging section has for it, its file its own or the module's,
    /// <paramref name="source"/>, each instruction, local and capture checked against the function.
    /// </summary>
    private static void ReadDebugging(WebLingoBytecodeReader r, List<FunctionHead> functions, WebLingoBytecodeKind kind, string source)
    {
        foreach (var f in functions)
        {
            if (r.Failed) return;
            if (!r.Boolean()) continue;
            var locals = kind == WebLingoBytecodeKind.Register ? f.Second : f.First;
            var points = new Dictionary<int, WebLingoSourceSpan>();
            var previous = -1;
            for (var count = r.Count(); count > 0 && !r.Failed; count--)
            {
                var (instruction, start, length) = (r.Int(), r.Int(), r.Int());
                if (instruction <= previous || instruction >= f.InstructionCount)
                    r.Fail("has a statement's instruction outside its function, or out of order");
                if (start < 0 || length < 0) r.Fail("has a statement's span that is negative");
                points[instruction] = new WebLingoSourceSpan(Math.Max(0, start), Math.Max(0, length));
                previous = instruction;
            }
            var variables = new List<WebLingoDebugLocal>();
            for (var count = r.Count(); count > 0 && !r.Failed; count--)
            {
                var (name, register, from, until) = (r.Text(), r.Int(), r.Int(), r.Int());
                var export = r.Boolean() ? r.Text() : null;
                if (register < 0 || register >= locals) r.Fail("has a variable in a local its function does not have");
                if (from < 0 || until < from) r.Fail("has a variable visible in a span that ends before it starts");
                variables.Add(new WebLingoDebugLocal(name, register, from, until) { ExportName = export });
            }
            var captures = ReadNames(r);
            if (captures.Length != 0 && captures.Length != f.Captures.Count) r.Fail("names captures its function does not have");
            f.DebugInfo = new WebLingoFunctionDebugInfo(f.Source ?? source, points, variables, captures);
        }
    }

    /// <summary>Gives each function its spans and its locals' names, and reads the files' line and column tables.</summary>
    private static WebLingoSourcePositions[] ReadPositions(WebLingoBytecodeReader r, List<FunctionHead> functions)
    {
        foreach (var f in functions)
        {
            var flags = r.Flags(HasSpansFlag | HasLocalNamesFlag);
            if ((flags & HasSpansFlag) != 0)
            {
                var spans = new WebLingoSourceSpan[r.Count()];
                for (var i = 0; i < spans.Length; i++) spans[i] = new WebLingoSourceSpan(r.Int(), r.Int());
                f.Spans = spans;
            }
            if ((flags & HasLocalNamesFlag) != 0) f.LocalNames = ReadNames(r);
        }
        var tables = new WebLingoSourcePositions[r.Count()];
        for (var t = 0; t < tables.Length; t++)
        {
            var source = r.Boolean() ? r.Text() : null;
            var locations = new WebLingoSourceLocation[r.Count()];
            for (var i = 0; i < locations.Length; i++)
            {
                locations[i] = new WebLingoSourceLocation(r.Int(), r.Int(), r.Int());
                if (i > 0 && locations[i].Offset <= locations[i - 1].Offset) r.Fail("has positions out of order");
            }
            tables[t] = new WebLingoSourcePositions(source, locations);
        }
        return tables;
    }
}
