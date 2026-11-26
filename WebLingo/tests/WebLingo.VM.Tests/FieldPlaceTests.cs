using WebLingo.Bytecode;
using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM;
using Xunit;
namespace WebLingo.VM.Tests;

/// <summary>
/// A class's fields in places (specification, section 9, "M0.21 parts", part 2): <c>this.x</c> in a class's own methods
/// read and assigned at its place, which gives what reading and assigning it by its name gives, an instance's fields and
/// members found as before.
/// </summary>
public sealed class FieldPlaceTests
{
    [Fact]
    public void AClassesFieldsHavePlacesItsBasesFirst()
    {
        var module = Compile("""
            import { writeLn } from "std:system";
            class A { constructor() { this.a = 1; } }
            class B extends A {
                constructor() { super(); this.b = 2; }
                more() { this.c = 3; return this.a + this.b + this.c; }
                m() { return 0; }
                property p { get { return this.b; } }
                static var s = 0;
            }
            writeLn(new B().more());
            """);
        Assert.Equal(["a"], ClassNamed(module, "A").Fields);
        Assert.Equal(["a", "b", "c"], ClassNamed(module, "B").Fields);
        var code = module.Functions.SelectMany(function => function.Code).ToList();
        Assert.Contains(code, instruction => instruction.OpCode == WebLingoOpCode.SetField && instruction.D == 2);
        Assert.Contains(code, instruction => instruction.OpCode == WebLingoOpCode.GetField && instruction.D == 0);
        Assert.Equal(["6"], Run(module).Lines);
    }

    [Fact]
    public void AClassWhoseBaseIsAnotherValueHasItsBasesPlaces()
    {
        var module = Compile("""
            import { writeLn } from "std:system";
            function made(base) {
                class C extends base { constructor() { this.c = 1; } read() { return this.c; } }
                return new C().read();
            }
            writeLn(made(new Class("Made")));
            """);
        Assert.Empty(ClassNamed(module, "C").Fields);
        Assert.Equal(["1"], Run(module).Lines);
    }

    /// <summary>
    /// Each program twice, <c>this</c> as it is, its fields read and assigned at their places, and <c>this</c> held in a
    /// variable, read and assigned by their names: the same output and the same error, its code and message.
    /// </summary>
    [Theory]
    [InlineData("class P { constructor() { this.x = 1; this.y = 2; } sum() { return this.x + this.y; } } writeLn(new P().sum());")]
    [InlineData("class P { constructor() { this.n = 1; } bump() { this.n += 2; this.n++; ++this.n; return this.n; } } writeLn(new P().bump());")]
    [InlineData("class P { constructor() { this.x = 1; this.y = [2]; } } writeLn(new P());")]
    [InlineData("class P { constructor(late) { this.a = 1; if (late) this.b = 2; this.c = 3; } } writeLn(new P(false)); writeLn(new P(true));")]
    [InlineData("class P { read() { return this.never; } } writeLn(new P().read());")]
    [InlineData("class P { static var count = 0; bump() { this.count = 5; return this.count; } } writeLn(new P().bump()); writeLn(P.count);")]
    [InlineData("class P { property v { get { return 1; } set(x) { writeLn(x); } } m() { this.v = 7; return this.v; } } writeLn(new P().m());")]
    [InlineData("class P { property v { get { return 1; } } m() { this.v = 7; } } new P().m();")]
    [InlineData("class P { constructor() { this.x = 1; } again() { this.x = 2; return this.x; } } var p = new P(); P.setMethod(\"x\", (this) => 0); writeLn(p.again());")]
    [InlineData("class P { constructor() { this.x = 1; } again() { this.x = 2; return this.x; } } var p = new P(); extend P { x() { return 0; } } writeLn(p.again());")]
    [InlineData("class P { constructor() { this.x = 1; } again() { this.x = 2; return this.x; } } var p = new P(); P.setProperty(\"x\", (this) => 9, null); writeLn(p.again());")]
    [InlineData("class A { constructor() { this.x = 1; } } class B extends A { x() { return 0; } } writeLn(new B());")]
    [InlineData("class A { constructor() { this.x = 1; } get() { return this.x; } } class B extends A { constructor() { super(); this.y = 2; } } var b = new B(); writeLn(b.get()); writeLn(b);")]
    [InlineData("class S extends Array { constructor() { this.top = 0; } push2(v) { this.push(v); this.top = v; return this.top; } } writeLn(new S().push2(4));")]
    [InlineData("class P { constructor() { this.x = 1; } } extend P { bump() { this.x = this.x + 1; this.z = 9; return this.x + this.z; } } writeLn(new P().bump());")]
    [InlineData("class P { constructor() { this.x = 1; } } var p = new P(); p.x = 5; p.w = 6; writeLn(p.x + p.w); writeLn(p);")]
    [InlineData("class P { constructor() { this.f = (a) => a + 1; } call() { return this.f(1); } } writeLn(new P().call());")]
    [InlineData("class P { constructor() { this.x = 1; } m() { var g = () => this.x + 1; this.x = 5; return g(); } } writeLn(new P().m());")]
    [InlineData("class E extends Error { constructor(m) { super(m); this.extra = 1; } } try { throw new E(\"no\"); } catch (E e) { writeLn(e.message); writeLn(e.extra); }")]
    public void APlaceGivesWhatTheNameGives(string source)
    {
        var placed = Run(Compile("import { writeLn } from \"std:system\";\n" + source));
        var named = Run(Compile("import { writeLn } from \"std:system\";\n" + Named(source)));
        Assert.Equal(named.Lines, placed.Lines);
        Assert.Equal(named.Result.Succeeded, placed.Result.Succeeded);
        Assert.Equal(named.Result.Error?.Code, placed.Result.Error?.Code);
        Assert.Equal(named.Result.Error?.Message, placed.Result.Error?.Message);
    }

    /// <summary>
    /// The program with each <c>this.</c> read through a function, <c>self(this).</c>, so that its fields are read and
    /// assigned by their names, as any instance's are.
    /// </summary>
    private static string Named(string source) =>
        "function self(x) { return x; }\n" + source.Replace("this.", "self(this).", StringComparison.Ordinal);

    private static WebLingoClassInfo ClassNamed(WebLingoCompiledModule module, string name) =>
        module.Classes.Single(info => info.Name == name);

    private static readonly WebLingoCompilerOptions WithWrite =
        new([new WebLingoBuiltinDeclaration("write", 1), new WebLingoBuiltinDeclaration("writeLn", 1)]);

    private static WebLingoCompiledModule Compile(string source)
    {
        var compiled = new WebLingoCompiler(WithWrite).Compile(source, "test.wlg");
        Assert.Empty(compiled.Diagnostics);
        return compiled.Module!;
    }

    private static (WebLingoExecutionResult Result, IReadOnlyList<string> Lines) Run(WebLingoCompiledModule module)
    {
        var output = new WebLingoCollectedOutput();
        var context = new WebLingoExecutionContext(new WebLingoWorkerId(0))
        {
            Services = new WebLingoBuiltinRegistry(WebLingoStandardBuiltins.Create(output))
        };
        return (new WebLingoVirtualMachine().Execute(module, context), output.Lines);
    }
}
