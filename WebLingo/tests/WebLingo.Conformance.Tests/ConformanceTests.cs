using WebLingo.Bytecode;
using WebLingo.Cli;
using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// The specification's initial conformance tests (section 8.6), end to end: each program is written to a file and
/// run through the <c>weblingo</c> command line, as a user would, with its output, its diagnostics and its exit code
/// checked.
/// </summary>
public sealed class ConformanceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-conformance-" + Guid.NewGuid().ToString("N"));

    public ConformanceTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("var x = 10;")]
    [InlineData("const x = 10;")]
    [InlineData("var x = 10 + 20;")]
    [InlineData("var x = 10 / 2;")]
    public void ValidProgramsCompileAndRun(string source)
    {
        Assert.Equal((WebLingoCommandLine.Success, "", ""), Weblingo("check", source));
        Assert.Equal((WebLingoCommandLine.Success, "", ""), Weblingo("run", source));
    }

    [Theory]
    [InlineData("const x;", "WL1006", 1, 7)]
    [InlineData("var x;", "WL1006", 1, 5)]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(x); var x = 1;", "WL2001", 1, 48)]
    [InlineData("var x = 10", "WL1002", 1, 11)]
    [InlineData("var x = 1; var x = 2;", "WL2002", 1, 16)]
    [InlineData("x = 10;", "WL2001", 1, 1)]
    [InlineData("import { writeLn } from \"std:console\"; { var y = 1; } writeLn(y);", "WL2001", 1, 63)]
    [InlineData("{ var y = 1; var y = 2; }", "WL2002", 1, 18)]
    [InlineData("import { writeLn } from \"std:console\"; if (true) var y = 1; writeLn(y);", "WL2001", 1, 69)]
    [InlineData("import { writeLn } from \"std:console\"; if true) writeLn(1);", "WL1001", 1, 43)]
    [InlineData("import { writeLn } from \"std:console\"; else writeLn(1);", "WL1001", 1, 40)]
    [InlineData("break;", "WL2007", 1, 1)]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"a\\qb\");", "WL1009", 1, 50)]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"open);", "WL1010", 1, 48)]
    [InlineData("if (true) { continue; }", "WL2007", 1, 13)]
    [InlineData("import { writeLn } from \"std:console\"; for (var i = 0; i < 1; i = i + 1) {} writeLn(i);", "WL2001", 1, 85)]
    [InlineData("import { writeLn } from \"std:console\"; do writeLn(1) while (true);", "WL1002", 1, 53)]
    [InlineData("return;", "WL2008", 1, 1)]
    [InlineData("function f(a) {} f(1, 2);", "WL2006", 1, 18)]
    [InlineData("var m = {}; m?.a = 1;", "WL1007", 1, 13)]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(this);", "WL2009", 1, 48)]
    [InlineData("var f = function () { return this; };", "WL2009", 1, 30)]
    [InlineData("class A { m() {} m() {} }", "WL2002", 1, 18)]
    [InlineData("class A {} class A {}", "WL2002", 1, 18)]
    [InlineData("class A {} A = 1;", "WL2003", 1, 12)]
    [InlineData("class A {} var a = A();", "WL2005", 1, 20)]
    [InlineData("var a = new A;", "WL1001", 1, 14)]
    [InlineData("class { }", "WL1001", 1, 7)]
    [InlineData("class A extends B {} class B extends A {}", "WL2013", 1, 1)]
    [InlineData("Object = 1;", "WL2003", 1, 1)]
    [InlineData("throw;", "WL2010", 1, 1)]
    [InlineData("function f() { try {} finally { return; } }", "WL2012", 1, 33)]
    [InlineData("while (true) { try {} finally { break; } }", "WL2012", 1, 33)]
    [InlineData("import { writeLn } from \"std:console\"; try {} writeLn(1);", "WL1001", 1, 47)]
    [InlineData("try {} catch (e) {} catch (Error e) {}", "WL1001", 1, 21)]
    [InlineData("try {} catch (Nope e) {}", "WL2001", 1, 15)]
    [InlineData("function f(a = 1, b) {}", "WL1001", 1, 19)]
    [InlineData("function f(a, b = 1) {} f();", "WL2006", 1, 25)]
    [InlineData("function f(a, b = 1) {} f(1, 2, 3);", "WL2006", 1, 25)]
    [InlineData("try { } catch (e) { var e = 1; }", "WL2002", 1, 25)]
    [InlineData("class A {} class B extends A { m() { super(); } }", "WL2009", 1, 38)]
    [InlineData("var X = 5; class A extends X {}", "WL2001", 1, 28)]
    [InlineData("var s = super;", "WL2009", 1, 9)]
    [InlineData("class A { dispose() {} destructor() {} }", "WL2011", 1, 24)]
    [InlineData("class A { destructor() {} function dispose() {} }", "WL2011", 1, 36)]
    [InlineData("import { writeLn } from \"std:console\"; using (var r = 1) {} writeLn(r);", "WL2001", 1, 69)]
    [InlineData("using (var r = r) {}", "WL2001", 1, 16)]
    [InlineData("using (const r = 1) {}", "WL1001", 1, 8)]
    [InlineData("class A { static operator +(a, b) { return this; } }", "WL2009", 1, 44)]
    [InlineData("class A { static m(this) { } }", "WL1001", 1, 20)]
    [InlineData("class A { static var x = this; }", "WL2009", 1, 26)]
    [InlineData("class A {} class B extends A { static m() { return super.m(); } }", "WL2009", 1, 52)]
    [InlineData("class A { static var x = 1; static x() {} }", "WL2002", 1, 36)]
    [InlineData("class A { static var x; }", "WL1006", 1, 22)]
    [InlineData("class A { static constructor() {} }", "WL1001", 1, 18)]
    [InlineData("var v = 1; class A { static var x = v; }", "WL2001", 1, 37)]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"abc\".);", "WL1001", 1, 54)]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"abc\"[0);", "WL1001", 1, 55)]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([1,]);", "WL1001", 1, 51)]
    [InlineData("var a = [1 2];", "WL1001", 1, 12)]
    [InlineData("var m = {1: 2};", "WL1001", 1, 10)]
    [InlineData("var m = {a 1};", "WL1001", 1, 12)]
    [InlineData("var m = {a: 1,};", "WL1001", 1, 15)]
    [InlineData("import { writeLn } from \"std:console\"; var m = {a: {1: 2}, b: 3}; writeLn(1);", "WL1001", 1, 53)]
    [InlineData("import { writeLn } from \"std:console\"; foreach (const x in [1]) writeLn(x);", "WL1001", 1, 49)]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(#{1,});", "WL1001", 1, 52)]
    [InlineData("var m = {}; m?[\"a\"] = 2;", "WL1007", 1, 13)]
    [InlineData("var x = true @ 1;", "WL1003", 1, 14)]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(#[1]);", "WL1003", 1, 48)]
    [InlineData("import { writeLn } from \"std:console\"; foreach (var x of [1]) writeLn(x);", "WL1001", 1, 55)]
    [InlineData("import { writeLn } from \"std:console\"; foreach (var (k, k) in {}) writeLn(k);", "WL2002", 1, 57)]
    [InlineData("import { writeLn } from \"std:console\"; foreach (var x in [1]) {} writeLn(x);", "WL2001", 1, 74)]
    [InlineData("function g() { await 1; }", "WL2014", 1, 16)]
    [InlineData("var f = function () { await 1; };", "WL2014", 1, 23)]
    [InlineData("async function f() { var g = function () { await 1; }; }", "WL2014", 1, 44)]
    [InlineData("class A { async constructor() {} }", "WL1001", 1, 17)]
    [InlineData("var async = 1;", "WL1001", 1, 5)]
    [InlineData("class P { m() { return 1; } } extend P { m() { return super.m(); } }", "WL2009", 1, 55)]
    [InlineData("class P {} extend P { constructor() {} }", "WL1001", 1, 23)]
    [InlineData("class P {} extend P { dispose() {} destructor() {} }", "WL2011", 1, 36)]
    [InlineData("class P {} extend P { m() {} m() {} }", "WL2002", 1, 30)]
    [InlineData("var extend = 1;", "WL1001", 1, 5)]
    [InlineData("extend Q { }", "WL2001", 1, 8)]
    [InlineData("var x = 5; extend x { }", "WL2001", 1, 19)]
    [InlineData("class E { static operator ==(a, b) { return true; } }", "WL1001", 1, 27)]
    [InlineData("class W { static operator +(a) { return 1; } }", "WL1001", 1, 27)]
    [InlineData("class A { property a { } }", "WL1001", 1, 24)]
    [InlineData("class A { property a { set() { } } }", "WL1001", 1, 24)]
    [InlineData("class A { property a { set(x, y) { } } }", "WL1001", 1, 24)]
    [InlineData("class A { property a { set(v) { return v; } } }", "WL1001", 1, 33)]
    [InlineData("class A { property a { get { return 1; } get { return 2; } } }", "WL2002", 1, 20)]
    [InlineData("class A { a() { return 1; } property a { get { return 1; } } }", "WL2002", 1, 38)]
    [InlineData("class A { static var a = 1; static property a { get { return 1; } } }", "WL2002", 1, 45)]
    [InlineData("extend Integer { static property a { get { return field; } } }", "WL2001", 1, 51)]
    [InlineData("function f(a, this) { }", "WL1001", 1, 15)]
    [InlineData("class A { m(this) { } }", "WL1001", 1, 13)]
    [InlineData("function f(this = 1) { }", "WL1001", 1, 17)]
    [InlineData("class B { m() { return 1; } } class A extends B { m() { var f = function (this) { return super.m(); }; return f(this); } }", "WL2009", 1, 90)]
    [InlineData("function f(this, a) { return a; } f(1);", "WL2006", 1, 35)]
    [InlineData("class A { static const x; }", "WL1006", 1, 24)]
    [InlineData("class A { static let x = 1; }", "WL1001", 1, 18)]
    [InlineData("class A { const X; }", "WL1006", 1, 17)]
    [InlineData("class A { const X = 1; const X = 2; }", "WL2002", 1, 30)]
    [InlineData("var f = (1) => { return 2; };", "WL1001", 1, 10)]
    [InlineData("var f = x => ;", "WL1001", 1, 14)]
    [InlineData("var s = \"\"\"\n  a\n\";", "WL1011", 1, 9)]
    [InlineData("var s = $\"{}\";", "WL1001", 1, 12)]
    [InlineData("var s = $\"{1:}\";", "WL1012", 1, 13)]
    [InlineData("var s = $\"{1:N1000}\";", "WL1012", 1, 13)]
    [InlineData("var s = $\"{1,}\";", "WL1001", 1, 14)]
    [InlineData("var s = $\"{1,2,3}\";", "WL1001", 1, 15)]
    [InlineData("var s = $\"{1 2}\";", "WL1001", 1, 14)]
    [InlineData("var s = \"\"\"\n  a\n b\n  \"\"\";", "WL1012", 3, 1)]
    [InlineData("var f = async (a) => ;", "WL1001", 1, 22)]
    [InlineData("var f = (this = 1) => { };", "WL1001", 1, 15)]
    [InlineData("var f = (a, a) => { return a; };", "WL2002", 1, 13)]
    [InlineData("var f = (a = 1, b) => { };", "WL1001", 1, 17)]
    [InlineData("const c = 1; c += 1;", "WL2003", 1, 14)]
    [InlineData("function f() { return 1; } f() += 1;", "WL1007", 1, 28)]
    [InlineData("var o = null; o?.a += 1;", "WL1007", 1, 15)]
    [InlineData("5++;", "WL1007", 1, 1)]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(if (true) 1);", "WL1001", 1, 59)]
    [InlineData("var x = 1 switch { 1 => 2 };", "WL1001", 1, 9)]
    [InlineData("switch (1) { case Point p, 2 { } }", "WL1001", 1, 19)]
    [InlineData("var d = 79228162514264337593543950336m;", "WL1004", 1, 9)]
    [InlineData("var x = 0xFFFFFFFFFFFFFFFF;", "WL1004", 1, 9)]
    [InlineData("var x = 0b2;", "WL1004", 1, 9)]
    [InlineData("var x = 1__000;", "WL1004", 1, 9)]
    [InlineData("var x = 1_000_;", "WL1004", 1, 9)]
    [InlineData("var x = 1_.5;", "WL1004", 1, 9)]
    [InlineData("function f(...rest, a) { }", "WL1001", 1, 15)]
    [InlineData("function f(...rest = []) { }", "WL1001", 1, 20)]
    [InlineData("function f(...) { }", "WL1001", 1, 15)]
    [InlineData("function f(a, ...b) { } f();", "WL2006", 1, 25)]
    [InlineData("var a = [1]; var m = Module.import(...a);", "WL1001", 1, 36)]
    [InlineData("var a = [1]; var b = ...a;", "WL1001", 1, 22)]
    public void CompileErrorsAreReportedAndNothingRuns(string source, string code, int line, int column)
    {
        foreach (var command in new[] { "check", "run" })
        {
            var (exit, output, error) = Weblingo(command, source);
            Assert.Equal(WebLingoCommandLine.CompilationFailed, exit);
            Assert.Equal("", output);
            Assert.Equal($"program.wlg({line},{column}): ERROR {code}:", FirstLineStart(error));
        }
    }

    /// <summary>
    /// An array's sort() or reverse() whose result is discarded is a warning, WL2017, at the method's name, and the program
    /// still runs (the owner's answer, 16 November 2026), as it does for a call of any value's sort() or reverse().
    /// </summary>
    [Theory]
    [InlineData("import { writeLn } from \"std:console\"; var a = [2, 1]; a.sort(); writeLn(a);", 58, "[2, 1]")]
    [InlineData("var a = [2, 1];\na.reverse();", 3, "")]
    public void ADiscardedSortOrReverseIsAWarning(string source, int column, string printed)
    {
        var (exit, output, error) = Weblingo("run", source);
        Assert.Equal((WebLingoCommandLine.Success, printed.Length == 0 ? "" : printed + "\n"), (exit, output));
        var line = source.Contains('\n') ? 2 : 1;
        Assert.StartsWith($"program.wlg({line},{column}): warning WL2017:", error);
        Assert.Equal((WebLingoCommandLine.Success, ""), (Weblingo("check", "var a = [2, 1]; var b = a.sort();").Exit, Weblingo("check", "var a = [2, 1]; var b = a.sort();").Error));
    }

    [Theory]
    [InlineData("import { writeLn } from \"std:console\"; var x = 10 + 20; writeLn(x);", "30")]
    [InlineData("import { writeLn } from \"std:console\"; var x = 10 / 2; writeLn(x);", "5.0")]
    [InlineData("import { writeLn } from \"std:console\"; var x = 1; { var x = 2; writeLn(x); } writeLn(x);", "2\n1")]
    [InlineData("import { writeLn } from \"std:console\"; var x = 1; { x = 3; } writeLn(x);", "3")]
    [InlineData("import { writeLn } from \"std:console\"; { var y = 4; { writeLn(y * y); } }", "16")]
    [InlineData("{} { { } }", "")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(true); writeLn(false); writeLn(null);", "true\nfalse\nnull")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(1 == 1.0); writeLn(1 != 1); writeLn(NaN == NaN); writeLn(NaN != NaN);", "true\nfalse\nfalse\ntrue")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(null == null); writeLn(null == false); writeLn(0 == false); writeLn(true == true);", "true\nfalse\nfalse\ntrue")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(1 < 2); writeLn(2 <= 2); writeLn(3 > 4); writeLn(4 >= 4.5); writeLn(1 < NaN);", "true\ntrue\nfalse\nfalse\nfalse")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(9007199254740993 == 9007199254740992.0);", "false")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(9007199254740993 > 9007199254740992.0);", "true")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(9223372036854775807 < 9223372036854775808.0);", "true")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(!true); writeLn(!(1 > 2));", "false\ntrue")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(true && false); writeLn(true || false); writeLn(false || false && true);", "false\ntrue\nfalse")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(1 + 2 == 3 && 2 * 2 > 3);", "true")]
    [InlineData("import { writeLn } from \"std:console\"; var hit = 0; writeLn(false && (hit = 1) == 1); writeLn(true || (hit = 2) == 2); writeLn(hit);", "false\ntrue\n0")]
    [InlineData("import { writeLn } from \"std:console\"; var x = true; var y = false; x = y && x; writeLn(x);", "false")]
    [InlineData("import { writeLn } from \"std:console\"; var x = false; x = true || x; writeLn(x);", "true")]
    [InlineData("import { writeLn } from \"std:console\"; if (1 < 2) writeLn(1); else writeLn(2);", "1")]
    [InlineData("import { writeLn } from \"std:console\"; if (1 > 2) writeLn(1); else writeLn(2);", "2")]
    [InlineData("import { writeLn } from \"std:console\"; if (false) writeLn(1); writeLn(3);", "3")]
    [InlineData("import { writeLn } from \"std:console\"; var x = 5; if (x < 3) writeLn(1); else if (x == 5) writeLn(2); else writeLn(3);", "2")]
    [InlineData("import { writeLn } from \"std:console\"; if (true) if (false) writeLn(1); else writeLn(2);", "2")]
    [InlineData("import { writeLn } from \"std:console\"; if (false) if (true) writeLn(1); else writeLn(2); writeLn(3);", "3")]
    [InlineData("import { writeLn } from \"std:console\"; var x = 1; if (true) { var x = 2; writeLn(x); } writeLn(x);", "2\n1")]
    [InlineData("import { writeLn } from \"std:console\"; var x = 1; if (true) var x = 2; writeLn(x);", "1")]
    [InlineData("import { writeLn } from \"std:console\"; var n = 0; if (true) { n = n + 1; } else { n = n + 10; } writeLn(n);", "1")]
    [InlineData("import { write, writeLn } from \"std:console\"; var i = 0; while (i < 3) { write(i); i = i + 1; } writeLn(null);", "012null")]
    [InlineData("import { writeLn } from \"std:console\"; while (false) writeLn(1); writeLn(2);", "2")]
    [InlineData("import { write, writeLn } from \"std:console\"; var i = 0; do { write(i); i = i + 1; } while (i < 3); writeLn(null);", "012null")]
    [InlineData("import { writeLn } from \"std:console\"; do writeLn(1); while (false);", "1")]
    [InlineData("import { write, writeLn } from \"std:console\"; for (var i = 0; i < 3; i = i + 1) write(i); writeLn(null);", "012null")]
    [InlineData("import { writeLn } from \"std:console\"; var i = 10; for (i = 0; i < 2; i = i + 1) {} writeLn(i);", "2")]
    [InlineData("import { writeLn } from \"std:console\"; var n = 0; for (;;) { n = n + 1; if (n == 5) break; } writeLn(n);", "5")]
    [InlineData("import { write, writeLn } from \"std:console\"; for (var i = 0; i < 6; i = i + 1) { if (i == 1 || i == 3) continue; write(i); } writeLn(null);", "0245null")]
    [InlineData("import { writeLn } from \"std:console\"; var i = 0; while (true) { i = i + 1; if (i < 3) continue; break; } writeLn(i);", "3")]
    [InlineData("import { write, writeLn } from \"std:console\"; var i = 0; do { i = i + 1; if (i == 2) continue; write(i); } while (i < 4); writeLn(null);", "134null")]
    [InlineData("import { write, writeLn } from \"std:console\"; for (var i = 0; i < 2; i = i + 1) for (var j = 0; j < 3; j = j + 1) { if (j == 1) break; write(i * 10 + j); } writeLn(null);", "010null")]
    [InlineData("import { writeLn } from \"std:console\"; var s = 0; for (var n = 1; n <= 1000; n = n + 1) s = s + n; writeLn(s);", "500500")]
    [InlineData("import { write, writeLn } from \"std:console\"; var i = 1; for (var i = 5; i < 6; i = i + 1) write(i); writeLn(i);", "51")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"Hello\" + \", \" + 'world');", "Hello, world")]
    [InlineData("import { write } from \"std:console\"; write('a\\n'); write(\"b\\tc\\n\");", "a\nb\tc")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"x\" == 'x'); writeLn(\"x\" != \"y\"); writeLn(\"1\" == 1); writeLn(\"\" == null);", "true\ntrue\nfalse\nfalse")]
    [InlineData("import { writeLn } from \"std:console\"; var s = \"\"; for (var i = 0; i < 3; i = i + 1) s = s + \"ab\"; writeLn(s);", "ababab")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"\\u{1F600}\");", "\U0001F600")]
    [InlineData("import { writeLn } from \"std:console\"; function add(a, b) { return a + b; } writeLn(add(2, 3));", "5")]
    [InlineData("import { writeLn } from \"std:console\"; function f() {} writeLn(f()); function g() { return; } writeLn(g());", "null\nnull")]
    [InlineData("import { writeLn } from \"std:console\"; function fact(n) { if (n <= 1) return 1; return n * fact(n - 1); } writeLn(fact(20));", "2432902008176640000")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(even(10)); function even(n) { if (n == 0) return true; return odd(n - 1); } function odd(n) { if (n == 0) return false; return even(n - 1); }", "true")]
    [InlineData("import { writeLn } from \"std:console\"; var make = function () { var c = 0; return function () { c = c + 1; return c; }; }; var a = make(); a(); var b = make(); writeLn(a()); writeLn(b());", "2\n1")]
    [InlineData("import { writeLn } from \"std:console\"; function times(k) { return function (x) { return x * k; }; } writeLn(times(3)(7));", "21")]
    [InlineData("import { writeLn } from \"std:console\"; var w = writeLn; w(\"through a variable\");", "through a variable")]
    [InlineData("import { writeLn } from \"std:console\"; var total = 0; function bump(by) { total = total + by; } bump(5); bump(6); writeLn(total);", "11")]
    [InlineData("import { writeLn } from \"std:console\"; var f = 0; var g = 0; for (var i = 0; i < 3; i = i + 1) { if (i == 0) f = function () { return i; }; if (i == 2) g = function () { return i; }; } writeLn(f()); writeLn(g());", "0\n2")]
    [InlineData("import { writeLn } from \"std:console\"; var fs = 0; var k = 0; while (k < 2) { var j = k; if (k == 0) fs = function () { return j; }; k = k + 1; } writeLn(fs());", "0")]
    [InlineData("import { writeLn } from \"std:console\"; function f(x) { var g = function () { x = x + 1; return x; }; g(); return g(); } writeLn(f(10));", "12")]
    [InlineData("import { writeLn } from \"std:console\"; function outer() { var v = 1; function mid() { function inner() { return v; } return inner(); } v = 5; return mid(); } writeLn(outer());", "5")]
    [InlineData("import { writeLn } from \"std:console\"; function deep(n) { if (n == 0) return 0; return 1 + deep(n - 1); } writeLn(deep(5000));", "5000")]
    [InlineData("import { writeLn } from \"std:console\"; function f() {} var g = f; writeLn(f == g); writeLn(function () {} == function () {}); writeLn(writeLn == writeLn);", "true\nfalse\ntrue")]
    [InlineData("import { writeLn } from \"std:console\"; function f(a) { var a = 2; return a; } writeLn(f(1));", "2")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"héllo\".length); writeLn(\"a\U0001F600b\".length); writeLn(\"\".length);", "5\n3\n0")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"a\U0001F600b\"[1]); var s = \"abc\"; writeLn(s[0] + s[2]); writeLn(s[s.length - 1]);", "\U0001F600\nac\nc")]
    [InlineData("import { writeLn } from \"std:console\"; var i = 0; var s = \"xyz\"; writeLn(s[i = 2] + s[i]); writeLn((\"a\" + \"b\").length);", "zz\n2")]
    [InlineData("import { writeLn } from \"std:console\"; var s = \"ab\"; writeLn(s[(s = \"cd\").length - 1]); writeLn(s);", "b\ncd")]
    [InlineData("import { writeLn } from \"std:console\"; var a = [1, \"a\\n\\\"q\\\"\", null, 2.5, [true, []]]; writeLn(a); writeLn(a.length); writeLn(a[4][0]);", "[1, \"a\\n\\\"q\\\"\", null, 2.5, [true, []]]\n5\ntrue")]
    [InlineData("import { writeLn } from \"std:console\"; var a = [1, 2]; a[0] = a[0] + 40; a[1] = \"x\"; writeLn(a); writeLn([]); writeLn([].length);", "[41, \"x\"]\n[]\n0")]
    [InlineData("import { writeLn } from \"std:console\"; var a = []; a.push(1); writeLn(a.push(2)); writeLn(a.pop()); writeLn(a.pop()); writeLn(a);", "null\n2\n1\n[]")]
    [InlineData("import { writeLn } from \"std:console\"; var a = [1]; var b = a; b[0] = 9; writeLn(a[0]); writeLn(a == b); writeLn([1] == [1]); writeLn(a != [9]);", "9\ntrue\nfalse\ntrue")]
    [InlineData("import { writeLn } from \"std:console\"; var a = [0]; a.push(a); writeLn(a);", "[0, […]]")]
    [InlineData("import { writeLn } from \"std:console\"; var a = [writeLn]; a[0](\"called\"); var b = [function (x) { return x * 2; }]; writeLn(b[0](21));", "called\n42")]
    [InlineData("import { writeLn } from \"std:console\"; var i = 0; var a = [i = 1, i, i = 2]; writeLn(a); var b = [0, 0]; b[i = 1] = i; writeLn(b);", "[1, 1, 2]\n[0, 1]")]
    [InlineData("import { writeLn } from \"std:console\"; var m = [[1, 2], [3, 4]]; m[1][0] = 30; writeLn(m[0][1] + m[1][0]); writeLn(m);", "32\n[[1, 2], [30, 4]]")]
    [InlineData("import { writeLn } from \"std:console\"; var m = { active: true, \"n a\": 1 }; writeLn(m[\"active\"]); writeLn(m[\"n a\"]); writeLn(m.length);", "true\n1\n2")]
    [InlineData("import { writeLn } from \"std:console\"; var m = {}; m[1] = \"one\"; m[true] = 2; m[null] = 3; m[\"1\"] = 4; writeLn(m.length); writeLn(m[1]); writeLn(m[\"1\"] + m[true]);", "4\none\n6")]
    [InlineData("import { writeLn } from \"std:console\"; var m = {a: 1}; writeLn(m.has(\"a\")); writeLn(m.has(\"b\")); writeLn(m.remove(\"a\")); writeLn(m.remove(\"a\")); writeLn(m);", "true\nfalse\ntrue\nfalse\n{}")]
    [InlineData("import { writeLn } from \"std:console\"; var m = {a: 1, a: 2}; writeLn(m); m[\"a\"] = 3; writeLn(m.length); writeLn(m[\"a\"]);", "{\"a\": 2}\n1\n3")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn({a: {b: [\"c\", {}]}}); writeLn({\"q\\\"\": null});", "{\"a\": {\"b\": [\"c\", {}]}}\n{\"q\\\"\": null}")]
    [InlineData("import { writeLn } from \"std:console\"; var m = {x: 1}; var n = m; n[\"x\"] = 2; writeLn(m[\"x\"]); writeLn(m == n); writeLn({x: 1} == {x: 1});", "2\ntrue\nfalse")]
    [InlineData("import { writeLn } from \"std:console\"; var m = {}; m[\"self\"] = m; writeLn(m); var a = [m]; writeLn(a);", "{\"self\": {…}}\n[{\"self\": {…}}]")]
    [InlineData("import { writeLn } from \"std:console\"; var i = 0; var m = {a: i = 1, b: i}; writeLn(m[\"b\"]); writeLn(i);", "1\n1")]
    [InlineData("import { writeLn } from \"std:console\"; var s = 0; foreach (var x in [1, 2, 3]) s = s + x; writeLn(s);", "6")]
    [InlineData("import { writeLn } from \"std:console\"; class Point { constructor(x, y) { this.x = x; this.y = y; } function sum() { return this.x + this.y; } scaled(k) { return new Point(this.x * k, this.y * k); } } var p = new Point(1, 2); writeLn(p.sum()); writeLn(p); writeLn(p.scaled(10).x);", "3\nPoint {x: 1, y: 2}\n10")]
    // A value's toString() as Object's gives it, at once for the kinds the formatter writes (the specification, section 5.6).
    [InlineData("import { writeLn } from \"std:console\"; var i = -42; var n = 1.5; var z = -0.0; var d = 2.50m; var b = true; var s = \"tea\"; writeLn([i.toString(), n.toString(), z.toString(), NaN.toString(), d.toString(), b.toString(), s.toString(), $\"{i}|{d}|{b}\"]); writeLn([i.toString().length, s.toString() == s]);", "[\"-42\", \"1.5\", \"-0.0\", \"NaN\", \"2.5m\", \"true\", \"tea\", \"-42|2.5m|true\"]\n[3, true]")]
    // A map and a set of many entries, past those they search in order, their entries then found by their positions, and removed, before and after (the specification, section 5.6).
    [InlineData("import { writeLn } from \"std:console\"; var m = {}; var s = #{}; for (var i = 0; i < 12; i++) { m[\"k\" + i.toString()] = i; s.add(i); } m[\"k3\"] = 30; writeLn([m.length, m[\"k3\"], m[\"k11\"], m.has(\"k12\"), m.remove(\"k0\"), m.remove(\"k0\"), m.length, m[\"k11\"]]); var n = 0; foreach (var (k, v) in m) { n += v; } writeLn(n); for (var i = 0; i < 12; i++) s.remove(i * 2); writeLn([s.length, s.has(3), s.has(4), s.add(4), s.length]); var small = { a: 1, b: 2 }; small.remove(\"a\"); small[\"c\"] = 3; writeLn([small.length, small.has(\"a\"), small[\"b\"], small[\"c\"]]);", "[12, 30, 11, false, true, false, 11, 11]\n93\n[6, true, false, true, 7]\n[2, false, 2, 3]")]
    // An instance of many fields, past those it searches in order, its fields then found by their positions (the specification, section 5.6).
    [InlineData("import { writeLn } from \"std:console\"; class W { constructor() { this.a = 1; this.b = 2; this.c = 3; this.d = 4; this.e = 5; this.f = 6; this.g = 7; this.h = 8; this.i = 9; this.j = 10; this.k = 11; this.l = 12; } } var w = new W(); w.a = 100; w.l = w.l + w.i; w.m = 13; writeLn([w.a, w.b, w.h, w.i, w.l, w.m, w.a + w.b + w.c + w.d + w.e + w.f + w.g + w.h + w.i + w.j + w.k]); writeLn(w?.n);", "[100, 2, 8, 9, 21, 13, 165]\nnull")]
    [InlineData("import { writeLn } from \"std:console\"; class Point { constructor(x) { this.x = x; } get() { return this.x; } } var p = new Point(4); var f = p.get; p.x = 5; writeLn(f()); writeLn(p?.z); writeLn(null?.x); writeLn(p?.x);", "5\nnull\nnull\n5")]
    [InlineData("import { writeLn } from \"std:console\"; class E {} var a = new E(); writeLn(a); writeLn(a == a); writeLn(new E() == new E()); writeLn(E); a.self = a; writeLn(a);", "E {}\ntrue\nfalse\nclass E\nE {self: E {…}}")]
    [InlineData("import { writeLn } from \"std:console\"; class C { constructor() { this.n = 0; } next() { var bump = function () { this.n = this.n + 1; return this.n; }; return bump(); } } var c = new C(); c.next(); writeLn(c.next());", "2")]
    [InlineData("import { writeLn } from \"std:console\"; var a = new A(3); writeLn(a.twice()); class A { constructor(v) { this.v = v; } twice() { return this.v * 2; } }", "6")]
    [InlineData("import { writeLn } from \"std:console\"; function make(k) { class K { get() { return k; } } return new K(); } writeLn(make(7).get()); writeLn(make(8).get());", "7\n8")]
    [InlineData("import { writeLn } from \"std:console\"; class F { constructor() { this.w = writeLn; this.g = function (x) { return x + 1; }; } } var f = new F(); f.w(\"field\"); writeLn(f.g(1));", "field\n2")]
    [InlineData("import { writeLn } from \"std:console\"; class R { constructor(n) { this.n = n; return 5; } } writeLn(new R(1).n);", "1")]
    [InlineData("import { writeLn } from \"std:console\"; class N { count(n) { if (n == 0) return 0; return 1 + this.count(n - 1); } } writeLn(new N().count(100));", "100")]
    [InlineData("import { writeLn } from \"std:console\"; class B extends A { constructor(x) { super(x * 2); this.b = 1; } who() { return \"B/\" + super.who(); } } class A { constructor(x) { this.a = x; } who() { return \"A\"; } base() { return this.a; } } var b = new B(5); writeLn(b.who()); writeLn(b.base()); writeLn(b); var f = b.who; writeLn(f());", "B/A\n10\nB {a: 10, b: 1}\nB/A")]
    [InlineData("import { writeLn } from \"std:console\"; class A { constructor() { this.log = \"A\"; } } class B extends A { constructor() { this.log = this.log + \"B\"; } } class C extends B {} writeLn(new C().log);", "AB")]
    [InlineData("import { writeLn } from \"std:console\"; class A { name() { return \"A\"; } hello() { return \"hi \" + this.name(); } } class B extends A { name() { return \"B\"; } } writeLn(new B().hello()); writeLn(new A().hello());", "hi B\nhi A")]
    [InlineData("import { writeLn } from \"std:console\"; class A { m() { return 1; } } class B extends A { m() { var f = super.m; return f() + 1; } } writeLn(new B().m());", "2")]
    [InlineData("import { writeLn } from \"std:console\"; class A { v() { return 7; } } { class B extends A {} writeLn(new B().v()); }", "7")]
    [InlineData("import { writeLn } from \"std:console\"; class A { constructor(x) { this.x = x; } } class B extends A { constructor(x) { super(x + 1); } } class C extends B { constructor() { super(1); } } writeLn(new C().x);", "2")]
    [InlineData("import { writeLn } from \"std:console\"; class A { dispose() { writeLn(\"A\"); } } class B extends A { destructor() { writeLn(\"B\"); } } var b = new B(); b.dispose(); writeLn(b.dispose());", "B\nA\nnull")]
    [InlineData("import { write, writeLn } from \"std:console\"; class R { constructor(n) { this.n = n; } dispose() { write(\"end \"); writeLn(this.n); } } function f() { using (var a = new R(1)) { using (var b = new R(2)) { return \"r\"; } } } writeLn(f()); for (var i = 0; i < 3; i = i + 1) { using (var c = new R(i)) { if (i == 0) continue; if (i == 1) break; } } using (var d = new R(9)) writeLn(\"body\");", "end 2\nend 1\nr\nend 0\nend 1\nbody\nend 9")]
    [InlineData("import { writeLn } from \"std:console\"; class A { dispose() { writeLn(\"d\"); return 5; } } var f = new A().dispose; writeLn(f()); writeLn(f());", "d\nnull\nnull")]
    [InlineData("import { writeLn } from \"std:console\"; class A { dispose() { writeLn(\"d\"); this.dispose(); } } using (var a = new A()) {} using (var n = null) {} writeLn(\"done\");", "d\ndone")]
    [InlineData("import { writeLn } from \"std:console\"; class A { dispose() { writeLn(\"A\"); } } class B extends A { dispose() { super.dispose(); } } class C extends B {} new C().dispose();", "A\nA")]
    [InlineData("import { writeLn } from \"std:console\"; class A { dispose() { writeLn(this.v); } } class B extends A { constructor() { this.v = \"b\"; } } using (var b = new B()) { var g = function () { return b; }; writeLn(g() == b); }", "true\nb")]
    [InlineData("import { writeLn } from \"std:console\"; class Counter { static var count = 0; static next() { Counter.count = Counter.count + 1; return Counter.count; } } Counter.next(); writeLn(Counter.next()); writeLn(Counter.count);", "2\n2")]
    [InlineData("import { writeLn } from \"std:console\"; class A { static var n = 1; static who() { return \"A\"; } } class B extends A {} writeLn(B.who()); B.n = 5; writeLn(A.n); class C extends A { static var n = 9; } writeLn(C.n); writeLn(A.n);", "A\n5\n9\n5")]
    [InlineData("import { writeLn } from \"std:console\"; class P { static make(x) { var p = new P(); p.x = x; return p; } } class Q { static var origin = P.make(0); } writeLn(Q.origin); var f = P.make; writeLn(f(2).x);", "P {x: 0}\n2")]
    [InlineData("import { writeLn } from \"std:console\"; class M { static var twice = function (x) { return x * 2; }; } writeLn(M.twice(4)); writeLn(M?.nope);", "8\nnull")]
    [InlineData("import { writeLn } from \"std:console\"; class A { static var x = B.y; } class B { static var y = 1; } writeLn(A.x); writeLn(B.y);", "null\n1")]
    [InlineData("import { writeLn } from \"std:console\"; function make(k) { class K { static get() { return k; } } return K; } writeLn(make(3).get());", "3")]
    [InlineData("import { writeLn } from \"std:console\"; class A { static v() { return 1; } v() { return 2; } } writeLn(A.v() + new A().v());", "3")]
    [InlineData("import { writeLn } from \"std:console\"; var o = new Object(); writeLn(o); writeLn(o.equal(o)); writeLn(o.equal(new Object()));", "Object {}\ntrue\nfalse")]
    [InlineData("import { writeLn } from \"std:console\"; class P { constructor(x) { this.x = x; } equal(other) { return other.x == this.x; } } var a = new P(1); writeLn(a.equal(new P(1))); writeLn(a == new P(1));", "true\nfalse")]
    [InlineData("import { writeLn } from \"std:console\"; class A {} writeLn(new A().equal(5)); class B { equal(o) { return super.equal(o) || o == 1; } } var b = new B(); writeLn(b.equal(b)); writeLn(b.equal(1)); writeLn(b.equal(2));", "false\ntrue\ntrue\nfalse")]
    [InlineData("import { writeLn } from \"std:console\"; class Object { hi() { return 1; } } class A {} writeLn(new A().equal(1)); writeLn(new Object().hi()); writeLn(new Object().equal(1));", "false\n1\nfalse")]
    [InlineData("import { writeLn } from \"std:console\"; class A extends Object { constructor() { super(); this.v = 1; } } class B { constructor() { super(); } } writeLn(new A()); writeLn(new B());", "A {v: 1}\nB {}")]
    [InlineData("import { writeLn } from \"std:console\"; function f() { class K {} return new K(); } var k = f(); writeLn(k.equal(k)); writeLn(k.equal(f()));", "true\nfalse")]
    [InlineData("import { writeLn } from \"std:console\"; try { throw \"boom\"; } catch (e) { writeLn(\"caught \" + e); }", "caught boom")]
    [InlineData("import { writeLn } from \"std:console\"; try { var a = [1]; a[5]; } catch (e) { writeLn(e); writeLn(e.code); }", "IndexError {message: \"The index 5 is outside 0 to 0.\", code: \"WL3005\"}\nWL3005")]
    [InlineData("import { writeLn } from \"std:console\"; function f(n) { try { if (n == 0) return \"zero\"; return \"other\"; } finally { writeLn(\"finally\"); } } writeLn(f(0)); writeLn(f(1));", "finally\nzero\nfinally\nother")]
    [InlineData("import { writeLn } from \"std:console\"; var i = 0; while (i < 3) { i = i + 1; try { if (i == 1) continue; if (i == 2) break; } finally { writeLn(i); } } writeLn(\"end\");", "1\n2\nend")]
    [InlineData("import { writeLn } from \"std:console\"; function deep() { var m = {}; return m[\"nope\"]; } try { deep(); } catch (e) { writeLn(e.message); }", "The map has no key \"nope\".")]
    [InlineData("import { writeLn } from \"std:console\"; try { try { throw new Error(\"inner\"); } finally { writeLn(\"inner finally\"); } } catch (e) { writeLn(\"caught \" + e.message); }", "inner finally\ncaught inner")]
    [InlineData("import { writeLn } from \"std:console\"; try { try { throw 1; } catch (e) { writeLn(\"again\"); throw; } } catch (e) { writeLn(e); }", "again\n1")]
    [InlineData("import { writeLn } from \"std:console\"; class R { dispose() { writeLn(\"disposed\"); } } try { using (var r = new R()) { throw \"x\"; } } catch (e) { writeLn(\"caught \" + e); }", "disposed\ncaught x")]
    [InlineData("import { writeLn } from \"std:console\"; class Bad { dispose() { throw \"from dispose\"; } } try { using (var b = new Bad()) { throw \"first\"; } } catch (e) { writeLn(e); }", "from dispose")]
    [InlineData("import { writeLn } from \"std:console\"; class A { dispose() { writeLn(\"A\"); } } class B extends A { dispose() { throw \"B failed\"; } } try { new B().dispose(); } catch (e) { writeLn(e); }", "A\nB failed")]
    [InlineData("import { writeLn } from \"std:console\"; class MyError extends Error { constructor(m) { super(m); this.extra = 1; } } try { throw new MyError(\"mine\"); } catch (e) { writeLn(e); }", "MyError {message: \"mine\", code: null, extra: 1}")]
    [InlineData("import { writeLn } from \"std:console\"; var fs = []; for (var i = 0; i < 2; i = i + 1) { try { throw i; } catch (e) { fs.push(function () { return e; }); } } writeLn(fs[0]() + fs[1]());", "1")]
    [InlineData("import { writeLn } from \"std:console\"; try { throw 1; } catch (e) { writeLn(\"c\"); } finally { writeLn(\"f\"); }", "c\nf")]
    [InlineData("import { writeLn } from \"std:console\"; try { try { throw 1; } catch (e) { throw 2; } finally { writeLn(\"f\"); } } catch (e) { writeLn(e); }", "f\n2")]
    [InlineData("import { writeLn } from \"std:console\"; try { var x = 9223372036854775807 + 1; } catch (e) { writeLn(e.code); }", "WL3002")]
    [InlineData("import { writeLn } from \"std:console\"; var e = new Error(\"m\"); writeLn(e.message); writeLn(e.code);", "m\nnull")]
    [InlineData("import { writeLn } from \"std:console\"; class C { constructor() { throw \"no\"; } } try { new C(); } catch (e) { writeLn(e); }", "no")]
    [InlineData("import { writeLn } from \"std:console\"; for (var i = 0; i < 1; i = i + 1) { try {} finally { for (var j = 0; j < 3; j = j + 1) { if (j == 1) break; writeLn(j); } } } writeLn(\"ok\");", "0\nok")]
    [InlineData("import { writeLn } from \"std:console\"; var n = 5; writeLn(\"n = \" + n.toString()); writeLn((1.5).toString() + \"|\" + \"s\".toString() + \"|\" + [1, \"a\", null].toString()); var z = null; writeLn(z.toString());", "n = 5\n1.5|s|[1, \"a\", null]\nnull")]
    [InlineData("import { writeLn } from \"std:console\"; class Point { constructor(x) { this.x = x; } } var p = new Point(1); writeLn(p.toString()); writeLn(Point.toString());", "Point {x: 1}\nclass Point")]
    [InlineData("import { writeLn } from \"std:console\"; class Money { constructor(a) { this.a = a; } toString() { return \"$\" + this.a.toString(); } } var m = new Money(7); writeLn(m); writeLn([m, new Money(8)]); writeLn({price: m}); var w = writeLn; w([m]); var f = m.toString; writeLn(f());", "$7\n[$7, $8]\n{\"price\": $7}\n[$7]\n$7")]
    [InlineData("import { writeLn } from \"std:console\"; class M { toString() { return \"m\"; } } class T { constructor() { this.inner = new M(); } toString() { return \"T(\" + super.toString() + \")\"; } } writeLn(new T());", "T(T {inner: m})")]
    [InlineData("import { writeLn } from \"std:console\"; class S { toString() { return \"self\"; } } var s = new S(); s.me = s; writeLn([s]); writeLn(s); writeLn(s.me.toString());", "[self]\nself\nself")]
    [InlineData("import { writeLn } from \"std:console\"; class Thrower { toString() { throw \"nope\"; } } try { writeLn([new Thrower()]); } catch (e) { writeLn(\"caught \" + e); } writeLn(\"after\");", "caught nope\nafter")]
    [InlineData("import { write, writeLn } from \"std:console\"; class L { toString() { write(\"<\"); return \"L\"; } } writeLn([new L(), new L()]);", "<<[L, L]")]
    [InlineData("import { writeLn } from \"std:console\"; try { var m = {}; m[\"x\"]; } catch (KeyError e) { writeLn(\"key \" + e.code); }", "key WL3007")]
    [InlineData("import { writeLn } from \"std:console\"; try { [][0]; } catch (KeyError e) { writeLn(\"k\"); } catch (IndexError e) { writeLn(\"i \" + (e is Error).toString()); } catch (e) { writeLn(\"any\"); }", "i true")]
    [InlineData("import { writeLn } from \"std:console\"; try { try { throw \"s\"; } catch (Integer e) { writeLn(\"int\"); } finally { writeLn(\"f\"); } } catch (String e) { writeLn(\"string \" + e); }", "f\nstring s")]
    [InlineData("import { writeLn } from \"std:console\"; class P {} writeLn([5 is Integer, 5 is Number, 1.5 is Number, \"a\" is String, [1] is Array, {} is Map, #{} is Set]); writeLn([null is Object, 5 is Object, writeLn is Function, new P() is P, new P() is Object, P is Class, true is Boolean, P is P]);", "[true, false, true, true, true, true, true]\n[false, true, true, true, true, true, true, false]")]
    [InlineData("import { writeLn } from \"std:console\"; class NotFound extends Error { constructor(m) { super(m); } } class errors { static var NotFound = NotFound; } try { throw new NotFound(\"x\"); } catch (errors.NotFound e) { writeLn(\"nf \" + e.message); }", "nf x")]
    [InlineData("import { writeLn } from \"std:console\"; var A = 5; try { throw 5; } catch (A e) { writeLn(\"five\"); } writeLn(5 is A); writeLn(6 is A);", "five\ntrue\nfalse")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"a\".equal(\"a\")); writeLn(5.equal(6)); var z = null; writeLn(z.equal(null));", "true\nfalse\ntrue")]
    [InlineData("import { writeLn } from \"std:console\"; try { 1 + \"a\"; } catch (TypeError e) { writeLn(e is TypeError); writeLn(e.code); } try { throw new KeyError(\"mine\"); } catch (KeyError e) { writeLn(e); }", "true\nWL3001\nKeyError {message: \"mine\", code: null}")]
    [InlineData("import { writeLn } from \"std:console\"; try { throw null; } catch (Object e) { writeLn(\"object\"); } catch (e) { writeLn(\"any \" + e.toString()); }", "any null")]
    [InlineData("import { write, writeLn } from \"std:console\"; class Stack extends Array { top() { return this[this.length - 1]; } } var s = new Stack(); s.push(1); s.push(2); writeLn(s.top()); writeLn(s.length); writeLn(s); writeLn([s is Array, s is Stack, [] is Stack]); s.name = \"st\"; writeLn(s.name); foreach (var x in s) write(x); writeLn(\"\");", "2\n2\nStack:[1, 2]\n[true, true, false]\nst\n12")]
    [InlineData("import { writeLn } from \"std:console\"; class Bag extends Set {} var b = new Bag(); b.add(1); b.add(1); writeLn(b); class Dict extends Map { get(k) { return this?[k]; } } var d = new Dict(); d[\"a\"] = 1; writeLn(d.get(\"a\")); writeLn(d.get(\"b\")); writeLn([d]);", "Bag:#{1}\n1\nnull\n[Dict:{\"a\": 1}]")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(new Array()); writeLn(new Map()); writeLn(new Set()); writeLn([new Array() is Array, new Map() is Map]);", "[]\n{}\n#{}\n[true, true]")]
    [InlineData("import { write, writeLn } from \"std:console\"; class Stack extends Array { constructor(x) { super(); this.push(x); } push(v) { write(\"push \"); writeLn(v); } } var s = new Stack(5); writeLn(s.length);", "push 5\n0")]
    [InlineData("import { writeLn } from \"std:console\"; class Stack extends Array { toString() { return \"stack of \" + this.length.toString(); } } var s = new Stack(); s.push(1); writeLn(s); writeLn([s]);", "stack of 1\n[stack of 1]")]
    [InlineData("import { writeLn } from \"std:console\"; class P {} P.origin = 0; P.make = function (x) { return x * 2; }; writeLn(P.origin); writeLn(P.make(4)); Integer.twice = function (n) { return n * 2; }; writeLn(Integer.twice(3));", "0\n8\n6")]
    [InlineData("import { writeLn } from \"std:console\"; class Code { constructor(c) { this.c = c; } equal(other) { return other is Code && other.c == this.c; } } var NotFound = new Code(404); try { throw new Code(404); } catch (NotFound e) { writeLn(\"404\"); } writeLn(new Code(404) is NotFound); writeLn(new Code(500) is NotFound);", "404\ntrue\nfalse")]
    [InlineData("import { writeLn } from \"std:console\"; class Bad { equal(o) { throw \"from equal\"; } } var B = new Bad(); try { try { throw 1; } catch (B e) { writeLn(\"no\"); } } catch (e) { writeLn(e); }", "from equal")]
    [InlineData("import { writeLn } from \"std:console\"; function f(a, b = 1) { return b; } writeLn(f(2)); writeLn(f(2, 3)); writeLn(f(2, null));", "1\n3\nnull")]
    [InlineData("import { writeLn } from \"std:console\"; var n = 0; function next() { n = n + 1; return n; } function g(a, b = a * 10, c = next()) { writeLn([a, b, c]); } g(1); g(2); g(3, 4, 5);", "[1, 10, 1]\n[2, 20, 2]\n[3, 4, 5]")]
    [InlineData("import { writeLn } from \"std:console\"; function h(x = 5) { return function () { return x; }; } writeLn(h()()); writeLn(h(7)());", "5\n7")]
    [InlineData("import { writeLn } from \"std:console\"; class P { constructor(x = 0, y = 0) { this.x = x; this.y = y; } move(dx = 1) { return new P(this.x + dx, this.y); } } writeLn(new P()); writeLn(new P(1).move()); writeLn(new P(1, 2).move(5));", "P {x: 0, y: 0}\nP {x: 2, y: 0}\nP {x: 6, y: 2}")]
    [InlineData("import { writeLn } from \"std:console\"; var f = function (a = \"x\") { return a; }; writeLn(f()); var g = f; writeLn(g(\"y\"));", "x\ny")]
    [InlineData("import { writeLn } from \"std:console\"; var b = 9; function f(a, b = b) { return b; } writeLn(f(1));", "9")]
    [InlineData("import { writeLn } from \"std:console\"; var m = {a: 1}; writeLn(m?[\"a\"]); writeLn(m?[\"b\"]); "
        + "writeLn([1]?[5]); writeLn([1]?[-2]); writeLn(\"ab\"?[9]); writeLn(\"ab\"?[1]);", "1\nnull\nnull\nnull\nnull\nb")]
    [InlineData("import { writeLn } from \"std:console\"; var rows = null; writeLn(rows?[0]?[\"name\"]); rows = [{name: \"x\"}]; writeLn(rows?[0]?[\"name\"]); writeLn(rows?[1]?[\"name\"]);", "null\nx\nnull")]
    [InlineData("import { writeLn } from \"std:console\"; var i = 0; var n = null; writeLn(n?[i = 5]); writeLn(i); var m = {a: [1]}; writeLn(m?[\"a\"]?[0]); writeLn(m?[\"b\"]?[0]);", "null\n5\n1\nnull")]
    [InlineData("import { writeLn } from \"std:console\"; var s = #{1, \"a\", 1, null}; writeLn(s.length); writeLn(s.has(\"a\")); writeLn(s.has(2)); writeLn(#{}); writeLn(#{\"q\"}); writeLn([#{true}]);", "3\ntrue\nfalse\n#{}\n#{\"q\"}\n[#{true}]")]
    [InlineData("import { writeLn } from \"std:console\"; var s = #{}; writeLn(s.add(2)); writeLn(s.add(2)); writeLn(s.remove(2)); writeLn(s.remove(2)); writeLn(s.length);", "true\nfalse\ntrue\nfalse\n0")]
    [InlineData("import { writeLn } from \"std:console\"; var t = 0; foreach (var v in #{1, 2, 3, 3}) t = t + v; writeLn(t); writeLn(#{1} == #{1}); var s = #{1}; var u = s; u.add(2); writeLn(s.length);", "6\nfalse\n2")]
    [InlineData("import { writeLn } from \"std:console\"; var s = #{1, 2, 3}; var n = 0; foreach (var v in s) { s.remove(v); n = n + 1; } writeLn(n < 4); writeLn({set: #{\"x\"}});", "true\n{\"set\": #{\"x\"}}")]
    [InlineData("import { write, writeLn } from \"std:console\"; foreach (var c in \"a\U0001F600b\") write(c + \"|\"); writeLn(null);", "a|\U0001F600|b|null")]
    [InlineData("import { writeLn } from \"std:console\"; var m = {a: 1, bb: 2, ccc: 3}; var t = 0; var k = 0; foreach (var (key, value) in m) { t = t + value; k = k + key.length; } writeLn(t); writeLn(k);", "6\n6")]
    [InlineData("import { write, writeLn } from \"std:console\"; var a = [1, 2]; foreach (var x in a) { if (x < 4) a.push(x + 2); write(x); } writeLn(null);", "12345null")]
    [InlineData("import { write, writeLn } from \"std:console\"; var fs = []; foreach (var x in [1, 2, 3]) fs.push(function () { return x; }); foreach (var f in fs) write(f()); writeLn(null);", "123null")]
    [InlineData("import { write, writeLn } from \"std:console\"; foreach (var x in [1, 2, 3, 4]) { if (x == 2) continue; if (x == 4) break; write(x); } writeLn(null);", "13null")]
    [InlineData("import { writeLn } from \"std:console\"; var x = [7]; foreach (var x in x) writeLn(x); writeLn(x);", "7\n[7]")]
    [InlineData("import { writeLn } from \"std:console\"; foreach (var x in []) writeLn(x); foreach (var (k, v) in {}) writeLn(k); foreach (var c in \"\") writeLn(c); writeLn(\"done\");", "done")]
    [InlineData("import { write, writeLn } from \"std:console\"; foreach (var a in [[1, 2], [3]]) foreach (var b in a) write(b); writeLn(null);", "123null")]
    [InlineData("import { writeLn } from \"std:console\"; function first(a) { foreach (var x in a) return x; return null; } writeLn(first([5, 6])); writeLn(first([]));", "5\nnull")]
    [InlineData("import { writeLn } from \"std:console\"; var m = {a: 1, b: 2, c: 3}; var n = 0; foreach (var (k, v) in m) { m.remove(k); n = n + 1; } writeLn(n < 4); writeLn(m.length < 3);", "true\ntrue")]
    [InlineData("import { writeLn } from \"std:console\"; var m = {x: 1}; foreach (var (k, v) in m) { var g = function () { return [k, v]; }; writeLn(g()); }", "[\"x\", 1]")]
    [InlineData("import { write, writeLn } from \"std:console\"; class R { constructor(n) { this.n = n; } iterate() { return new It(this.n); } } class It { constructor(n) { this.i = 0; this.n = n; } hasNext() { return this.i < this.n; } next() { this.i = this.i + 1; return this.i; } } foreach (var x in new R(3)) write(x); writeLn(null);", "123null")]
    [InlineData("import { writeLn } from \"std:console\"; class N { iterate() { return new NI(); } } class NI { constructor() { this.k = 0; } hasNext() { return this.k < 2; } next() { this.k = this.k + 1; return null; } } var c = 0; foreach (var x in new N()) { if (x == null) c = c + 1; } writeLn(c);", "2")]
    [InlineData("import { writeLn } from \"std:console\"; class C { iterate() { return [1, 2, 3, 4].iterate(); } } var fs = []; foreach (var x in new C()) { if (x == 2) continue; if (x == 4) break; fs.push(() => x); } writeLn([fs.length, fs[0](), fs[1]()]);", "[2, 1, 3]")]
    [InlineData("import { writeLn } from \"std:console\"; class C { iterate() { return [1, 2].iterate(); } } function f() { foreach (var x in new C()) foreach (var y in new C()) if (x + y == 3) return [x, y]; return null; } writeLn(f());", "[1, 2]")]
    [InlineData("import { write, writeLn } from \"std:console\"; extend Integer { iterate() { return [this, this * 2].iterate(); } } foreach (var x in 3) write(x); writeLn(null);", "36null")]
    [InlineData("import { writeLn } from \"std:console\"; class K { static iterate() { return [\"a\"].iterate(); } } foreach (var x in K) writeLn(x);", "a")]
    [InlineData("import { writeLn } from \"std:console\"; class S extends Array { iterate() { return [9].iterate(); } } var s = new S(); s.push(1); foreach (var x in s) writeLn(x); writeLn(s.iterate().next());", "1\n9")]
    [InlineData("import { writeLn } from \"std:console\"; var a = [1, 2]; var i = a.iterate(); a.push(3); var t = []; while (i.hasNext()) t.push(i.next()); writeLn(t); writeLn(i is Iterator);", "[1, 2, 3]\ntrue")]
    [InlineData("import { writeLn } from \"std:console\"; var m = {a: 1}; var i = m.iterate(); m[\"b\"] = 2; writeLn([i.next(), i.hasNext()]); writeLn(#{5}.iterate().next()); var s = \"\"; var j = \"a\U0001F600\".iterate(); while (j.hasNext()) s = s + j.next() + \"|\"; writeLn(s);", "[[\"a\", 1], false]\n5\na|\U0001F600|")]
    [InlineData("import { writeLn } from \"std:console\"; var i = [].iterate(); try { i.next(); } catch (IndexError e) { writeLn(e.code); } writeLn(\"\".iterate().hasNext());", "WL3005\nfalse")]
    [InlineData("import { writeLn } from \"std:console\"; var m = {}; for (var i = 0; i < 100; i = i + 1) m[i] = i * i; for (var i = 0; i < 100; i = i + 2) m.remove(i); writeLn(m.length); writeLn(m[99]);", "50\n9801")]
    [InlineData("import { writeLn } from \"std:console\"; import { delay } from \"std:system\"; async function w(n, d) { writeLn(n + \"<\"); await delay(d); writeLn(n + \">\"); return d; } var a = w(\"a\", 20); var b = w(\"b\", 10); writeLn(a is Task); writeLn(await a + await b);", "a<\nb<\ntrue\nb>\na>\n30")]
    [InlineData("import { writeLn } from \"std:console\"; async function f() { writeLn(\"f1\"); await null; writeLn(\"f2\"); } var t = f(); writeLn(\"main\"); writeLn(await 5); await t;", "f1\nmain\nf2\n5")]
    [InlineData("import { writeLn } from \"std:console\"; async function bad() { await null; throw new Error(\"no\"); } try { await bad(); } catch (e) { writeLn(e.message); }", "no")]
    [InlineData("import { writeLn } from \"std:console\"; async function ix() { return [1][5]; } try { await ix(); } catch (IndexError e) { writeLn(e.code); }", "WL3005")]
    [InlineData("import { writeLn } from \"std:console\"; async function bad() { throw \"x\"; } bad().forget(); var t = bad(); t.forget(); writeLn(\"ok\");", "ok")]
    [InlineData("import { writeLn } from \"std:console\"; class A { constructor() { this.k = 1; } async m(x) { await null; return x + this.k; } } writeLn(await new A().m(41)); var h = async function (x) { return x * 2; }; writeLn(await h(4));", "42\n8")]
    [InlineData("import { writeLn } from \"std:console\"; async function f() { await null; writeLn(\"never\"); } f(); writeLn(\"end\");", "end")]
    [InlineData("import { writeLn } from \"std:console\"; async function f() { return 3; } var t = f(); writeLn(await t); writeLn(await t); writeLn(t);", "3\n3\ntask")]
    [InlineData("import { writeLn } from \"std:console\"; import { delay } from \"std:system\"; writeLn(delay(0) is Task); writeLn(5 is Task); writeLn(delay(0) is Object); writeLn([delay(1)]);", "true\nfalse\ntrue\n[task]")]
    [InlineData("import { writeLn } from \"std:console\"; async function count(n) { var total = 0; for (var i = 0; i < n; i = i + 1) { total = total + await i; } return total; } writeLn(await count(5));", "10")]
    [InlineData("import { writeLn } from \"std:console\"; import { delay } from \"std:system\"; async function inner(x) { await delay(1); return x + 1; } async function outer() { var a = await inner(1); return await inner(a); } writeLn(await outer());", "3")]
    [InlineData("import { writeLn } from \"std:console\"; import { delay } from \"std:system\"; async function f() { try { await delay(1); throw \"e\"; } finally { writeLn(\"fin\"); } } try { await f(); } catch (e) { writeLn(\"caught \" + e); }", "fin\ncaught e")]
    [InlineData("import { writeLn } from \"std:console\"; class R { dispose() { writeLn(\"disposed\"); } } async function f() { using (var r = new R()) { await null; writeLn(\"in\"); } } await f();", "in\ndisposed")]
    [InlineData("import { write, writeLn } from \"std:console\"; import { delay } from \"std:system\"; var fs = []; async function f(n) { await delay(n); fs.push(function () { return n; }); } await f(2); await f(1); foreach (var g in fs) write(g()); writeLn(null);", "21null")]
    [InlineData("import { write, writeLn } from \"std:console\"; async function f() { var m = {a: [1, 2]}; foreach (var (k, v) in m) { foreach (var x in v) { await null; write(k + x.toString()); } } } await f(); writeLn(null);", "a1a2null")]
    [InlineData("import { writeLn } from \"std:console\"; import { delay } from \"std:system\"; var order = []; async function t(n, d) { await delay(d); order.push(n); } var all = [t(1, 30), t(2, 10), t(3, 20), t(4, 10)]; foreach (var x in all) await x; writeLn(order);", "[2, 4, 3, 1]")]
    [InlineData("import { writeLn } from \"std:console\"; async function f() { await null; return \"late\"; } var t = f(); writeLn(\"early\"); writeLn(await t);", "early\nlate")]
    [InlineData("import { writeLn } from \"std:console\"; import { delay } from \"std:system\"; class R { async dispose() { await delay(5); writeLn(\"disposed\"); } } async function f() { using (var r = new R()) { writeLn(\"in\"); } writeLn(\"after\"); } await f();", "in\ndisposed\nafter")]
    [InlineData("import { writeLn } from \"std:console\"; class R { async dispose() { await null; writeLn(\"disposed\"); } } using (var r = new R()) { writeLn(\"in\"); } writeLn(\"after\");", "in\ndisposed\nafter")]
    [InlineData("import { writeLn } from \"std:console\"; import { delay } from \"std:system\"; class A { dispose() { writeLn(\"A\"); } } class B extends A { async dispose() { await delay(1); writeLn(\"B\"); } } using (var b = new B()) {} writeLn(\"end\");", "B\nA\nend")]
    [InlineData("import { writeLn } from \"std:console\"; class A { async dispose() { await null; writeLn(\"A\"); } } class B extends A { dispose() { writeLn(\"B\"); } } var b = new B(); var t = b.dispose(); writeLn(t is Task); await t; writeLn(\"end\");", "B\ntrue\nA\nend")]
    [InlineData("import { writeLn } from \"std:console\"; class R { async dispose() { await null; } } var r = new R(); var t = r.dispose(); writeLn(r.dispose()); await t; writeLn(\"ok\");", "null\nok")]
    [InlineData("import { writeLn } from \"std:console\"; class R { async dispose() { await null; throw \"bad\"; } } try { using (var r = new R()) {} } catch (e) { writeLn(\"caught \" + e); }", "caught bad")]
    [InlineData("import { writeLn } from \"std:console\"; class R { async dispose() { await null; writeLn(\"disposed\"); } } try { using (var r = new R()) { throw \"body\"; } } catch (e) { writeLn(e); }", "disposed\nbody")]
    [InlineData("import { writeLn } from \"std:console\"; class A { dispose() { writeLn(\"A\"); } } class B extends A { async dispose() { await null; throw \"B\"; } } try { using (var b = new B()) {} } catch (e) { writeLn(\"caught \" + e); }", "A\ncaught B")]
    [InlineData("import { writeLn } from \"std:console\"; class A { async dispose() { throw \"A\"; } } class B extends A { async dispose() { await null; throw \"B\"; } } try { await new B().dispose(); } catch (e) { writeLn(\"caught \" + e); }", "caught A")]
    [InlineData("import { writeLn } from \"std:console\"; class R { async dispose() { writeLn(\"d\"); } } var r = new R(); function f() { using (var x = r) {} } try { f(); } catch (e) { writeLn(e.code); } await r.dispose();", "WL3001\nd")]
    [InlineData("import { writeLn } from \"std:console\"; class R { async dispose() { writeLn(\"d\"); } } function f(r) { return r.dispose(); } var t = f(new R()); writeLn(t is Task); await t;", "d\ntrue")]
    [InlineData("import { writeLn } from \"std:console\"; class R { constructor(n) { this.n = n; } async dispose() { await null; writeLn(\"d\" + this.n.toString()); } } async function f() { foreach (var i in [1, 2]) { using (var r = new R(i)) { if (i == 1) continue; return i; } } } writeLn(await f());", "d1\nd2\n2")]
    [InlineData("import { writeLn } from \"std:console\"; import { delay } from \"std:system\"; class R { constructor(n) { this.n = n; } async dispose() { await delay(this.n); writeLn(this.n); } } async function use(n) { using (var r = new R(n)) {} } var a = use(20); var b = use(10); await a; await b;", "10\n20")]
    [InlineData("import { writeLn } from \"std:console\"; class R { dispose() { writeLn(\"d\"); } } async function f() { using (var r = new R()) {} writeLn(\"f\"); } var t = f(); writeLn(\"main\"); await t;", "d\nf\nmain")]
    [InlineData("import { writeLn } from \"std:console\"; class R { async dispose() { await null; writeLn(\"d\"); } } async function f() { using (var r = new R()) { return 1; } } var t = f(); writeLn(\"main\"); writeLn(await t);", "main\nd\n1")]
    [InlineData("import { writeLn } from \"std:console\"; class S extends Array {} var s = new S(); s.push(1); s.push(s); writeLn(s); writeLn([s]); class M extends Map {} var m = new M(); m[\"k\"] = s; writeLn(m);", "S:[1, S:[…]]\n[S:[1, S:[…]]]\nM:{\"k\": S:[1, S:[…]]}")]
    [InlineData("import { writeLn } from \"std:console\"; class A { static var x = 1; } class B extends A {} extend B { static var x = 2; } writeLn(A.x); writeLn(B.x);", "1\n2")]
    [InlineData("import { writeLn } from \"std:console\"; class A { m() { return \"A\"; } } class B extends A { m() { return \"B\"; } } extend A { m() { return \"A2\"; } } writeLn(new B().m()); writeLn(new A().m());", "B\nA2")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(new P().hi()); class P {} extend P { hi() { return \"hi\"; } }", "hi")]
    [InlineData("import { writeLn } from \"std:console\"; class P {} var p = new P(); { extend P { hi() { return \"hi\"; } } writeLn(p.hi()); } writeLn(p.hi());", "hi\nhi")]
    [InlineData("import { writeLn } from \"std:console\"; extend Integer { async later() { await null; return this + 1; } } writeLn(await 1.later());", "2")]
    [InlineData("import { writeLn } from \"std:console\"; extend String { toString() { return \"S\"; } } writeLn(\"a\".toString()); writeLn(\"a\");", "S\na")]
    [InlineData("import { writeLn } from \"std:console\"; extend Class { describe() { return \"class \" + this.toString(); } } class P {} writeLn(P.describe());", "class class P")]
    [InlineData("import { writeLn } from \"std:console\"; class Stack extends Array {} extend Array { top() { return this[this.length - 1]; } } var s = new Stack(); s.push(1); s.push(2); writeLn(s.top()); writeLn([3].top());", "2\n3")]
    [InlineData("import { writeLn } from \"std:console\"; extend Array { push(v) { writeLn(\"pushing\"); } } var a = []; a.push(1); writeLn(a.length);", "pushing\n0")]
    [InlineData("import { writeLn } from \"std:console\"; extend Object { d() { return \"d\"; } } writeLn(null.toString()); writeLn(true.d()); writeLn(1.5.d());", "null\nd\nd")]
    [InlineData("import { writeLn } from \"std:console\"; class R { dispose() { writeLn(\"old\"); } } extend R { dispose() { writeLn(\"new\"); } } using (var r = new R()) {}", "new")]
    [InlineData("import { writeLn } from \"std:console\"; class A { dispose() { writeLn(\"A\"); } } class B extends A {} extend B { destructor() { writeLn(\"B\"); } } new B().dispose();", "B\nA")]
    [InlineData("import { writeLn } from \"std:console\"; { var k = 10; extend Integer { plusK() { return this + k; } } writeLn(1.plusK()); }", "11")]
    [InlineData("import { writeLn } from \"std:console\"; extend Integer { isPositive() { return this > 0; } static zero() { return 0; } } writeLn([4.isPositive(), (0 - 3).isPositive(), Integer.zero()]); var f = 4.isPositive; writeLn(f());", "[true, false, 0]\ntrue")]
    [InlineData("import { writeLn } from \"std:console\"; class P {} extend P { equal(o) { return true; } } writeLn(new P().equal(1)); try { throw 5; } catch (P e) { writeLn(\"not caught here\"); } catch (e) { writeLn(e); }", "true\n5")]
    [InlineData("import { writeLn } from \"std:console\"; class Celsius extends Number { warm() { return this > 20; } } var c = new Celsius(21.5); writeLn([c, c + 1, 1 + c, -c, c * 2]); writeLn([c is Number, c is Celsius, 21.5 is Celsius, c.warm(), c == 21.5, c < 22]);", "[Celsius:21.5, Celsius:22.5, Celsius:22.5, Celsius:-21.5, Celsius:43.0]\n[true, true, false, true, true, true]")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([new Number(5), new Integer(), new Integer(42), new Boolean(), new Boolean(true)]); writeLn(new String() == \"\"); writeLn(new Integer(42) is Integer);", "[5.0, 0, 42, false, true]\ntrue\ntrue")]
    [InlineData("import { write, writeLn } from \"std:console\"; class Name extends String { shout() { return this + \"!\"; } } var n = new Name(\"Anna\"); writeLn(n); writeLn([n.length, n[0], n.shout()]); foreach (var ch in n) write(ch); writeLn(null);", "Name:\"Anna\"\n[4, \"A\", Name:\"Anna!\"]\nAnnanull")]
    [InlineData("import { writeLn } from \"std:console\"; class Flag extends Boolean {} var f = new Flag(true); if (f) writeLn(\"on\"); writeLn([!f, f && true, f == true]); while (new Flag()) { writeLn(\"never\"); }", "on\n[false, true, true]")]
    [InlineData("import { writeLn } from \"std:console\"; class Count extends Integer { constructor(n) { super(n * 2); this.label = \"x\"; } } var k = new Count(3); writeLn([k, k + 1, (k + 1).label, k / 4, k - k]);", "[Count:6, Count:7, \"x\", 1.5, Count:0]")]
    [InlineData("import { writeLn } from \"std:console\"; class Celsius extends Number {} class Hot extends Celsius {} writeLn([new Hot(40), new Celsius(), new Hot(1) + new Celsius(2)]);", "[Hot:40.0, Celsius:0.0, Hot:3.0]")]
    [InlineData("import { writeLn } from \"std:console\"; class C extends Number { toString() { return \"C\" + super.toString(); } } writeLn(new C(1)); writeLn(new C(2).toString());", "CC:1.0\nCC:2.0")]
    [InlineData("import { writeLn } from \"std:console\"; class Celsius extends Number {} extend Celsius { f() { return this * 9 / 5 + 32; } } writeLn(new Celsius(100).f());", "Celsius:212.0")]
    [InlineData("import { writeLn } from \"std:console\"; var f = function (x) { return x + 1; }; writeLn(new Function(f) == f); writeLn(new Function(f)(2)); var g = new Function(); writeLn([g(), g is Function]); writeLn(g);", "true\n3\n[null, true]\nFunction:function")]
    [InlineData("import { writeLn } from \"std:console\"; class Handler extends Function { constructor(f) { super(f); this.calls = 0; } describe() { return \"handler\"; } } var h = new Handler(function (x) { return x * 2; }); writeLn(h(21)); writeLn(h.describe()); writeLn([h is Function, h is Handler]); writeLn(h); h.calls = h.calls + 1; writeLn(h.calls);", "42\nhandler\n[true, true]\nHandler:function\n1")]
    [InlineData("import { writeLn } from \"std:console\"; class Say extends Function {} var say = new Say(writeLn); say(\"via a built-in\"); class P { m() { return \"m\"; } } writeLn(new Say(new P().m)()); writeLn(new Say()());", "via a built-in\nm\nnull")]
    [InlineData("import { write, writeLn } from \"std:console\"; class H extends Function {} var h = new H(async function () { return 5; }); writeLn(await h()); var calls = [new H(function () { return 1; }), function () { return 2; }]; foreach (var c in calls) write(c()); writeLn(null);", "5\n12null")]
    [InlineData("import { writeLn } from \"std:console\"; class V { constructor(x) { this.x = x; } static operator +(a, b) { return new V(a.x + b.x); } static operator <(a, b) { return a.x < b.x; } } var v = new V(1) + new V(2); writeLn(v.x); writeLn([new V(1) < new V(2), new V(3) < new V(2)]);", "3\n[true, false]")]
    [InlineData("import { writeLn } from \"std:console\"; class M { static operator *(a, b) { return \"M*\"; } } writeLn(2 * new M()); writeLn(new M() * 2);", "M*\nM*")]
    [InlineData("import { writeLn } from \"std:console\"; class A { static operator +(a, b) { return \"A\"; } } class B { static operator +(a, b) { return \"B\"; } } writeLn(new A() + new B()); writeLn(new B() + new A());", "A\nB")]
    [InlineData("import { writeLn } from \"std:console\"; class A { static operator -(a, b) { return \"A-\"; } } class B extends A {} writeLn(new B() - 1);", "A-")]
    [InlineData("import { writeLn } from \"std:console\"; class Money extends Number { static operator +(a, b) { return \"money\"; } } writeLn(new Money(1) + 2); class C extends Number {} writeLn(new C(1) + 2);", "money\nC:3.0")]
    [InlineData("import { writeLn } from \"std:console\"; class Vec extends Array { static operator +(a, b) { var r = new Vec(); r.push(a[0] + b[0]); return r; } } var p = new Vec(); p.push(1); var q = new Vec(); q.push(2); writeLn(p + q);", "Vec:[3]")]
    [InlineData("import { writeLn } from \"std:console\"; class P { constructor(x) { this.x = x; } } extend P { static operator /(a, b) { return a.x / b.x; } } writeLn(new P(3) / new P(2));", "1.5")]
    [InlineData("import { writeLn } from \"std:console\"; class T { static operator +(a, b) { throw \"no\"; } } try { new T() + 1; } catch (e) { writeLn(e); }", "no")]
    [InlineData("import { writeLn } from \"std:console\"; class V { constructor(x) { this.x = x; } static operator +(a, b) { return new V(a.x + b.x); } static operator <(a, b) { return a.x < b.x; } } writeLn([1, 2, (new V(1) + new V(2)).x, [3, [4, (new V(5) + new V(6)).x]]]);", "[1, 2, 3, [3, [4, 11]]]")]
    [InlineData("import { writeLn } from \"std:console\"; class S { static operator >=(a, b) { return true; } static operator <=(a, b) { return false; } } writeLn([new S() >= 1, new S() <= 1, new S() == new S()]);", "[true, false, false]")]
    [InlineData("import { writeLn } from \"std:console\"; class A { static var n = 1; static make(x) { return x * 2; } } var a = new A(); writeLn([a.n, a.make(4), A.n]);", "[1, 8, 1]")]
    [InlineData("import { writeLn } from \"std:console\"; class A { static var n = 1; static m() { return \"static\"; } m() { return \"method\"; } } var a = new A(); a.n = 5; writeLn([a.n, A.n, a.m()]);", "[5, 5, \"method\"]")]
    [InlineData("import { writeLn } from \"std:console\"; class A { static var n = 1; static f() { return 1; } } class B extends A {} var b = new B(); A.n = 2; writeLn([b.n, b?.n, b?.missing]); var g = b.f; writeLn(g == A.f);", "[2, 2, null]\ntrue")]
    [InlineData("import { writeLn } from \"std:console\"; class A { static var k = \"s\"; } var a = new A(); a.k = function () { return \"field\"; }; writeLn(a.k()); class S extends Array { static var size = 3; } writeLn(new S().size);", "field\n3")]
    [InlineData("import { writeLn } from \"std:console\"; class A { constructor(x, y = 2) { this.x = x; this.y = y; } } class B extends A {} class C extends B { m() { return 1; } } writeLn([new B(5).x, new C(1, 3).y, new C(1).y]);", "[5, 3, 2]")]
    [InlineData("import { writeLn } from \"std:console\"; class E extends Error {} var e = new E(\"bad\"); writeLn([e.message, e is Error]);", "[\"bad\", true]")]
    [InlineData("import { writeLn } from \"std:console\"; class R { constructor(w, h) { this.w = w; this.h = h; } property area { get { return this.w * this.h; } } } var n = null; writeLn([new R(2, 3).area, n?.area, new R(1, 1)?.area]);", "[6, null, 1]")]
    [InlineData("import { writeLn } from \"std:console\"; class T { property v { get { return field; } set(value) { field = value; } } } var t = new T(); var before = t.v; t.v = 20; writeLn([before, t.v, t]);", "[null, 20, T {v: 20}]")]
    [InlineData("import { writeLn } from \"std:console\"; class D { property v { get { return field; } set(value) { field = value * 2; } } } var d = new D(); var a = d.v = 5; writeLn([a, d.v, d.v = 1]);", "[5, 10, 1]")]
    [InlineData("import { writeLn } from \"std:console\"; class T { property v { get { return field; } set(value) { field = value; } } } var t = new T(); for (var i = 0; i < 1000; i = i + 1) { t.v = i; } writeLn(t.v);", "999")]
    // A property's field, after a built-in written in WebLingo bound where the accessor first uses it, new Error or map.
    [InlineData("import { writeLn } from \"std:console\"; class T { property v { get { var e = [1].map(x => x); return field; } set(value) { if (value < 0) throw new Error(\"negative\"); field = value; } } } var t = new T(); t.v = 4; try { t.v = -1; } catch (e) { writeLn(e.message); } writeLn(t.v);", "negative\n4")]
    [InlineData("import { writeLn } from \"std:console\"; class S { static var n = 3; static property count { get { return S.n * 2; } set(v) { S.n = v; } } } S.count = 10; writeLn([S.count, S.n]);", "[20, 10]")]
    [InlineData("import { writeLn } from \"std:console\"; class K { static property total { get { return field; } set(v) { field = v; } } } K.total = 4; writeLn([K.total, K.total = 5]);", "[4, 5]")]
    [InlineData("import { writeLn } from \"std:console\"; class B { property v { get { return 1; } } } class C extends B { property v { get { return super.v + 10; } } } class E extends B {} writeLn([new B().v, new C().v, new E().v]);", "[1, 11, 1]")]
    [InlineData("import { writeLn } from \"std:console\"; class B { property v { get { return field; } } } class C extends B { property v { set(value) { field = value; } } } var c = new C(); c.v = 3; writeLn(c.v);", "3")]
    [InlineData("import { writeLn } from \"std:console\"; extend Integer { property doubled { get { return this * 2; } } } class P { constructor() { this.x = 1; } } extend P { property twice { get { return this.x * 2; } } } writeLn([4.doubled, new P().twice]);", "[8, 2]")]
    [InlineData("import { writeLn } from \"std:console\"; class X { property v { set(value) { throw \"bad\"; } } } var x = new X(); var r = 0; for (var i = 0; i < 3; i = i + 1) { try { x.v = i; } catch (e) { r = r + 1; } } writeLn(r);", "3")]
    [InlineData("import { writeLn } from \"std:console\"; class G { property a { get { this.b = 5; return this.b; } } property b { get { return field; } set(value) { field = value + 1; } } } writeLn(new G().a);", "6")]
    [InlineData("import { writeLn } from \"std:console\"; class Celsius extends Number { property kelvin { get { return this + 273.0; } } } writeLn(new Celsius(1.5).kelvin);", "Celsius:274.5")]
    [InlineData("import { writeLn } from \"std:console\"; var field = 9; class F { property a { get { return field; } set(value) { var field = value; } } } var f = new F(); f.a = 1; writeLn([f.a, field]);", "[null, 9]")]
    [InlineData("import { writeLn } from \"std:console\"; class A { property() { return 1; } } var property = 2; writeLn([new A().property(), property]);", "[1, 2]")]
    [InlineData("import { writeLn } from \"std:console\"; function describe(this, prefix) { return prefix + this.x.toString(); } class P { constructor() { this.x = 3; } } var f = function (this, n) { return this.x + n; }; var p = new P(); writeLn([describe(p, \"x = \"), f(p, 1)]);", "[\"x = 3\", 4]")]
    [InlineData("import { writeLn } from \"std:console\"; var Point = new Class(\"Point\"); Point.setMethod(\"constructor\", function (this, x, y) { this.x = x; this.y = y; }); Point.setMethod(\"describe\", function (this, prefix) { return prefix + this.x.toString() + \", \" + this.y.toString(); }); var p = new Point(3, 4); var d = p.describe; writeLn([p.describe(\"p = \"), d(\"d = \"), p, Point, p is Point, p is Object]);", "[\"p = 3, 4\", \"d = 3, 4\", Point {x: 3, y: 4}, class Point, true, true]")]
    [InlineData("import { writeLn } from \"std:console\"; var Point = new Class(\"Point\"); Point.setMethod(\"constructor\", function (this, x, y) { this.x = x; this.y = y; }); Point.setMethod(\"describe\", function (this, prefix) { return prefix + this.x.toString() + \", \" + this.y.toString(); }); var p = new Point(3, 4); var Line = new Class(\"Line\", Point); var l = new Line(1, 2); Point.setMethod(\"describe\", function (this, prefix) { return prefix + \"replaced\"; }); writeLn([p.describe(\"p: \"), l.describe(\"l: \"), l is Point, l]);", "[\"p: replaced\", \"l: replaced\", true, Line {x: 1, y: 2}]")]
    [InlineData("import { writeLn } from \"std:console\"; var C = new Class(); var c = new C(); c.x = 1; var N = new Class(null, C); writeLn([C, c, new N() is C]);", "[class, class {x: 1}, true]")]
    [InlineData("import { writeLn } from \"std:console\"; var Point = new Class(\"Point\"); Point.setMethod(\"constructor\", function (this, x, y) { this.x = x; this.y = y; }); Point.setMethod(\"describe\", function (this, prefix) { return prefix + this.x.toString() + \", \" + this.y.toString(); }); Point.origin = function () { return new Point(0, 0); }; Point.count = 0; Point.count = Point.count + 1; writeLn([Point.origin(), Point.count]);", "[Point {x: 0, y: 0}, 1]")]
    [InlineData("import { writeLn } from \"std:console\"; var Base = new Class(\"Base\"); Base.setMethod(\"hi\", function (this) { return \"hi \" + this.toString(); }); function make() { class D extends Base { constructor() { this.n = 1; } } return new D(); } writeLn(make().hi());", "hi D {n: 1}")]
    [InlineData("import { writeLn } from \"std:console\"; var Base = new Class(\"Base\"); function make() { class D extends Base {} return new D(); } writeLn(make());", "D {}")]
    [InlineData("import { writeLn } from \"std:console\"; class P { constructor() { this.x = 2; } } var f = function (this, n) { return this.x * n; }; var p = new P(); var q = new P(); q.x = 5; p.g = f; writeLn(p.g(q, 3));", "15")]
    [InlineData("import { writeLn } from \"std:console\"; var R = new Class(\"R\"); var log = []; R.setMethod(\"destructor\", function (this) { log.push(\"disposed\"); }); using (var r = new R()) { log.push(\"used\"); } writeLn(log);", "[\"used\", \"disposed\"]")]
    [InlineData("import { writeLn } from \"std:console\"; extend Integer { describe(prefix) { return prefix + this.toString(); } } Integer.setMethod(\"twice\", function (this) { return this * 2; }); writeLn([4.twice(), 4.describe(\"n = \")]);", "[8, \"n = 4\"]")]
    [InlineData("import { writeLn } from \"std:console\"; var C = new Class(); var c = new C(); var before = [C.__className, C.toString()]; C.__className = \"Point\"; writeLn([before, C.__className, C, c, c.__instanceOf == C, C.__parent, Object.__parent]);", "[[null, \"class\"], \"Point\", class Point, Point {}, true, class Object, null]")]
    [InlineData("import { writeLn } from \"std:console\"; class A {} class B extends A {} var n = null; writeLn([B.__parent, new B().__instanceOf, 5.__instanceOf, \"s\".__instanceOf, [].__instanceOf, null.__instanceOf, n?.__instanceOf]);", "[class A, class B, class Integer, class String, class Array, null, null]")]
    [InlineData("import { writeLn } from \"std:console\"; Integer.__className = \"Double\"; writeLn([Integer, 5.__instanceOf, new Class(\"P\").__className]);", "[class Double, class Double, \"P\"]")]
    [InlineData("import { writeLn } from \"std:console\"; var C = new Class(\"C\"); C.__className = null; writeLn([C, C.__className, new C()]);", "[class, null, class {}]")]
    [InlineData("import { writeLn } from \"std:console\"; var Point = new Class(\"Point\"); Point.setMethod(\"constructor\", function (this, x, y) { this.x = x; this.y = y; }); Point.setProperty(\"sum\", function (this) { return this.x + this.y; }, function (this, value) { this.x = value; this.y = 0; }); var p = new Point(3, 4); var before = p.sum; p.sum = 10; Point.setProperty(\"sum\", function (this) { return 100; }, null); writeLn([before, p, p.sum]);", "[7, Point {x: 10, y: 0}, 100]")]
    [InlineData("import { writeLn } from \"std:console\"; var Point = new Class(\"Point\"); Point.setMethod(\"constructor\", function (this, x, y) { this.x = x; this.y = y; }); Point.setStaticProperty(\"unit\", function () { return Point.unitValue; }, function (value) { Point.unitValue = value; }); Point.unitValue = 1; var before = Point.unit; Point.unit = 5; writeLn([before, Point.unit, Point.unitValue]);", "[1, 5, 5]")]
    [InlineData("import { writeLn } from \"std:console\"; var Point = new Class(\"Point\"); Point.setMethod(\"constructor\", function (this, x, y) { this.x = x; this.y = y; }); Point.setConstant(\"dimensions\", 2); var before = Point.dimensions; Point.setConstant(\"dimensions\", 3); writeLn([before, Point.dimensions, new Point(1, 2).dimensions]);", "[2, 3, 3]")]
    [InlineData("import { writeLn } from \"std:console\"; class K { static const max = 10; static make() { return 1; } } var before = [K.max, K.make()]; K.make = function () { return 2; }; extend K { static const min = 1; } writeLn([before, K.make(), K.min, new K().max]);", "[[10, 1], 2, 1, 10]")]
    [InlineData("import { writeLn } from \"std:console\"; class A { property v { get { return 1; } } } class B extends A {} A.setProperty(\"v\", function (this) { return 2; }, null); writeLn([new A().v, new B().v]);", "[2, 2]")]
    [InlineData("import { writeLn } from \"std:console\"; extend Integer {} Integer.setProperty(\"doubled\", function (this) { return this * 2; }, null); writeLn(4.doubled);", "8")]
    [InlineData("import { writeLn } from \"std:console\"; class Entity extends Class { constructor(name) { super(name); this.setMethod(\"save\", function (this) { return \"saved \" + this.toString(); }); } describe() { return \"entity \" + this.__className; } } var User = new Entity(\"User\"); var u = new User(); writeLn([User.describe(), User is Entity, User is Class, User.__instanceOf == Entity, User, u.save(), u is User]);", "[\"entity User\", true, true, true, class User, \"saved User {}\", true]")]
    [InlineData("import { writeLn } from \"std:console\"; class Meta extends Class { hello() { return \"hi \" + this.__className; } } class Meta2 extends Meta {} var A = new Meta(\"A\"); var M = new Meta2(\"M\"); writeLn([A.hello(), new A(), A.__parent, M.hello(), M is Meta]);", "[\"hi A\", A {}, class Object, \"hi M\", true]")]
    [InlineData("import { writeLn } from \"std:console\"; class Meta extends Class {} class Shape { area() { return 0; } } var Sq = new Meta(\"Sq\", Shape); writeLn([new Sq().area(), new Sq() is Shape, Sq.__parent]);", "[0, true, class Shape]")]
    [InlineData("import { writeLn } from \"std:console\"; class Meta extends Class { hello() { return \"metaclass\"; } } var A = new Meta(\"A\"); A.hello = function () { return \"static\"; }; var P = new Class(\"P\"); writeLn([A.hello(), P.__instanceOf, P is Class, P is Meta]);", "[\"static\", class Class, true, false]")]
    [InlineData("import { writeLn } from \"std:console\"; class Meta extends Class {} var C = new Meta(); writeLn([C, new C()]);", "[class, class {}]")]
    [InlineData("import { writeLn } from \"std:console\"; import { delay } from \"std:system\"; async function after(ms, v) { await delay(ms); return v; } async function fails(ms, m) { await delay(ms); throw m; } writeLn([await Task.all([after(3, \"a\"), 2, after(1, \"c\")]), await Task.all([])]);", "[[\"a\", 2, \"c\"], []]")]
    [InlineData("import { writeLn } from \"std:console\"; import { delay } from \"std:system\"; async function after(ms, v) { await delay(ms); return v; } async function fails(ms, m) { await delay(ms); throw m; } try { await Task.all([after(200, 1), fails(1, \"bad\"), fails(200, \"worse\")]); } catch (e) { writeLn(e); }", "bad")]
    [InlineData("import { writeLn } from \"std:console\"; import { delay } from \"std:system\"; async function after(ms, v) { await delay(ms); return v; } async function fails(ms, m) { await delay(ms); throw m; } writeLn([await Task.any([after(200, \"slow\"), after(1, \"fast\")]), await Task.any([after(200, \"slow\"), 5])]);", "[\"fast\", 5]")]
    [InlineData("import { writeLn } from \"std:console\"; import { delay } from \"std:system\"; async function after(ms, v) { await delay(ms); return v; } async function fails(ms, m) { await delay(ms); throw m; } try { await Task.any([after(200, 1), fails(1, \"first\")]); } catch (e) { writeLn(e); }", "first")]
    [InlineData("import { writeLn } from \"std:console\"; import { delay } from \"std:system\"; async function after(ms, v) { await delay(ms); return v; } async function fails(ms, m) { await delay(ms); throw m; } writeLn([await Task.timeout(after(1, \"in time\"), 50), await Task.timeout(7, 0)]); try { await Task.timeout(after(50, \"late\"), 2); } catch (TimeoutError e) { writeLn([e.code, e is Error]); }", "[\"in time\", 7]\n[\"WL3010\", true]")]
    [InlineData("import { writeLn } from \"std:console\"; import { delay } from \"std:system\"; async function after(ms, v) { await delay(ms); return v; } async function fails(ms, m) { await delay(ms); throw m; } writeLn(await Task.all([Task.any([after(2, \"x\")]), Task.timeout(after(1, \"y\"), 50)]));", "[\"x\", \"y\"]")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([Integer.parse(\"10\"), Integer.parse(\"-7\"), Integer.parse(\"+007\"), Number.parse(\"1.5\"), Number.parse(\"10\"), Number.parse(\"-1e3\"), Number.parse(\"Infinity\"), Boolean.parse(\"true\"), Integer.parse(\"-9223372036854775808\")]);", "[10, -7, 7, 1.5, 10.0, -1000.0, Infinity, true, -9223372036854775808]")]
    [InlineData("import { writeLn } from \"std:console\"; class Celsius extends Number {} writeLn([new Integer(\"10\"), new Number(\"2.5\"), new Boolean(\"false\"), new Celsius(\"21.5\")]);", "[10, 2.5, false, Celsius:21.5]")]
    [InlineData("import { writeLn } from \"std:console\"; class C { static var count = 0; } class D extends C {} var p = new C(); var q = new D(); p.count = 5; q.count = q.count + 1; p.other = 1; writeLn([C.count, p.count, q.count, p, C?.other]);", "[6, 6, 6, C {other: 1}, null]")]
    [InlineData("import { writeLn } from \"std:console\"; class Stack extends Array { static var made = 0; } var s = new Stack(); s.made = 3; writeLn([Stack.made, s]);", "[3, Stack:[]]")]
    [InlineData("import { writeLn } from \"std:console\"; class A { hi() { return \"A\"; } } class B extends A { hi() { return \"B\"; } } var b = new B(); var before = b.hi(); var removed = [B.removeMethod(\"hi\"), B.removeMethod(\"hi\")]; writeLn([before, removed, b.hi()]);", "[\"B\", [true, false], \"A\"]")]
    [InlineData("import { writeLn } from \"std:console\"; var P = new Class(\"P\"); P.setMethod(\"describe\", function (this) { return \"p\"; }); var p = new P(); var before = p.describe(); P.removeMethod(\"describe\"); try { p.describe(); } catch (e) { writeLn([before, e.code]); }", "[\"p\", \"WL3006\"]")]
    [InlineData("import { writeLn } from \"std:console\"; class A { constructor(x) { this.x = x; } } class B extends A { constructor() { super(1); this.y = 2; } } B.removeMethod(\"constructor\"); writeLn(new B(5));", "B {x: 5}")]
    [InlineData("import { writeLn } from \"std:console\"; class MathStuff { const PI = 3.14; static half() { return MathStuff.PI / 2; } } var m = new MathStuff(); writeLn([m.PI, MathStuff.PI, MathStuff.half()]);", "[3.14, 3.14, 1.57]")]
    [InlineData("import { writeLn } from \"std:console\"; class Tea { static var made = 0; static make() { this.made += 1; return new this(); } static named(name, strength = \"mild\") { var t = this.make(); t.name = name; t.strength = strength; return t; } } class Green extends Tea {} var g = Green.named(\"sencha\"); writeLn([Tea.make() is Tea, Green.make().__instanceOf == Green, g.name, g.strength, g is Green, Tea.made]);", "[true, true, \"sencha\", \"mild\", true, 3]")]
    [InlineData("import { writeLn } from \"std:console\"; class Tea { static each(names) { return names.map(n => new this()); } static property label { get { return $\"{this.__className} tea\"; } } } class Green extends Tea {} extend Tea { static kind() { return this.__className; } } var kind = Green.kind; var t = new Green(); writeLn([kind(), t.kind(), Tea.kind(), Green.each([1, 2]).map(x => x.__instanceOf.__className), Tea.label, Green.label]);", "[\"Green\", \"Green\", \"Tea\", [\"Green\", \"Green\"], \"Tea tea\", \"Green tea\"]")]
    [InlineData("import { writeLn } from \"std:console\"; class Stack extends Array { static of(x) { var s = new this(); s.push(x); return s; } static kind() { return this.__className; } } var s = Stack.of(1); writeLn([s, s.kind(), Stack.kind()]);", "[Stack:[1], \"Stack\", \"Stack\"]")]
    [InlineData("import { writeLn } from \"std:console\"; class Outer { m() { class Inner { static who() { return this.__className; } } return [Inner.who(), this.__instanceOf.__className]; } } class Tea { static async brew() { await delay(1); return new this(); } } class Green extends Tea {} writeLn([new Outer().m(), (await Green.brew()) is Green]);", "[[\"Inner\", \"Outer\"], true]")]
    [InlineData("import { writeLn } from \"std:console\"; class K {} extend K { const MAX = 10; } writeLn([K.MAX, new K().MAX]);", "[10, 10]")]
    [InlineData("import { writeLn } from \"std:console\"; var log = []; class A { constructor(x) { this.x = x; } sum(y) { return this.x + y; } property twice { get { return this.x * 2; } } static make() { return new A(1); } static var count = 3; static const MAX = 9; dispose() { log.push(\"A gone\"); } } var B = new Class(\"B\"); B.setMethod(\"constructor\", function (this, x) { this.x = x; }); B.setMethod(\"sum\", function (this, y) { return this.x + y; }); B.setProperty(\"twice\", function (this) { return this.x * 2; }, null); B.make = function () { return new B(1); }; B.count = 3; B.setConstant(\"MAX\", 9); B.setMethod(\"dispose\", function (this) { log.push(\"B gone\"); }); foreach (var C in [A, B]) { var c = new C(5); log.push([c.sum(1), c.twice, C.make().x, C.count, C.MAX, c is C]); c.dispose(); log.push(c.toString()); } writeLn(log);", "[[6, 10, 1, 3, 9, true], \"A gone\", \"A {x: 5}\", [6, 10, 1, 3, 9, true], \"B gone\", \"B {x: 5}\"]")]
    [InlineData("import { writeLn } from \"std:console\"; class A { constructor(x) { this.x = x; } describe() { return \"A\" + this.x.toString(); } } class D extends A { describe() { return \"D/\" + super.describe(); } } var E = new Class(\"E\", A); E.setMethod(\"describe\", function (this) { return \"E/A\" + this.x.toString(); }); writeLn([new D(1).describe(), new E(2).describe(), new E(3) is A, D.__parent == E.__parent]);", "[\"D/A1\", \"E/A2\", true, true]")]
    [InlineData("import { writeLn } from \"std:console\"; class K { static const X = 1; } extend K { static X() { return 2; } } var before = K.X(); K.X = 5; K.X = 6; writeLn([before, K.X]);", "[2, 6]")]
    [InlineData("import { writeLn } from \"std:console\"; var add = (a, b) => { return a + b; }; var none = () => { return \"none\"; }; writeLn([add(2, 3), none(), none]);", "[5, \"none\", function]")]
    [InlineData("import { writeLn } from \"std:console\"; var g = (a, b = 10) => { return a + b; }; var k = 5; var h = (x) => { return x + k; }; k = 6; writeLn([g(1), g(1, 2), h(1)]);", "[11, 3, 7]")]
    [InlineData("import { writeLn } from \"std:console\"; var P = new Class(\"P\"); P.setMethod(\"sum\", (this) => { return this.x + this.y; }); var p = new P(); p.x = 1; p.y = 2; var describe = (this, prefix) => { return prefix + this.x.toString(); }; writeLn([p.sum(), describe(p, \"x = \")]);", "[3, \"x = 1\"]")]
    [InlineData("import { writeLn } from \"std:console\"; class A { constructor() { this.v = 7; } get() { var f = () => { return this.v; }; return f(); } } writeLn(new A().get());", "7")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([((x) => { return x * 2; })(21), (1 + 2) * 3, ((4))]);", "[42, 9, 4]")]
    [InlineData("import { writeLn } from \"std:console\"; var add = (a, b) => a + b; var twice = x => x * 2; var none = () => null; writeLn([add(2, 3), twice(21), none()]);", "[5, 42, null]")]
    [InlineData("import { writeLn } from \"std:console\"; var k = 1; var f = x => x + k; k = 2; var add = a => b => a + b; function apply(g, v) { return g(v); } writeLn([f(1), add(2)(3), apply(x => x + 1, 41)]);", "[3, 5, 42]")]
    [InlineData("import { writeLn } from \"std:console\"; var m = () => ({a: 1}); var b = x => { }; writeLn([m(), b(1)]);", "[{\"a\": 1}, null]")]
    [InlineData("import { writeLn } from \"std:console\"; import { delay } from \"std:system\"; var f = async x => { await delay(1); return x * 2; }; var g = async (a, b) => a + b; var h = async x => await f(x) + 1; writeLn([await f(21), await g(1, 2), await h(1)]);", "[42, 3, 3]")]
    [InlineData("import { writeLn } from \"std:console\"; var s = \"\"\"\n    a\n      b\n    \"\"\"; writeLn(s);", "a\n  b")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn('''x \"y\" 'z' \\n''');", "x \"y\" 'z' \\n")]
    [InlineData("import { writeLn } from \"std:console\"; var name = \"Anna\"; writeLn($\"Hi, {name}! {1 + 2}{{}} \\{x\\} [{null}]\");", "Hi, Anna! 3{} {x} []")]
    [InlineData("import { writeLn } from \"std:console\"; class P { toString() { return \"a P\"; } } writeLn($\"{new P()}|{[1, \"a\"]}|{true}|{1.5}\");", "a P|[1, \"a\"]|true|1.5")]
    [InlineData("import { writeLn } from \"std:console\"; var n = 2; writeLn($$\"\"\"\n  { \"n\": {{n}}, \"s\": \"{{\"x\" + n.toString()}}\" }\n  \"\"\");", "{ \"n\": 2, \"s\": \"x2\" }")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([$\"{$\"[{1}]\"}\", $\"\", $\"a\", $\"{\"b\"}\"]);", "[\"[1]\", \"\", \"a\", \"b\"]")]
    [InlineData("import { writeLn } from \"std:console\"; var Text = 5; writeLn($\"{Text}\");", "5")]
    [InlineData("import { writeLn } from \"std:console\"; var price = 1234.5; writeLn($\"{price:N2}|{42:D5}|{255:X4}|{1.5:0.00}|{12345.678:E3}|{-7:0;(0)}|{0.125:F2}|{0.125:0.00}\");", "1,234.50|00042|00FF|1.50|1.235E+004|(7)|0.12|0.13")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn($\"[{\"ab\",5}][{\"ab\",-5}][{1.5,6:F2}][{null,3}][{null:N2}][{\"abc\",2}]\");", "[   ab][ab   ][  1.50][   ][][abc]")]
    [InlineData("import { writeLn } from \"std:console\"; var w = 4; writeLn($\"[{7,w}][{7,-w}][{[1, 2],8}][{7, 0}]\");", "[   7][7   ][  [1, 2]][7]")]
    [InlineData("import { writeLn } from \"std:console\"; class C extends Number {} class I extends Integer {} writeLn($\"{new C(2.5):F3} {new I(5):D2}\");", "2.500 05")]
    [InlineData("import { writeLn } from \"std:console\"; var p = 1234.5; writeLn($$\"\"\"{\"p\": {{p,8:F1}}, \"q\": \"{{\"x\",-3}}\"}\"\"\");", "{\"p\":   1234.5, \"q\": \"x  \"}")]
    [InlineData("import { writeLn } from \"std:console\"; class P { toString() { return \"a P\"; } } writeLn($\"[{new P(),5}]\");", "[  a P]")]
    // % (the owner's answers, 16 November 2026): the dividend's sign; by -1 always 0; a number's as IEEE 754's.
    [InlineData("import { writeLn } from \"std:console\"; writeLn([7 % 3, -7 % 3, 7 % -3, -7 % -3, 5.5 % 2, -5.5 % 2, 7 % 2.5, 6 % 3]);", "[1, -1, 1, -1, 1.5, -1.5, 2.0, 0]")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([(-9223372036854775807 - 1) % -1, 7.0 % 0, 1 + 7 % 4 * 2]);", "[0, NaN, 7]")]
    [InlineData("import { writeLn } from \"std:console\"; class C { constructor(v) { this.v = v; } static operator %(a, b) { return a.v % b; } } writeLn(new C(7) % 4);", "3")]
    [InlineData("import { writeLn } from \"std:console\"; try { writeLn(7 % 0); } catch (DivisionByZeroError e) { writeLn([e.code, e.message, e is Error]); }", "[\"WL3013\", \"7 % 0 divides by zero.\", true]")]
    // \ (the owner's answers, 18 November 2026, "Mathematics"): two integers' quotient, rounded towards zero, pairing with %; by 0 WL3013, the smallest by -1 WL3002, anything but integers WL3001.
    [InlineData("import { writeLn } from \"std:console\"; writeLn([7 \\ 2, -7 \\ 2, 7 \\ -2, -7 \\ -2, 0 \\ 5, 9223372036854775807 \\ 2, (-9223372036854775807 - 1) \\ 2]);",
        "[3, -3, -3, 3, 0, 4611686018427387903, -4611686018427387904]")]
    [InlineData("import { writeLn } from \"std:console\"; var ok = true; foreach (var a in [-7, -6, 0, 6, 7]) { foreach (var b in [-3, -2, 2, 3]) { ok = ok && a == (a \\ b) * b + a % b; } } writeLn(ok);",
        "true")]
    [InlineData("import { writeLn } from \"std:console\"; var n = 17; n \\= 5; writeLn([n, $\"{17 \\ 5}\", 2 * 7 \\ 3, 7 \\ 2 * 3, 1 + 7 \\ 2, 7 \\ 2 % 2]);",
        "[3, \"3\", 4, 9, 4, 1]")]
    [InlineData("import { writeLn } from \"std:console\"; class Money { constructor(c) { this.c = c; } static operator \\(a, b) { return new Money(a.c \\ b); } } class Count extends Integer {} writeLn([(new Money(1000) \\ 3).c, new Count(7) \\ 2]);",
        "[333, Count:3]")]
    [InlineData("import { writeLn } from \"std:console\"; foreach (var f in [() => 7 \\ 0, () => (-9223372036854775807 - 1) \\ -1, () => 7.0 \\ 2, () => 7 \\ 2m, () => \"7\" \\ 2]) { try { writeLn(f()); } catch (e) { writeLn($\"{e.__instanceOf.__className} {e.code}: {e.message}\"); } }",
        "DivisionByZeroError WL3013: 7 \\ 0 divides by zero.\nOverflowError WL3002: -9223372036854775808 \\ -1 does not fit in a 64-bit integer.\nTypeError WL3001: '\\' divides two integers, not the number 7.0 and the integer 2: make a number an integer first, with std:math's floor, round or truncate.\nTypeError WL3001: '\\' divides two integers, not the integer 7 and the decimal 2m: make a number an integer first, with std:math's floor, round or truncate.\nTypeError WL3001: '\\' divides two integers, not a string and the integer 2.")]
    // Compound assignment, ++ and --, ??, ??=, ?: and if expressions (the owner's answers, 16 November 2026).
    [InlineData("import { writeLn } from \"std:console\"; var x = 5; x += 3; x -= 1; x *= 2; x /= 7; var n = 10; n %= 4; writeLn([x, n]);", "[2.0, 2]")]
    [InlineData("import { writeLn } from \"std:console\"; var calls = 0; function at() { calls = calls + 1; return 1; } var a = [10, 20, 30]; a[at()] += 5; writeLn([a, calls]);", "[[10, 25, 30], 1]")]
    [InlineData("import { writeLn } from \"std:console\"; var i = 0; writeLn([i++, i, ++i, i, i--, --i]);", "[0, 1, 2, 2, 2, 0]")]
    [InlineData("import { writeLn } from \"std:console\"; var b = [1, 2]; var j = 0; b[j++] += 10; writeLn([b, j]);", "[[11, 2], 1]")]
    [InlineData("import { writeLn } from \"std:console\"; var x = 1; var y = (x += 1) * 10; writeLn([x, y]);", "[2, 20]")]
    [InlineData("import { writeLn } from \"std:console\"; class P { constructor() { this.v = 1; } } var p = new P(); p.v += 41; p.v++; writeLn(p.v);", "43")]
    [InlineData("import { writeLn } from \"std:console\"; var s = \"a\"; s += \"b\"; writeLn(s);", "ab")]
    [InlineData("import { writeLn } from \"std:console\"; var u = null; u ??= \"set\"; u ??= \"not again\"; var e = { a: null }; e[\"a\"] ??= 7; writeLn([u, e, null ?? null ?? 3, 0 ?? 1]);", "[\"set\", {\"a\": 7}, 3, 0]")]
    [InlineData("import { writeLn } from \"std:console\"; var o = null; writeLn(o?.name ?? \"stranger\");", "stranger")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([true ? \"yes\" : \"no\", false ? 1 : 2 > 1 ? \"nested\" : \"no\", true ? [1] : [2]]);", "[\"yes\", \"nested\", [1]]")]
    [InlineData("import { writeLn } from \"std:console\"; var f = v => if (v % 2 == 0) \"even\" else \"odd\"; writeLn([f(2), f(3), if (true) 1 else 2]);", "[\"even\", \"odd\", 1]")]
    [InlineData("import { writeLn } from \"std:console\"; var calls = 0; function f() { calls = calls + 1; return null; } writeLn([f() ?? \"x\", calls, true ? \"a\" : f(), calls]);", "[\"x\", 1, \"a\", 1]")]
    [InlineData("import { writeLn } from \"std:console\"; var x = 2; writeLn($\"{(x > 1 ? \"big\" : \"small\")}\");", "big")]
    // Strings compared by their Unicode scalar values (the owner's answers, 16 November 2026).
    [InlineData("import { writeLn } from \"std:console\"; writeLn([\"a\" < \"b\", \"b\" <= \"a\", \"abc\" > \"ab\", \"\" >= \"\", \"B\" < \"a\", \"é\" > \"z\"]);", "[true, false, true, true, true, true]")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([\"\\u{FFFD}\" < \"\\u{1F375}\", \"\\u{1F375}\".compare(\"\\u{FFFD}\"), \"a\".compare(\"a\"), \"a\".compare(\"b\")]);", "[true, 1, 0, -1]")]
    // switch, a statement of case blocks and an expression of arms (the owner's answers, 16 November 2026).
    [InlineData("import { writeLn } from \"std:console\"; class Point { constructor(x) { this.x = x; } } function d(v) { switch (v) { case 1, 2 { return \"low\"; } case Point p { return p.x; } default { return \"other\"; } } } writeLn([d(1), d(2), d(new Point(7)), d(null)]);", "[\"low\", \"low\", 7, \"other\"]")]
    [InlineData("import { writeLn } from \"std:console\"; var total = 0; var i = 0; while (i < 6) { i++; switch (i % 3) { case 0 { continue; } case 1 { total += 10; } default { total += 1; } } if (i == 5) break; } writeLn([total, i]);", "[22, 5]")]
    [InlineData("import { writeLn } from \"std:console\"; switch (\"x\") { case \"y\" { writeLn(\"no\"); } } writeLn(\"after\");", "after")]
    [InlineData("import { writeLn } from \"std:console\"; var calls = 0; function next() { calls++; return 2; } writeLn([next() switch { 1 => \"one\", 2 => \"two\", _ => \"many\" }, calls]);", "[\"two\", 1]")]
    [InlineData("import { writeLn } from \"std:console\"; class C { constructor(r) { this.r = r; } } writeLn([new C(2) switch { C c => c.r * 3, _ => 0 }, 7 switch { 1, 2 => \"a\", _ => \"b\" } + \"!\"]);", "[6, \"b!\"]")]
    // Decimals (the owner's answers, 16 November 2026): .NET's decimal; integers converted exactly; written 1.5m.
    [InlineData("import { writeLn } from \"std:console\"; writeLn([1.50m, 0.1m + 0.2m, 1.5m * 2, 7m / 2, 10m % 3, -1.5m, 2.000m, 100m]);", "[1.5m, 0.3m, 3m, 3.5m, 1m, -1.5m, 2m, 100m]")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([1.5m == 1.50m, 1m == 1, 1.5m == 1.5, 2.5m > 2, 1.1m < 1.2m]);", "[true, true, false, true, true]")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([1.5m is Decimal, Decimal.parse(\"3.25\"), new Decimal(4), new Decimal(\"2.50\"), new Decimal(0.1), new Number(2.5m), new Integer(3m), new Decimal()]);", "[true, 3.25m, 4m, 2.5m, 0.1m, 2.5, 3, 0m]")]
    [InlineData("import { writeLn } from \"std:console\"; var p = 1.5m; writeLn($\"{p:0.00} {1234.5m:N1} {p}\");", "1.50 1,234.5 1.5m")]
    [InlineData("import { writeLn } from \"std:console\"; var total = 0m; foreach (var x in [1.10m, 2.20m, 3.30m]) total += x; writeLn(total);", "6.6m")]
    [InlineData("import { writeLn } from \"std:console\"; class Money extends Decimal {} var m = new Money(1.25m); writeLn([m, m + 1, m is Decimal]);", "[Money:1.25m, Money:2.25m, true]")]
    // Time zones (the owner's answers, 16 November 2026): the platform's, by their IANA names; a time skipped or had twice
    // takes the zone's standard offset.
    [InlineData("import { writeLn } from \"std:console\"; import { DateTime } from \"std:datetime\"; var d = DateTime.utc(2026, 7, 1, 12); writeLn([d.toZone(\"Europe/Warsaw\"), d.addMonths(6).toZone(\"Europe/Warsaw\"), d.toZone(\"Asia/Kolkata\"), d.toZone(\"UTC\")]);", "[DateTime:2026-07-01T14:00:00+02:00, DateTime:2027-01-01T13:00:00+01:00, DateTime:2026-07-01T17:30:00+05:30, DateTime:2026-07-01T12:00:00Z]")]
    [InlineData("import { writeLn } from \"std:console\"; import { DateTime } from \"std:datetime\"; var d = DateTime.inZone(\"America/New_York\", 2026, 11, 16, 9, 30); writeLn([d, d.toUtc(), d.toZone(\"Europe/Warsaw\").hour]);", "[DateTime:2026-11-16T09:30:00-05:00, DateTime:2026-11-16T14:30:00Z, 15]")]
    [InlineData("import { writeLn } from \"std:console\"; import { DateTime } from \"std:datetime\"; writeLn([DateTime.inZone(\"Europe/Warsaw\", 2026, 3, 29, 2, 30), DateTime.inZone(\"Europe/Warsaw\", 2026, 10, 25, 2, 30)]);", "[DateTime:2026-03-29T02:30:00+01:00, DateTime:2026-10-25T02:30:00+01:00]")]
    // Hexadecimal and binary literals (the owner's answers, 16 November 2026): int64s, their letters in either case.
    [InlineData("import { writeLn } from \"std:console\"; writeLn([0x1F, 0X1f, 0b1010, 0B11, 0xff + 1, -0x10, 0x7FFFFFFFFFFFFFFF, 0b0]);", "[31, 31, 10, 3, 256, -16, 9223372036854775807, 0]")]
    [InlineData("import { writeLn } from \"std:console\"; var flags = 0b0101; writeLn([flags == 5, 0x10 * 0b10, $\"{0xFF:X4}\"]);", "[true, 32, \"00FF\"]")]
    // Digit separators (the owner's answer, 16 November 2026): `_` between two digits, one at a time.
    [InlineData("import { writeLn } from \"std:console\"; writeLn([1_000_000, 0.000_1, 1e1_0, 0xFF_FF, 0b1010_0101, 1_000.50m, 9_223_372_036_854_775_807]);", "[1000000, 0.0001, 10000000000.0, 65535, 165, 1000.5m, 9223372036854775807]")]
    // Rest parameters (the owner's answer, 16 November 2026): JavaScript's, ...rest, an array of the arguments after the others.
    [InlineData("import { writeLn } from \"std:console\"; function sum(...numbers) { var total = 0; foreach (var n in numbers) total += n; return total; } writeLn([sum(), sum(1), sum(1, 2, 3)]);", "[0, 1, 6]")]
    [InlineData("import { writeLn } from \"std:console\"; function tag(name, size = 1, ...rest) { return [name, size, rest]; } writeLn([tag(\"a\"), tag(\"a\", 2), tag(\"a\", 2, 3, 4)]);", "[[\"a\", 1, []], [\"a\", 2, []], [\"a\", 2, [3, 4]]]")]
    [InlineData("import { writeLn } from \"std:console\"; var count = (...xs) => xs.length; class Box { constructor(...items) { this.items = items; } add(...more) { return this.items.length + more.length; } } var b = new Box(1, 2); writeLn([count(), count(1, 2), b.items, b.add(3, 4, 5)]);", "[0, 2, [1, 2], 5]")]
    [InlineData("import { writeLn } from \"std:console\"; async function later(first, ...others) { await null; return others; } function own(this, ...xs) { return xs; } writeLn([await later(1, 2, 3), own(null, 4, 5)]);", "[[2, 3], [4, 5]]")]
    [InlineData("import { writeLn } from \"std:console\"; function f(...xs) { return () => xs; } var g = f(1, \"two\"); writeLn([g(), g() == g()]);", "[[1, \"two\"], true]")]
    // Spreading (the owner's answers, 16 November 2026): into calls and array, map and set literals; anything foreach visits.
    [InlineData("import { writeLn } from \"std:console\"; function sum(...xs) { var t = 0; foreach (var x in xs) t += x; return t; } var a = [1, 2, 3]; writeLn([sum(...a), sum(0, ...a, 10), sum(...#{5}), sum(...[])]);", "[6, 16, 5, 0]")]
    [InlineData("import { writeLn } from \"std:console\"; var a = [1, 2]; writeLn([[...a, 3, ...a], [0, ...\"hé\"], #{...a, 2, 3}]);", "[[1, 2, 3, 1, 2], [0, \"h\", \"é\"], #{1, 2, 3}]")]
    [InlineData("import { writeLn } from \"std:console\"; var d = { size: 1, color: \"red\" }; var m = { ...d, size: 2, ...{ shape: \"round\" } }; writeLn([m[\"size\"], m[\"color\"], m[\"shape\"], m.length, d[\"size\"]]);", "[2, \"red\", \"round\", 3, 1]")]
    [InlineData("import { writeLn } from \"std:console\"; class R { constructor(n) { this.n = n; } iterate() { return new I(this.n); } } class I { constructor(n) { this.i = 0; this.n = n; } hasNext() { return this.i < this.n; } next() { this.i += 1; return this.i; } } writeLn([...new R(3)]);", "[1, 2, 3]")]
    [InlineData("import { writeLn } from \"std:console\"; class P { constructor(x, y) { this.x = x; this.y = y; } sum(...more) { return this.x + this.y + more.length; } } class Q extends P { constructor(...xs) { super(...xs); } } var q = new Q(...[1, 2]); writeLn([q.x, q.sum(...[7, 8])]);", "[1, 5]")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(...[\"done\"]);", "done")]
    [InlineData("import { writeLn } from \"std:console\"; var big = []; for (var i = 0; i < 1000; i++) big.push(i); function count(...xs) { return xs.length; } writeLn([count(...big), count(...big, ...big)]);", "[1000, 2000]")]
    [InlineData("import { writeLn } from \"std:console\"; var log = []; function f(...xs) { return xs; } function t(x) { log.push(x); return [x]; } var r = f(...t(1), t(2), ...t(3)); writeLn([log, r]);", "[[1, 2, 3], [1, [2], 3]]")]
    [InlineData("import { writeLn } from \"std:console\"; async function later(...xs) { await null; return xs.length; } writeLn(await later(...[1, 2, 3]));", "3")]
    // Strings' methods (the owner's answers, 16 November 2026): C#'s names and JavaScript's; case by the invariant culture.
    [InlineData("import { writeLn } from \"std:console\"; var s = \"Green tea, black tea\"; writeLn([s.indexOf(\"tea\"), s.indexOf(\"tea\", 7), s.lastIndexOf(\"tea\"), s.indexOf(\"coffee\"), s.lastIndexOf(\"\")]);", "[6, 17, 17, -1, 20]")]
    [InlineData("import { writeLn } from \"std:console\"; var s = \"Green tea, black tea\"; writeLn([s.contains(\"black\"), s.includes(\"white\"), s.startsWith(\"Green\"), s.endsWith(\"tea\")]);", "[true, false, true, true]")]
    [InlineData("import { writeLn } from \"std:console\"; var s = \"Green tea, black tea\"; writeLn([s.slice(6), s.slice(0, 5), s.slice(5, 5)]);", "[\"tea, black tea\", \"Green\", \"\"]")]
    [InlineData("import { writeLn } from \"std:console\"; var s = \"Green tea, black tea\"; writeLn([s.split(\", \"), \"a,,b\".split(\",\"), \"hé😀\".split(\"\")]);", "[[\"Green tea\", \"black tea\"], [\"a\", \"\", \"b\"], [\"h\", \"é\", \"😀\"]]")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([\"  x  \".trim(), \"  x  \".trimStart(), \"  x  \".trimEnd()]);", "[\"x\", \"x  \", \"  x\"]")]
    // Given characters, any of them trimmed, each a Unicode scalar value, an empty one trimming nothing; and a fixed text removed
    // once, only where the string begins or ends with it (the owner's answers, 18 November 2026).
    [InlineData("import { writeLn } from \"std:console\"; writeLn([\"a//\".trimEnd(\"/\"), \"x;,;\".trimEnd(\";,\"), \"//a//\".trimStart(\"/\"), \"//a//\".trim(\"/\"), \"aaa\".trim(\"a\"), \"a\".trim(\"\")]);",
        "[\"a\", \"x\", \"a//\", \"a\", \"\", \"a\"]")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([\"--😀a😀--\".trim(\"-😀\"), \"😀b\".trimStart(\"😃\"), \"C:\\\\Tea\\\\\".toLower().trimEnd(\"\\\\\") + \"\\\\\"]);",
        "[\"a\", \"😀b\", \"c:\\\\tea\\\\\"]")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([\"report.txt\".removeSuffix(\".txt\"), \"report.txt\".trimEnd(\".txt\"), \"report.txt\".removePrefix(\"re\"), \"report\".removeSuffix(\".txt\"), \".txt.txt\".removeSuffix(\".txt\"), \"tea\".removePrefix(\"\")]);",
        "[\"report\", \"repor\", \"port.txt\", \"report\", \".txt\", \"tea\"]")]
    [InlineData("import { writeLn } from \"std:console\"; var s = \"Green tea, black tea\"; writeLn([s.toUpper(), s.toUpperCase(), \"ÉCOLE\".toLower(), \"Straße\".toUpperCase(), \"ABC\".toLowerCase()]);", "[\"GREEN TEA, BLACK TEA\", \"GREEN TEA, BLACK TEA\", \"école\", \"STRAßE\", \"abc\"]")]
    [InlineData("import { writeLn } from \"std:console\"; var s = \"Green tea, black tea\"; writeLn([s.replace(\"tea\", \"coffee\"), s.replaceAll(\"tea\", \"chai\"), \"aaa\".replace(\"aa\", \"b\")]);", "[\"Green coffee, black coffee\", \"Green chai, black chai\", \"ba\"]")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([\"ab\".repeat(3), \"x\".repeat(0), \"\".repeat(1000000000000)]);", "[\"ababab\", \"\", \"\"]")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([\"7\".padStart(3, \"0\"), \"7\".padLeft(3), \"7\".padEnd(4, \"ab\"), \"7\".padRight(2), \"long\".padStart(2), \"😀\".padStart(3, \"é\")]);", "[\"007\", \"  7\", \"7aba\", \"7 \", \"long\", \"éé😀\"]")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([\", \".join([\"a\", 1, 2.5, true, null, 1.5m]), \"\".join([]), \"-\".join([\"x\"])]);", "[\"a, 1, 2.5, true, , 1.5m\", \"\", \"x\"]")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([\"😀x😀\".indexOf(\"x\"), \"😀x😀\".lastIndexOf(\"😀\"), \"😀x😀\".slice(1, 2), \"😀x\".padEnd(3, \"y\")]);", "[1, 2, \"x\", \"😀xy\"]")]
    // Arrays' methods (the owner's answers, 16 November 2026): with aliases; sort() and reverse() a new array; sort by value.
    [InlineData("import { writeLn } from \"std:console\"; var a = [3, 1, 2]; writeLn([a.map(x => x * 2), a.select(x => x + 1), a.filter(x => x > 1), a.where(x => x < 3)]);", "[[6, 2, 4], [4, 2, 3], [3, 2], [1, 2]]")]
    [InlineData("import { writeLn } from \"std:console\"; var a = [3, 1, 2]; writeLn([a.reduce((sum, x) => sum + x, 0), a.find(x => x > 1), a.find(x => x > 5), a.any(x => x == 2), a.some(x => x > 9)]);", "[6, 3, null, true, false]")]
    [InlineData("import { writeLn } from \"std:console\"; var a = [3, 1, 2]; writeLn([a.all(x => x > 0), a.every(x => x > 1), a.sort(), a, a.reverse(), a.sort((x, y) => y - x)]);", "[true, false, [1, 2, 3], [3, 1, 2], [2, 1, 3], [3, 2, 1]]")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([[\"b\", \"a\", \"c\"].sort(), [2.5, 1, 3].sort(), [].sort(), [1].sort(), [\"bb\", \"a\"].sort((x, y) => x.length - y.length)]);", "[[\"a\", \"b\", \"c\"], [1, 2.5, 3], [], [1], [\"a\", \"bb\"]]")]
    [InlineData("import { writeLn } from \"std:console\"; var a = [3, 1, 2]; writeLn([a.indexOf(2), a.indexOf(9), a.contains(1), a.includes(1.0), a.slice(1), a.concat([4, 5]), a.join(\"-\"), a.join()]);", "[2, -1, true, true, [1, 2], [3, 1, 2, 4, 5], \"3-1-2\", \"3,1,2\"]")]
    [InlineData("import { writeLn } from \"std:console\"; var b = [1, 2, 3]; b.insert(0, 0); b.insert(4, 4); writeLn([b.removeAt(1), b]);", "[1, [0, 2, 3, 4]]")]
    [InlineData("import { writeLn } from \"std:console\"; var people = [{ name: \"Anna\", age: 30 }, { name: \"Bob\", age: 25 }, { name: \"Cy\", age: 30 }]; writeLn(people.sort((x, y) => x[\"age\"] - y[\"age\"]).map(p => p[\"name\"]));", "[\"Bob\", \"Anna\", \"Cy\"]")]
    [InlineData("import { writeLn } from \"std:console\"; var a = [3, 1, 2]; async function twice(x) { await null; return x * 2; } writeLn([await a.map(twice), await a.filter(async x => { await null; return x != 1; }), await a.reduce(async (s, x) => { await null; return s + x; }, 10), await a.find(async x => x == 2), await a.any(async x => x == 7), await a.all(async x => x < 4)]);", "[[6, 2, 4], [3, 2], 16, 2, false, true]")]
    [InlineData("import { writeLn } from \"std:console\"; class Stack extends Array {} var s = new Stack(); s.push(5); s.push(4); writeLn([s.sort(), s.map(x => x + 1), s.contains(4)]);", "[[4, 5], [6, 5], true]")]
    // Regular expressions (the owner's answers, 16 November 2026): .NET's syntax; a class only, new Regex(pattern, flags).
    [InlineData("import { writeLn } from \"std:console\"; import { Regex } from \"std:regex\"; var r = new Regex(\"(\\\\w+)@(\\\\w+)\\\\.com\"); var text = \"anna@tea.com, bob@cake.com\"; writeLn([r, r is Regex, r.pattern, r.flags, r.test(text), r.isMatch(\"nobody\")]);", "[Regex:/(\\w+)@(\\w+)\\.com/, true, \"(\\\\w+)@(\\\\w+)\\\\.com\", \"\", true, false]")]
    [InlineData("import { writeLn } from \"std:console\"; import { Regex } from \"std:regex\"; var r = new Regex(\"(\\\\w+)@(\\\\w+)\\\\.com\"); var text = \"anna@tea.com, bob@cake.com\"; writeLn([r.match(text), r.exec(\"none\"), r.matchAll(text), r.matches(text).length]);", "[[\"anna@tea.com\", \"anna\", \"tea\"], null, [[\"anna@tea.com\", \"anna\", \"tea\"], [\"bob@cake.com\", \"bob\", \"cake\"]], 2]")]
    [InlineData("import { writeLn } from \"std:console\"; import { Regex } from \"std:regex\"; var r = new Regex(\"(\\\\w+)@(\\\\w+)\\\\.com\"); var text = \"anna@tea.com, bob@cake.com\"; writeLn([r.replace(text, \"$2:$1\"), text.replace(r, \"<$0>\"), new Regex(\",\\\\s*\").split(text), text.split(new Regex(\", \"))]);", "[\"tea:anna, cake:bob\", \"<anna@tea.com>, <bob@cake.com>\", [\"anna@tea.com\", \"bob@cake.com\"], [\"anna@tea.com\", \"bob@cake.com\"]]")]
    [InlineData("import { writeLn } from \"std:console\"; import { Regex } from \"std:regex\"; var r = new Regex(\"(\\\\w+)@(\\\\w+)\\\\.com\"); var text = \"anna@tea.com, bob@cake.com\"; writeLn([text.match(r), text.matchAll(r).length]);", "[[\"anna@tea.com\", \"anna\", \"tea\"], 2]")]
    [InlineData("import { writeLn } from \"std:console\"; import { Regex } from \"std:regex\"; var i = new Regex(\"^TEA$\", \"im\"); writeLn([i.test(\"green\\ntea\"), i, $\"{i}\"]);", "[true, Regex:/^TEA$/im, \"Regex:/^TEA$/im\"]")]
    [InlineData("import { writeLn } from \"std:console\"; import { Regex } from \"std:regex\"; writeLn(new Regex(\"(a)|(b)\").match(\"b\"));", "[\"b\", null, \"b\"]")]
    [InlineData("import { writeLn } from \"std:console\"; import { Regex } from \"std:regex\"; class Strict extends Regex {} var s = new Strict(\"x+\"); writeLn([s is Regex, s is Strict]);", "[true, true]")]
    // A class extending a kind of value's has its value's methods, as it has its properties, each giving what the value's gives;
    // a member neither its class nor its value has is the instance's error (section 3.7, "No sealed classes").
    [InlineData("import { writeLn } from \"std:console\"; class Name extends String {} var n = new Name(\"anna\"); writeLn([n.length, n.toUpper(), n.startsWith(\"a\"), n.split(\"n\"), n.toUpper() is Name, n.iterate().next()]);",
        "[4, \"ANNA\", true, [\"a\", \"\", \"a\"], false, \"a\"]")]
    [InlineData("import { writeLn } from \"std:console\"; import { Regex } from \"std:regex\"; class Strict extends Regex { twice(s) { return [this.test(s), this.match(s)]; } } var s = new Strict(\"a+\"); writeLn([s.test(\"caab\"), s.twice(\"x\"), s.replace(\"caab\", \"-\"), s.pattern]);",
        "[true, [false, null], \"c-b\", \"a+\"]")]
    [InlineData("import { writeLn } from \"std:console\"; class Name extends String {} extend String { shout() { return this.toUpper() + \"!\"; } } var n = new Name(\"hi\"); writeLn([n.shout(), \"yo\".shout()]);",
        "[\"HI!\", \"YO!\"]")]
    [InlineData("import { writeLn } from \"std:console\"; class Name extends String {} try { new Name(\"a\").nope(); } catch (e) { writeLn(e.code + \" \" + e.message); }",
        "WL3006 An instance of Name has no member 'nope'.")]
    // Json and Html extended, a class extending one reaching their class methods, the runtime's too (section 3.7).
    [InlineData("import { writeLn } from \"std:console\"; import { Json } from \"std:json\"; import { Html } from \"std:html\"; class J extends Json {} class H extends Html {} writeLn([J.stringify([1]), J.parse(\"[2]\"), new J(), H.escape(\"<b>\"), H.js(\"x\"), new J() is Json]);",
        "[\"[1]\", [2], J {}, \"&lt;b&gt;\", \"\\\"x\\\"\", true]")]
    [InlineData("import { writeLn } from \"std:console\"; class I extends Integer {} class B extends Boolean {} writeLn([I.parse(\"10\"), B.parse(\"true\"), new I(3) + 1]);", "[10, true, I:4]")]
    // A module defined in code: new Module(name), and a class extending Module, whose instances are modules, with fields, their
    // class's methods, and exec(), which they may override (section 3.7, "No sealed classes").
    [InlineData("import { writeLn } from \"std:console\"; class Plugin extends Module { constructor(name) { super(name); this.count = 0; } run() { this.count += 1; return this.count; } } var p = new Plugin(\"tools\"); writeLn([p is Module, p is Plugin, p.run(), p.run(), p.count, p]); var m = new Module(\"plain\"); writeLn([m, m is Module, m is Plugin]);",
        "[true, true, 1, 2, 2, Plugin:module tools]\n[module plain, true, false]")]
    [InlineData("import { writeLn } from \"std:console\"; class Plugin extends Module { constructor(name, size) { super(name); this.size = size; } async exec() { return this.size * 2; } } class Plain extends Module {} writeLn([await new Plugin(\"x\", 21).exec(), await new Plain(\"q\").exec(), new Plain(\"q\").__instanceOf == Plain]);",
        "[42, null, true]")]
    [InlineData("import { writeLn } from \"std:console\"; class Plugin extends Module {} try { new Plugin(5); } catch (e) { writeLn(e.message); } try { new Module(); } catch (e) { writeLn(e.message); } try { new Module(\"m\").y = 2; } catch (e) { writeLn(e.code); } var p = new Plugin(\"p\"); p.y = 2; writeLn(p.y);",
        "The argument of 'Module' must be a module's name, a string, not the integer 5.\n'Module' takes 1 argument, not 0.\nWL3001\n2")]
    // Wherever a built-in takes a value of a kind, it takes an instance holding one, as that value, but where it keeps what it
    // is given, push, or writes it, write (section 3.7, "No sealed classes").
    [InlineData("import { writeLn } from \"std:console\"; import { Bytes } from \"std:bytes\"; import { DateTime } from \"std:datetime\"; import { Regex } from \"std:regex\"; import crypto from \"std:crypto\"; class Name extends String {} class Index extends Integer {} class Blob extends Bytes {} class Pattern extends Regex {} writeLn([\"ab\".startsWith(new Name(\"a\")), \"tea\".indexOf(new Name(\"e\"), new Index(0)), [10, 20][new Index(1)], \"abc\"[new Index(2)]]); var m = {}; m[new Name(\"k\")] = 1; writeLn([m[\"k\"], m.has(new Name(\"k\")), #{}.add(new Name(\"v\"))]); writeLn([crypto.sha256(new Blob(\"x\")) == crypto.sha256(\"x\"), Bytes.concat([new Blob(\"a\"), new Blob(\"b\")]).text(), \", \".join([new Name(\"a\"), 1])]); writeLn([\"aab\".replace(new Pattern(\"a+\"), new Name(\"x\")), DateTime.utc(new Index(2026), 1, 1).year, [1, 2, 3].slice(new Index(1))]); writeLn(new Name(\"printed\")); var a = []; a.push(new Name(\"kept\")); writeLn(a[0] is Name);",
        "[true, 1, 20, \"c\"]\n[1, true, true]\n[true, \"ab\", \"a, 1\"]\n[\"xb\", 2026, [2, 3]]\nName:\"printed\"\ntrue")]
    // A class extending Task makes tasks of its own, completed by hand, awaited and combined as any task, its own members first
    // (section 3.7, "No sealed classes").
    [InlineData("import { writeLn } from \"std:console\"; class Job extends Task { constructor(name) { super(); this.name = name; } finish(value) { return this.complete($\"{this.name}: {value}\"); } } var job = new Job(\"report\"); writeLn([job is Task, job is Job, job]); async function later() { await delay(5); job.finish(42); } later(); writeLn(await job); writeLn([job.finish(1), job.name, (await Task.all([job, 1]))[0]]);",
        "[true, true, Job:task]\nreport: 42\n[false, \"report\", \"report: 42\"]")]
    [InlineData("import { writeLn } from \"std:console\"; class Job extends Task {} var other = new Job(); writeLn(other.fail(new Error(\"no\"))); try { await other; } catch (e) { writeLn(e.message); } var stuck = new Job(); try { await stuck; } catch (DeadlockError e) { writeLn(e.message); }",
        "true\nno\nThe program waits for a task nothing can complete: awaited at line 1.")]
    // A task completed by hand: new Task(), complete(value) and fail(error), whether they completed it, an error on a task new
    // did not make; a program stuck awaiting one given a DeadlockError at that await (section 3.8, "Tasks completed by hand").
    [InlineData("import { writeLn } from \"std:console\"; var t = new Task(); async function later() { await delay(10); t.complete(42); } later(); writeLn(await t); writeLn([t.complete(1), t.fail(new Error(\"x\")), t is Task, await t]);",
        "42\n[false, false, true, 42]")]
    [InlineData("import { writeLn } from \"std:console\"; var f = new Task(); writeLn(f.fail(new Error(\"broken\"))); try { await f; } catch (e) { writeLn(e.message); } async function work() { return 1; } try { work().complete(2); } catch (e) { writeLn(e.code); } try { Task.all([]).fail(1); } catch (e) { writeLn(e.code); }",
        "true\nbroken\nWL3001\nWL3001")]
    [InlineData("import { writeLn } from \"std:console\"; var stuck = new Task(); try { await stuck; } catch (DeadlockError e) { writeLn([e.code, e.message, e is Error]); } async function waits() { await stuck; } try { await waits(); } catch (DeadlockError e) { writeLn(e.code); } var never = new Task(); writeLn(\"done\");",
        "[\"WL3020\", \"The program waits for a task nothing can complete: made at line 1, awaited at line 1.\", true]\nWL3020\ndone")]
    [InlineData("import { writeLn } from \"std:console\"; import { Regex } from \"std:regex\"; var r = new Regex(\"a\"); writeLn([r == r, r == new Regex(r.pattern)]);", "[true, false]")]
    // JSON (the owner's answers, 16 November 2026): a class, Json; integers when integral; instances by their fields or toJson().
    [InlineData("import { writeLn } from \"std:console\"; import { Json } from \"std:json\"; var v = Json.parse(\"{\\\"name\\\": \\\"Anna\\\", \\\"age\\\": 30, \\\"height\\\": 1.7, \\\"tags\\\": [\\\"a\\\", true, null], \\\"e\\\": 1e3, \\\"s\\\": \\\"\\\\u00e9\\\\n\\\\uD83D\\\\uDE00\\\"}\"); writeLn(v);", "{\"name\": \"Anna\", \"age\": 30, \"height\": 1.7, \"tags\": [\"a\", true, null], \"e\": 1000.0, \"s\": \"é\\n😀\"}")]
    [InlineData("import { writeLn } from \"std:console\"; import { Json } from \"std:json\"; var v = Json.parse(\"{\\\"name\\\": \\\"Anna\\\", \\\"age\\\": 30, \\\"height\\\": 1.7, \\\"tags\\\": [\\\"a\\\", true, null], \\\"e\\\": 1e3, \\\"s\\\": \\\"\\\\u00e9\\\\n\\\\uD83D\\\\uDE00\\\"}\"); writeLn([v[\"age\"] is Integer, v[\"height\"] is Number, v[\"e\"] is Number, Json.parse(\"12345678901234567890\") is Number]);", "[true, true, true, true]")]
    [InlineData("import { writeLn } from \"std:console\"; import { Json } from \"std:json\"; var v = Json.parse(\"{\\\"name\\\": \\\"Anna\\\", \\\"age\\\": 30, \\\"height\\\": 1.7, \\\"tags\\\": [\\\"a\\\", true, null], \\\"e\\\": 1e3, \\\"s\\\": \\\"\\\\u00e9\\\\n\\\\uD83D\\\\uDE00\\\"}\"); writeLn(Json.stringify(v));", "{\"name\":\"Anna\",\"age\":30,\"height\":1.7,\"tags\":[\"a\",true,null],\"e\":1000.0,\"s\":\"é\\n😀\"}")]
    [InlineData("import { writeLn } from \"std:console\"; import { DateTime } from \"std:datetime\"; import { Json } from \"std:json\"; writeLn(Json.stringify([1, 2.5, 5.0, \"x\\\"y\", null, false, 1.5m, #{3}, DateTime.utc(2026, 11, 16, 14, 30)]));", "[1,2.5,5.0,\"x\\\"y\",null,false,1.5,[3],\"2026-11-16T14:30:00Z\"]")]
    [InlineData("import { writeLn } from \"std:console\"; import { Json } from \"std:json\"; writeLn(Json.stringify({ a: [1, { b: 2 }], c: {}, d: [] }, 2));", "{\n  \"a\": [\n    1,\n    {\n      \"b\": 2\n    }\n  ],\n  \"c\": {},\n  \"d\": []\n}")]
    [InlineData("import { writeLn } from \"std:console\"; import { Json } from \"std:json\"; class Point { constructor(x, y) { this.x = x; this.y = y; } } class Money { constructor(amount) { this.amount = amount; } toJson() { return { amount: this.amount, currency: \"EUR\" }; } } class Celsius extends Number {} writeLn(Json.stringify([new Point(1, 2), new Money(5), new Celsius(21.5)]));", "[{\"x\":1,\"y\":2},{\"amount\":5,\"currency\":\"EUR\"},21.5]")]
    [InlineData("import { writeLn } from \"std:console\"; import { Json } from \"std:json\"; var m = {}; m[1] = \"one\"; writeLn([Json.stringify(m), Json.parse(\"  [ ]  \"), Json.parse(\"\\\"\\\\u0041\\\\/\\\"\"), Json.parse(\"-0.5e-1\")]);", "[\"{\\\"1\\\":\\\"one\\\"}\", [], \"A/\", -0.05]")]
    [InlineData("import { writeLn } from \"std:console\"; import { Json } from \"std:json\"; var text = Json.stringify({ list: [1, 2.5, \"three\", null, true] }); writeLn(Json.stringify(Json.parse(text)) == text);", "true")]
    // std:console's instance, and std:console imported while the program runs (section 3.9, "The console: std:console").
    [InlineData("import io from \"std:console\"; io.write(\"a\"); io.writeLn(\"b\"); var again = await Module.import(\"std:console\"); again.writeLn(again == io);", "ab\ntrue")]
    // std:json's instance, and std:json imported while the program runs (section 3.9, M0.20.2).
    [InlineData("import { write, writeLn } from \"std:console\"; import json from \"std:json\"; var j = await Module.import(\"std:json\"); writeLn([json.Json.parse(\"2\"), j.Json.stringify([1]), j.Json == json.Json]);",
        "[2, \"[1]\", true]")]
    // Html's escapers (section 13, "Context-aware escaping"): what a page's author calls where the compiler cannot tell.
    [InlineData("import { write, writeLn } from \"std:console\"; import { Html } from \"std:html\"; writeLn(Html.escape(\"<a href='x'>&\")); writeLn(Html.url(\"javascript:alert(1)\")); writeLn(Html.url(\"/a b?c\")); "
        + "writeLn(Html.urlPart(\"a b&c=d\")); writeLn(Html.jsString(\"it's </script>\")); writeLn(Html.css(\"red\")); writeLn(Html.css(\"x;y\")); "
        + "writeLn(Html.escape(null)); writeLn(Html.escape(5));",
        "&lt;a href=&#39;x&#39;&gt;&amp;\n#zWebLingoz\n/a%20b?c\na%20b%26c%3Dd\nit\\u0027s \\u003c\\/script\\u003e\nred\nzWebLingoz\n\n5")]
    [InlineData("import { writeLn } from \"std:console\"; import { Html } from \"std:html\"; writeLn(Html.js([1, \"<b>\", null])); writeLn(Html.js(5)); writeLn(Html.js({ a: true }));", "[1,\"\\u003cb\\u003e\",null]\n 5 \n{\"a\":true}")]
    [InlineData("import { writeLn } from \"std:console\"; class Html { static js(v) { return \"mine\"; } } writeLn(Html.js(1));", "mine")]
    [InlineData("import { writeLn } from \"std:console\"; var a = [10, 20, 30]; a[-1] += 2; a[-2] = 25; "
        + "writeLn([a[-3], a?[-2], a[-1], a?[-4], []?[-1], a?[-9223372036854775807 - 1]]); "
        + "var m = {}; m[-1] = 50; writeLn(m[-1]);", "[10, 25, 32, null, null, null]\n50")]
    [InlineData("import { writeLn } from \"std:console\"; class Items extends Array {} "
        + "var a = new Items(); a.push(1); a[-1] = 2; writeLn(a[-1]);", "2")]
    [InlineData("import { writeLn } from \"std:console\"; var a = [1, 2]; "
        + "try { a[-3] = 0; } catch (IndexError e) { writeLn(e.code + \" \" + e.message); } "
        + "try { writeLn(a[-3]); } catch (IndexError e) { writeLn(e.code + \" \" + e.message); }",
        "WL3005 The index -3 is outside -2 to -1.\nWL3005 The index -3 is outside -2 to -1.")]
    [InlineData("import { writeLn } from \"std:console\"; try { var sorted = [].sort(1); } catch (TypeError e) { writeLn(e.code + \" \" + e.message); } "
        + "writeLn([].sort(null));",
        "WL3001 The argument of 'sort' must be a function, not the integer 1.\n[]")]
    [InlineData("import { writeLn } from \"std:console\"; var map = [].map; try { map(1); } catch (TypeError e) { writeLn(e.code + \" \" + e.message); } "
        + "class Callback extends Function {} writeLn([2].map(new Callback(x => x + 1)));",
        "WL3001 The argument of 'map' must be a function, not the integer 1.\n[3]")]
    public void ResultsAreTheSpecifiedOnes(string source, string printed)
    {
        Assert.Equal((WebLingoCommandLine.Success, printed.Length == 0 ? "" : printed + "\n", ""), Weblingo("run", source));
    }

    [Theory]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(1 && true);")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(true && 1);")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(false || null);")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(!0);")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(1 < true);")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(null >= 0);")]
    [InlineData("import { writeLn } from \"std:console\"; if (1) writeLn(1);")]
    [InlineData("import { writeLn } from \"std:console\"; if (null) writeLn(1); else writeLn(2);")]
    [InlineData("import { writeLn } from \"std:console\"; while (1) writeLn(1);")]
    [InlineData("import { writeLn } from \"std:console\"; do writeLn(1); while (0);")]
    [InlineData("import { writeLn } from \"std:console\"; for (; 1; ) writeLn(1);")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"a\" + 1);")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(1 + \"a\");")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"a\" < 1);")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(-\"a\");")]
    [InlineData("import { writeLn } from \"std:console\"; if (\"true\") writeLn(1);")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(function () {} + 1);")]
    public void BooleansAndNumbersDoNotMix(string source)
    {
        var (exit, _, error) = Weblingo("run", source);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Contains("ERROR WL3001 (TypeError):", error);
    }

    [Theory]
    [InlineData("var x = 1; x(2);", "WL2005")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(1)(2);", "WL2005")]
    [InlineData("var write = 1; write(2);", "WL2005")]
    [InlineData("var f = function (a) {}; f();", "WL2006")]
    [InlineData("import { writeLn } from \"std:console\"; var w = writeLn; w(1, 2);", "WL2006")]
    [InlineData("function f() { return f(); } f();", "WL3003")]
    [InlineData("import { writeLn } from \"std:console\"; f(); var y = 1; function f() { writeLn(y); }", "WL2004")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(7 % 0);", "WL3013")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"a\" % 2);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(7 \\ 0);", "WL3013")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(7.5 \\ 2);", "WL3001")]
    [InlineData("var i = 9223372036854775807; i++;", "WL3002")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(1 ? 2 : 3);", "WL3001")]
    [InlineData("var s = \"a\"; s -= 1;", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(if (null) 1 else 2);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"a\".compare(1));", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(1.5m + 1.5);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(1.5m < 2.0);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(1m / 0);", "WL3013")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(1m % 0m);", "WL3013")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(79228162514264337593543950335m + 1);", "WL3002")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(new Integer(2.5m));", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(Decimal.parse(\"1.5m\"));", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; import { DateTime } from \"std:datetime\"; writeLn(DateTime.utc(2026, 1, 1).toZone(\"Nowhere/Atlantis\"));", "WL3001")]
    [InlineData("import { Html } from \"std:html\"; Html.url();", "WL2006")]
    [InlineData("import { writeLn } from \"std:console\"; import { Html } from \"std:html\"; writeLn(Html.js(writeLn));", "WL3001")]
    [InlineData("function g(a, ...rest) { return rest; } var h = g; h();", "WL2006")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([...5]);", "WL3001")]
    [InlineData("var m = { ...[1] };", "WL3001")]
    [InlineData("function f(a) { return a; } var g = f; g(...[1, 2]);", "WL2006")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(...[1, 2]);", "WL2006")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"tea\".slice(1, 9));", "WL3005")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"tea\".indexOf(\"a\", 4));", "WL3005")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"tea\".indexOf(1));", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"tea\".replace(\"\", \"x\"));", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"x\".repeat(-1));", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"x\".repeat(10000000000));", "WL3003")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\",\".join([[1]]));", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\",\".join(\"ab\"));", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"tea\".toUpper);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"tea\".trim(1));", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"tea\".trim(\"a\", \"b\"));", "WL2006")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"tea\".removeSuffix(5));", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"tea\".removePrefix());", "WL2006")]
    [InlineData("var b = [1, \"a\"].sort();", "WL3001")]
    [InlineData("var b = [1].filter(x => 1);", "WL3001")]
    [InlineData("var b = [1].removeAt(3);", "WL3005")]
    [InlineData("var b = [].removeAt(0);", "WL3005")]
    [InlineData("var b = [1].insert(2, 0);", "WL3005")]
    [InlineData("var b = [1].slice(0, 2);", "WL3005")]
    [InlineData("var b = [1].concat(2);", "WL3001")]
    [InlineData("var b = [1].map();", "WL2006")]
    [InlineData("var b = [[1]].join();", "WL3001")]
    [InlineData("import { Regex } from \"std:regex\"; var r = new Regex(\"a(\");", "WL3001")]
    [InlineData("import { Regex } from \"std:regex\"; var r = new Regex(\"a\", \"q\");", "WL3001")]
    [InlineData("import { Regex } from \"std:regex\"; var r = new Regex(\"a\", \"ii\");", "WL3001")]
    [InlineData("import { Regex } from \"std:regex\"; var r = new Regex(1);", "WL3001")]
    [InlineData("import { Regex } from \"std:regex\"; var r = new Regex();", "WL2006")]
    [InlineData("var m = \"x\".match(\"x\");", "WL3001")]
    [InlineData("import { Regex } from \"std:regex\"; var m = {}; m[new Regex(\"a\")] = 1;", "WL3001")]
    [InlineData("import { Regex } from \"std:regex\"; var t = new Regex(\"(a+)+$\").test(\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!\");", "WL3010")]
    [InlineData("import { Json } from \"std:json\"; var v = Json.parse(\"{\\\"a\\\": }\");", "WL3001")]
    [InlineData("import { Json } from \"std:json\"; var v = Json.parse(\"[1, 2\");", "WL3001")]
    [InlineData("import { Json } from \"std:json\"; var v = Json.parse(\"01\");", "WL3001")]
    [InlineData("import { Json } from \"std:json\"; var v = Json.parse(\"\\\"\\\\uD800\\\"\");", "WL3001")]
    [InlineData("import { Json } from \"std:json\"; var v = Json.parse(1);", "WL3001")]
    [InlineData("import { Json } from \"std:json\"; var t = Json.stringify(NaN);", "WL3001")]
    [InlineData("import { Json } from \"std:json\"; var loop = []; loop.push(loop); var t = Json.stringify(loop);", "WL3001")]
    [InlineData("import { Json } from \"std:json\"; var t = Json.stringify([function () {}]);", "WL3001")]
    [InlineData("import { Json } from \"std:json\"; var k = {}; k[true] = 1; var t = Json.stringify(k);", "WL3001")]
    [InlineData("import { Json } from \"std:json\"; class Selfish { toJson() { return this; } } var t = Json.stringify(new Selfish());", "WL3001")]
    [InlineData("import { Json } from \"std:json\"; var t = Json.stringify(1, 11);", "WL3001")]
    [InlineData("var t = new Task();\n\nawait t;", "WL3020")]
    [InlineData("var t = new Task(1);", "WL2006")]
    public void CallsAreCheckedAtRunTime(string source, string code)
    {
        var (exit, _, error) = Weblingo("run", source);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Contains($"ERROR {code}", error);
    }

    [Theory]
    // A wrong argument's error names the member, and, where the member has several parameters, the argument by its place
    // and its name, then says what it must be and what it was given (the owner's answers, 18 November 2026, "Argument
    // Errors That Name the Argument"); an array's own elements, not an argument, keep the member's words.
    [InlineData("\"tea\".trim(1)", "The argument of 'trim' must be a string of characters to trim, not the integer 1.")]
    [InlineData("\"7\".padStart(\"3\")", "The first argument of 'padStart', the width, must be an integer, not a string.")]
    [InlineData("\"7\".padStart(3, 0)", "The second argument of 'padStart', the pad, must be a string, not the integer 0.")]
    [InlineData("\"tea\".indexOf(1)", "The first argument of 'indexOf', the search, must be a string, not the integer 1.")]
    [InlineData("\"tea\".indexOf(\"e\", \"1\")", "The second argument of 'indexOf', the start, must be an integer, not a string.")]
    [InlineData("\"tea\".includes(1)", "The argument of 'includes' must be a string, not the integer 1.")]
    [InlineData("\"tea\".slice(\"1\")", "The first argument of 'slice', the start, must be an integer, not a string.")]
    [InlineData("\"tea\".slice(1, 2.5)", "The second argument of 'slice', the end, must be an integer, not the number 2.5.")]
    [InlineData("\"tea\".split(1)", "The argument of 'split' must be a string or a regular expression, not the integer 1.")]
    [InlineData("\"tea\".replace(1, \"x\")", "The first argument of 'replace', the text replaced, must be a string or a regular expression, not the integer 1.")]
    [InlineData("\"tea\".replace(\"t\", 1)", "The second argument of 'replace', the replacement, must be a string, not the integer 1.")]
    [InlineData("\"tea\".repeat(-1)", "The argument of 'repeat' must be an integer, 0 or more, not the integer -1.")]
    [InlineData("\"tea\".compare(1)", "The argument of 'compare' must be a string, not the integer 1.")]
    [InlineData("\"tea\".match(\"t\")", "The argument of 'match' must be a regular expression, new Regex(…), not a string.")]
    [InlineData("\"tea\".removePrefix(1)", "The argument of 'removePrefix' must be a string, not the integer 1.")]
    [InlineData("\", \".join(5)", "The argument of 'join' must be an array, not the integer 5.")]
    [InlineData("\", \".join([new P()])", "The argument of 'join' must hold strings, numbers, Booleans, decimals, dates and null, not an instance of P: give its text first, x.toString().")]
    [InlineData("[new P()].join()", "'join' writes strings, numbers, Booleans, decimals, dates and null, not an instance of P: give its text first, x.toString().")]
    [InlineData("[1].join(5)", "The argument of 'join' must be a string, not the integer 5.")]
    [InlineData("[1].insert(\"0\", 2)", "The first argument of 'insert', the position, must be an integer, not a string.")]
    [InlineData("[1].removeAt(\"0\")", "The argument of 'removeAt' must be a position, an integer, not a string.")]
    [InlineData("[1].concat(2)", "The argument of 'concat' must be an array, not the integer 2.")]
    [InlineData("[1].slice(0, \"1\")", "The second argument of 'slice', the end, must be an integer, not a string.")]
    [InlineData("({}).has(1.5)", "The argument of 'has' must be a string, an integer, a Boolean or null, not the number 1.5.")]
    [InlineData("({}).remove(1.5)", "The argument of 'remove' must be a string, an integer, a Boolean or null, not the number 1.5.")]
    [InlineData("#{}.add(1.5)", "The argument of 'add' must be a string, an integer, a Boolean or null, not the number 1.5.")]
    [InlineData("#{}.has(1.5)", "The argument of 'has' must be a string, an integer, a Boolean or null, not the number 1.5.")]
    [InlineData("#{}.remove(1.5)", "The argument of 'remove' must be a string, an integer, a Boolean or null, not the number 1.5.")]
    [InlineData("[1].map(1)", "The argument of 'map' must be a function, not the integer 1.")]
    [InlineData("[].map(1)", "The argument of 'map' must be a function, not the integer 1.")]
    [InlineData("[].select(null)", "The argument of 'select' must be a function, not null.")]
    [InlineData("[].filter(1)", "The argument of 'filter' must be a function, not the integer 1.")]
    [InlineData("[].where(1)", "The argument of 'where' must be a function, not the integer 1.")]
    [InlineData("[].reduce(\"sum\", 0)", "The first argument of 'reduce', the callback, must be a function, not a string.")]
    [InlineData("[].find(1)", "The argument of 'find' must be a function, not the integer 1.")]
    [InlineData("[].any(1)", "The argument of 'any' must be a function, not the integer 1.")]
    [InlineData("[].some(1)", "The argument of 'some' must be a function, not the integer 1.")]
    [InlineData("[].all(1)", "The argument of 'all' must be a function, not the integer 1.")]
    [InlineData("[].every(1)", "The argument of 'every' must be a function, not the integer 1.")]
    [InlineData("Integer.parse(1)", "The argument of 'parse' must be a string, not the integer 1.")]
    [InlineData("Task.all(1)", "The argument of 'all' must be an array of values, not the integer 1.")]
    [InlineData("Task.timeout(null, -1)", "The second argument of 'timeout', the time in milliseconds, must be an integer, 0 or more, not the integer -1.")]
    [InlineData("delay(-1)", "The argument of 'delay' must be a number of milliseconds, an integer, 0 or more, not the integer -1.")]
    [InlineData("P.setMethod(\"a b\", null)", "The first argument of 'setMethod', the member's name, must be a name, letters, digits and '_', not a string.")]
    [InlineData("P.setMethod(\"go\", 1)", "The second argument of 'setMethod', the method, must be a function, not the integer 1.")]
    [InlineData("P.setProperty(\"p\", null, 1)", "The third argument of 'setProperty', the setter, must be a function, or null, not the integer 1.")]
    [InlineData("P.removeMethod(1)", "The argument of 'removeMethod' must be a name, letters, digits and '_', not the integer 1.")]
    [InlineData("new Regex(\"a\").test(1)", "The argument of 'test' must be a string, not the integer 1.")]
    [InlineData("new Regex(\"a\").replace(\"a\", 1)", "The second argument of 'replace', the replacement, must be a string, not the integer 1.")]
    [InlineData("Json.stringify(1, 11)", "The second argument of 'stringify', the indent, must be a number of spaces, an integer from 0 to 10, not the integer 11.")]
    [InlineData("Json.parse(1)", "The argument of 'parse' must be a string, not the integer 1.")]
    [InlineData("DateTime.parse(1)", "The first argument of 'parse', the text, must be a string, not the integer 1.")]
    [InlineData("DateTime.parse(\"2026-01-01\", 1)", "The second argument of 'parse', the format, must be a string, not the integer 1.")]
    [InlineData("DateTime.utc(2026, 1, 1).format(1)", "The argument of 'format' must be a string, not the integer 1.")]
    [InlineData("DateTime.inZone(1, 2026, 1, 1)", "The first argument of 'inZone', the zone's name, must be a string, not the integer 1.")]
    [InlineData("DateTime.fromUnixMilliseconds(\"0\")", "The argument of 'fromUnixMilliseconds' must be an integer, not a string.")]
    [InlineData("Bytes.from(\"x\").text(1)", "The argument of 'text' must be a charset's name, a string, not the integer 1.")]
    [InlineData("Bytes.fromBase64(\"\", 1)", "The second argument of 'fromBase64', whether the alphabet is URL-safe, must be a Boolean, not the integer 1.")]
    [InlineData("Bytes.from(\"x\", 1)", "The second argument of 'from', the charset, must be a charset's name, a string, not the integer 1.")]
    [InlineData("Bytes.fixedTimeEqual(Bytes.from(\"x\"), \"x\")", "The second argument of 'fixedTimeEqual', b, must be bytes, not a string.")]
    public void AWrongArgumentsErrorNamesTheArgument(string call, string message)
    {
        var source = "import { writeLn } from \"std:console\"; import { Regex } from \"std:regex\"; import { Json } from \"std:json\"; "
            + "import { Bytes } from \"std:bytes\"; import { DateTime } from \"std:datetime\"; class P {} "
            + "try { " + call + "; } catch (e) { writeLn(e.code + \" \" + e.message); }";
        var (exit, output, error) = Weblingo("run", source);
        Assert.Equal((WebLingoCommandLine.Success, "WL3001 " + message + "\n", ""), (exit, output, error));
    }

    [Theory]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"abc\"[3]);", "WL3005")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"abc\"[-1]);", "WL3005")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"\"[0]);", "WL3005")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"abc\"[1.0]);", "WL3001")]
    [InlineData("var s = \"abc\"; s[0] = \"x\";", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(5[0]);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"abc\".size);", "WL3006")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(5.length);", "WL3006")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(null.length);", "WL3006")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"abc\".length());", "WL2005")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(\"abc\".size(1));", "WL3006")]
    [InlineData("var x = 1; x.length = 2;", "WL3001")]
    [InlineData("var x = new 5();", "WL2005")]
    [InlineData("class P {} var c = P; c();", "WL2005")]
    [InlineData("class P { constructor(a, b) {} } var p = new P(1);", "WL2006")]
    [InlineData("class E {} var e = new E(1);", "WL2006")]
    [InlineData("class P { m(a) {} } new P().m();", "WL2006")]
    [InlineData("import { writeLn } from \"std:console\"; class P {} writeLn(new P().missing);", "WL3006")]
    [InlineData("class P {} new P().missing();", "WL3006")]
    [InlineData("class P { m() {} } var p = new P(); p.m = 1;", "WL3001")]
    [InlineData("class P {} var m = {}; m[new P()] = 1;", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; class P {} foreach (var x in new P()) writeLn(x);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; class P {} writeLn(new P()[0]);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; class P {} writeLn(P.x);", "WL3006")]
    [InlineData("class A { constructor(x) {} } class B extends A {} new B();", "WL2006")]
    [InlineData("class A {} class B extends A { m() { return super.nope(); } } new B().m();", "WL3006")]
    [InlineData("var NotClass = 5; { class A extends NotClass {} }", "WL3001")]
    [InlineData("using (var x = 5) {}", "WL3006")]
    [InlineData("class A {} using (var a = new A()) {}", "WL3006")]
    [InlineData("class A { dispose() {} } new A().dispose(1);", "WL2006")]
    [InlineData("class A { dispose(x) {} } using (var a = new A()) {}", "WL2006")]
    [InlineData("import { writeLn } from \"std:console\"; class A { static var n = 1; } writeLn(new A().m);", "WL3006")]
    [InlineData("class A { static m(x) {} } A.m();", "WL2006")]
    [InlineData("class A { m() { return super.m(); } } new A().m();", "WL3006")]
    [InlineData("new Object().equal();", "WL2006")]
    [InlineData("class A {} new A().equal = 1;", "WL3001")]
    [InlineData("throw \"x\";", "WL3008")]
    [InlineData("throw new Error(\"m\");", "WL3008")]
    [InlineData("import { writeLn } from \"std:console\"; try { var a = []; a[1]; } finally { writeLn(1); }", "WL3005")]
    [InlineData("try { var a = []; a[1]; } catch (e) { throw; }", "WL3005")]
    [InlineData("import { writeLn } from \"std:console\"; class Bad { toString() { return 5; } } writeLn(new Bad());", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; class Bad { toString(x) { return \"\"; } } writeLn([new Bad()]);", "WL2006")]
    [InlineData("class Bad { equal(o) { return 1; } } var B = new Bad(); try { throw 1; } catch (B e) {}", "WL3001")]
    [InlineData("try { throw 1; } catch (String e) {}", "WL3008")]
    [InlineData("var f = function (a, b = 1) {}; f();", "WL2006")]
    [InlineData("var f = function (a = 1) {}; f(1, 2);", "WL2006")]
    [InlineData("var a = [1]; a.length = 2;", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([].pop());", "WL3005")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([1, 2][2]);", "WL3005")]
    [InlineData("var a = [1]; a[-2] = 0;", "WL3005")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([1][\"0\"]);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([1].push);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; class P { iterate() { return 5; } } foreach (var x in new P()) writeLn(x);", "WL3006")]
    [InlineData("import { writeLn } from \"std:console\"; class P { iterate() { return new Q(); } } class Q { hasNext() { return 1; } next() { return 1; } } foreach (var x in new P()) writeLn(x);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; class P { iterate() { return [1].iterate(); } } foreach (var (k, v) in new P()) writeLn(k);", "WL3001")]
    [InlineData("[1].iterate(1);", "WL2006")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(5.iterate());", "WL3006")]
    [InlineData("var a = [1]; a.push(1, 2);", "WL2006")]
    [InlineData("var a = [1]; a.pop(1);", "WL2006")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([1].size);", "WL3006")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([1].length());", "WL2005")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([1] + [2]);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([1] < [2]);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; var m = {a: 1}; writeLn(m[\"b\"]);", "WL3007")]
    [InlineData("import { writeLn } from \"std:console\"; var m = {a: 1}; writeLn(m[1]);", "WL3007")]
    [InlineData("var m = {}; m[1.5] = 2;", "WL3001")]
    [InlineData("var m = {}; m[[1]] = 2;", "WL3001")]
    [InlineData("var m = {}; m[m] = 2;", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; var m = {}; writeLn(m.has(NaN));", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; var m = {a: 1}; writeLn(m.a);", "WL3006")]
    [InlineData("import { writeLn } from \"std:console\"; var m = {a: 1}; writeLn(m.has);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; var m = {a: 1}; writeLn(m.has());", "WL2006")]
    [InlineData("import { writeLn } from \"std:console\"; var m = {a: 1}; writeLn(m.length());", "WL2005")]
    [InlineData("var m = {a: 1}; m.push(1);", "WL3006")]
    [InlineData("import { writeLn } from \"std:console\"; foreach (var x in {a: 1}) writeLn(x);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; foreach (var (k, v) in [1]) writeLn(k);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; foreach (var (k, v) in \"ab\") writeLn(k);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; foreach (var x in 5) writeLn(x);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; foreach (var x in null) writeLn(x);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(#{1.5});", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; var m = {}; writeLn(m?[1.5]);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn([1]?[\"x\"]);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(5?[0]);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(true?[0]);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(#{1}?[0]);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; var m = {}; writeLn(m?[\"a\"].length);", "WL3006")]
    [InlineData("var s = #{}; s.add([1]);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; var s = #{}; writeLn(s.has(#{}));", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(#{1}[0]);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; foreach (var (k, v) in #{1}) writeLn(k);", "WL3001")]
    [InlineData("var s = #{1}; s.add();", "WL2006")]
    [InlineData("import { writeLn } from \"std:console\"; var s = #{1}; writeLn(s.add);", "WL3001")]
    [InlineData("var s = #{1}; s.push(1);", "WL3006")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(#{1}.length());", "WL2005")]
    [InlineData("var x = 5; { extend x { m() {} } }", "WL3001")]
    [InlineData("extend Object { d() { return 1; } } null.d();", "WL3006")]
    [InlineData("extend Integer { m() { return 1; } } \"s\".m();", "WL3006")]
    [InlineData("var s = new String(5);", "WL3001")]
    [InlineData("new Integer(1.5);", "WL3001")]
    [InlineData("new Integer(1, 2);", "WL2006")]
    [InlineData("class Count extends Integer {} var k = new Count(2.5);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; class Flag extends Boolean {} writeLn(new Flag(true) + 1);", "WL3001")]
    [InlineData("class N extends Number {} var m = {}; m[new N(1)] = 1;", "WL3001")]
    [InlineData("new Function(5);", "WL3001")]
    [InlineData("class H extends Function {} new H(1);", "WL3001")]
    [InlineData("class H extends Function {} var h = new H(function (x) { return x; }); h(1, 2);", "WL2006")]
    [InlineData("class C extends Number {} var c = new C(1); c();", "WL2005")]
    [InlineData("import { writeLn } from \"std:console\"; class N { static operator +(a, b) { return 1; } } writeLn(new N() > 1);", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; class N { static operator +(a, b) { return 1; } } writeLn(-new N());", "WL3001")]
    [InlineData("class A {} class B extends A {} new B(1);", "WL2006")]
    [InlineData("class A { constructor() {} } class B extends A {} new B(1);", "WL2006")]
    [InlineData("class R { property a { get { return 1; } } } new R().a = 2;", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; class R { property a { set(v) { } } } writeLn(new R().a);", "WL3006")]
    [InlineData("class R { property a { get { return 1; } } } new R().a();", "WL3001")]
    [InlineData("class S { static property a { get { return 1; } } } S.a = 2;", "WL3001")]
    [InlineData("class S { static property a { get { return 1; } } } S.a();", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; class S { static property a { set(v) { } } } writeLn(S.a);", "WL3006")]
    [InlineData("new Class(5);", "WL3001")]
    [InlineData("new Class(\"A\", 5);", "WL3001")]
    [InlineData("new Class(\"A\", Object, 1);", "WL2006")]
    [InlineData("var C = new Class(\"C\"); C.setMethod(\"x y\", function (this) {});", "WL3001")]
    [InlineData("var C = new Class(\"C\"); C.setMethod(\"m\", 5);", "WL3001")]
    [InlineData("var C = new Class(\"C\"); C.setMethod(\"m\");", "WL2006")]
    [InlineData("var C = new Class(\"C\"); var m = C.setMethod;", "WL3001")]
    [InlineData("var C = new Class(\"C\"); new C(1);", "WL2006")]
    [InlineData("var f = function (this, a) { return a; }; f(1, 2, 3);", "WL2006")]
    [InlineData("var C = new Class(\"C\"); var c = new C(); c.__instanceOf = 1;", "WL3001")]
    [InlineData("class A {} var C = new Class(\"C\"); C.__parent = A;", "WL3001")]
    [InlineData("var C = new Class(\"C\"); C.__className = 5;", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; var C = new Class(\"C\"); var c = new C(); writeLn(c.__className);", "WL3006")]
    [InlineData("class K { static const max = 10; } K.max = 1;", "WL3001")]
    [InlineData("var C = new Class(\"C\"); C.setConstant(\"n\", 1); C.n = 2;", "WL3001")]
    [InlineData("var C = new Class(\"C\"); C.setProperty(\"p\", 5, null);", "WL3001")]
    [InlineData("var C = new Class(\"C\"); C.setProperty(\"p\", null);", "WL2006")]
    [InlineData("var C = new Class(\"C\"); C.setConstant(\"a b\", 1);", "WL3001")]
    [InlineData("var C = new Class(\"C\"); C.setProperty(\"p\", function (this) { return 1; }, null); new C().p = 1;", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; var C = new Class(\"C\"); C.setStaticProperty(\"p\", null, function (v) {}); writeLn(C.p);", "WL3006")]
    [InlineData("class Meta extends Class {} new Meta(5);", "WL3001")]
    [InlineData("class Meta extends Class {} new Meta(\"A\", 5);", "WL3001")]
    [InlineData("class Bad extends Class { constructor() { super(\"B\", this); } } new Bad();", "WL3001")]
    [InlineData("Task.all(5);", "WL3001")]
    [InlineData("Task.any([]);", "WL3001")]
    [InlineData("Task.timeout(1, -1);", "WL3001")]
    [InlineData("Task.all();", "WL2006")]
    [InlineData("var f = Task.all;", "WL3001")]
    [InlineData("class P {} P.all([]);", "WL3006")]
    [InlineData("import { delay } from \"std:system\"; async function late() { await delay(50); return 1; } var t = Task.timeout(late(), 2); await delay(60);", "WL3010")]
    [InlineData("import { delay } from \"std:system\"; async function late() { await delay(50); return 1; } await Task.timeout(late(), 2);", "WL3010")]
    [InlineData("Integer.parse(\"abc\");", "WL3001")]
    [InlineData("Integer.parse(\"1.5\");", "WL3001")]
    [InlineData("Integer.parse(\" 1\");", "WL3001")]
    [InlineData("Integer.parse(\"9223372036854775808\");", "WL3002")]
    [InlineData("Number.parse(\"1.\");", "WL3001")]
    [InlineData("Number.parse(\"1e400\");", "WL3001")]
    [InlineData("Boolean.parse(\"True\");", "WL3001")]
    [InlineData("Integer.parse(5);", "WL3001")]
    [InlineData("Integer.parse();", "WL2006")]
    [InlineData("new Integer(\"x\");", "WL3001")]
    [InlineData("String.parse(\"x\");", "WL3006")]
    [InlineData("class K { static const max = 1; } new K().max = 2;", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; class C {} var p = new C(); p.count = 1; writeLn(C.count);", "WL3006")]
    [InlineData("class A {} A.removeMethod(5);", "WL3001")]
    [InlineData("class A {} A.removeMethod();", "WL2006")]
    [InlineData("class MathStuff { const PI = 3.14; } MathStuff.PI = 3;", "WL3001")]
    [InlineData("class MathStuff { const PI = 3.14; } new MathStuff().PI = 3;", "WL3001")]
    [InlineData("class Meta extends Class { constructor() { super(\"M\", 5); } } new Meta();", "WL3001")]
    public void MembersAndIndexesAreCheckedAtRunTime(string source, string code)
    {
        var (exit, _, error) = Weblingo("run", source);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Contains($"ERROR {code}", error);
    }

    [Theory]
    [InlineData("import { writeLn } from \"std:console\"; writeLn($\"{\"a\":N2}\");", "WL3001", 1, 51)]
    [InlineData("import { writeLn } from \"std:console\"; writeLn($\"{true:N2}\");", "WL3001", 1, 51)]
    [InlineData("import { writeLn } from \"std:console\"; writeLn($\"{1.5:X}\");", "WL3001", 1, 51)]
    [InlineData("import { writeLn } from \"std:console\"; writeLn($\"{1,\"a\"}\");", "WL3001", 1, 51)]
    [InlineData("import { writeLn } from \"std:console\"; writeLn($\"{1,1.5}\");", "WL3001", 1, 51)]
    [InlineData("import { writeLn } from \"std:console\"; writeLn($\"{1,1000000}\");", "WL3001", 1, 51)]
    [InlineData("import { writeLn } from \"std:console\"; writeLn($\"{1,-1000000}\");", "WL3001", 1, 51)]
    public void InterpolationFormatsAreCheckedAtRunTime(string source, string code, int line, int column)
    {
        // A format given a value it does not apply to, and an alignment that is not an integer, or too wide (section 3.5).
        var (exit, _, error) = Weblingo("run", source);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Contains($"({line},{column}): ERROR {code}", error);
    }

    [Theory]
    [InlineData("import { writeLn } from \"std:console\"; async function f() { throw \"lost\"; } f(); writeLn(\"end\");", "WL3008")]
    [InlineData("async function f() { await null; throw new Error(\"x\"); } await f();", "WL3008")]
    [InlineData("async function f() { return [1][3]; } await f();", "WL3005")]
    [InlineData("import { delay } from \"std:system\"; async function f() { await delay(1); return [1][3]; } f(); await delay(5);", "WL3005")]
    [InlineData("var other = null; async function a() { await null; await other; } var ta = a(); async function b() { await ta; } other = b(); await ta;", "WL3009")]
    [InlineData("async function forever() { await forever(); } await forever();", "WL3003")]
    [InlineData("import { delay } from \"std:system\"; delay(-1);", "WL3001")]
    [InlineData("import { delay } from \"std:system\"; delay(1.5);", "WL3001")]
    [InlineData("import { delay } from \"std:system\"; delay(0).forget(1);", "WL2006")]
    [InlineData("var n = 5; n.forget();", "WL3006")]
    [InlineData("import { delay } from \"std:system\"; var m = {}; m[delay(0)] = 1;", "WL3001")]
    [InlineData("class R { async dispose() {} } function f() { using (var r = new R()) {} } f();", "WL3001")]
    [InlineData("class R { async dispose() {} } var f = function () { using (var r = new R()) {} }; f();", "WL3001")]
    [InlineData("class R { async dispose() { throw \"x\"; } } new R().dispose();", "WL3008")]
    [InlineData("import { writeLn } from \"std:console\"; class P { async toString() { return \"p\"; } } writeLn(new P());", "WL3001")]
    [InlineData("import { writeLn } from \"std:console\"; class P { async toString() { await null; return \"p\"; } } writeLn([new P()]);", "WL3001")]
    public void AsynchronyErrorsAreReported(string source, string code)
    {
        var (exit, _, error) = Weblingo("run", source);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Contains($"ERROR {code}", error);
    }

    [Fact]
    public void ADelayWaitsOnTheClock()
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        Assert.Equal((WebLingoCommandLine.Success, "1\n2\n", ""), Weblingo("run", "import { write, writeLn } from \"std:console\"; import { delay } from \"std:system\"; writeLn(1); await delay(60); writeLn(2);"));
        Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 60);
    }

    [Fact]
    public void AFaultNothingAwaitedIsReportedWhereItWasThrown()
    {
        var (exit, output, error) = Weblingo("run", "import { write, writeLn } from \"std:console\"; import { BytesBuilder } from \"std:bytes\"; import { Regex } from \"std:regex\"; async function f() {\n    throw \"lost\";\n}\nf();\nwriteLn(\"end\");");
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Equal("end\n", output);
        Assert.Equal("program.wlg(2,5): ERROR WL3008:", FirstLineStart(error));
    }

    [Theory]
    // A built-in written in WebLingo raises it where the program called it (section 3.9), on line 13, below twelve lines
    // that its own source's positions would name: a built-in class's method and constructor, and an array method's own
    // error after it has awaited, its call no longer on the stack.
    [InlineData("var it = [].iterate(); it.next();", "WL3005", 24)]
    [InlineData("var b = new BytesBuilder().add(true);", "WL3001", 9)]
    [InlineData("class I extends Integer {} var i = new I(\"x\");", "WL3001", 36)]
    [InlineData("class R extends Regex {} var r = new R(\"(\");", "WL3001", 34)]
    [InlineData("var kept = await [1, 2].filter(async (x) => 5);", "WL3001", 18)]
    public void AnErrorInABuiltinWrittenInWebLingoIsReportedWhereItWasCalled(string call, string code, int column)
    {
        // The classes the calls name imported on the first of the twelve lines before them, which their positions are not.
        var source = "import { BytesBuilder } from \"std:bytes\"; import { Regex } from \"std:regex\";\n"
            + string.Concat(Enumerable.Repeat("// A line its source's positions would otherwise name.\n", 11)) + call + "\n// The end.\n";
        var (exit, _, error) = Weblingo("run", source);
        Assert.Equal((WebLingoCommandLine.RuntimeFailed, $"program.wlg(13,{column}): ERROR {code}:"), (exit, FirstLineStart(error)));

        // From its compiled file, where the file's own positions locate it.
        var program = Path.Combine(_folder, "program.wlg");
        Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["compile", program], new StringWriter(), new StringWriter()));
        var fileError = new StringWriter();
        Assert.Equal(WebLingoCommandLine.RuntimeFailed,
            WebLingoCommandLine.Run(["run", Path.ChangeExtension(program, ".wlb")], new StringWriter(), fileError));
        Assert.Equal($"program.wlg(13,{column}): ERROR {code}:", FirstLineStart(fileError.ToString().Replace(program, "program.wlg", StringComparison.Ordinal)));
    }

    [Theory]
    // A toString() that gives anything but a string is WL3001 where an interpolation writes it, as where write writes it
    // (section 3.7), with write's message: alone, beside text, aligned, and giving null or a class's string. Until 17
    // November 2026 the interpolation gave the value itself, $"{x}" an integer, and beside text the error was '+''s.
    [InlineData("5", "writeLn($\"{new T()}\");", 12, "the integer 5")]
    [InlineData("5", "writeLn($\"a{new T()}b\");", 13, "the integer 5")]
    [InlineData("5", "writeLn($\"{new T(),6}\");", 12, "the integer 5")]
    [InlineData("null", "writeLn($\"{new T()}\");", 12, "null")]
    [InlineData("new N(\"n\")", "writeLn($\"{new T()}\");", 12, "an instance of N")]
    [InlineData("5", "writeLn(new T());", 1, "the integer 5")]
    public void AToStringThatGivesNoStringIsAnErrorWhereItIsWritten(string given, string call, int column, string described)
    {
        var source = $"import {{ writeLn }} from \"std:console\"; class N extends String {{}} class T {{ toString() {{ return {given}; }} }}\n{call}\n";
        var expected = $"program.wlg(2,{column}): ERROR WL3001 (TypeError): 'toString()' gave {described}, not a string, to be written.";
        var (exit, output, error) = Weblingo("run", source);
        Assert.Equal((WebLingoCommandLine.RuntimeFailed, "", expected), (exit, output, error.Split('\n')[0].TrimEnd('\r')));

        // From its compiled file.
        var program = Path.Combine(_folder, "program.wlg");
        Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["compile", program], new StringWriter(), new StringWriter()));
        var fileError = new StringWriter();
        Assert.Equal(WebLingoCommandLine.RuntimeFailed,
            WebLingoCommandLine.Run(["run", Path.ChangeExtension(program, ".wlb")], new StringWriter(), fileError));
        Assert.Equal(expected, fileError.ToString().Replace(program, "program.wlg", StringComparison.Ordinal).Split('\n')[0].TrimEnd('\r'));
    }

    [Theory]
    // An interpolation writes a value as write writes it (section 3.5): a toString() an extend block gives a kind of value
    // built into the VMs is called by x.toString() alone, not by write nor by an interpolation, at any depth (the owner's
    // decision, 17 November 2026); one a class's own instances have, an extension's included, by every one of them.
    [InlineData("import { write, writeLn } from \"std:console\"; extend Integer { toString() { return \"int\"; } } writeLn($\"{5}|{[5]}|{5,4}|\"); writeLn(5); writeLn(5.toString());",
        "5|[5]|   5|\n5\nint")]
    [InlineData("import { writeLn } from \"std:console\"; extend Array { toString() { return \"arr\"; } } writeLn($\"{[5]}\"); writeLn([5]); writeLn([5].toString());", "[5]\n[5]\narr")]
    [InlineData("import { writeLn } from \"std:console\"; extend String { toString() { return \"S\"; } } writeLn($\"{\"a\"}\"); writeLn(\"a\"); writeLn(\"a\".toString());", "a\na\nS")]
    [InlineData("import { writeLn } from \"std:console\"; class P {} extend P { toString() { return \"P!\"; } } writeLn($\"{new P()}|{[new P()]}\"); writeLn(new P());", "P!|[P!]\nP!")]
    [InlineData("import { writeLn } from \"std:console\"; class S extends Array {} extend S { toString() { return \"S!\"; } } writeLn($\"{new S()}\"); writeLn(new S());", "S!\nS!")]
    public void AnInterpolationWritesAValueAsWriteDoes(string source, string printed)
    {
        Assert.Equal((WebLingoCommandLine.Success, printed + "\n", ""), Weblingo("run", source));

        // From its compiled file.
        var program = Path.Combine(_folder, "program.wlg");
        Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["compile", program], new StringWriter(), new StringWriter()));
        var output = new StringWriter();
        Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["run", Path.ChangeExtension(program, ".wlb")], output, new StringWriter()));
        Assert.Equal(printed + "\n", output.ToString());
    }

    [Fact]
    public void AStringAToStringGivesIsWrittenAsItIsInAnInterpolation()
    {
        // What every value's own toString() gives is a string, and an instance's, a classed value's and a collection's are
        // written as they were, null as nothing (section 3.5).
        const string source = "import { write, writeLn } from \"std:console\"; class N extends String {} class C extends Number {} class P { toString() { return \"P!\"; } } "
            + "writeLn($\"{new P()}|{[new P()]}|{new N(\"x\")}|{new C(1.5)}|{null}|{5}|{\"s\"}|{new P(),4}|\");";
        Assert.Equal((WebLingoCommandLine.Success, "P!|[P!]|N:\"x\"|C:1.5||5|s|  P!|\n", ""), Weblingo("run", source));
    }

    [Theory]
    // A toString() a class is given while the program runs is called by write as a declared one is, wherever the instance
    // is (section 3.7): by setMethod, to a class new Class made, by a built-in called as a value, and from when it is given.
    // Until 18 November 2026 write ignored it unless a class declared in the source had a toString() of its own.
    [InlineData("import { write, writeLn } from \"std:console\"; class P {} P.setMethod(\"toString\", function (this) { return \"P!\"; }); "
        + "writeLn(new P()); writeLn([new P()]); writeLn({ p: new P() }); writeLn($\"{new P()}\");", "P!\n[P!]\n{\"p\": P!}\nP!")]
    [InlineData("import { write, writeLn } from \"std:console\"; var K = new Class(\"K\"); K.setMethod(\"toString\", function (this) { return \"K!\"; }); "
        + "writeLn(new K()); write([new K()]); writeLn(\"\");", "K!\n[K!]")]
    [InlineData("import { writeLn } from \"std:console\"; class P {} P.setMethod(\"toString\", function (this) { return \"P!\"; }); var w = writeLn; w(new P());", "P!")]
    [InlineData("import { writeLn } from \"std:console\"; class P {} writeLn(new P()); P.setMethod(\"toString\", function (this) { return \"P!\"; }); writeLn(new P());", "P {}\nP!")]
    public void AToStringGivenWhileTheProgramRunsIsCalledByWrite(string source, string printed)
    {
        Assert.Equal((WebLingoCommandLine.Success, printed + "\n", ""), Weblingo("run", source));

        // From its compiled file.
        var program = Path.Combine(_folder, "program.wlg");
        Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["compile", program], new StringWriter(), new StringWriter()));
        var output = new StringWriter();
        Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["run", Path.ChangeExtension(program, ".wlb")], output, new StringWriter()));
        Assert.Equal(printed + "\n", output.ToString());
    }

    [Theory]
    // A name that is not defined, WL2001, suggests what it may have been meant as (section 3.9, the owner's decision, 18
    // November 2026): a name in scope it differs from only by case; else a standard module's export, of that name or of
    // one it differs from only by case, with the module to import it from; else nothing.
    [InlineData("var db = Database.open(\"x\");", "program.wlg(1,10): ERROR WL2001: 'Database' is not defined; did you mean Database, from std:db?")]
    [InlineData("var e = environment.machineName;",
        "program.wlg(1,9): ERROR WL2001: 'environment' is not defined; did you mean Environment, from std:env?")]
    [InlineData("var t = readText(\"a\");", "program.wlg(1,9): ERROR WL2001: 'readText' is not defined; did you mean readText, from std:fs?")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(JSON.stringify(1));", "program.wlg(1,48): ERROR WL2001: 'JSON' is not defined; did you mean Json, from std:json?")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(Json.stringify(1));", "program.wlg(1,48): ERROR WL2001: 'Json' is not defined; did you mean Json, from std:json?")]
    [InlineData("import { writeLn } from \"std:console\"; writeLn(Html.url(\"/\"));", "program.wlg(1,48): ERROR WL2001: 'Html' is not defined; did you mean Html, from std:html?")]
    [InlineData("Writeln(1);", "program.wlg(1,1): ERROR WL2001: 'Writeln' is not defined; did you mean writeLn, from std:console?")]
    // The classes of the kinds of values no literal makes, each a module's, only by an import (section 3.9, M0.20.4).
    [InlineData("var b = Bytes.from(\"x\");", "program.wlg(1,9): ERROR WL2001: 'Bytes' is not defined; did you mean Bytes, from std:bytes?")]
    [InlineData("var b = new BytesBuilder();",
        "program.wlg(1,13): ERROR WL2001: 'BytesBuilder' is not defined; did you mean BytesBuilder, from std:bytes?")]
    [InlineData("var d = DateTime.now();", "program.wlg(1,9): ERROR WL2001: 'DateTime' is not defined; did you mean DateTime, from std:datetime?")]
    [InlineData("var d = Datetime.now();", "program.wlg(1,9): ERROR WL2001: 'Datetime' is not defined; did you mean DateTime, from std:datetime?")]
    [InlineData("var r = new Regex(\"a\");", "program.wlg(1,13): ERROR WL2001: 'Regex' is not defined; did you mean Regex, from std:regex?")]
    [InlineData("var t = \"a\" is Regex;", "program.wlg(1,16): ERROR WL2001: 'Regex' is not defined; did you mean Regex, from std:regex?")]
    [InlineData("import { writeLn } from \"std:console\"; var count = 1; writeLn(Count);", "program.wlg(1,63): ERROR WL2001: 'Count' is not defined; did you mean count?")]
    [InlineData("var x = nothingLikeIt;", "program.wlg(1,9): ERROR WL2001: 'nothingLikeIt' is not defined.")]
    public void AnUndefinedNameSuggestsWhatItMayHaveBeenMeantAs(string source, string reported)
    {
        var (exit, _, error) = Weblingo("check", source);
        Assert.Equal((WebLingoCommandLine.CompilationFailed, reported), (exit, error.Split('\n')[0].TrimEnd('\r')));
    }

    [Fact]
    public void WritingIsStdConsolesAndDelayStdSystems()
    {
        // write and writeLn are std:console's, which a program imports, a script being a console program only where it does;
        // std:system, every program's without an import, keeps delay, an import of it allowed and meaning the same (the
        // owner's answers, 20 November 2026, "Console Module", and 18 November 2026, "Standard Modules, Imported: The Rest").
        Assert.Equal((WebLingoCommandLine.Success, "tea time\n", ""),
            Weblingo("run", "import { write, writeLn } from \"std:console\"; write(\"tea \"); await delay(1); writeLn(\"time\");"));
        Assert.Equal((WebLingoCommandLine.Success, "tea\n", ""), Weblingo("run", "import { writeLn } from \"std:console\"; import { delay } from \"std:system\"; await delay(1); writeLn(\"tea\");"));
        Assert.Equal((WebLingoCommandLine.Success, "[module std:system, true]\n", ""),
            Weblingo("run", "import system from \"std:system\"; import { writeLn } from \"std:console\"; writeLn([system, system.delay == delay]);"));
        // Without its import, write is not defined, its message naming the module that has it; std:system has it no more.
        var (exit, _, error) = Weblingo("check", "write(\"tea\");");
        Assert.Equal((WebLingoCommandLine.CompilationFailed, "program.wlg(1,1): ERROR WL2001: 'write' is not defined; did you mean write, from std:console?"),
            (exit, error.Split('\n')[0].TrimEnd('\r')));
        (exit, _, error) = Weblingo("run", "import { writeLn } from \"std:system\"; writeLn(\"tea\");");
        Assert.Equal((WebLingoCommandLine.RuntimeFailed, "program.wlg(1,39): ERROR WL3006 (MemberError): The module std:system has no member 'writeLn'."),
            (exit, error.Split('\n')[0].TrimEnd('\r')));
    }

    [Fact]
    public void PrintAndPrintLnAreWriteAndWriteLn()
    {
        // std:console's print and printLn, write and writeLn by other names (the owner's request, 20 November 2026,
        // "Write Aliases"), an instance's toString() run as write runs it.
        const string source = "import { print, printLn, write } from \"std:console\"; class P { toString() { return \"P!\"; } } "
                              + "print(\"tea \"); printLn(new P()); printLn([1, null]); write(\"done\"); var p = print; p(\"!\");";
        Assert.Equal((WebLingoCommandLine.Success, "tea P!\n[1, null]\ndone!", ""), Weblingo("run", source));
        var (exit, _, error) = Weblingo("check", "printLn(1);");
        Assert.Equal((WebLingoCommandLine.CompilationFailed, "program.wlg(1,1): ERROR WL2001: 'printLn' is not defined; did you mean printLn, from std:console?"),
            (exit, error.Split('\n')[0].TrimEnd('\r')));
    }

    [Fact]
    public void WriteErrorWritesTheStandardError()
    {
        // std:console's writeError and writeErrorLn, as write and writeLn, to a script's standard error (the owner's
        // answer, 20 November 2026, "Console Module", A9), an instance's toString() run as write runs it.
        const string source = "import { write, writeLn, writeError, writeErrorLn } from \"std:console\"; class P { toString() { return \"P!\"; } } "
                              + "write(\"out \"); writeError(\"err \"); writeErrorLn(new P()); writeErrorLn([1, null]); writeLn(\"done\");";
        Assert.Equal((WebLingoCommandLine.Success, "out done\n", "err P!\n[1, null]\n"), Weblingo("run", source));
    }

    [Fact]
    public void AValueOfAModulesKindKeepsItsMembersWithoutItsImport()
    {
        // Only a class's own name needs its module's import (section 3.9, M0.20.4): bytes a module gives, std:crypto's hash,
        // have their members without std:bytes; and std:system exports delay, std:io being no more.
        const string source = "import { writeLn } from \"std:console\"; import { delay } from \"std:system\"; import crypto from \"std:crypto\"; var h = crypto.sha256(\"tea\"); "
                              + "await delay(1); writeLn([h.length, h.slice(0, 2).length, h.toBase64().length, h.text(\"latin1\").length]);";
        Assert.Equal((WebLingoCommandLine.Success, "[32, 2, 44, 32]\n", ""), Weblingo("run", source));
        var (exit, _, error) = Weblingo("check", "import { writeLn } from \"std:io\";");
        Assert.Equal((WebLingoCommandLine.CompilationFailed,
            "program.wlg(1,25): ERROR WL2015: The module 'std:io' cannot be imported: there is no such standard module."), (exit, error.Split('\n')[0].TrimEnd('\r')));
    }

    [Fact]
    public void SignedInt64OverflowIsARuntimeError()
    {
        const string source = "var x = 9223372036854775807 + 1;";
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("check", source).Exit);
        var (exit, _, error) = Weblingo("run", source);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Equal("program.wlg(1,9): ERROR WL3002:", FirstLineStart(error));
    }

    [Fact]
    public void TheSpecificationsExamplePrints30()
    {
        Assert.Equal((WebLingoCommandLine.Success, "30\n", ""),
            Weblingo("run", "import { write, writeLn } from \"std:console\"; var a = 10;\nvar b = 20;\nvar result = a + b;\nwriteLn(result);\n"));
    }

    [Fact]
    public void TheVersionIsTheSolutionsAndComesFirstWithoutArguments()
    {
        // The solution's version, every assembly's, set once in Directory.Build.props, 0.N.0 for the last milestone landed
        // whole, printed by --version, and first by weblingo without arguments (the owner's answer, 18 November 2026), with
        // the build's number after it, the file version, its local time (the owner's request, 21 November 2026).
        var version = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(WebLingoCommandLine).Assembly)!.InformationalVersion;
        Assert.Matches(@"^0\.\d+\.0$", version);
        Assert.Equal(version,
            System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(WebLingoValue).Assembly)!.InformationalVersion);
        var build = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyFileVersionAttribute>(typeof(WebLingoCommandLine).Assembly)!.Version;
        Assert.Matches(@"^20\d\d\.(0[1-9]|1[0-2])\.(0[1-9]|[12]\d|3[01])\.([01]\d|2[0-3])[0-5]\d$", build);
        var output = new StringWriter();
        Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["--version"], output, new StringWriter()));
        Assert.Equal($"WebLingo {version} ({build})", output.ToString().TrimEnd());
        output = new StringWriter();
        Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run([], output, new StringWriter()));
        var lines = output.ToString().Split('\n');
        Assert.Equal($"WebLingo {version} ({build})", lines[0].TrimEnd('\r'));
        Assert.StartsWith("Usage: weblingo", lines[1]);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("--unknown")]
    [InlineData("run", "a.wlg", "b.wlg")]
    public void AUsageErrorExitsWith2(params string[] args)
    {
        var output = new StringWriter();
        Assert.Equal(WebLingoCommandLine.UsageError, WebLingoCommandLine.Run(args, output, new StringWriter()));
        Assert.StartsWith("Usage: weblingo", output.ToString());
    }

    // A usage error says what was wrong, on the standard error, before how weblingo is used (the owner's request,
    // 21 November 2026, "Enhance Errors About Command Line"); none of these reaches a file.
    [Theory]
    [InlineData("'rnu' is not a command: run, check, compile, disasm, test or --version.", "rnu", "a.wlg")]
    [InlineData("'notes.txt' is not a command: run, check, compile, disasm, test or --version; "
        + "a file named first is run when it is a .wlg or .wlb file.", "notes.txt")]
    [InlineData("--version takes nothing after it.", "--version", "x")]
    [InlineData("run requires the file to run.", "run")]
    [InlineData("check requires the file to check.", "check")]
    [InlineData("compile requires the file to compile.", "compile")]
    [InlineData("disasm requires the bytecode file to show.", "disasm")]
    [InlineData("disasm takes one file, not 'b' after it.", "disasm", "a.wlb", "b")]
    [InlineData("--scheme requires a scheme, name=folder.", "run", "--scheme")]
    [InlineData("run requires the file to run after its options.", "run", "--timeout", "5")]
    [InlineData("check takes one file, not 'b.wlg' after it.", "check", "a.wlg", "b.wlg")]
    [InlineData("'--timeout' is not an option of check, but of run.", "check", "--timeout", "5", "a.wlg")]
    [InlineData("'--fast' is not an option of compile.", "compile", "--fast", "a.wlg")]
    [InlineData("'--vm' is not an option of compile.", "compile", "--vm", "a.wlg")]
    [InlineData("'--vm' is not an option of run.", "run", "--vm", "stack", "a.wlg")]
    [InlineData("'--debug' is not an option of check, but of compile.", "check", "--debug", "a.wlg")]
    [InlineData("--debug may be given only once.", "compile", "--debug", "--debug", "a.wlg")]
    [InlineData("test requires the .wlg files whose tests it runs.", "test")]
    [InlineData("'notes.txt' is not a .wlg file of tests.", "test", "notes.txt")]
    [InlineData("--list may be given only once.", "test", "--list", "--list", "a.wlg")]
    [InlineData("--filter requires the text that the labels of the tests to run contain.", "test", "--filter")]
    [InlineData("'--fast' is not an option of test: --list or --filter <text>.", "test", "--fast", "a.wlg")]
    public void AUsageErrorSaysWhatWasWrong(string message, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(WebLingoCommandLine.UsageError, WebLingoCommandLine.Run(args, output, error));
        Assert.Equal(message, error.ToString().Split('\n')[0].TrimEnd('\r'));
        Assert.Contains("Usage: weblingo", output.ToString() + error.ToString());
    }

    [Fact]
    public void AFileThatCannotBeReadExitsWith4()
    {
        var error = new StringWriter();
        var missing = Path.Combine(_folder, "missing.wlg");
        Assert.Equal(WebLingoCommandLine.FileUnreadable, WebLingoCommandLine.Run(["run", missing], new StringWriter(), error));
        Assert.Contains("cannot read the file", error.ToString());
    }

    [Fact]
    public void TheHelloWorldSampleGreets()
    {
        var sample = Path.Combine(SolutionFolder(), "samples", "HelloWorld", "hello.wlg");
        var output = new StringWriter();
        Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["run", sample], output, new StringWriter()));
        Assert.Equal("Hello from WebLingo!\n", output.ToString());
    }

    [Fact]
    public void TheArithmeticSamplePrints30()
    {
        var sample = Path.Combine(SolutionFolder(), "samples", "Arithmetic", "arithmetic.wlg");
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["run", sample], output, error));
        Assert.Equal(("30\n", ""), (output.ToString(), error.ToString()));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void EveryLineEndingGivesTheSameResultAndPositions(string newline)
    {
        var good = string.Join(newline, "import { writeLn } from \"std:console\"; var a = 2;", "var b = a * 3;", "writeLn(b);");
        Assert.Equal((WebLingoCommandLine.Success, "6\n", ""), Weblingo("run", good));
        var bad = string.Join(newline, "import { writeLn } from \"std:console\"; var x = 1;", "writeLn(y);");
        Assert.Equal("program.wlg(2,9): ERROR WL2001:", FirstLineStart(Weblingo("run", bad).Error));
    }

    [Fact]
    public void MalformedSourceGivesDiagnosticsNotACrash()
    {
        string[] fragments =
        [
            "var", "const", "let", "x", "y", "writeLn", "=", "+", "-", "*", "/", "(", ")", ";", ",", "10", "0", "1.5", "1e309", "Infinity", "NaN", "\"s\"", "'t'", "\"\\q\"", "\"", "true", "null", "==", "&&", "{", "}", "if", "while",
            "9223372036854775807", "9223372036854775808", "1x", "@", "\"", ".", "/*", "*/", "//", "\n", "\r\n", " ", "é",
            "[", "]", ".length", "x[0]",
        ];
        var random = new Random(8_6);
        var compiler = new WebLingoCompiler(new WebLingoCompilerOptions([new WebLingoBuiltinDeclaration("writeLn", 1)]));
        var vm = new WebLingoVirtualMachine();
        var compiled = 0;
        for (var round = 0; round < 5_000; round++)
        {
            var source = string.Concat(Enumerable.Range(0, random.Next(1, 30)).Select(_ => fragments[random.Next(fragments.Length)] + " "));
            var result = compiler.Compile(source, "fuzz.wlg");
            if (result.Module is null)
            {
                Assert.NotEmpty(result.Diagnostics);
                Assert.All(result.Diagnostics, d => Assert.InRange(d.Span.Start, 0, source.Length));
                continue;
            }
            compiled++;
            var context = new WebLingoExecutionContext(new WebLingoWorkerId(0))
            {
                Services = new WebLingoBuiltinRegistry(WebLingoStandardBuiltins.Create(new WebLingoCollectedOutput())),
                InstructionBudget = 10_000,
            };
            var run = vm.Execute(result.Module, context);
            Assert.True(run.Succeeded || run.Error is not null);
        }
        Assert.InRange(compiled, 1, 4_999);
    }

    [Fact]
    public void MalformedBytecodeFailsVerificationNotACrash()
    {
        var module = new WebLingoCompiler(new WebLingoCompilerOptions([new WebLingoBuiltinDeclaration("writeLn", 1)]))
            .Compile("var a = 10; var b = a * 2 - 3; var c = 0; c = -b / a; writeLn(c);", "base.wlg").Module!;
        var random = new Random(8_6);
        var opCodes = Enum.GetValues<WebLingoOpCode>();
        var vm = new WebLingoVirtualMachine();
        var rejected = 0;
        for (var round = 0; round < 5_000; round++)
        {
            var code = module.EntryPoint.Code.ToArray();
            for (var mutation = random.Next(1, 4); mutation > 0; mutation--)
            {
                var i = random.Next(code.Length);
                code[i] = random.Next(4) switch
                {
                    0 => code[i] with { OpCode = random.Next(8) == 0 ? (WebLingoOpCode)random.Next(256) : opCodes[random.Next(opCodes.Length)] },
                    1 => code[i] with { A = random.Next(-2, 8) },
                    2 => code[i] with { B = random.Next(-2, 8) },
                    _ => code[i] with { C = random.Next(-2, 8), D = random.Next(-1, 3) },
                };
            }
            var mutated = module with { EntryPoint = module.EntryPoint with { Code = code } };
            var context = new WebLingoExecutionContext(new WebLingoWorkerId(0))
            {
                Services = new WebLingoBuiltinRegistry(WebLingoStandardBuiltins.Create(new WebLingoCollectedOutput())),
                InstructionBudget = 10_000,
            };
            var result = vm.Execute(mutated, context);
            if (result.Error?.Code == WebLingoDiagnosticCodes.BytecodeVerificationFailure) rejected++;
        }
        Assert.InRange(rejected, 1, 5_000);
    }

    [Fact]
    public void ModulesAreFoundFromTheirImporter()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "lib"));
        File.WriteAllText(Path.Combine(_folder, "lib", "util.wlg"), "export function twice(x) { return x * 2; }");
        File.WriteAllText(Path.Combine(_folder, "lib", "db.wlg"),
            "import { twice } from \"./util\";\nexport var n = twice(2);\nexport function f() {\n    return [1][3];\n}");
        var (exit, output, error) = Weblingo("run", "import { write, writeLn } from \"std:console\"; import db from \"lib/db.wlg\";\nwriteLn(db.n);\ndb.f();");
        Assert.Equal((WebLingoCommandLine.RuntimeFailed, "4\n"), (exit, output));
        // An imported file is named by its path from the current folder, which the test's folder is somewhere below.
        var folder = Path.GetRelativePath(Directory.GetCurrentDirectory(), _folder).Replace('\\', '/') + "/";
        Assert.Equal("lib/db.wlg(4,12): ERROR WL3005:", FirstLineStart(error.Replace(folder, "", StringComparison.Ordinal)));
    }

    [Fact]
    public void AnImportedModulesTopLevelHasTheBuiltinsWrittenInWebLingo()
    {
        // A native function's optional parameters and the methods of arrays that take a function are made with the built-in
        // classes, before the program's imports run, so that a module's top level has them too (sections 3.6 and 3.9), on
        // either VM.
        Directory.CreateDirectory(Path.Combine(_folder, "lib"));
        File.WriteAllText(Path.Combine(_folder, "lib", "top.wlg"),
            "import fs from \"std:fs\";\nexport var code = null;\ntry { fs.readTextSync(\"no-such-file.txt\"); } catch (IOError e) { code = e.code; }\n" +
            "export const found = [1, 2, 3].find(x => x > 1);\nexport const doubled = [1, 2].map(x => x * 2);");
        var source = "import { write, writeLn } from \"std:console\"; import { code, found, doubled } from \"lib/top.wlg\";\nwriteLn([code, found, doubled]);";
        Assert.Equal((WebLingoCommandLine.Success, "[\"WL3011\", 2, [2, 4]]\n", ""), Weblingo("run", source));
        var program = Path.Combine(_folder, "program.wlg");
        Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["compile", program], new StringWriter(), new StringWriter()));
        var output = new StringWriter();
        Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["run", Path.ChangeExtension(program, ".wlb")], output, new StringWriter()));
        Assert.Equal("[\"WL3011\", 2, [2, 4]]\n", output.ToString());
    }

    [Fact]
    public void TheCliImportsModulesWhileTheProgramRuns()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "lib"));
        File.WriteAllText(Path.Combine(_folder, "lib", "util.wlg"), "export function twice(x) { return x * 2; }");
        var (exit, output, _) = Weblingo("run", "import { write, writeLn } from \"std:console\"; var u = await Module.import(\"lib/util\");\nwriteLn(u.twice(21));");
        Assert.Equal((WebLingoCommandLine.Success, "42\n"), (exit, output));
    }

    [Fact]
    public void TheCliGivesAScriptItsParameters()
    {
        const string script = "param size;\nparam name = \"x\";\nimport { writeLn } from \"std:console\"; writeLn([size, name, Integer.parse(size) * 2]);";
        var (exit, output, error) = Weblingo("run", script, "size=10", "name=Anna");
        Assert.Equal((WebLingoCommandLine.Success, "[\"10\", \"Anna\", 20]\n"), (exit, output));
        (exit, output, _) = Weblingo("run", script, "size=10");
        Assert.Equal((WebLingoCommandLine.Success, "[\"10\", \"x\", 20]\n"), (exit, output));
        (exit, _, error) = Weblingo("run", script);
        Assert.Equal((WebLingoCommandLine.RuntimeFailed, "program.wlg(1,1): ERROR WL2016:"), (exit, FirstLineStart(error)));
        (exit, _, error) = Weblingo("run", script, "size=1", "colour=red");
        Assert.Equal((WebLingoCommandLine.RuntimeFailed, "program.wlg(1,1): ERROR WL2016:"), (exit, FirstLineStart(error)));
        Assert.Equal(WebLingoCommandLine.UsageError, Weblingo("run", script, "size").Exit);
        Assert.Equal(WebLingoCommandLine.UsageError, Weblingo("run", script, "size=1", "size=2").Exit);
        Assert.Equal(WebLingoCommandLine.UsageError, Weblingo("run", script, "=1").Exit);
    }

    [Fact]
    public void AModuleThatCannotBeFoundIsWL2015()
    {
        var (exit, output, error) = Weblingo("run", "import x from \"none\";");
        Assert.Equal((WebLingoCommandLine.CompilationFailed, ""), (exit, output));
        Assert.Equal("program.wlg(1,15): ERROR WL2015:", FirstLineStart(error));
    }

    [Fact]
    public void TheModulesSampleRuns()
    {
        var sample = Path.Combine(SolutionFolder(), "samples", "Modules", "main.wlg");
        var output = new StringWriter();
        Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["run", sample], output, new StringWriter()));
        Assert.Equal("Hello, WebLingo!\nHello, modules!\n2 greetings\n", output.ToString());
    }

    private (int Exit, string Output, string Error) Weblingo(string command, string source, params string[] parameters)
    {
        var path = Path.Combine(_folder, "program.wlg");
        File.WriteAllText(path, source);
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WebLingoCommandLine.Run([command, path, .. parameters], output, error);
        // Diagnostics name the file as it was given; show only its name, so the expectations hold on every machine.
        return (exit, output.ToString(), error.ToString().Replace(path, "program.wlg", StringComparison.Ordinal));
    }

    /// <summary>The first line of the error output, up to the end of its "ERROR CODE:".</summary>
    private static string FirstLineStart(string error)
    {
        var line = error.Split('\n')[0];
        var end = line.IndexOf(": ERROR ", StringComparison.Ordinal);
        if (end < 0) return line;
        var codeEnd = line.IndexOfAny([' ', ':'], end + ": ERROR ".Length);
        return line[..codeEnd] + ":";
    }

    private static string SolutionFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "WebLingo.slnx"))) return folder.FullName;
        }
        throw new InvalidOperationException("The solution folder was not found above the test's folder.");
    }
}
