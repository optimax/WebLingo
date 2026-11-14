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
        Assert.Equal((WsCommandLine.Success, "", ""), Weblingo("check", source));
        Assert.Equal((WsCommandLine.Success, "", ""), Weblingo("run", source));
    }

    [Theory]
    [InlineData("const x;", "WS1006", 1, 7)]
    [InlineData("var x;", "WS1006", 1, 5)]
    [InlineData("writeLn(x); var x = 1;", "WS2001", 1, 9)]
    [InlineData("var x = 10", "WS1002", 1, 11)]
    [InlineData("var x = 1; var x = 2;", "WS2002", 1, 16)]
    [InlineData("x = 10;", "WS2001", 1, 1)]
    [InlineData("{ var y = 1; } writeLn(y);", "WS2001", 1, 24)]
    [InlineData("{ var y = 1; var y = 2; }", "WS2002", 1, 18)]
    [InlineData("if (true) var y = 1; writeLn(y);", "WS2001", 1, 30)]
    [InlineData("if true) writeLn(1);", "WS1001", 1, 4)]
    [InlineData("else writeLn(1);", "WS1001", 1, 1)]
    [InlineData("break;", "WS2007", 1, 1)]
    [InlineData("writeLn(\"a\\qb\");", "WS1009", 1, 11)]
    [InlineData("writeLn(\"open);", "WS1010", 1, 9)]
    [InlineData("if (true) { continue; }", "WS2007", 1, 13)]
    [InlineData("for (var i = 0; i < 1; i = i + 1) {} writeLn(i);", "WS2001", 1, 46)]
    [InlineData("do writeLn(1) while (true);", "WS1002", 1, 14)]
    [InlineData("return;", "WS2008", 1, 1)]
    [InlineData("function f(a) {} f(1, 2);", "WS2006", 1, 18)]
    [InlineData("var m = {}; m?.a = 1;", "WS1007", 1, 13)]
    [InlineData("writeLn(this);", "WS2009", 1, 9)]
    [InlineData("var f = function () { return this; };", "WS2009", 1, 30)]
    [InlineData("class A { m() {} m() {} }", "WS2002", 1, 18)]
    [InlineData("class A {} class A {}", "WS2002", 1, 18)]
    [InlineData("class A {} A = 1;", "WS2003", 1, 12)]
    [InlineData("class A {} var a = A();", "WS2005", 1, 20)]
    [InlineData("var a = new A;", "WS1001", 1, 14)]
    [InlineData("class { }", "WS1001", 1, 7)]
    [InlineData("class A extends B {} class B extends A {}", "WS2013", 1, 1)]
    [InlineData("Object = 1;", "WS2003", 1, 1)]
    [InlineData("throw;", "WS2010", 1, 1)]
    [InlineData("function f() { try {} finally { return; } }", "WS2012", 1, 33)]
    [InlineData("while (true) { try {} finally { break; } }", "WS2012", 1, 33)]
    [InlineData("try {} writeLn(1);", "WS1001", 1, 8)]
    [InlineData("try {} catch (e) {} catch (Error e) {}", "WS1001", 1, 21)]
    [InlineData("try {} catch (Nope e) {}", "WS2001", 1, 15)]
    [InlineData("function f(a = 1, b) {}", "WS1001", 1, 19)]
    [InlineData("function f(a, b = 1) {} f();", "WS2006", 1, 25)]
    [InlineData("function f(a, b = 1) {} f(1, 2, 3);", "WS2006", 1, 25)]
    [InlineData("try { } catch (e) { var e = 1; }", "WS2002", 1, 25)]
    [InlineData("class A {} class B extends A { m() { super(); } }", "WS2009", 1, 38)]
    [InlineData("var X = 5; class A extends X {}", "WS2001", 1, 28)]
    [InlineData("var s = super;", "WS2009", 1, 9)]
    [InlineData("class A { dispose() {} destructor() {} }", "WS2011", 1, 24)]
    [InlineData("class A { destructor() {} function dispose() {} }", "WS2011", 1, 36)]
    [InlineData("using (var r = 1) {} writeLn(r);", "WS2001", 1, 30)]
    [InlineData("using (var r = r) {}", "WS2001", 1, 16)]
    [InlineData("using (const r = 1) {}", "WS1001", 1, 8)]
    [InlineData("class A { static m() { return this; } }", "WS2009", 1, 31)]
    [InlineData("class A { static var x = this; }", "WS2009", 1, 26)]
    [InlineData("class A {} class B extends A { static m() { return super.m(); } }", "WS2009", 1, 52)]
    [InlineData("class A { static var x = 1; static x() {} }", "WS2002", 1, 36)]
    [InlineData("class A { static var x; }", "WS1006", 1, 22)]
    [InlineData("class A { static constructor() {} }", "WS1001", 1, 18)]
    [InlineData("var v = 1; class A { static var x = v; }", "WS2001", 1, 37)]
    [InlineData("writeLn(\"abc\".);", "WS1001", 1, 15)]
    [InlineData("writeLn(\"abc\"[0);", "WS1001", 1, 16)]
    [InlineData("writeLn([1,]);", "WS1001", 1, 12)]
    [InlineData("var a = [1 2];", "WS1001", 1, 12)]
    [InlineData("var m = {1: 2};", "WS1001", 1, 10)]
    [InlineData("var m = {a 1};", "WS1001", 1, 12)]
    [InlineData("var m = {a: 1,};", "WS1001", 1, 15)]
    [InlineData("var m = {a: {1: 2}, b: 3}; writeLn(1);", "WS1001", 1, 14)]
    [InlineData("foreach (const x in [1]) writeLn(x);", "WS1001", 1, 10)]
    [InlineData("writeLn(#{1,});", "WS1001", 1, 13)]
    [InlineData("var m = {}; m?[\"a\"] = 2;", "WS1007", 1, 13)]
    [InlineData("var x = true ? 1 : 2;", "WS1003", 1, 14)]
    [InlineData("writeLn(#[1]);", "WS1003", 1, 9)]
    [InlineData("foreach (var x of [1]) writeLn(x);", "WS1001", 1, 16)]
    [InlineData("foreach (var (k, k) in {}) writeLn(k);", "WS2002", 1, 18)]
    [InlineData("foreach (var x in [1]) {} writeLn(x);", "WS2001", 1, 35)]
    [InlineData("function g() { await 1; }", "WS2014", 1, 16)]
    [InlineData("var f = function () { await 1; };", "WS2014", 1, 23)]
    [InlineData("async function f() { var g = function () { await 1; }; }", "WS2014", 1, 44)]
    [InlineData("class A { async constructor() {} }", "WS1001", 1, 17)]
    [InlineData("var async = 1;", "WS1001", 1, 5)]
    public void CompileErrorsAreReportedAndNothingRuns(string source, string code, int line, int column)
    {
        foreach (var command in new[] { "check", "run" })
        {
            var (exit, output, error) = Weblingo(command, source);
            Assert.Equal(WsCommandLine.CompilationFailed, exit);
            Assert.Equal("", output);
            Assert.Equal($"program.ws({line},{column}): error {code}:", FirstLineStart(error));
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
    [InlineData("try { var a = [1]; a[5]; } catch (e) { writeLn(e); writeLn(e.code); }", "IndexError {message: \"The index 5 is outside 0 to 0.\", code: \"WS3005\"}\nWS3005")]
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
    [InlineData("try { var x = 9223372036854775807 + 1; } catch (e) { writeLn(e.code); }", "WS3002")]
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
    [InlineData("try { var m = {}; m[\"x\"]; } catch (KeyError e) { writeLn(\"key \" + e.code); }", "key WS3007")]
    [InlineData("try { [][0]; } catch (KeyError e) { writeLn(\"k\"); } catch (IndexError e) { writeLn(\"i \" + (e is Error).toString()); } catch (e) { writeLn(\"any\"); }", "i true")]
    [InlineData("try { try { throw \"s\"; } catch (Integer e) { writeLn(\"int\"); } finally { writeLn(\"f\"); } } catch (String e) { writeLn(\"string \" + e); }", "f\nstring s")]
    [InlineData("class P {} writeLn([5 is Integer, 5 is Number, 1.5 is Number, \"a\" is String, [1] is Array, {} is Map, #{} is Set]); writeLn([null is Object, 5 is Object, writeLn is Function, new P() is P, new P() is Object, P is Class, true is Boolean, P is P]);", "[true, false, true, true, true, true, true]\n[false, true, true, true, true, true, true, false]")]
    [InlineData("class NotFound extends Error { constructor(m) { super(m); } } class errors { static var NotFound = NotFound; } try { throw new NotFound(\"x\"); } catch (errors.NotFound e) { writeLn(\"nf \" + e.message); }", "nf x")]
    [InlineData("var A = 5; try { throw 5; } catch (A e) { writeLn(\"five\"); } writeLn(5 is A); writeLn(6 is A);", "five\ntrue\nfalse")]
    [InlineData("writeLn(\"a\".equal(\"a\")); writeLn(5.equal(6)); var z = null; writeLn(z.equal(null));", "true\nfalse\ntrue")]
    [InlineData("try { 1 + \"a\"; } catch (TypeError e) { writeLn(e is TypeError); writeLn(e.code); } try { throw new KeyError(\"mine\"); } catch (KeyError e) { writeLn(e); }", "true\nWS3001\nKeyError {message: \"mine\", code: null}")]
    [InlineData("try { throw null; } catch (Object e) { writeLn(\"object\"); } catch (e) { writeLn(\"any \" + e.toString()); }", "any null")]
    [InlineData("class Stack extends Array { top() { return this[this.length - 1]; } } var s = new Stack(); s.push(1); s.push(2); writeLn(s.top()); writeLn(s.length); writeLn(s); writeLn([s is Array, s is Stack, [] is Stack]); s.name = \"st\"; writeLn(s.name); foreach (var x in s) write(x); writeLn(\"\");", "2\n2\nStack([1, 2])\n[true, true, false]\nst\n12")]
    [InlineData("class Bag extends Set {} var b = new Bag(); b.add(1); b.add(1); writeLn(b); class Dict extends Map { get(k) { return this?[k]; } } var d = new Dict(); d[\"a\"] = 1; writeLn(d.get(\"a\")); writeLn(d.get(\"b\")); writeLn([d]);", "Bag(#{1})\n1\nnull\n[Dict({\"a\": 1})]")]
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
    [InlineData("async function ix() { return [1][5]; } try { await ix(); } catch (IndexError e) { writeLn(e.code); }", "WS3005")]
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
    [InlineData("class R { async dispose() { writeLn(\"d\"); } } var r = new R(); function f() { using (var x = r) {} } try { f(); } catch (e) { writeLn(e.code); } await r.dispose();", "WS3001\nd")]
    [InlineData("class R { async dispose() { writeLn(\"d\"); } } function f(r) { return r.dispose(); } var t = f(new R()); writeLn(t is Task); await t;", "d\ntrue")]
    [InlineData("class R { constructor(n) { this.n = n; } async dispose() { await null; writeLn(\"d\" + this.n.toString()); } } async function f() { foreach (var i in [1, 2]) { using (var r = new R(i)) { if (i == 1) continue; return i; } } } writeLn(await f());", "d1\nd2\n2")]
    [InlineData("class R { constructor(n) { this.n = n; } async dispose() { await delay(this.n); writeLn(this.n); } } async function use(n) { using (var r = new R(n)) {} } var a = use(20); var b = use(10); await a; await b;", "10\n20")]
    [InlineData("class R { dispose() { writeLn(\"d\"); } } async function f() { using (var r = new R()) {} writeLn(\"f\"); } var t = f(); writeLn(\"main\"); await t;", "d\nf\nmain")]
    [InlineData("class R { async dispose() { await null; writeLn(\"d\"); } } async function f() { using (var r = new R()) { return 1; } } var t = f(); writeLn(\"main\"); writeLn(await t);", "main\nd\n1")]
    [InlineData("class S extends Array {} var s = new S(); s.push(1); s.push(s); writeLn(s); writeLn([s]); class M extends Map {} var m = new M(); m[\"k\"] = s; writeLn(m);", "S([1, S([…])])\n[S([1, S([…])])]\nM({\"k\": S([1, S([…])])})")]
    public void ResultsAreTheSpecifiedOnes(string source, string printed)
    {
        Assert.Equal((WsCommandLine.Success, printed.Length == 0 ? "" : printed + "\n", ""), Weblingo("run", source));
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
        Assert.Equal(WsCommandLine.RuntimeFailed, exit);
        Assert.Contains("error WS3001:", error);
    }

    [Theory]
    [InlineData("var x = 1; x(2);", "WS2005")]
    [InlineData("writeLn(1)(2);", "WS2005")]
    [InlineData("var write = 1; write(2);", "WS2005")]
    [InlineData("var f = function (a) {}; f();", "WS2006")]
    [InlineData("var w = writeLn; w(1, 2);", "WS2006")]
    [InlineData("function f() { return f(); } f();", "WS3003")]
    [InlineData("f(); var y = 1; function f() { writeLn(y); }", "WS2004")]
    public void CallsAreCheckedAtRunTime(string source, string code)
    {
        var (exit, _, error) = Weblingo("run", source);
        Assert.Equal(WsCommandLine.RuntimeFailed, exit);
        Assert.Contains($"error {code}:", error);
    }

    [Theory]
    [InlineData("writeLn(\"abc\"[3]);", "WS3005")]
    [InlineData("writeLn(\"abc\"[-1]);", "WS3005")]
    [InlineData("writeLn(\"\"[0]);", "WS3005")]
    [InlineData("writeLn(\"abc\"[1.0]);", "WS3001")]
    [InlineData("var s = \"abc\"; s[0] = \"x\";", "WS3001")]
    [InlineData("writeLn(5[0]);", "WS3001")]
    [InlineData("writeLn(\"abc\".size);", "WS3006")]
    [InlineData("writeLn(5.length);", "WS3006")]
    [InlineData("writeLn(null.length);", "WS3006")]
    [InlineData("writeLn(\"abc\".length());", "WS2005")]
    [InlineData("writeLn(\"abc\".size(1));", "WS3006")]
    [InlineData("var x = 1; x.length = 2;", "WS3001")]
    [InlineData("var x = new 5();", "WS2005")]
    [InlineData("class P {} var c = P; c();", "WS2005")]
    [InlineData("class P { constructor(a, b) {} } var p = new P(1);", "WS2006")]
    [InlineData("class E {} var e = new E(1);", "WS2006")]
    [InlineData("class P { m(a) {} } new P().m();", "WS2006")]
    [InlineData("class P {} writeLn(new P().missing);", "WS3006")]
    [InlineData("class P {} new P().missing();", "WS3006")]
    [InlineData("class P { m() {} } var p = new P(); p.m = 1;", "WS3001")]
    [InlineData("class P {} var m = {}; m[new P()] = 1;", "WS3001")]
    [InlineData("class P {} foreach (var x in new P()) writeLn(x);", "WS3001")]
    [InlineData("class P {} writeLn(new P()[0]);", "WS3001")]
    [InlineData("class P {} writeLn(P.x);", "WS3006")]
    [InlineData("class A { constructor(x) {} } class B extends A {} new B();", "WS2006")]
    [InlineData("class A {} class B extends A { m() { return super.nope(); } } new B().m();", "WS3006")]
    [InlineData("var NotClass = 5; { class A extends NotClass {} }", "WS3001")]
    [InlineData("using (var x = 5) {}", "WS3006")]
    [InlineData("class A {} using (var a = new A()) {}", "WS3006")]
    [InlineData("class A { dispose() {} } new A().dispose(1);", "WS2006")]
    [InlineData("class A { dispose(x) {} } using (var a = new A()) {}", "WS2006")]
    [InlineData("class A { static m() {} } A.m = 1;", "WS3001")]
    [InlineData("class A { static var n = 1; } writeLn(new A().n);", "WS3006")]
    [InlineData("class A { static m(x) {} } A.m();", "WS2006")]
    [InlineData("class A { m() { return super.m(); } } new A().m();", "WS3006")]
    [InlineData("new Object().equal();", "WS2006")]
    [InlineData("class A {} new A().equal = 1;", "WS3001")]
    [InlineData("throw \"x\";", "WS3008")]
    [InlineData("throw new Error(\"m\");", "WS3008")]
    [InlineData("try { var a = []; a[1]; } finally { writeLn(1); }", "WS3005")]
    [InlineData("try { var a = []; a[1]; } catch (e) { throw; }", "WS3005")]
    [InlineData("class Bad { toString() { return 5; } } writeLn(new Bad());", "WS3001")]
    [InlineData("class Bad { toString(x) { return \"\"; } } writeLn([new Bad()]);", "WS2006")]
    [InlineData("class X extends Integer {}", "WS3001")]
    [InlineData("var s = new String();", "WS3001")]
    [InlineData("class Bad { equal(o) { return 1; } } var B = new Bad(); try { throw 1; } catch (B e) {}", "WS3001")]
    [InlineData("try { throw 1; } catch (String e) {}", "WS3008")]
    [InlineData("var f = function (a, b = 1) {}; f();", "WS2006")]
    [InlineData("var f = function (a = 1) {}; f(1, 2);", "WS2006")]
    [InlineData("var a = [1]; a.length = 2;", "WS3001")]
    [InlineData("writeLn([].pop());", "WS3005")]
    [InlineData("writeLn([1, 2][2]);", "WS3005")]
    [InlineData("var a = [1]; a[-1] = 0;", "WS3005")]
    [InlineData("writeLn([1][\"0\"]);", "WS3001")]
    [InlineData("writeLn([1].push);", "WS3001")]
    [InlineData("var a = [1]; a.push(1, 2);", "WS2006")]
    [InlineData("var a = [1]; a.pop(1);", "WS2006")]
    [InlineData("writeLn([1].size);", "WS3006")]
    [InlineData("writeLn([1].length());", "WS2005")]
    [InlineData("writeLn([1] + [2]);", "WS3001")]
    [InlineData("writeLn([1] < [2]);", "WS3001")]
    [InlineData("var m = {a: 1}; writeLn(m[\"b\"]);", "WS3007")]
    [InlineData("var m = {a: 1}; writeLn(m[1]);", "WS3007")]
    [InlineData("var m = {}; m[1.5] = 2;", "WS3001")]
    [InlineData("var m = {}; m[[1]] = 2;", "WS3001")]
    [InlineData("var m = {}; m[m] = 2;", "WS3001")]
    [InlineData("var m = {}; writeLn(m.has(NaN));", "WS3001")]
    [InlineData("var m = {a: 1}; writeLn(m.a);", "WS3006")]
    [InlineData("var m = {a: 1}; writeLn(m.has);", "WS3001")]
    [InlineData("var m = {a: 1}; writeLn(m.has());", "WS2006")]
    [InlineData("var m = {a: 1}; writeLn(m.length());", "WS2005")]
    [InlineData("var m = {a: 1}; m.push(1);", "WS3006")]
    [InlineData("foreach (var x in {a: 1}) writeLn(x);", "WS3001")]
    [InlineData("foreach (var (k, v) in [1]) writeLn(k);", "WS3001")]
    [InlineData("foreach (var (k, v) in \"ab\") writeLn(k);", "WS3001")]
    [InlineData("foreach (var x in 5) writeLn(x);", "WS3001")]
    [InlineData("foreach (var x in null) writeLn(x);", "WS3001")]
    [InlineData("writeLn(#{1.5});", "WS3001")]
    [InlineData("var m = {}; writeLn(m?[1.5]);", "WS3001")]
    [InlineData("writeLn([1]?[\"x\"]);", "WS3001")]
    [InlineData("writeLn(5?[0]);", "WS3001")]
    [InlineData("writeLn(true?[0]);", "WS3001")]
    [InlineData("writeLn(#{1}?[0]);", "WS3001")]
    [InlineData("var m = {}; writeLn(m?[\"a\"].length);", "WS3006")]
    [InlineData("var s = #{}; s.add([1]);", "WS3001")]
    [InlineData("var s = #{}; writeLn(s.has(#{}));", "WS3001")]
    [InlineData("writeLn(#{1}[0]);", "WS3001")]
    [InlineData("foreach (var (k, v) in #{1}) writeLn(k);", "WS3001")]
    [InlineData("var s = #{1}; s.add();", "WS2006")]
    [InlineData("var s = #{1}; writeLn(s.add);", "WS3001")]
    [InlineData("var s = #{1}; s.push(1);", "WS3006")]
    [InlineData("writeLn(#{1}.length());", "WS2005")]
    public void MembersAndIndexesAreCheckedAtRunTime(string source, string code)
    {
        var (exit, _, error) = Weblingo("run", source);
        Assert.Equal(WsCommandLine.RuntimeFailed, exit);
        Assert.Contains($"error {code}:", error);
    }

    [Theory]
    [InlineData("async function f() { throw \"lost\"; } f(); writeLn(\"end\");", "WS3008")]
    [InlineData("async function f() { await null; throw new Error(\"x\"); } await f();", "WS3008")]
    [InlineData("async function f() { return [1][3]; } await f();", "WS3005")]
    [InlineData("async function f() { await delay(1); return [1][3]; } f(); await delay(5);", "WS3005")]
    [InlineData("var other = null; async function a() { await null; await other; } var ta = a(); async function b() { await ta; } other = b(); await ta;", "WS3009")]
    [InlineData("async function forever() { await forever(); } await forever();", "WS3003")]
    [InlineData("delay(-1);", "WS3001")]
    [InlineData("delay(1.5);", "WS3001")]
    [InlineData("delay(0).forget(1);", "WS2006")]
    [InlineData("var n = 5; n.forget();", "WS3006")]
    [InlineData("class T extends Task {}", "WS3001")]
    [InlineData("var t = new Task();", "WS3001")]
    [InlineData("var m = {}; m[delay(0)] = 1;", "WS3001")]
    [InlineData("class R { async dispose() {} } function f() { using (var r = new R()) {} } f();", "WS3001")]
    [InlineData("class R { async dispose() {} } var f = function () { using (var r = new R()) {} }; f();", "WS3001")]
    [InlineData("class R { async dispose() { throw \"x\"; } } new R().dispose();", "WS3008")]
    [InlineData("class P { async toString() { return \"p\"; } } writeLn(new P());", "WS3001")]
    [InlineData("class P { async toString() { await null; return \"p\"; } } writeLn([new P()]);", "WS3001")]
    public void AsynchronyErrorsAreReported(string source, string code)
    {
        var (exit, _, error) = Weblingo("run", source);
        Assert.Equal(WsCommandLine.RuntimeFailed, exit);
        Assert.Contains($"error {code}:", error);
    }

    [Fact]
    public void ADelayWaitsOnTheClock()
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        Assert.Equal((WsCommandLine.Success, "1\n2\n", ""), Weblingo("run", "writeLn(1); await delay(60); writeLn(2);"));
        Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 60);
    }

    [Fact]
    public void AFaultNothingAwaitedIsReportedWhereItWasThrown()
    {
        var (exit, output, error) = Weblingo("run", "async function f() {\n    throw \"lost\";\n}\nf();\nwriteLn(\"end\");");
        Assert.Equal(WsCommandLine.RuntimeFailed, exit);
        Assert.Equal("end\n", output);
        Assert.Equal("program.ws(2,5): error WS3008:", FirstLineStart(error));
    }

    [Fact]
    public void SignedInt64OverflowIsARuntimeError()
    {
        const string source = "var x = 9223372036854775807 + 1;";
        Assert.Equal(WsCommandLine.Success, Weblingo("check", source).Exit);
        var (exit, _, error) = Weblingo("run", source);
        Assert.Equal(WsCommandLine.RuntimeFailed, exit);
        Assert.Equal("program.ws(1,9): error WS3002:", FirstLineStart(error));
    }

    [Fact]
    public void TheSpecificationsExamplePrints30()
    {
        Assert.Equal((WsCommandLine.Success, "30\n", ""),
            Weblingo("run", "var a = 10;\nvar b = 20;\nvar result = a + b;\nwriteLn(result);\n"));
    }

    [Theory]
    [InlineData("run")]
    [InlineData("--unknown")]
    [InlineData("run", "a.ws", "b.ws")]
    public void AUsageErrorExitsWith2(params string[] args)
    {
        var output = new StringWriter();
        Assert.Equal(WsCommandLine.UsageError, WsCommandLine.Run(args, output, new StringWriter()));
        Assert.StartsWith("Usage: weblingo", output.ToString());
    }

    [Fact]
    public void AFileThatCannotBeReadExitsWith4()
    {
        var error = new StringWriter();
        var missing = Path.Combine(_folder, "missing.ws");
        Assert.Equal(WsCommandLine.FileUnreadable, WsCommandLine.Run(["run", missing], new StringWriter(), error));
        Assert.Contains("cannot read the file", error.ToString());
    }

    [Fact]
    public void TheHelloWorldSampleGreets()
    {
        var sample = Path.Combine(SolutionFolder(), "samples", "HelloWorld", "hello.ws");
        var output = new StringWriter();
        Assert.Equal(WsCommandLine.Success, WsCommandLine.Run(["run", sample], output, new StringWriter()));
        Assert.Equal("Hello from WebLingo!\n", output.ToString());
    }

    [Fact]
    public void TheArithmeticSamplePrints30()
    {
        var sample = Path.Combine(SolutionFolder(), "samples", "Arithmetic", "arithmetic.ws");
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(WsCommandLine.Success, WsCommandLine.Run(["run", sample], output, error));
        Assert.Equal(("30\n", ""), (output.ToString(), error.ToString()));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void EveryLineEndingGivesTheSameResultAndPositions(string newline)
    {
        var good = string.Join(newline, "var a = 2;", "var b = a * 3;", "writeLn(b);");
        Assert.Equal((WsCommandLine.Success, "6\n", ""), Weblingo("run", good));
        var bad = string.Join(newline, "var x = 1;", "writeLn(y);");
        Assert.Equal("program.ws(2,9): error WS2001:", FirstLineStart(Weblingo("run", bad).Error));
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
        var compiler = new WsCompiler(new WsCompilerOptions([new WsBuiltinDeclaration("writeLn", 1)]));
        var vm = new WsVirtualMachine();
        var compiled = 0;
        for (var round = 0; round < 5_000; round++)
        {
            var source = string.Concat(Enumerable.Range(0, random.Next(1, 30)).Select(_ => fragments[random.Next(fragments.Length)] + " "));
            var result = compiler.Compile(source, "fuzz.ws");
            if (result.Module is null)
            {
                Assert.NotEmpty(result.Diagnostics);
                Assert.All(result.Diagnostics, d => Assert.InRange(d.Span.Start, 0, source.Length));
                continue;
            }
            compiled++;
            var context = new WsExecutionContext(new WsWorkerId(0))
            {
                Services = new WsBuiltinRegistry(WsStandardBuiltins.Create(new WsCollectedOutput())),
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
        var module = new WsCompiler(new WsCompilerOptions([new WsBuiltinDeclaration("writeLn", 1)]))
            .Compile("var a = 10; var b = a * 2 - 3; var c = 0; c = -b / a; writeLn(c);", "base.ws").Module!;
        var random = new Random(8_6);
        var opCodes = Enum.GetValues<WsOpCode>();
        var vm = new WsVirtualMachine();
        var rejected = 0;
        for (var round = 0; round < 5_000; round++)
        {
            var code = module.EntryPoint.Code.ToArray();
            for (var mutation = random.Next(1, 4); mutation > 0; mutation--)
            {
                var i = random.Next(code.Length);
                code[i] = random.Next(4) switch
                {
                    0 => code[i] with { OpCode = random.Next(8) == 0 ? (WsOpCode)random.Next(256) : opCodes[random.Next(opCodes.Length)] },
                    1 => code[i] with { A = random.Next(-2, 8) },
                    2 => code[i] with { B = random.Next(-2, 8) },
                    _ => code[i] with { C = random.Next(-2, 8), D = random.Next(-1, 3) },
                };
            }
            var mutated = module with { EntryPoint = module.EntryPoint with { Code = code } };
            var context = new WsExecutionContext(new WsWorkerId(0))
            {
                Services = new WsBuiltinRegistry(WsStandardBuiltins.Create(new WsCollectedOutput())),
                InstructionBudget = 10_000,
            };
            var result = vm.Execute(mutated, context);
            if (result.Error?.Code == WsDiagnosticCodes.BytecodeVerificationFailure) rejected++;
        }
        Assert.InRange(rejected, 1, 5_000);
    }

    private (int Exit, string Output, string Error) Weblingo(string command, string source)
    {
        var path = Path.Combine(_folder, "program.ws");
        File.WriteAllText(path, source);
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WsCommandLine.Run([command, path], output, error);
        // Diagnostics name the file as it was given; show only its name, so the expectations hold on every machine.
        return (exit, output.ToString(), error.ToString().Replace(path, "program.ws", StringComparison.Ordinal));
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
