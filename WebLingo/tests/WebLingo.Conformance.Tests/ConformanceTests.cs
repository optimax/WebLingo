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
    [InlineData("writeLn(x); var x = 1;", "WL2001", 1, 9)]
    [InlineData("var x = 10", "WL1002", 1, 11)]
    [InlineData("var x = 1; var x = 2;", "WL2002", 1, 16)]
    [InlineData("x = 10;", "WL2001", 1, 1)]
    [InlineData("{ var y = 1; } writeLn(y);", "WL2001", 1, 24)]
    [InlineData("{ var y = 1; var y = 2; }", "WL2002", 1, 18)]
    [InlineData("if (true) var y = 1; writeLn(y);", "WL2001", 1, 30)]
    [InlineData("if true) writeLn(1);", "WL1001", 1, 4)]
    [InlineData("else writeLn(1);", "WL1001", 1, 1)]
    [InlineData("break;", "WL2007", 1, 1)]
    [InlineData("writeLn(\"a\\qb\");", "WL1009", 1, 11)]
    [InlineData("writeLn(\"open);", "WL1010", 1, 9)]
    [InlineData("if (true) { continue; }", "WL2007", 1, 13)]
    [InlineData("for (var i = 0; i < 1; i = i + 1) {} writeLn(i);", "WL2001", 1, 46)]
    [InlineData("do writeLn(1) while (true);", "WL1002", 1, 14)]
    [InlineData("return;", "WL2008", 1, 1)]
    [InlineData("function f(a) {} f(1, 2);", "WL2006", 1, 18)]
    [InlineData("var m = {}; m?.a = 1;", "WL1007", 1, 13)]
    [InlineData("writeLn(this);", "WL2009", 1, 9)]
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
    [InlineData("try {} writeLn(1);", "WL1001", 1, 8)]
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
    [InlineData("using (var r = 1) {} writeLn(r);", "WL2001", 1, 30)]
    [InlineData("using (var r = r) {}", "WL2001", 1, 16)]
    [InlineData("using (const r = 1) {}", "WL1001", 1, 8)]
    [InlineData("class A { static m() { return this; } }", "WL2009", 1, 31)]
    [InlineData("class A { static var x = this; }", "WL2009", 1, 26)]
    [InlineData("class A {} class B extends A { static m() { return super.m(); } }", "WL2009", 1, 52)]
    [InlineData("class A { static var x = 1; static x() {} }", "WL2002", 1, 36)]
    [InlineData("class A { static var x; }", "WL1006", 1, 22)]
    [InlineData("class A { static constructor() {} }", "WL1001", 1, 18)]
    [InlineData("var v = 1; class A { static var x = v; }", "WL2001", 1, 37)]
    [InlineData("writeLn(\"abc\".);", "WL1001", 1, 15)]
    [InlineData("writeLn(\"abc\"[0);", "WL1001", 1, 16)]
    [InlineData("writeLn([1,]);", "WL1001", 1, 12)]
    [InlineData("var a = [1 2];", "WL1001", 1, 12)]
    [InlineData("var m = {1: 2};", "WL1001", 1, 10)]
    [InlineData("var m = {a 1};", "WL1001", 1, 12)]
    [InlineData("var m = {a: 1,};", "WL1001", 1, 15)]
    [InlineData("var m = {a: {1: 2}, b: 3}; writeLn(1);", "WL1001", 1, 14)]
    [InlineData("foreach (const x in [1]) writeLn(x);", "WL1001", 1, 10)]
    [InlineData("writeLn(#{1,});", "WL1001", 1, 13)]
    [InlineData("var m = {}; m?[\"a\"] = 2;", "WL1007", 1, 13)]
    [InlineData("var x = true ? 1 : 2;", "WL1003", 1, 14)]
    [InlineData("writeLn(#[1]);", "WL1003", 1, 9)]
    [InlineData("foreach (var x of [1]) writeLn(x);", "WL1001", 1, 16)]
    [InlineData("foreach (var (k, k) in {}) writeLn(k);", "WL2002", 1, 18)]
    [InlineData("foreach (var x in [1]) {} writeLn(x);", "WL2001", 1, 35)]
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
    [InlineData("var f = async (a) => ;", "WL1001", 1, 22)]
    [InlineData("var f = (this = 1) => { };", "WL1001", 1, 15)]
    [InlineData("var f = (a, a) => { return a; };", "WL2002", 1, 13)]
    [InlineData("var f = (a = 1, b) => { };", "WL1001", 1, 17)]
    public void CompileErrorsAreReportedAndNothingRuns(string source, string code, int line, int column)
    {
        foreach (var command in new[] { "check", "run" })
        {
            var (exit, output, error) = Weblingo(command, source);
            Assert.Equal(WebLingoCommandLine.CompilationFailed, exit);
            Assert.Equal("", output);
            Assert.Equal($"program.wl({line},{column}): error {code}:", FirstLineStart(error));
        }
    }

    [Theory]
    [InlineData("var x = 10 + 20; writeLn(x);", "30")]
    [InlineData("var x = 10 / 2; writeLn(x);", "5.0")]
    [InlineData("var x = 1; { var x = 2; writeLn(x); } writeLn(x);", "2\n1")]
    [InlineData("var x = 1; { x = 3; } writeLn(x);", "3")]
    [InlineData("{ var y = 4; { writeLn(y * y); } }", "16")]
    [InlineData("{} { { } }", "")]
    [InlineData("writeLn(true); writeLn(false); writeLn(null);", "true\nfalse\nnull")]
    [InlineData("writeLn(1 == 1.0); writeLn(1 != 1); writeLn(NaN == NaN); writeLn(NaN != NaN);", "true\nfalse\nfalse\ntrue")]
    [InlineData("writeLn(null == null); writeLn(null == false); writeLn(0 == false); writeLn(true == true);", "true\nfalse\nfalse\ntrue")]
    [InlineData("writeLn(1 < 2); writeLn(2 <= 2); writeLn(3 > 4); writeLn(4 >= 4.5); writeLn(1 < NaN);", "true\ntrue\nfalse\nfalse\nfalse")]
    [InlineData("writeLn(9007199254740993 == 9007199254740992.0);", "false")]
    [InlineData("writeLn(9007199254740993 > 9007199254740992.0);", "true")]
    [InlineData("writeLn(9223372036854775807 < 9223372036854775808.0);", "true")]
    [InlineData("writeLn(!true); writeLn(!(1 > 2));", "false\ntrue")]
    [InlineData("writeLn(true && false); writeLn(true || false); writeLn(false || false && true);", "false\ntrue\nfalse")]
    [InlineData("writeLn(1 + 2 == 3 && 2 * 2 > 3);", "true")]
    [InlineData("var hit = 0; writeLn(false && (hit = 1) == 1); writeLn(true || (hit = 2) == 2); writeLn(hit);", "false\ntrue\n0")]
    [InlineData("var x = true; var y = false; x = y && x; writeLn(x);", "false")]
    [InlineData("var x = false; x = true || x; writeLn(x);", "true")]
    [InlineData("if (1 < 2) writeLn(1); else writeLn(2);", "1")]
    [InlineData("if (1 > 2) writeLn(1); else writeLn(2);", "2")]
    [InlineData("if (false) writeLn(1); writeLn(3);", "3")]
    [InlineData("var x = 5; if (x < 3) writeLn(1); else if (x == 5) writeLn(2); else writeLn(3);", "2")]
    [InlineData("if (true) if (false) writeLn(1); else writeLn(2);", "2")]
    [InlineData("if (false) if (true) writeLn(1); else writeLn(2); writeLn(3);", "3")]
    [InlineData("var x = 1; if (true) { var x = 2; writeLn(x); } writeLn(x);", "2\n1")]
    [InlineData("var x = 1; if (true) var x = 2; writeLn(x);", "1")]
    [InlineData("var n = 0; if (true) { n = n + 1; } else { n = n + 10; } writeLn(n);", "1")]
    [InlineData("var i = 0; while (i < 3) { write(i); i = i + 1; } writeLn(null);", "012null")]
    [InlineData("while (false) writeLn(1); writeLn(2);", "2")]
    [InlineData("var i = 0; do { write(i); i = i + 1; } while (i < 3); writeLn(null);", "012null")]
    [InlineData("do writeLn(1); while (false);", "1")]
    [InlineData("for (var i = 0; i < 3; i = i + 1) write(i); writeLn(null);", "012null")]
    [InlineData("var i = 10; for (i = 0; i < 2; i = i + 1) {} writeLn(i);", "2")]
    [InlineData("var n = 0; for (;;) { n = n + 1; if (n == 5) break; } writeLn(n);", "5")]
    [InlineData("for (var i = 0; i < 6; i = i + 1) { if (i == 1 || i == 3) continue; write(i); } writeLn(null);", "0245null")]
    [InlineData("var i = 0; while (true) { i = i + 1; if (i < 3) continue; break; } writeLn(i);", "3")]
    [InlineData("var i = 0; do { i = i + 1; if (i == 2) continue; write(i); } while (i < 4); writeLn(null);", "134null")]
    [InlineData("for (var i = 0; i < 2; i = i + 1) for (var j = 0; j < 3; j = j + 1) { if (j == 1) break; write(i * 10 + j); } writeLn(null);", "010null")]
    [InlineData("var s = 0; for (var n = 1; n <= 1000; n = n + 1) s = s + n; writeLn(s);", "500500")]
    [InlineData("var i = 1; for (var i = 5; i < 6; i = i + 1) write(i); writeLn(i);", "51")]
    [InlineData("writeLn(\"Hello\" + \", \" + 'world');", "Hello, world")]
    [InlineData("write('a\\n'); write(\"b\\tc\\n\");", "a\nb\tc")]
    [InlineData("writeLn(\"x\" == 'x'); writeLn(\"x\" != \"y\"); writeLn(\"1\" == 1); writeLn(\"\" == null);", "true\ntrue\nfalse\nfalse")]
    [InlineData("var s = \"\"; for (var i = 0; i < 3; i = i + 1) s = s + \"ab\"; writeLn(s);", "ababab")]
    [InlineData("writeLn(\"\\u{1F600}\");", "\U0001F600")]
    [InlineData("function add(a, b) { return a + b; } writeLn(add(2, 3));", "5")]
    [InlineData("function f() {} writeLn(f()); function g() { return; } writeLn(g());", "null\nnull")]
    [InlineData("function fact(n) { if (n <= 1) return 1; return n * fact(n - 1); } writeLn(fact(20));", "2432902008176640000")]
    [InlineData("writeLn(even(10)); function even(n) { if (n == 0) return true; return odd(n - 1); } function odd(n) { if (n == 0) return false; return even(n - 1); }", "true")]
    [InlineData("var make = function () { var c = 0; return function () { c = c + 1; return c; }; }; var a = make(); a(); var b = make(); writeLn(a()); writeLn(b());", "2\n1")]
    [InlineData("function times(k) { return function (x) { return x * k; }; } writeLn(times(3)(7));", "21")]
    [InlineData("var w = writeLn; w(\"through a variable\");", "through a variable")]
    [InlineData("var total = 0; function bump(by) { total = total + by; } bump(5); bump(6); writeLn(total);", "11")]
    [InlineData("var f = 0; var g = 0; for (var i = 0; i < 3; i = i + 1) { if (i == 0) f = function () { return i; }; if (i == 2) g = function () { return i; }; } writeLn(f()); writeLn(g());", "0\n2")]
    [InlineData("var fs = 0; var k = 0; while (k < 2) { var j = k; if (k == 0) fs = function () { return j; }; k = k + 1; } writeLn(fs());", "0")]
    [InlineData("function f(x) { var g = function () { x = x + 1; return x; }; g(); return g(); } writeLn(f(10));", "12")]
    [InlineData("function outer() { var v = 1; function mid() { function inner() { return v; } return inner(); } v = 5; return mid(); } writeLn(outer());", "5")]
    [InlineData("function deep(n) { if (n == 0) return 0; return 1 + deep(n - 1); } writeLn(deep(5000));", "5000")]
    [InlineData("function f() {} var g = f; writeLn(f == g); writeLn(function () {} == function () {}); writeLn(writeLn == writeLn);", "true\nfalse\ntrue")]
    [InlineData("function f(a) { var a = 2; return a; } writeLn(f(1));", "2")]
    [InlineData("writeLn(\"héllo\".length); writeLn(\"a\U0001F600b\".length); writeLn(\"\".length);", "5\n3\n0")]
    [InlineData("writeLn(\"a\U0001F600b\"[1]); var s = \"abc\"; writeLn(s[0] + s[2]); writeLn(s[s.length - 1]);", "\U0001F600\nac\nc")]
    [InlineData("var i = 0; var s = \"xyz\"; writeLn(s[i = 2] + s[i]); writeLn((\"a\" + \"b\").length);", "zz\n2")]
    [InlineData("var s = \"ab\"; writeLn(s[(s = \"cd\").length - 1]); writeLn(s);", "b\ncd")]
    [InlineData("var a = [1, \"a\\n\\\"q\\\"\", null, 2.5, [true, []]]; writeLn(a); writeLn(a.length); writeLn(a[4][0]);", "[1, \"a\\n\\\"q\\\"\", null, 2.5, [true, []]]\n5\ntrue")]
    [InlineData("var a = [1, 2]; a[0] = a[0] + 40; a[1] = \"x\"; writeLn(a); writeLn([]); writeLn([].length);", "[41, \"x\"]\n[]\n0")]
    [InlineData("var a = []; a.push(1); writeLn(a.push(2)); writeLn(a.pop()); writeLn(a.pop()); writeLn(a);", "null\n2\n1\n[]")]
    [InlineData("var a = [1]; var b = a; b[0] = 9; writeLn(a[0]); writeLn(a == b); writeLn([1] == [1]); writeLn(a != [9]);", "9\ntrue\nfalse\ntrue")]
    [InlineData("var a = [0]; a.push(a); writeLn(a);", "[0, […]]")]
    [InlineData("var a = [writeLn]; a[0](\"called\"); var b = [function (x) { return x * 2; }]; writeLn(b[0](21));", "called\n42")]
    [InlineData("var i = 0; var a = [i = 1, i, i = 2]; writeLn(a); var b = [0, 0]; b[i = 1] = i; writeLn(b);", "[1, 1, 2]\n[0, 1]")]
    [InlineData("var m = [[1, 2], [3, 4]]; m[1][0] = 30; writeLn(m[0][1] + m[1][0]); writeLn(m);", "32\n[[1, 2], [30, 4]]")]
    [InlineData("var m = { active: true, \"n a\": 1 }; writeLn(m[\"active\"]); writeLn(m[\"n a\"]); writeLn(m.length);", "true\n1\n2")]
    [InlineData("var m = {}; m[1] = \"one\"; m[true] = 2; m[null] = 3; m[\"1\"] = 4; writeLn(m.length); writeLn(m[1]); writeLn(m[\"1\"] + m[true]);", "4\none\n6")]
    [InlineData("var m = {a: 1}; writeLn(m.has(\"a\")); writeLn(m.has(\"b\")); writeLn(m.remove(\"a\")); writeLn(m.remove(\"a\")); writeLn(m);", "true\nfalse\ntrue\nfalse\n{}")]
    [InlineData("var m = {a: 1, a: 2}; writeLn(m); m[\"a\"] = 3; writeLn(m.length); writeLn(m[\"a\"]);", "{\"a\": 2}\n1\n3")]
    [InlineData("writeLn({a: {b: [\"c\", {}]}}); writeLn({\"q\\\"\": null});", "{\"a\": {\"b\": [\"c\", {}]}}\n{\"q\\\"\": null}")]
    [InlineData("var m = {x: 1}; var n = m; n[\"x\"] = 2; writeLn(m[\"x\"]); writeLn(m == n); writeLn({x: 1} == {x: 1});", "2\ntrue\nfalse")]
    [InlineData("var m = {}; m[\"self\"] = m; writeLn(m); var a = [m]; writeLn(a);", "{\"self\": {…}}\n[{\"self\": {…}}]")]
    [InlineData("var i = 0; var m = {a: i = 1, b: i}; writeLn(m[\"b\"]); writeLn(i);", "1\n1")]
    [InlineData("var s = 0; foreach (var x in [1, 2, 3]) s = s + x; writeLn(s);", "6")]
    [InlineData("class Point { constructor(x, y) { this.x = x; this.y = y; } function sum() { return this.x + this.y; } scaled(k) { return new Point(this.x * k, this.y * k); } } var p = new Point(1, 2); writeLn(p.sum()); writeLn(p); writeLn(p.scaled(10).x);", "3\nPoint {x: 1, y: 2}\n10")]
    [InlineData("class Point { constructor(x) { this.x = x; } get() { return this.x; } } var p = new Point(4); var f = p.get; p.x = 5; writeLn(f()); writeLn(p?.z); writeLn(null?.x); writeLn(p?.x);", "5\nnull\nnull\n5")]
    [InlineData("class E {} var a = new E(); writeLn(a); writeLn(a == a); writeLn(new E() == new E()); writeLn(E); a.self = a; writeLn(a);", "E {}\ntrue\nfalse\nclass E\nE {self: E {…}}")]
    [InlineData("class C { constructor() { this.n = 0; } next() { var bump = function () { this.n = this.n + 1; return this.n; }; return bump(); } } var c = new C(); c.next(); writeLn(c.next());", "2")]
    [InlineData("var a = new A(3); writeLn(a.twice()); class A { constructor(v) { this.v = v; } twice() { return this.v * 2; } }", "6")]
    [InlineData("function make(k) { class K { get() { return k; } } return new K(); } writeLn(make(7).get()); writeLn(make(8).get());", "7\n8")]
    [InlineData("class F { constructor() { this.w = writeLn; this.g = function (x) { return x + 1; }; } } var f = new F(); f.w(\"field\"); writeLn(f.g(1));", "field\n2")]
    [InlineData("class R { constructor(n) { this.n = n; return 5; } } writeLn(new R(1).n);", "1")]
    [InlineData("class N { count(n) { if (n == 0) return 0; return 1 + this.count(n - 1); } } writeLn(new N().count(100));", "100")]
    [InlineData("class B extends A { constructor(x) { super(x * 2); this.b = 1; } who() { return \"B/\" + super.who(); } } class A { constructor(x) { this.a = x; } who() { return \"A\"; } base() { return this.a; } } var b = new B(5); writeLn(b.who()); writeLn(b.base()); writeLn(b); var f = b.who; writeLn(f());", "B/A\n10\nB {a: 10, b: 1}\nB/A")]
    [InlineData("class A { constructor() { this.log = \"A\"; } } class B extends A { constructor() { this.log = this.log + \"B\"; } } class C extends B {} writeLn(new C().log);", "AB")]
    [InlineData("class A { name() { return \"A\"; } hello() { return \"hi \" + this.name(); } } class B extends A { name() { return \"B\"; } } writeLn(new B().hello()); writeLn(new A().hello());", "hi B\nhi A")]
    [InlineData("class A { m() { return 1; } } class B extends A { m() { var f = super.m; return f() + 1; } } writeLn(new B().m());", "2")]
    [InlineData("class A { v() { return 7; } } { class B extends A {} writeLn(new B().v()); }", "7")]
    [InlineData("class A { constructor(x) { this.x = x; } } class B extends A { constructor(x) { super(x + 1); } } class C extends B { constructor() { super(1); } } writeLn(new C().x);", "2")]
    [InlineData("class A { dispose() { writeLn(\"A\"); } } class B extends A { destructor() { writeLn(\"B\"); } } var b = new B(); b.dispose(); writeLn(b.dispose());", "B\nA\nnull")]
    [InlineData("class R { constructor(n) { this.n = n; } dispose() { write(\"end \"); writeLn(this.n); } } function f() { using (var a = new R(1)) { using (var b = new R(2)) { return \"r\"; } } } writeLn(f()); for (var i = 0; i < 3; i = i + 1) { using (var c = new R(i)) { if (i == 0) continue; if (i == 1) break; } } using (var d = new R(9)) writeLn(\"body\");", "end 2\nend 1\nr\nend 0\nend 1\nbody\nend 9")]
    [InlineData("class A { dispose() { writeLn(\"d\"); return 5; } } var f = new A().dispose; writeLn(f()); writeLn(f());", "d\nnull\nnull")]
    [InlineData("class A { dispose() { writeLn(\"d\"); this.dispose(); } } using (var a = new A()) {} using (var n = null) {} writeLn(\"done\");", "d\ndone")]
    [InlineData("class A { dispose() { writeLn(\"A\"); } } class B extends A { dispose() { super.dispose(); } } class C extends B {} new C().dispose();", "A\nA")]
    [InlineData("class A { dispose() { writeLn(this.v); } } class B extends A { constructor() { this.v = \"b\"; } } using (var b = new B()) { var g = function () { return b; }; writeLn(g() == b); }", "true\nb")]
    [InlineData("class Counter { static var count = 0; static next() { Counter.count = Counter.count + 1; return Counter.count; } } Counter.next(); writeLn(Counter.next()); writeLn(Counter.count);", "2\n2")]
    [InlineData("class A { static var n = 1; static who() { return \"A\"; } } class B extends A {} writeLn(B.who()); B.n = 5; writeLn(A.n); class C extends A { static var n = 9; } writeLn(C.n); writeLn(A.n);", "A\n5\n9\n5")]
    [InlineData("class P { static make(x) { var p = new P(); p.x = x; return p; } } class Q { static var origin = P.make(0); } writeLn(Q.origin); var f = P.make; writeLn(f(2).x);", "P {x: 0}\n2")]
    [InlineData("class M { static var twice = function (x) { return x * 2; }; } writeLn(M.twice(4)); writeLn(M?.nope);", "8\nnull")]
    [InlineData("class A { static var x = B.y; } class B { static var y = 1; } writeLn(A.x); writeLn(B.y);", "null\n1")]
    [InlineData("function make(k) { class K { static get() { return k; } } return K; } writeLn(make(3).get());", "3")]
    [InlineData("class A { static v() { return 1; } v() { return 2; } } writeLn(A.v() + new A().v());", "3")]
    [InlineData("var o = new Object(); writeLn(o); writeLn(o.equal(o)); writeLn(o.equal(new Object()));", "Object {}\ntrue\nfalse")]
    [InlineData("class P { constructor(x) { this.x = x; } equal(other) { return other.x == this.x; } } var a = new P(1); writeLn(a.equal(new P(1))); writeLn(a == new P(1));", "true\nfalse")]
    [InlineData("class A {} writeLn(new A().equal(5)); class B { equal(o) { return super.equal(o) || o == 1; } } var b = new B(); writeLn(b.equal(b)); writeLn(b.equal(1)); writeLn(b.equal(2));", "false\ntrue\ntrue\nfalse")]
    [InlineData("class Object { hi() { return 1; } } class A {} writeLn(new A().equal(1)); writeLn(new Object().hi()); writeLn(new Object().equal(1));", "false\n1\nfalse")]
    [InlineData("class A extends Object { constructor() { super(); this.v = 1; } } class B { constructor() { super(); } } writeLn(new A()); writeLn(new B());", "A {v: 1}\nB {}")]
    [InlineData("function f() { class K {} return new K(); } var k = f(); writeLn(k.equal(k)); writeLn(k.equal(f()));", "true\nfalse")]
    [InlineData("try { throw \"boom\"; } catch (e) { writeLn(\"caught \" + e); }", "caught boom")]
    [InlineData("try { var a = [1]; a[5]; } catch (e) { writeLn(e); writeLn(e.code); }", "IndexError {message: \"The index 5 is outside 0 to 0.\", code: \"WL3005\"}\nWL3005")]
    [InlineData("function f(n) { try { if (n == 0) return \"zero\"; return \"other\"; } finally { writeLn(\"finally\"); } } writeLn(f(0)); writeLn(f(1));", "finally\nzero\nfinally\nother")]
    [InlineData("var i = 0; while (i < 3) { i = i + 1; try { if (i == 1) continue; if (i == 2) break; } finally { writeLn(i); } } writeLn(\"end\");", "1\n2\nend")]
    [InlineData("function deep() { var m = {}; return m[\"nope\"]; } try { deep(); } catch (e) { writeLn(e.message); }", "The map has no key \"nope\".")]
    [InlineData("try { try { throw new Error(\"inner\"); } finally { writeLn(\"inner finally\"); } } catch (e) { writeLn(\"caught \" + e.message); }", "inner finally\ncaught inner")]
    [InlineData("try { try { throw 1; } catch (e) { writeLn(\"again\"); throw; } } catch (e) { writeLn(e); }", "again\n1")]
    [InlineData("class R { dispose() { writeLn(\"disposed\"); } } try { using (var r = new R()) { throw \"x\"; } } catch (e) { writeLn(\"caught \" + e); }", "disposed\ncaught x")]
    [InlineData("class Bad { dispose() { throw \"from dispose\"; } } try { using (var b = new Bad()) { throw \"first\"; } } catch (e) { writeLn(e); }", "from dispose")]
    [InlineData("class A { dispose() { writeLn(\"A\"); } } class B extends A { dispose() { throw \"B failed\"; } } try { new B().dispose(); } catch (e) { writeLn(e); }", "A\nB failed")]
    [InlineData("class MyError extends Error { constructor(m) { super(m); this.extra = 1; } } try { throw new MyError(\"mine\"); } catch (e) { writeLn(e); }", "MyError {message: \"mine\", code: null, extra: 1}")]
    [InlineData("var fs = []; for (var i = 0; i < 2; i = i + 1) { try { throw i; } catch (e) { fs.push(function () { return e; }); } } writeLn(fs[0]() + fs[1]());", "1")]
    [InlineData("try { throw 1; } catch (e) { writeLn(\"c\"); } finally { writeLn(\"f\"); }", "c\nf")]
    [InlineData("try { try { throw 1; } catch (e) { throw 2; } finally { writeLn(\"f\"); } } catch (e) { writeLn(e); }", "f\n2")]
    [InlineData("try { var x = 9223372036854775807 + 1; } catch (e) { writeLn(e.code); }", "WL3002")]
    [InlineData("var e = new Error(\"m\"); writeLn(e.message); writeLn(e.code);", "m\nnull")]
    [InlineData("class C { constructor() { throw \"no\"; } } try { new C(); } catch (e) { writeLn(e); }", "no")]
    [InlineData("for (var i = 0; i < 1; i = i + 1) { try {} finally { for (var j = 0; j < 3; j = j + 1) { if (j == 1) break; writeLn(j); } } } writeLn(\"ok\");", "0\nok")]
    [InlineData("var n = 5; writeLn(\"n = \" + n.toString()); writeLn((1.5).toString() + \"|\" + \"s\".toString() + \"|\" + [1, \"a\", null].toString()); var z = null; writeLn(z.toString());", "n = 5\n1.5|s|[1, \"a\", null]\nnull")]
    [InlineData("class Point { constructor(x) { this.x = x; } } var p = new Point(1); writeLn(p.toString()); writeLn(Point.toString());", "Point {x: 1}\nclass Point")]
    [InlineData("class Money { constructor(a) { this.a = a; } toString() { return \"$\" + this.a.toString(); } } var m = new Money(7); writeLn(m); writeLn([m, new Money(8)]); writeLn({price: m}); var w = writeLn; w([m]); var f = m.toString; writeLn(f());", "$7\n[$7, $8]\n{\"price\": $7}\n[$7]\n$7")]
    [InlineData("class M { toString() { return \"m\"; } } class T { constructor() { this.inner = new M(); } toString() { return \"T(\" + super.toString() + \")\"; } } writeLn(new T());", "T(T {inner: m})")]
    [InlineData("class S { toString() { return \"self\"; } } var s = new S(); s.me = s; writeLn([s]); writeLn(s); writeLn(s.me.toString());", "[self]\nself\nself")]
    [InlineData("class Thrower { toString() { throw \"nope\"; } } try { writeLn([new Thrower()]); } catch (e) { writeLn(\"caught \" + e); } writeLn(\"after\");", "caught nope\nafter")]
    [InlineData("class L { toString() { write(\"<\"); return \"L\"; } } writeLn([new L(), new L()]);", "<<[L, L]")]
    [InlineData("try { var m = {}; m[\"x\"]; } catch (KeyError e) { writeLn(\"key \" + e.code); }", "key WL3007")]
    [InlineData("try { [][0]; } catch (KeyError e) { writeLn(\"k\"); } catch (IndexError e) { writeLn(\"i \" + (e is Error).toString()); } catch (e) { writeLn(\"any\"); }", "i true")]
    [InlineData("try { try { throw \"s\"; } catch (Integer e) { writeLn(\"int\"); } finally { writeLn(\"f\"); } } catch (String e) { writeLn(\"string \" + e); }", "f\nstring s")]
    [InlineData("class P {} writeLn([5 is Integer, 5 is Number, 1.5 is Number, \"a\" is String, [1] is Array, {} is Map, #{} is Set]); writeLn([null is Object, 5 is Object, writeLn is Function, new P() is P, new P() is Object, P is Class, true is Boolean, P is P]);", "[true, false, true, true, true, true, true]\n[false, true, true, true, true, true, true, false]")]
    [InlineData("class NotFound extends Error { constructor(m) { super(m); } } class errors { static var NotFound = NotFound; } try { throw new NotFound(\"x\"); } catch (errors.NotFound e) { writeLn(\"nf \" + e.message); }", "nf x")]
    [InlineData("var A = 5; try { throw 5; } catch (A e) { writeLn(\"five\"); } writeLn(5 is A); writeLn(6 is A);", "five\ntrue\nfalse")]
    [InlineData("writeLn(\"a\".equal(\"a\")); writeLn(5.equal(6)); var z = null; writeLn(z.equal(null));", "true\nfalse\ntrue")]
    [InlineData("try { 1 + \"a\"; } catch (TypeError e) { writeLn(e is TypeError); writeLn(e.code); } try { throw new KeyError(\"mine\"); } catch (KeyError e) { writeLn(e); }", "true\nWL3001\nKeyError {message: \"mine\", code: null}")]
    [InlineData("try { throw null; } catch (Object e) { writeLn(\"object\"); } catch (e) { writeLn(\"any \" + e.toString()); }", "any null")]
    [InlineData("class Stack extends Array { top() { return this[this.length - 1]; } } var s = new Stack(); s.push(1); s.push(2); writeLn(s.top()); writeLn(s.length); writeLn(s); writeLn([s is Array, s is Stack, [] is Stack]); s.name = \"st\"; writeLn(s.name); foreach (var x in s) write(x); writeLn(\"\");", "2\n2\nStack:[1, 2]\n[true, true, false]\nst\n12")]
    [InlineData("class Bag extends Set {} var b = new Bag(); b.add(1); b.add(1); writeLn(b); class Dict extends Map { get(k) { return this?[k]; } } var d = new Dict(); d[\"a\"] = 1; writeLn(d.get(\"a\")); writeLn(d.get(\"b\")); writeLn([d]);", "Bag:#{1}\n1\nnull\n[Dict:{\"a\": 1}]")]
    [InlineData("writeLn(new Array()); writeLn(new Map()); writeLn(new Set()); writeLn([new Array() is Array, new Map() is Map]);", "[]\n{}\n#{}\n[true, true]")]
    [InlineData("class Stack extends Array { constructor(x) { super(); this.push(x); } push(v) { write(\"push \"); writeLn(v); } } var s = new Stack(5); writeLn(s.length);", "push 5\n0")]
    [InlineData("class Stack extends Array { toString() { return \"stack of \" + this.length.toString(); } } var s = new Stack(); s.push(1); writeLn(s); writeLn([s]);", "stack of 1\n[stack of 1]")]
    [InlineData("class P {} P.origin = 0; P.make = function (x) { return x * 2; }; writeLn(P.origin); writeLn(P.make(4)); Integer.twice = function (n) { return n * 2; }; writeLn(Integer.twice(3));", "0\n8\n6")]
    [InlineData("class Code { constructor(c) { this.c = c; } equal(other) { return other is Code && other.c == this.c; } } var NotFound = new Code(404); try { throw new Code(404); } catch (NotFound e) { writeLn(\"404\"); } writeLn(new Code(404) is NotFound); writeLn(new Code(500) is NotFound);", "404\ntrue\nfalse")]
    [InlineData("class Bad { equal(o) { throw \"from equal\"; } } var B = new Bad(); try { try { throw 1; } catch (B e) { writeLn(\"no\"); } } catch (e) { writeLn(e); }", "from equal")]
    [InlineData("function f(a, b = 1) { return b; } writeLn(f(2)); writeLn(f(2, 3)); writeLn(f(2, null));", "1\n3\nnull")]
    [InlineData("var n = 0; function next() { n = n + 1; return n; } function g(a, b = a * 10, c = next()) { writeLn([a, b, c]); } g(1); g(2); g(3, 4, 5);", "[1, 10, 1]\n[2, 20, 2]\n[3, 4, 5]")]
    [InlineData("function h(x = 5) { return function () { return x; }; } writeLn(h()()); writeLn(h(7)());", "5\n7")]
    [InlineData("class P { constructor(x = 0, y = 0) { this.x = x; this.y = y; } move(dx = 1) { return new P(this.x + dx, this.y); } } writeLn(new P()); writeLn(new P(1).move()); writeLn(new P(1, 2).move(5));", "P {x: 0, y: 0}\nP {x: 2, y: 0}\nP {x: 6, y: 2}")]
    [InlineData("var f = function (a = \"x\") { return a; }; writeLn(f()); var g = f; writeLn(g(\"y\"));", "x\ny")]
    [InlineData("var b = 9; function f(a, b = b) { return b; } writeLn(f(1));", "9")]
    [InlineData("var m = {a: 1}; writeLn(m?[\"a\"]); writeLn(m?[\"b\"]); writeLn([1]?[5]); writeLn([1]?[-1]); writeLn(\"ab\"?[9]); writeLn(\"ab\"?[1]);", "1\nnull\nnull\nnull\nnull\nb")]
    [InlineData("var rows = null; writeLn(rows?[0]?[\"name\"]); rows = [{name: \"x\"}]; writeLn(rows?[0]?[\"name\"]); writeLn(rows?[1]?[\"name\"]);", "null\nx\nnull")]
    [InlineData("var i = 0; var n = null; writeLn(n?[i = 5]); writeLn(i); var m = {a: [1]}; writeLn(m?[\"a\"]?[0]); writeLn(m?[\"b\"]?[0]);", "null\n5\n1\nnull")]
    [InlineData("var s = #{1, \"a\", 1, null}; writeLn(s.length); writeLn(s.has(\"a\")); writeLn(s.has(2)); writeLn(#{}); writeLn(#{\"q\"}); writeLn([#{true}]);", "3\ntrue\nfalse\n#{}\n#{\"q\"}\n[#{true}]")]
    [InlineData("var s = #{}; writeLn(s.add(2)); writeLn(s.add(2)); writeLn(s.remove(2)); writeLn(s.remove(2)); writeLn(s.length);", "true\nfalse\ntrue\nfalse\n0")]
    [InlineData("var t = 0; foreach (var v in #{1, 2, 3, 3}) t = t + v; writeLn(t); writeLn(#{1} == #{1}); var s = #{1}; var u = s; u.add(2); writeLn(s.length);", "6\nfalse\n2")]
    [InlineData("var s = #{1, 2, 3}; var n = 0; foreach (var v in s) { s.remove(v); n = n + 1; } writeLn(n < 4); writeLn({set: #{\"x\"}});", "true\n{\"set\": #{\"x\"}}")]
    [InlineData("foreach (var c in \"a\U0001F600b\") write(c + \"|\"); writeLn(null);", "a|\U0001F600|b|null")]
    [InlineData("var m = {a: 1, bb: 2, ccc: 3}; var t = 0; var k = 0; foreach (var (key, value) in m) { t = t + value; k = k + key.length; } writeLn(t); writeLn(k);", "6\n6")]
    [InlineData("var a = [1, 2]; foreach (var x in a) { if (x < 4) a.push(x + 2); write(x); } writeLn(null);", "12345null")]
    [InlineData("var fs = []; foreach (var x in [1, 2, 3]) fs.push(function () { return x; }); foreach (var f in fs) write(f()); writeLn(null);", "123null")]
    [InlineData("foreach (var x in [1, 2, 3, 4]) { if (x == 2) continue; if (x == 4) break; write(x); } writeLn(null);", "13null")]
    [InlineData("var x = [7]; foreach (var x in x) writeLn(x); writeLn(x);", "7\n[7]")]
    [InlineData("foreach (var x in []) writeLn(x); foreach (var (k, v) in {}) writeLn(k); foreach (var c in \"\") writeLn(c); writeLn(\"done\");", "done")]
    [InlineData("foreach (var a in [[1, 2], [3]]) foreach (var b in a) write(b); writeLn(null);", "123null")]
    [InlineData("function first(a) { foreach (var x in a) return x; return null; } writeLn(first([5, 6])); writeLn(first([]));", "5\nnull")]
    [InlineData("var m = {a: 1, b: 2, c: 3}; var n = 0; foreach (var (k, v) in m) { m.remove(k); n = n + 1; } writeLn(n < 4); writeLn(m.length < 3);", "true\ntrue")]
    [InlineData("var m = {x: 1}; foreach (var (k, v) in m) { var g = function () { return [k, v]; }; writeLn(g()); }", "[\"x\", 1]")]
    [InlineData("var m = {}; for (var i = 0; i < 100; i = i + 1) m[i] = i * i; for (var i = 0; i < 100; i = i + 2) m.remove(i); writeLn(m.length); writeLn(m[99]);", "50\n9801")]
    [InlineData("async function w(n, d) { writeLn(n + \"<\"); await delay(d); writeLn(n + \">\"); return d; } var a = w(\"a\", 20); var b = w(\"b\", 10); writeLn(a is Task); writeLn(await a + await b);", "a<\nb<\ntrue\nb>\na>\n30")]
    [InlineData("async function f() { writeLn(\"f1\"); await null; writeLn(\"f2\"); } var t = f(); writeLn(\"main\"); writeLn(await 5); await t;", "f1\nmain\nf2\n5")]
    [InlineData("async function bad() { await null; throw new Error(\"no\"); } try { await bad(); } catch (e) { writeLn(e.message); }", "no")]
    [InlineData("async function ix() { return [1][5]; } try { await ix(); } catch (IndexError e) { writeLn(e.code); }", "WL3005")]
    [InlineData("async function bad() { throw \"x\"; } bad().forget(); var t = bad(); t.forget(); writeLn(\"ok\");", "ok")]
    [InlineData("class A { constructor() { this.k = 1; } async m(x) { await null; return x + this.k; } } writeLn(await new A().m(41)); var h = async function (x) { return x * 2; }; writeLn(await h(4));", "42\n8")]
    [InlineData("async function f() { await null; writeLn(\"never\"); } f(); writeLn(\"end\");", "end")]
    [InlineData("async function f() { return 3; } var t = f(); writeLn(await t); writeLn(await t); writeLn(t);", "3\n3\ntask")]
    [InlineData("writeLn(delay(0) is Task); writeLn(5 is Task); writeLn(delay(0) is Object); writeLn([delay(1)]);", "true\nfalse\ntrue\n[task]")]
    [InlineData("async function count(n) { var total = 0; for (var i = 0; i < n; i = i + 1) { total = total + await i; } return total; } writeLn(await count(5));", "10")]
    [InlineData("async function inner(x) { await delay(1); return x + 1; } async function outer() { var a = await inner(1); return await inner(a); } writeLn(await outer());", "3")]
    [InlineData("async function f() { try { await delay(1); throw \"e\"; } finally { writeLn(\"fin\"); } } try { await f(); } catch (e) { writeLn(\"caught \" + e); }", "fin\ncaught e")]
    [InlineData("class R { dispose() { writeLn(\"disposed\"); } } async function f() { using (var r = new R()) { await null; writeLn(\"in\"); } } await f();", "in\ndisposed")]
    [InlineData("var fs = []; async function f(n) { await delay(n); fs.push(function () { return n; }); } await f(2); await f(1); foreach (var g in fs) write(g()); writeLn(null);", "21null")]
    [InlineData("async function f() { var m = {a: [1, 2]}; foreach (var (k, v) in m) { foreach (var x in v) { await null; write(k + x.toString()); } } } await f(); writeLn(null);", "a1a2null")]
    [InlineData("var order = []; async function t(n, d) { await delay(d); order.push(n); } var all = [t(1, 30), t(2, 10), t(3, 20), t(4, 10)]; foreach (var x in all) await x; writeLn(order);", "[2, 4, 3, 1]")]
    [InlineData("async function f() { await null; return \"late\"; } var t = f(); writeLn(\"early\"); writeLn(await t);", "early\nlate")]
    [InlineData("class R { async dispose() { await delay(5); writeLn(\"disposed\"); } } async function f() { using (var r = new R()) { writeLn(\"in\"); } writeLn(\"after\"); } await f();", "in\ndisposed\nafter")]
    [InlineData("class R { async dispose() { await null; writeLn(\"disposed\"); } } using (var r = new R()) { writeLn(\"in\"); } writeLn(\"after\");", "in\ndisposed\nafter")]
    [InlineData("class A { dispose() { writeLn(\"A\"); } } class B extends A { async dispose() { await delay(1); writeLn(\"B\"); } } using (var b = new B()) {} writeLn(\"end\");", "B\nA\nend")]
    [InlineData("class A { async dispose() { await null; writeLn(\"A\"); } } class B extends A { dispose() { writeLn(\"B\"); } } var b = new B(); var t = b.dispose(); writeLn(t is Task); await t; writeLn(\"end\");", "B\ntrue\nA\nend")]
    [InlineData("class R { async dispose() { await null; } } var r = new R(); var t = r.dispose(); writeLn(r.dispose()); await t; writeLn(\"ok\");", "null\nok")]
    [InlineData("class R { async dispose() { await null; throw \"bad\"; } } try { using (var r = new R()) {} } catch (e) { writeLn(\"caught \" + e); }", "caught bad")]
    [InlineData("class R { async dispose() { await null; writeLn(\"disposed\"); } } try { using (var r = new R()) { throw \"body\"; } } catch (e) { writeLn(e); }", "disposed\nbody")]
    [InlineData("class A { dispose() { writeLn(\"A\"); } } class B extends A { async dispose() { await null; throw \"B\"; } } try { using (var b = new B()) {} } catch (e) { writeLn(\"caught \" + e); }", "A\ncaught B")]
    [InlineData("class A { async dispose() { throw \"A\"; } } class B extends A { async dispose() { await null; throw \"B\"; } } try { await new B().dispose(); } catch (e) { writeLn(\"caught \" + e); }", "caught A")]
    [InlineData("class R { async dispose() { writeLn(\"d\"); } } var r = new R(); function f() { using (var x = r) {} } try { f(); } catch (e) { writeLn(e.code); } await r.dispose();", "WL3001\nd")]
    [InlineData("class R { async dispose() { writeLn(\"d\"); } } function f(r) { return r.dispose(); } var t = f(new R()); writeLn(t is Task); await t;", "d\ntrue")]
    [InlineData("class R { constructor(n) { this.n = n; } async dispose() { await null; writeLn(\"d\" + this.n.toString()); } } async function f() { foreach (var i in [1, 2]) { using (var r = new R(i)) { if (i == 1) continue; return i; } } } writeLn(await f());", "d1\nd2\n2")]
    [InlineData("class R { constructor(n) { this.n = n; } async dispose() { await delay(this.n); writeLn(this.n); } } async function use(n) { using (var r = new R(n)) {} } var a = use(20); var b = use(10); await a; await b;", "10\n20")]
    [InlineData("class R { dispose() { writeLn(\"d\"); } } async function f() { using (var r = new R()) {} writeLn(\"f\"); } var t = f(); writeLn(\"main\"); await t;", "d\nf\nmain")]
    [InlineData("class R { async dispose() { await null; writeLn(\"d\"); } } async function f() { using (var r = new R()) { return 1; } } var t = f(); writeLn(\"main\"); writeLn(await t);", "main\nd\n1")]
    [InlineData("class S extends Array {} var s = new S(); s.push(1); s.push(s); writeLn(s); writeLn([s]); class M extends Map {} var m = new M(); m[\"k\"] = s; writeLn(m);", "S:[1, S:[…]]\n[S:[1, S:[…]]]\nM:{\"k\": S:[1, S:[…]]}")]
    [InlineData("class A { static var x = 1; } class B extends A {} extend B { static var x = 2; } writeLn(A.x); writeLn(B.x);", "1\n2")]
    [InlineData("class A { m() { return \"A\"; } } class B extends A { m() { return \"B\"; } } extend A { m() { return \"A2\"; } } writeLn(new B().m()); writeLn(new A().m());", "B\nA2")]
    [InlineData("writeLn(new P().hi()); class P {} extend P { hi() { return \"hi\"; } }", "hi")]
    [InlineData("class P {} var p = new P(); { extend P { hi() { return \"hi\"; } } writeLn(p.hi()); } writeLn(p.hi());", "hi\nhi")]
    [InlineData("extend Integer { async later() { await null; return this + 1; } } writeLn(await 1.later());", "2")]
    [InlineData("extend String { toString() { return \"S\"; } } writeLn(\"a\".toString()); writeLn(\"a\");", "S\na")]
    [InlineData("extend Class { describe() { return \"class \" + this.toString(); } } class P {} writeLn(P.describe());", "class class P")]
    [InlineData("class Stack extends Array {} extend Array { top() { return this[this.length - 1]; } } var s = new Stack(); s.push(1); s.push(2); writeLn(s.top()); writeLn([3].top());", "2\n3")]
    [InlineData("extend Array { push(v) { writeLn(\"pushing\"); } } var a = []; a.push(1); writeLn(a.length);", "pushing\n0")]
    [InlineData("extend Object { d() { return \"d\"; } } writeLn(null.toString()); writeLn(true.d()); writeLn(1.5.d());", "null\nd\nd")]
    [InlineData("class R { dispose() { writeLn(\"old\"); } } extend R { dispose() { writeLn(\"new\"); } } using (var r = new R()) {}", "new")]
    [InlineData("class A { dispose() { writeLn(\"A\"); } } class B extends A {} extend B { destructor() { writeLn(\"B\"); } } new B().dispose();", "B\nA")]
    [InlineData("{ var k = 10; extend Integer { plusK() { return this + k; } } writeLn(1.plusK()); }", "11")]
    [InlineData("extend Integer { isPositive() { return this > 0; } static zero() { return 0; } } writeLn([4.isPositive(), (0 - 3).isPositive(), Integer.zero()]); var f = 4.isPositive; writeLn(f());", "[true, false, 0]\ntrue")]
    [InlineData("class P {} extend P { equal(o) { return true; } } writeLn(new P().equal(1)); try { throw 5; } catch (P e) { writeLn(\"not caught here\"); } catch (e) { writeLn(e); }", "true\n5")]
    [InlineData("class Celsius extends Number { warm() { return this > 20; } } var c = new Celsius(21.5); writeLn([c, c + 1, 1 + c, -c, c * 2]); writeLn([c is Number, c is Celsius, 21.5 is Celsius, c.warm(), c == 21.5, c < 22]);", "[Celsius:21.5, Celsius:22.5, Celsius:22.5, Celsius:-21.5, Celsius:43.0]\n[true, true, false, true, true, true]")]
    [InlineData("writeLn([new Number(5), new Integer(), new Integer(42), new Boolean(), new Boolean(true)]); writeLn(new String() == \"\"); writeLn(new Integer(42) is Integer);", "[5.0, 0, 42, false, true]\ntrue\ntrue")]
    [InlineData("class Name extends String { shout() { return this + \"!\"; } } var n = new Name(\"Ann\"); writeLn(n); writeLn([n.length, n[0], n.shout()]); foreach (var ch in n) write(ch); writeLn(null);", "Name:\"Ann\"\n[3, \"A\", Name:\"Ann!\"]\nAnnnull")]
    [InlineData("class Flag extends Boolean {} var f = new Flag(true); if (f) writeLn(\"on\"); writeLn([!f, f && true, f == true]); while (new Flag()) { writeLn(\"never\"); }", "on\n[false, true, true]")]
    [InlineData("class Count extends Integer { constructor(n) { super(n * 2); this.label = \"x\"; } } var k = new Count(3); writeLn([k, k + 1, (k + 1).label, k / 4, k - k]);", "[Count:6, Count:7, \"x\", 1.5, Count:0]")]
    [InlineData("class Celsius extends Number {} class Hot extends Celsius {} writeLn([new Hot(40), new Celsius(), new Hot(1) + new Celsius(2)]);", "[Hot:40.0, Celsius:0.0, Hot:3.0]")]
    [InlineData("class C extends Number { toString() { return \"C\" + super.toString(); } } writeLn(new C(1)); writeLn(new C(2).toString());", "CC:1.0\nCC:2.0")]
    [InlineData("class Celsius extends Number {} extend Celsius { f() { return this * 9 / 5 + 32; } } writeLn(new Celsius(100).f());", "Celsius:212.0")]
    [InlineData("var f = function (x) { return x + 1; }; writeLn(new Function(f) == f); writeLn(new Function(f)(2)); var g = new Function(); writeLn([g(), g is Function]); writeLn(g);", "true\n3\n[null, true]\nFunction:function")]
    [InlineData("class Handler extends Function { constructor(f) { super(f); this.calls = 0; } describe() { return \"handler\"; } } var h = new Handler(function (x) { return x * 2; }); writeLn(h(21)); writeLn(h.describe()); writeLn([h is Function, h is Handler]); writeLn(h); h.calls = h.calls + 1; writeLn(h.calls);", "42\nhandler\n[true, true]\nHandler:function\n1")]
    [InlineData("class Say extends Function {} var say = new Say(writeLn); say(\"via a built-in\"); class P { m() { return \"m\"; } } writeLn(new Say(new P().m)()); writeLn(new Say()());", "via a built-in\nm\nnull")]
    [InlineData("class H extends Function {} var h = new H(async function () { return 5; }); writeLn(await h()); var calls = [new H(function () { return 1; }), function () { return 2; }]; foreach (var c in calls) write(c()); writeLn(null);", "5\n12null")]
    [InlineData("class V { constructor(x) { this.x = x; } static operator +(a, b) { return new V(a.x + b.x); } static operator <(a, b) { return a.x < b.x; } } var v = new V(1) + new V(2); writeLn(v.x); writeLn([new V(1) < new V(2), new V(3) < new V(2)]);", "3\n[true, false]")]
    [InlineData("class M { static operator *(a, b) { return \"M*\"; } } writeLn(2 * new M()); writeLn(new M() * 2);", "M*\nM*")]
    [InlineData("class A { static operator +(a, b) { return \"A\"; } } class B { static operator +(a, b) { return \"B\"; } } writeLn(new A() + new B()); writeLn(new B() + new A());", "A\nB")]
    [InlineData("class A { static operator -(a, b) { return \"A-\"; } } class B extends A {} writeLn(new B() - 1);", "A-")]
    [InlineData("class Money extends Number { static operator +(a, b) { return \"money\"; } } writeLn(new Money(1) + 2); class C extends Number {} writeLn(new C(1) + 2);", "money\nC:3.0")]
    [InlineData("class Vec extends Array { static operator +(a, b) { var r = new Vec(); r.push(a[0] + b[0]); return r; } } var p = new Vec(); p.push(1); var q = new Vec(); q.push(2); writeLn(p + q);", "Vec:[3]")]
    [InlineData("class P { constructor(x) { this.x = x; } } extend P { static operator /(a, b) { return a.x / b.x; } } writeLn(new P(3) / new P(2));", "1.5")]
    [InlineData("class T { static operator +(a, b) { throw \"no\"; } } try { new T() + 1; } catch (e) { writeLn(e); }", "no")]
    [InlineData("class V { constructor(x) { this.x = x; } static operator +(a, b) { return new V(a.x + b.x); } static operator <(a, b) { return a.x < b.x; } } writeLn([1, 2, (new V(1) + new V(2)).x, [3, [4, (new V(5) + new V(6)).x]]]);", "[1, 2, 3, [3, [4, 11]]]")]
    [InlineData("class S { static operator >=(a, b) { return true; } static operator <=(a, b) { return false; } } writeLn([new S() >= 1, new S() <= 1, new S() == new S()]);", "[true, false, false]")]
    [InlineData("class A { static var n = 1; static make(x) { return x * 2; } } var a = new A(); writeLn([a.n, a.make(4), A.n]);", "[1, 8, 1]")]
    [InlineData("class A { static var n = 1; static m() { return \"static\"; } m() { return \"method\"; } } var a = new A(); a.n = 5; writeLn([a.n, A.n, a.m()]);", "[5, 5, \"method\"]")]
    [InlineData("class A { static var n = 1; static f() { return 1; } } class B extends A {} var b = new B(); A.n = 2; writeLn([b.n, b?.n, b?.missing]); var g = b.f; writeLn(g == A.f);", "[2, 2, null]\ntrue")]
    [InlineData("class A { static var k = \"s\"; } var a = new A(); a.k = function () { return \"field\"; }; writeLn(a.k()); class S extends Array { static var size = 3; } writeLn(new S().size);", "field\n3")]
    [InlineData("class A { constructor(x, y = 2) { this.x = x; this.y = y; } } class B extends A {} class C extends B { m() { return 1; } } writeLn([new B(5).x, new C(1, 3).y, new C(1).y]);", "[5, 3, 2]")]
    [InlineData("class E extends Error {} var e = new E(\"bad\"); writeLn([e.message, e is Error]);", "[\"bad\", true]")]
    [InlineData("class R { constructor(w, h) { this.w = w; this.h = h; } property area { get { return this.w * this.h; } } } var n = null; writeLn([new R(2, 3).area, n?.area, new R(1, 1)?.area]);", "[6, null, 1]")]
    [InlineData("class T { property v { get { return field; } set(value) { field = value; } } } var t = new T(); var before = t.v; t.v = 20; writeLn([before, t.v, t]);", "[null, 20, T {v: 20}]")]
    [InlineData("class D { property v { get { return field; } set(value) { field = value * 2; } } } var d = new D(); var a = d.v = 5; writeLn([a, d.v, d.v = 1]);", "[5, 10, 1]")]
    [InlineData("class T { property v { get { return field; } set(value) { field = value; } } } var t = new T(); for (var i = 0; i < 1000; i = i + 1) { t.v = i; } writeLn(t.v);", "999")]
    [InlineData("class S { static var n = 3; static property count { get { return S.n * 2; } set(v) { S.n = v; } } } S.count = 10; writeLn([S.count, S.n]);", "[20, 10]")]
    [InlineData("class K { static property total { get { return field; } set(v) { field = v; } } } K.total = 4; writeLn([K.total, K.total = 5]);", "[4, 5]")]
    [InlineData("class B { property v { get { return 1; } } } class C extends B { property v { get { return super.v + 10; } } } class E extends B {} writeLn([new B().v, new C().v, new E().v]);", "[1, 11, 1]")]
    [InlineData("class B { property v { get { return field; } } } class C extends B { property v { set(value) { field = value; } } } var c = new C(); c.v = 3; writeLn(c.v);", "3")]
    [InlineData("extend Integer { property doubled { get { return this * 2; } } } class P { constructor() { this.x = 1; } } extend P { property twice { get { return this.x * 2; } } } writeLn([4.doubled, new P().twice]);", "[8, 2]")]
    [InlineData("class X { property v { set(value) { throw \"bad\"; } } } var x = new X(); var r = 0; for (var i = 0; i < 3; i = i + 1) { try { x.v = i; } catch (e) { r = r + 1; } } writeLn(r);", "3")]
    [InlineData("class G { property a { get { this.b = 5; return this.b; } } property b { get { return field; } set(value) { field = value + 1; } } } writeLn(new G().a);", "6")]
    [InlineData("class Celsius extends Number { property kelvin { get { return this + 273.0; } } } writeLn(new Celsius(1.5).kelvin);", "Celsius:274.5")]
    [InlineData("var field = 9; class F { property a { get { return field; } set(value) { var field = value; } } } var f = new F(); f.a = 1; writeLn([f.a, field]);", "[null, 9]")]
    [InlineData("class A { property() { return 1; } } var property = 2; writeLn([new A().property(), property]);", "[1, 2]")]
    [InlineData("function describe(this, prefix) { return prefix + this.x.toString(); } class P { constructor() { this.x = 3; } } var f = function (this, n) { return this.x + n; }; var p = new P(); writeLn([describe(p, \"x = \"), f(p, 1)]);", "[\"x = 3\", 4]")]
    [InlineData("var Point = new Class(\"Point\"); Point.setMethod(\"constructor\", function (this, x, y) { this.x = x; this.y = y; }); Point.setMethod(\"describe\", function (this, prefix) { return prefix + this.x.toString() + \", \" + this.y.toString(); }); var p = new Point(3, 4); var d = p.describe; writeLn([p.describe(\"p = \"), d(\"d = \"), p, Point, p is Point, p is Object]);", "[\"p = 3, 4\", \"d = 3, 4\", Point {x: 3, y: 4}, class Point, true, true]")]
    [InlineData("var Point = new Class(\"Point\"); Point.setMethod(\"constructor\", function (this, x, y) { this.x = x; this.y = y; }); Point.setMethod(\"describe\", function (this, prefix) { return prefix + this.x.toString() + \", \" + this.y.toString(); }); var p = new Point(3, 4); var Line = new Class(\"Line\", Point); var l = new Line(1, 2); Point.setMethod(\"describe\", function (this, prefix) { return prefix + \"replaced\"; }); writeLn([p.describe(\"p: \"), l.describe(\"l: \"), l is Point, l]);", "[\"p: replaced\", \"l: replaced\", true, Line {x: 1, y: 2}]")]
    [InlineData("var C = new Class(); var c = new C(); c.x = 1; var N = new Class(null, C); writeLn([C, c, new N() is C]);", "[class, class {x: 1}, true]")]
    [InlineData("var Point = new Class(\"Point\"); Point.setMethod(\"constructor\", function (this, x, y) { this.x = x; this.y = y; }); Point.setMethod(\"describe\", function (this, prefix) { return prefix + this.x.toString() + \", \" + this.y.toString(); }); Point.origin = function () { return new Point(0, 0); }; Point.count = 0; Point.count = Point.count + 1; writeLn([Point.origin(), Point.count]);", "[Point {x: 0, y: 0}, 1]")]
    [InlineData("var Base = new Class(\"Base\"); Base.setMethod(\"hi\", function (this) { return \"hi \" + this.toString(); }); function make() { class D extends Base { constructor() { this.n = 1; } } return new D(); } writeLn(make().hi());", "hi D {n: 1}")]
    [InlineData("var Base = new Class(\"Base\"); function make() { class D extends Base {} return new D(); } writeLn(make());", "D {}")]
    [InlineData("class P { constructor() { this.x = 2; } } var f = function (this, n) { return this.x * n; }; var p = new P(); var q = new P(); q.x = 5; p.g = f; writeLn(p.g(q, 3));", "15")]
    [InlineData("var R = new Class(\"R\"); var log = []; R.setMethod(\"destructor\", function (this) { log.push(\"disposed\"); }); using (var r = new R()) { log.push(\"used\"); } writeLn(log);", "[\"used\", \"disposed\"]")]
    [InlineData("extend Integer { describe(prefix) { return prefix + this.toString(); } } Integer.setMethod(\"twice\", function (this) { return this * 2; }); writeLn([4.twice(), 4.describe(\"n = \")]);", "[8, \"n = 4\"]")]
    [InlineData("var C = new Class(); var c = new C(); var before = [C.__className, C.toString()]; C.__className = \"Point\"; writeLn([before, C.__className, C, c, c.__instanceOf == C, C.__parent, Object.__parent]);", "[[null, \"class\"], \"Point\", class Point, Point {}, true, class Object, null]")]
    [InlineData("class A {} class B extends A {} var n = null; writeLn([B.__parent, new B().__instanceOf, 5.__instanceOf, \"s\".__instanceOf, [].__instanceOf, null.__instanceOf, n?.__instanceOf]);", "[class A, class B, class Integer, class String, class Array, null, null]")]
    [InlineData("Integer.__className = \"Double\"; writeLn([Integer, 5.__instanceOf, new Class(\"P\").__className]);", "[class Double, class Double, \"P\"]")]
    [InlineData("var C = new Class(\"C\"); C.__className = null; writeLn([C, C.__className, new C()]);", "[class, null, class {}]")]
    [InlineData("var Point = new Class(\"Point\"); Point.setMethod(\"constructor\", function (this, x, y) { this.x = x; this.y = y; }); Point.setProperty(\"sum\", function (this) { return this.x + this.y; }, function (this, value) { this.x = value; this.y = 0; }); var p = new Point(3, 4); var before = p.sum; p.sum = 10; Point.setProperty(\"sum\", function (this) { return 100; }, null); writeLn([before, p, p.sum]);", "[7, Point {x: 10, y: 0}, 100]")]
    [InlineData("var Point = new Class(\"Point\"); Point.setMethod(\"constructor\", function (this, x, y) { this.x = x; this.y = y; }); Point.setStaticProperty(\"unit\", function () { return Point.unitValue; }, function (value) { Point.unitValue = value; }); Point.unitValue = 1; var before = Point.unit; Point.unit = 5; writeLn([before, Point.unit, Point.unitValue]);", "[1, 5, 5]")]
    [InlineData("var Point = new Class(\"Point\"); Point.setMethod(\"constructor\", function (this, x, y) { this.x = x; this.y = y; }); Point.setConstant(\"dimensions\", 2); var before = Point.dimensions; Point.setConstant(\"dimensions\", 3); writeLn([before, Point.dimensions, new Point(1, 2).dimensions]);", "[2, 3, 3]")]
    [InlineData("class K { static const max = 10; static make() { return 1; } } var before = [K.max, K.make()]; K.make = function () { return 2; }; extend K { static const min = 1; } writeLn([before, K.make(), K.min, new K().max]);", "[[10, 1], 2, 1, 10]")]
    [InlineData("class A { property v { get { return 1; } } } class B extends A {} A.setProperty(\"v\", function (this) { return 2; }, null); writeLn([new A().v, new B().v]);", "[2, 2]")]
    [InlineData("extend Integer {} Integer.setProperty(\"doubled\", function (this) { return this * 2; }, null); writeLn(4.doubled);", "8")]
    [InlineData("class Entity extends Class { constructor(name) { super(name); this.setMethod(\"save\", function (this) { return \"saved \" + this.toString(); }); } describe() { return \"entity \" + this.__className; } } var User = new Entity(\"User\"); var u = new User(); writeLn([User.describe(), User is Entity, User is Class, User.__instanceOf == Entity, User, u.save(), u is User]);", "[\"entity User\", true, true, true, class User, \"saved User {}\", true]")]
    [InlineData("class Meta extends Class { hello() { return \"hi \" + this.__className; } } class Meta2 extends Meta {} var A = new Meta(\"A\"); var M = new Meta2(\"M\"); writeLn([A.hello(), new A(), A.__parent, M.hello(), M is Meta]);", "[\"hi A\", A {}, class Object, \"hi M\", true]")]
    [InlineData("class Meta extends Class {} class Shape { area() { return 0; } } var Sq = new Meta(\"Sq\", Shape); writeLn([new Sq().area(), new Sq() is Shape, Sq.__parent]);", "[0, true, class Shape]")]
    [InlineData("class Meta extends Class { hello() { return \"metaclass\"; } } var A = new Meta(\"A\"); A.hello = function () { return \"static\"; }; var P = new Class(\"P\"); writeLn([A.hello(), P.__instanceOf, P is Class, P is Meta]);", "[\"static\", class Class, true, false]")]
    [InlineData("class Meta extends Class {} var C = new Meta(); writeLn([C, new C()]);", "[class, class {}]")]
    [InlineData("async function after(ms, v) { await delay(ms); return v; } async function fails(ms, m) { await delay(ms); throw m; } writeLn([await Task.all([after(3, \"a\"), 2, after(1, \"c\")]), await Task.all([])]);", "[[\"a\", 2, \"c\"], []]")]
    [InlineData("async function after(ms, v) { await delay(ms); return v; } async function fails(ms, m) { await delay(ms); throw m; } try { await Task.all([after(3, 1), fails(1, \"bad\"), fails(2, \"worse\")]); } catch (e) { writeLn(e); }", "bad")]
    [InlineData("async function after(ms, v) { await delay(ms); return v; } async function fails(ms, m) { await delay(ms); throw m; } writeLn([await Task.any([after(3, \"slow\"), after(1, \"fast\")]), await Task.any([after(3, \"slow\"), 5])]);", "[\"fast\", 5]")]
    [InlineData("async function after(ms, v) { await delay(ms); return v; } async function fails(ms, m) { await delay(ms); throw m; } try { await Task.any([after(3, 1), fails(1, \"first\")]); } catch (e) { writeLn(e); }", "first")]
    [InlineData("async function after(ms, v) { await delay(ms); return v; } async function fails(ms, m) { await delay(ms); throw m; } writeLn([await Task.timeout(after(1, \"in time\"), 50), await Task.timeout(7, 0)]); try { await Task.timeout(after(50, \"late\"), 2); } catch (TimeoutError e) { writeLn([e.code, e is Error]); }", "[\"in time\", 7]\n[\"WL3010\", true]")]
    [InlineData("async function after(ms, v) { await delay(ms); return v; } async function fails(ms, m) { await delay(ms); throw m; } writeLn(await Task.all([Task.any([after(2, \"x\")]), Task.timeout(after(1, \"y\"), 50)]));", "[\"x\", \"y\"]")]
    [InlineData("writeLn([Integer.parse(\"10\"), Integer.parse(\"-7\"), Integer.parse(\"+007\"), Number.parse(\"1.5\"), Number.parse(\"10\"), Number.parse(\"-1e3\"), Number.parse(\"Infinity\"), Boolean.parse(\"true\"), Integer.parse(\"-9223372036854775808\")]);", "[10, -7, 7, 1.5, 10.0, -1000.0, Infinity, true, -9223372036854775808]")]
    [InlineData("class Celsius extends Number {} writeLn([new Integer(\"10\"), new Number(\"2.5\"), new Boolean(\"false\"), new Celsius(\"21.5\")]);", "[10, 2.5, false, Celsius:21.5]")]
    [InlineData("class C { static var count = 0; } class D extends C {} var p = new C(); var q = new D(); p.count = 5; q.count = q.count + 1; p.other = 1; writeLn([C.count, p.count, q.count, p, C?.other]);", "[6, 6, 6, C {other: 1}, null]")]
    [InlineData("class Stack extends Array { static var made = 0; } var s = new Stack(); s.made = 3; writeLn([Stack.made, s]);", "[3, Stack:[]]")]
    [InlineData("class A { hi() { return \"A\"; } } class B extends A { hi() { return \"B\"; } } var b = new B(); var before = b.hi(); var removed = [B.removeMethod(\"hi\"), B.removeMethod(\"hi\")]; writeLn([before, removed, b.hi()]);", "[\"B\", [true, false], \"A\"]")]
    [InlineData("var P = new Class(\"P\"); P.setMethod(\"describe\", function (this) { return \"p\"; }); var p = new P(); var before = p.describe(); P.removeMethod(\"describe\"); try { p.describe(); } catch (e) { writeLn([before, e.code]); }", "[\"p\", \"WL3006\"]")]
    [InlineData("class A { constructor(x) { this.x = x; } } class B extends A { constructor() { super(1); this.y = 2; } } B.removeMethod(\"constructor\"); writeLn(new B(5));", "B {x: 5}")]
    [InlineData("class MathStuff { const PI = 3.14; static half() { return MathStuff.PI / 2; } } var m = new MathStuff(); writeLn([m.PI, MathStuff.PI, MathStuff.half()]);", "[3.14, 3.14, 1.57]")]
    [InlineData("class K {} extend K { const MAX = 10; } writeLn([K.MAX, new K().MAX]);", "[10, 10]")]
    [InlineData("var log = []; class A { constructor(x) { this.x = x; } sum(y) { return this.x + y; } property twice { get { return this.x * 2; } } static make() { return new A(1); } static var count = 3; static const MAX = 9; dispose() { log.push(\"A gone\"); } } var B = new Class(\"B\"); B.setMethod(\"constructor\", function (this, x) { this.x = x; }); B.setMethod(\"sum\", function (this, y) { return this.x + y; }); B.setProperty(\"twice\", function (this) { return this.x * 2; }, null); B.make = function () { return new B(1); }; B.count = 3; B.setConstant(\"MAX\", 9); B.setMethod(\"dispose\", function (this) { log.push(\"B gone\"); }); foreach (var C in [A, B]) { var c = new C(5); log.push([c.sum(1), c.twice, C.make().x, C.count, C.MAX, c is C]); c.dispose(); log.push(c.toString()); } writeLn(log);", "[[6, 10, 1, 3, 9, true], \"A gone\", \"A {x: 5}\", [6, 10, 1, 3, 9, true], \"B gone\", \"B {x: 5}\"]")]
    [InlineData("class A { constructor(x) { this.x = x; } describe() { return \"A\" + this.x.toString(); } } class D extends A { describe() { return \"D/\" + super.describe(); } } var E = new Class(\"E\", A); E.setMethod(\"describe\", function (this) { return \"E/A\" + this.x.toString(); }); writeLn([new D(1).describe(), new E(2).describe(), new E(3) is A, D.__parent == E.__parent]);", "[\"D/A1\", \"E/A2\", true, true]")]
    [InlineData("class K { static const X = 1; } extend K { static X() { return 2; } } var before = K.X(); K.X = 5; K.X = 6; writeLn([before, K.X]);", "[2, 6]")]
    [InlineData("var add = (a, b) => { return a + b; }; var none = () => { return \"none\"; }; writeLn([add(2, 3), none(), none]);", "[5, \"none\", function]")]
    [InlineData("var g = (a, b = 10) => { return a + b; }; var k = 5; var h = (x) => { return x + k; }; k = 6; writeLn([g(1), g(1, 2), h(1)]);", "[11, 3, 7]")]
    [InlineData("var P = new Class(\"P\"); P.setMethod(\"sum\", (this) => { return this.x + this.y; }); var p = new P(); p.x = 1; p.y = 2; var describe = (this, prefix) => { return prefix + this.x.toString(); }; writeLn([p.sum(), describe(p, \"x = \")]);", "[3, \"x = 1\"]")]
    [InlineData("class A { constructor() { this.v = 7; } get() { var f = () => { return this.v; }; return f(); } } writeLn(new A().get());", "7")]
    [InlineData("writeLn([((x) => { return x * 2; })(21), (1 + 2) * 3, ((4))]);", "[42, 9, 4]")]
    [InlineData("var add = (a, b) => a + b; var twice = x => x * 2; var none = () => null; writeLn([add(2, 3), twice(21), none()]);", "[5, 42, null]")]
    [InlineData("var k = 1; var f = x => x + k; k = 2; var add = a => b => a + b; function apply(g, v) { return g(v); } writeLn([f(1), add(2)(3), apply(x => x + 1, 41)]);", "[3, 5, 42]")]
    [InlineData("var m = () => ({a: 1}); var b = x => { }; writeLn([m(), b(1)]);", "[{\"a\": 1}, null]")]
    [InlineData("var f = async x => { await delay(1); return x * 2; }; var g = async (a, b) => a + b; var h = async x => await f(x) + 1; writeLn([await f(21), await g(1, 2), await h(1)]);", "[42, 3, 3]")]
    public void ResultsAreTheSpecifiedOnes(string source, string printed)
    {
        Assert.Equal((WebLingoCommandLine.Success, printed.Length == 0 ? "" : printed + "\n", ""), Weblingo("run", source));
    }

    [Theory]
    [InlineData("writeLn(1 && true);")]
    [InlineData("writeLn(true && 1);")]
    [InlineData("writeLn(false || null);")]
    [InlineData("writeLn(!0);")]
    [InlineData("writeLn(1 < true);")]
    [InlineData("writeLn(null >= 0);")]
    [InlineData("if (1) writeLn(1);")]
    [InlineData("if (null) writeLn(1); else writeLn(2);")]
    [InlineData("while (1) writeLn(1);")]
    [InlineData("do writeLn(1); while (0);")]
    [InlineData("for (; 1; ) writeLn(1);")]
    [InlineData("writeLn(\"a\" + 1);")]
    [InlineData("writeLn(1 + \"a\");")]
    [InlineData("writeLn(\"a\" < \"b\");")]
    [InlineData("writeLn(-\"a\");")]
    [InlineData("if (\"true\") writeLn(1);")]
    [InlineData("writeLn(function () {} + 1);")]
    public void BooleansAndNumbersDoNotMix(string source)
    {
        var (exit, _, error) = Weblingo("run", source);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Contains("error WL3001:", error);
    }

    [Theory]
    [InlineData("var x = 1; x(2);", "WL2005")]
    [InlineData("writeLn(1)(2);", "WL2005")]
    [InlineData("var write = 1; write(2);", "WL2005")]
    [InlineData("var f = function (a) {}; f();", "WL2006")]
    [InlineData("var w = writeLn; w(1, 2);", "WL2006")]
    [InlineData("function f() { return f(); } f();", "WL3003")]
    [InlineData("f(); var y = 1; function f() { writeLn(y); }", "WL2004")]
    public void CallsAreCheckedAtRunTime(string source, string code)
    {
        var (exit, _, error) = Weblingo("run", source);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Contains($"error {code}:", error);
    }

    [Theory]
    [InlineData("writeLn(\"abc\"[3]);", "WL3005")]
    [InlineData("writeLn(\"abc\"[-1]);", "WL3005")]
    [InlineData("writeLn(\"\"[0]);", "WL3005")]
    [InlineData("writeLn(\"abc\"[1.0]);", "WL3001")]
    [InlineData("var s = \"abc\"; s[0] = \"x\";", "WL3001")]
    [InlineData("writeLn(5[0]);", "WL3001")]
    [InlineData("writeLn(\"abc\".size);", "WL3006")]
    [InlineData("writeLn(5.length);", "WL3006")]
    [InlineData("writeLn(null.length);", "WL3006")]
    [InlineData("writeLn(\"abc\".length());", "WL2005")]
    [InlineData("writeLn(\"abc\".size(1));", "WL3006")]
    [InlineData("var x = 1; x.length = 2;", "WL3001")]
    [InlineData("var x = new 5();", "WL2005")]
    [InlineData("class P {} var c = P; c();", "WL2005")]
    [InlineData("class P { constructor(a, b) {} } var p = new P(1);", "WL2006")]
    [InlineData("class E {} var e = new E(1);", "WL2006")]
    [InlineData("class P { m(a) {} } new P().m();", "WL2006")]
    [InlineData("class P {} writeLn(new P().missing);", "WL3006")]
    [InlineData("class P {} new P().missing();", "WL3006")]
    [InlineData("class P { m() {} } var p = new P(); p.m = 1;", "WL3001")]
    [InlineData("class P {} var m = {}; m[new P()] = 1;", "WL3001")]
    [InlineData("class P {} foreach (var x in new P()) writeLn(x);", "WL3001")]
    [InlineData("class P {} writeLn(new P()[0]);", "WL3001")]
    [InlineData("class P {} writeLn(P.x);", "WL3006")]
    [InlineData("class A { constructor(x) {} } class B extends A {} new B();", "WL2006")]
    [InlineData("class A {} class B extends A { m() { return super.nope(); } } new B().m();", "WL3006")]
    [InlineData("var NotClass = 5; { class A extends NotClass {} }", "WL3001")]
    [InlineData("using (var x = 5) {}", "WL3006")]
    [InlineData("class A {} using (var a = new A()) {}", "WL3006")]
    [InlineData("class A { dispose() {} } new A().dispose(1);", "WL2006")]
    [InlineData("class A { dispose(x) {} } using (var a = new A()) {}", "WL2006")]
    [InlineData("class A { static var n = 1; } writeLn(new A().m);", "WL3006")]
    [InlineData("class A { static m(x) {} } A.m();", "WL2006")]
    [InlineData("class A { m() { return super.m(); } } new A().m();", "WL3006")]
    [InlineData("new Object().equal();", "WL2006")]
    [InlineData("class A {} new A().equal = 1;", "WL3001")]
    [InlineData("throw \"x\";", "WL3008")]
    [InlineData("throw new Error(\"m\");", "WL3008")]
    [InlineData("try { var a = []; a[1]; } finally { writeLn(1); }", "WL3005")]
    [InlineData("try { var a = []; a[1]; } catch (e) { throw; }", "WL3005")]
    [InlineData("class Bad { toString() { return 5; } } writeLn(new Bad());", "WL3001")]
    [InlineData("class Bad { toString(x) { return \"\"; } } writeLn([new Bad()]);", "WL2006")]
    [InlineData("class Bad { equal(o) { return 1; } } var B = new Bad(); try { throw 1; } catch (B e) {}", "WL3001")]
    [InlineData("try { throw 1; } catch (String e) {}", "WL3008")]
    [InlineData("var f = function (a, b = 1) {}; f();", "WL2006")]
    [InlineData("var f = function (a = 1) {}; f(1, 2);", "WL2006")]
    [InlineData("var a = [1]; a.length = 2;", "WL3001")]
    [InlineData("writeLn([].pop());", "WL3005")]
    [InlineData("writeLn([1, 2][2]);", "WL3005")]
    [InlineData("var a = [1]; a[-1] = 0;", "WL3005")]
    [InlineData("writeLn([1][\"0\"]);", "WL3001")]
    [InlineData("writeLn([1].push);", "WL3001")]
    [InlineData("var a = [1]; a.push(1, 2);", "WL2006")]
    [InlineData("var a = [1]; a.pop(1);", "WL2006")]
    [InlineData("writeLn([1].size);", "WL3006")]
    [InlineData("writeLn([1].length());", "WL2005")]
    [InlineData("writeLn([1] + [2]);", "WL3001")]
    [InlineData("writeLn([1] < [2]);", "WL3001")]
    [InlineData("var m = {a: 1}; writeLn(m[\"b\"]);", "WL3007")]
    [InlineData("var m = {a: 1}; writeLn(m[1]);", "WL3007")]
    [InlineData("var m = {}; m[1.5] = 2;", "WL3001")]
    [InlineData("var m = {}; m[[1]] = 2;", "WL3001")]
    [InlineData("var m = {}; m[m] = 2;", "WL3001")]
    [InlineData("var m = {}; writeLn(m.has(NaN));", "WL3001")]
    [InlineData("var m = {a: 1}; writeLn(m.a);", "WL3006")]
    [InlineData("var m = {a: 1}; writeLn(m.has);", "WL3001")]
    [InlineData("var m = {a: 1}; writeLn(m.has());", "WL2006")]
    [InlineData("var m = {a: 1}; writeLn(m.length());", "WL2005")]
    [InlineData("var m = {a: 1}; m.push(1);", "WL3006")]
    [InlineData("foreach (var x in {a: 1}) writeLn(x);", "WL3001")]
    [InlineData("foreach (var (k, v) in [1]) writeLn(k);", "WL3001")]
    [InlineData("foreach (var (k, v) in \"ab\") writeLn(k);", "WL3001")]
    [InlineData("foreach (var x in 5) writeLn(x);", "WL3001")]
    [InlineData("foreach (var x in null) writeLn(x);", "WL3001")]
    [InlineData("writeLn(#{1.5});", "WL3001")]
    [InlineData("var m = {}; writeLn(m?[1.5]);", "WL3001")]
    [InlineData("writeLn([1]?[\"x\"]);", "WL3001")]
    [InlineData("writeLn(5?[0]);", "WL3001")]
    [InlineData("writeLn(true?[0]);", "WL3001")]
    [InlineData("writeLn(#{1}?[0]);", "WL3001")]
    [InlineData("var m = {}; writeLn(m?[\"a\"].length);", "WL3006")]
    [InlineData("var s = #{}; s.add([1]);", "WL3001")]
    [InlineData("var s = #{}; writeLn(s.has(#{}));", "WL3001")]
    [InlineData("writeLn(#{1}[0]);", "WL3001")]
    [InlineData("foreach (var (k, v) in #{1}) writeLn(k);", "WL3001")]
    [InlineData("var s = #{1}; s.add();", "WL2006")]
    [InlineData("var s = #{1}; writeLn(s.add);", "WL3001")]
    [InlineData("var s = #{1}; s.push(1);", "WL3006")]
    [InlineData("writeLn(#{1}.length());", "WL2005")]
    [InlineData("var x = 5; { extend x { m() {} } }", "WL3001")]
    [InlineData("extend Object { d() { return 1; } } null.d();", "WL3006")]
    [InlineData("extend Integer { m() { return 1; } } \"s\".m();", "WL3006")]
    [InlineData("var s = new String(5);", "WL3001")]
    [InlineData("new Integer(1.5);", "WL3001")]
    [InlineData("new Integer(1, 2);", "WL2006")]
    [InlineData("class Count extends Integer {} var k = new Count(2.5);", "WL3001")]
    [InlineData("class Flag extends Boolean {} writeLn(new Flag(true) + 1);", "WL3001")]
    [InlineData("class N extends Number {} var m = {}; m[new N(1)] = 1;", "WL3001")]
    [InlineData("new Function(5);", "WL3001")]
    [InlineData("class H extends Function {} new H(1);", "WL3001")]
    [InlineData("class H extends Function {} var h = new H(function (x) { return x; }); h(1, 2);", "WL2006")]
    [InlineData("class C extends Number {} var c = new C(1); c();", "WL2005")]
    [InlineData("class N { static operator +(a, b) { return 1; } } writeLn(new N() > 1);", "WL3001")]
    [InlineData("class N { static operator +(a, b) { return 1; } } writeLn(-new N());", "WL3001")]
    [InlineData("class A {} class B extends A {} new B(1);", "WL2006")]
    [InlineData("class A { constructor() {} } class B extends A {} new B(1);", "WL2006")]
    [InlineData("class R { property a { get { return 1; } } } new R().a = 2;", "WL3001")]
    [InlineData("class R { property a { set(v) { } } } writeLn(new R().a);", "WL3006")]
    [InlineData("class R { property a { get { return 1; } } } new R().a();", "WL3001")]
    [InlineData("class S { static property a { get { return 1; } } } S.a = 2;", "WL3001")]
    [InlineData("class S { static property a { get { return 1; } } } S.a();", "WL3001")]
    [InlineData("class S { static property a { set(v) { } } } writeLn(S.a);", "WL3006")]
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
    [InlineData("var C = new Class(\"C\"); var c = new C(); writeLn(c.__className);", "WL3006")]
    [InlineData("class K { static const max = 10; } K.max = 1;", "WL3001")]
    [InlineData("var C = new Class(\"C\"); C.setConstant(\"n\", 1); C.n = 2;", "WL3001")]
    [InlineData("var C = new Class(\"C\"); C.setProperty(\"p\", 5, null);", "WL3001")]
    [InlineData("var C = new Class(\"C\"); C.setProperty(\"p\", null);", "WL2006")]
    [InlineData("var C = new Class(\"C\"); C.setConstant(\"a b\", 1);", "WL3001")]
    [InlineData("var C = new Class(\"C\"); C.setProperty(\"p\", function (this) { return 1; }, null); new C().p = 1;", "WL3001")]
    [InlineData("var C = new Class(\"C\"); C.setStaticProperty(\"p\", null, function (v) {}); writeLn(C.p);", "WL3006")]
    [InlineData("class Meta extends Class {} new Meta(5);", "WL3001")]
    [InlineData("class Meta extends Class {} new Meta(\"A\", 5);", "WL3001")]
    [InlineData("class Bad extends Class { constructor() { super(\"B\", this); } } new Bad();", "WL3001")]
    [InlineData("class Meta extends Class {} new Meta(\"A\", Task);", "WL3001")]
    [InlineData("Task.all(5);", "WL3001")]
    [InlineData("Task.any([]);", "WL3001")]
    [InlineData("Task.timeout(1, -1);", "WL3001")]
    [InlineData("Task.all();", "WL2006")]
    [InlineData("var f = Task.all;", "WL3001")]
    [InlineData("class P {} P.all([]);", "WL3006")]
    [InlineData("async function late() { await delay(50); return 1; } var t = Task.timeout(late(), 2); await delay(60);", "WL3010")]
    [InlineData("async function late() { await delay(50); return 1; } await Task.timeout(late(), 2);", "WL3010")]
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
    [InlineData("class C {} var p = new C(); p.count = 1; writeLn(C.count);", "WL3006")]
    [InlineData("class A {} A.removeMethod(5);", "WL3001")]
    [InlineData("class A {} A.removeMethod();", "WL2006")]
    [InlineData("class MathStuff { const PI = 3.14; } MathStuff.PI = 3;", "WL3001")]
    [InlineData("class MathStuff { const PI = 3.14; } new MathStuff().PI = 3;", "WL3001")]
    [InlineData("class Meta extends Class { constructor() { super(\"M\", 5); } } new Meta();", "WL3001")]
    public void MembersAndIndexesAreCheckedAtRunTime(string source, string code)
    {
        var (exit, _, error) = Weblingo("run", source);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Contains($"error {code}:", error);
    }

    [Theory]
    [InlineData("async function f() { throw \"lost\"; } f(); writeLn(\"end\");", "WL3008")]
    [InlineData("async function f() { await null; throw new Error(\"x\"); } await f();", "WL3008")]
    [InlineData("async function f() { return [1][3]; } await f();", "WL3005")]
    [InlineData("async function f() { await delay(1); return [1][3]; } f(); await delay(5);", "WL3005")]
    [InlineData("var other = null; async function a() { await null; await other; } var ta = a(); async function b() { await ta; } other = b(); await ta;", "WL3009")]
    [InlineData("async function forever() { await forever(); } await forever();", "WL3003")]
    [InlineData("delay(-1);", "WL3001")]
    [InlineData("delay(1.5);", "WL3001")]
    [InlineData("delay(0).forget(1);", "WL2006")]
    [InlineData("var n = 5; n.forget();", "WL3006")]
    [InlineData("class T extends Task {}", "WL3001")]
    [InlineData("var t = new Task();", "WL3001")]
    [InlineData("var m = {}; m[delay(0)] = 1;", "WL3001")]
    [InlineData("class R { async dispose() {} } function f() { using (var r = new R()) {} } f();", "WL3001")]
    [InlineData("class R { async dispose() {} } var f = function () { using (var r = new R()) {} }; f();", "WL3001")]
    [InlineData("class R { async dispose() { throw \"x\"; } } new R().dispose();", "WL3008")]
    [InlineData("class P { async toString() { return \"p\"; } } writeLn(new P());", "WL3001")]
    [InlineData("class P { async toString() { await null; return \"p\"; } } writeLn([new P()]);", "WL3001")]
    public void AsynchronyErrorsAreReported(string source, string code)
    {
        var (exit, _, error) = Weblingo("run", source);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Contains($"error {code}:", error);
    }

    [Fact]
    public void ADelayWaitsOnTheClock()
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        Assert.Equal((WebLingoCommandLine.Success, "1\n2\n", ""), Weblingo("run", "writeLn(1); await delay(60); writeLn(2);"));
        Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 60);
    }

    [Fact]
    public void AFaultNothingAwaitedIsReportedWhereItWasThrown()
    {
        var (exit, output, error) = Weblingo("run", "async function f() {\n    throw \"lost\";\n}\nf();\nwriteLn(\"end\");");
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Equal("end\n", output);
        Assert.Equal("program.wl(2,5): error WL3008:", FirstLineStart(error));
    }

    [Fact]
    public void SignedInt64OverflowIsARuntimeError()
    {
        const string source = "var x = 9223372036854775807 + 1;";
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("check", source).Exit);
        var (exit, _, error) = Weblingo("run", source);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Equal("program.wl(1,9): error WL3002:", FirstLineStart(error));
    }

    [Fact]
    public void TheSpecificationsExamplePrints30()
    {
        Assert.Equal((WebLingoCommandLine.Success, "30\n", ""),
            Weblingo("run", "var a = 10;\nvar b = 20;\nvar result = a + b;\nwriteLn(result);\n"));
    }

    [Theory]
    [InlineData("run")]
    [InlineData("--unknown")]
    [InlineData("run", "a.wl", "b.wl")]
    public void AUsageErrorExitsWith2(params string[] args)
    {
        var output = new StringWriter();
        Assert.Equal(WebLingoCommandLine.UsageError, WebLingoCommandLine.Run(args, output, new StringWriter()));
        Assert.StartsWith("Usage: weblingo", output.ToString());
    }

    [Fact]
    public void AFileThatCannotBeReadExitsWith4()
    {
        var error = new StringWriter();
        var missing = Path.Combine(_folder, "missing.wl");
        Assert.Equal(WebLingoCommandLine.FileUnreadable, WebLingoCommandLine.Run(["run", missing], new StringWriter(), error));
        Assert.Contains("cannot read the file", error.ToString());
    }

    [Fact]
    public void TheHelloWorldSampleGreets()
    {
        var sample = Path.Combine(SolutionFolder(), "samples", "HelloWorld", "hello.wl");
        var output = new StringWriter();
        Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["run", sample], output, new StringWriter()));
        Assert.Equal("Hello from WebLingo!\n", output.ToString());
    }

    [Fact]
    public void TheArithmeticSamplePrints30()
    {
        var sample = Path.Combine(SolutionFolder(), "samples", "Arithmetic", "arithmetic.wl");
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
        var good = string.Join(newline, "var a = 2;", "var b = a * 3;", "writeLn(b);");
        Assert.Equal((WebLingoCommandLine.Success, "6\n", ""), Weblingo("run", good));
        var bad = string.Join(newline, "var x = 1;", "writeLn(y);");
        Assert.Equal("program.wl(2,9): error WL2001:", FirstLineStart(Weblingo("run", bad).Error));
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
            var result = compiler.Compile(source, "fuzz.wl");
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
            .Compile("var a = 10; var b = a * 2 - 3; var c = 0; c = -b / a; writeLn(c);", "base.wl").Module!;
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
        File.WriteAllText(Path.Combine(_folder, "lib", "util.wl"), "export function twice(x) { return x * 2; }");
        File.WriteAllText(Path.Combine(_folder, "lib", "db.wl"),
            "import { twice } from \"./util\";\nexport var n = twice(2);\nexport function f() {\n    return [1][3];\n}");
        var (exit, output, error) = Weblingo("run", "import db from \"lib/db.wl\";\nwriteLn(db.n);\ndb.f();");
        Assert.Equal((WebLingoCommandLine.RuntimeFailed, "4\n"), (exit, output));
        // An imported file is named by its path from the current folder, which the test's folder is somewhere below.
        var folder = Path.GetRelativePath(Directory.GetCurrentDirectory(), _folder).Replace('\\', '/') + "/";
        Assert.Equal("lib/db.wl(4,12): error WL3005:", FirstLineStart(error.Replace(folder, "", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheCliImportsModulesWhileTheProgramRuns()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "lib"));
        File.WriteAllText(Path.Combine(_folder, "lib", "util.wl"), "export function twice(x) { return x * 2; }");
        var (exit, output, _) = Weblingo("run", "var u = await Module.import(\"lib/util\");\nwriteLn(u.twice(21));");
        Assert.Equal((WebLingoCommandLine.Success, "42\n"), (exit, output));
    }

    [Fact]
    public void TheCliGivesAScriptItsParameters()
    {
        const string script = "param size;\nparam name = \"x\";\nwriteLn([size, name, Integer.parse(size) * 2]);";
        var (exit, output, error) = Weblingo("run", script, "size=10", "name=Ann");
        Assert.Equal((WebLingoCommandLine.Success, "[\"10\", \"Ann\", 20]\n"), (exit, output));
        (exit, output, _) = Weblingo("run", script, "size=10");
        Assert.Equal((WebLingoCommandLine.Success, "[\"10\", \"x\", 20]\n"), (exit, output));
        (exit, _, error) = Weblingo("run", script);
        Assert.Equal((WebLingoCommandLine.RuntimeFailed, "program.wl(1,1): error WL2016:"), (exit, FirstLineStart(error)));
        (exit, _, error) = Weblingo("run", script, "size=1", "colour=red");
        Assert.Equal((WebLingoCommandLine.RuntimeFailed, "program.wl(1,1): error WL2016:"), (exit, FirstLineStart(error)));
        Assert.Equal(WebLingoCommandLine.UsageError, Weblingo("run", script, "size").Exit);
        Assert.Equal(WebLingoCommandLine.UsageError, Weblingo("run", script, "size=1", "size=2").Exit);
        Assert.Equal(WebLingoCommandLine.UsageError, Weblingo("run", script, "=1").Exit);
    }

    [Fact]
    public void AModuleThatCannotBeFoundIsWL2015()
    {
        var (exit, output, error) = Weblingo("run", "import x from \"none\";");
        Assert.Equal((WebLingoCommandLine.CompilationFailed, ""), (exit, output));
        Assert.Equal("program.wl(1,15): error WL2015:", FirstLineStart(error));
    }

    [Fact]
    public void TheModulesSampleRuns()
    {
        var sample = Path.Combine(SolutionFolder(), "samples", "Modules", "main.wl");
        var output = new StringWriter();
        Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["run", sample], output, new StringWriter()));
        Assert.Equal("Hello, WebLingo!\nHello, modules!\n2 greetings\n", output.ToString());
    }

    private (int Exit, string Output, string Error) Weblingo(string command, string source, params string[] parameters)
    {
        var path = Path.Combine(_folder, "program.wl");
        File.WriteAllText(path, source);
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WebLingoCommandLine.Run([command, path, .. parameters], output, error);
        // Diagnostics name the file as it was given; show only its name, so the expectations hold on every machine.
        return (exit, output.ToString(), error.ToString().Replace(path, "program.wl", StringComparison.Ordinal));
    }

    /// <summary>The first line of the error output, up to the end of its "error CODE:".</summary>
    private static string FirstLineStart(string error)
    {
        var line = error.Split('\n')[0];
        var end = line.IndexOf(": error ", StringComparison.Ordinal);
        return end < 0 ? line : line[..line.IndexOf(':', end + ": error ".Length)] + ":";
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
