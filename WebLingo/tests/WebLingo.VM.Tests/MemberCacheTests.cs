using WebLingo.Bytecode;
using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM;
using Xunit;
namespace WebLingo.VM.Tests;

/// <summary>
/// A member's cache (specification, section 9, "M0.21 parts", part 3): what each instruction that reads, assigns or calls
/// a member found last, remembered by its class's definition, which every execution of the module shares, gives what
/// finding it by its name gives, a class changed since, another class at the same instruction, and a field that hides a
/// method among it.
/// </summary>
public sealed class MemberCacheTests
{
    [Theory]
    // A method replaced, by setMethod and by an extension a function runs, after the call remembered it.
    [InlineData("class P { m() { return 1; } } var p = new P(); var s = 0; for (var i = 0; i < 3; i++) s += p.m(); "
        + "P.setMethod(\"m\", (this) => 10); for (var i = 0; i < 3; i++) s += p.m(); writeLn(s);", "33")]
    // A subclass's method removed: its base's then.
    [InlineData("class A { m() { return 1; } } class B extends A { m() { return 2; } } var b = new B(); var s = 0; "
        + "for (var i = 0; i < 3; i++) s += b.m(); B.removeMethod(\"m\"); for (var i = 0; i < 3; i++) s += b.m(); writeLn(s);", "9")]
    // A base's method replaced, which its subclass's instances have.
    [InlineData("class A { m() { return 1; } } class B extends A { } var b = new B(); var s = 0; "
        + "for (var i = 0; i < 3; i++) s += b.m(); A.setMethod(\"m\", (this) => 5); for (var i = 0; i < 3; i++) s += b.m(); writeLn(s);", "18")]
    // Two classes, and an instance without a class's places, at one instruction, in turn.
    [InlineData("class A { constructor() { this.x = 1; } m() { return this.x; } } class B { constructor() { this.y = 0; this.x = 2; } m() { return 20; } } "
        + "function at(o) { return o.m() + o.x; } var o = new Object(); o.x = 3; o.m = () => 30; var s = 0; "
        + "for (var i = 0; i < 4; i++) { s += at(new A()); s += at(new B()); s += at(o); } writeLn(s);", "228")]
    // A field read and assigned by its name from outside its class, at its place, then hidden by a property's setter given later.
    [InlineData("class P { constructor() { this.x = 1; } } var p = new P(); var s = 0; for (var i = 0; i < 3; i++) { p.x = p.x + 1; s += p.x; } "
        + "writeLn(s); P.setProperty(\"x\", (this) => 99, null); writeLn(p.x); p.x = 5;", "9\n4", "WL3001")]
    // A field missing at its place, never assigned, read after the read remembered another instance's.
    [InlineData("class P { constructor(set) { if (set) this.x = 1; } } function at(o) { return o.x; } writeLn(at(new P(true))); writeLn(at(new P(false)));",
        "1", "WL3006")]
    // A class made from one definition twice, each with its own method, in a function that makes it.
    [InlineData("function made(n) { class P { m() { return n; } } return new P(); } var s = 0; "
        + "for (var i = 1; i <= 4; i++) { var p = made(i); s += p.m() + p.m(); } writeLn(s);", "20")]
    public void ACachedMemberIsWhatItsNameGives(string source, string output, string? code = null)
    {
        var (result, lines) = Run(Compile("import { writeLn } from \"std:system\";\n" + source));
        Assert.Equal(output.Split('\n'), lines);
        Assert.Equal(code, result.Error?.Code);
    }

    /// <summary>
    /// One module run twice, as a site runs its page for each request: its methods and fields remembered in the first
    /// execution, by the class's definition, found again in the second's class, made anew, and not found where the second
    /// changed its class before calling.
    /// </summary>
    [Fact]
    public void ACacheIsSharedByTheModulesExecutions()
    {
        var module = Compile("""
            param change = "no";
            import { writeLn } from "std:system";
            var base = 10;
            class P { constructor() { this.x = base; } m() { return this.x + base; } }
            if (change == "yes") P.setMethod("m", (this) => 0);
            var p = new P();
            var s = 0;
            for (var i = 0; i < 3; i++) s += p.m() + p.x;
            writeLn(s);
            """);
        Assert.Equal(["90"], Run(module).Lines);
        var sites = module.EntryPoint.MemberSites;
        Assert.Contains(Enumerable.Range(0, module.EntryPoint.Code.Count), i => sites[i] is not null);
        Assert.Equal(["90"], Run(module).Lines);
        Assert.Equal(["30"], Run(module, new Dictionary<string, string> { ["change"] = "yes" }).Lines);
        Assert.Equal(["90"], Run(module).Lines);
    }

    private static readonly WebLingoCompilerOptions WithWrite =
        new([new WebLingoBuiltinDeclaration("write", 1), new WebLingoBuiltinDeclaration("writeLn", 1)]);

    private static WebLingoCompiledModule Compile(string source)
    {
        var compiled = new WebLingoCompiler(WithWrite).Compile(source, "test.wlg");
        Assert.Empty(compiled.Diagnostics);
        return compiled.Module!;
    }

    private static (WebLingoExecutionResult Result, IReadOnlyList<string> Lines) Run(WebLingoCompiledModule module,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        var output = new WebLingoCollectedOutput();
        var context = new WebLingoExecutionContext(new WebLingoWorkerId(0))
        {
            Services = new WebLingoBuiltinRegistry(WebLingoStandardBuiltins.Create(output)),
            Parameters = parameters ?? new Dictionary<string, string>()
        };
        return (new WebLingoVirtualMachine().Execute(module, context), output.Lines);
    }
}
