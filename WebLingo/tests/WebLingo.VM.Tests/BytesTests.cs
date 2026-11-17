using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM.Stack;
using Xunit;
namespace WebLingo.VM.Tests;

/// <summary>
/// Bytes (specification, section 3.2, "Bytes"), on both VMs: a kind of value of its own, immutable, made by
/// <c>Bytes.from</c> and <c>Bytes.concat</c>, read by <c>length</c>, indexing, <c>slice</c> and <c>text</c>, written as
/// hexadecimal and Base64 and read from them, equal by
/// their contents, compared for secrets by <c>Bytes.fixedTimeEqual</c>, and built from pieces by a <c>BytesBuilder</c>.
/// </summary>
public sealed class BytesTests
{
    [Theory]
    [InlineData("writeLn(Bytes.from(\"hi\"));", "Bytes:[104, 105]\n")]
    [InlineData("writeLn(Bytes.from(\"é\"));", "Bytes:[195, 169]\n")]
    [InlineData("writeLn(Bytes.from(\"é\", \"latin1\"));", "Bytes:[233]\n")]
    [InlineData("writeLn(Bytes.from(\"a\", \"UTF-16BE\"));", "Bytes:[0, 97]\n")]
    [InlineData("writeLn(Bytes.from([104, 105]).text());", "hi\n")]
    [InlineData("writeLn(Bytes.from([]).length);", "0\n")]
    [InlineData("var b = Bytes.from(\"tea\"); writeLn(Bytes.from(b) == b);", "true\n")]
    [InlineData("var b = Bytes.from(\"héllo\"); writeLn([b.length, b[0], b[1], b?[9]]);", "[6, 104, 195, null]\n")]
    [InlineData("var b = Bytes.from(\"héllo\"); writeLn(b.text() == \"héllo\");", "true\n")]
    [InlineData("writeLn(Bytes.from([233]).text(\"iso-8859-1\") == \"é\");", "true\n")]
    [InlineData("writeLn(Bytes.from([255]).text() == \"\\u{FFFD}\");", "true\n")]
    [InlineData("var b = Bytes.from(\"teapot\"); writeLn(b.slice(3).text() + b.slice(0, 3).text() + b.slice(2, 2).text());", "pottea\n")]
    [InlineData("writeLn(Bytes.concat([Bytes.from(\"ab\"), 99, Bytes.from(\"\"), Bytes.from(\"d\")]).text());", "abcd\n")]
    [InlineData("writeLn([Bytes.from(\"hi\") == Bytes.from([104, 105]), Bytes.from(\"hi\") != Bytes.from(\"ho\"), Bytes.from(\"\") == \"\"]);",
        "[true, true, false]\n")]
    [InlineData("writeLn([Bytes.fixedTimeEqual(Bytes.from(\"key\"), Bytes.from(\"key\")), Bytes.fixedTimeEqual(Bytes.from(\"key\"), Bytes.from(\"kez\")), "
                + "Bytes.fixedTimeEqual(Bytes.from(\"key\"), Bytes.from(\"keys\"))]);", "[true, false, false]\n")]
    [InlineData("var b = Bytes.from(\"x\"); writeLn([b is Bytes, b is Object, \"x\" is Bytes]);", "[true, true, false]\n")]
    [InlineData("writeLn($\"[{Bytes.from(\"x\")}] {Bytes.from(\"x\").toString()}\");", "[Bytes:[120]] Bytes:[120]\n")]
    [InlineData("var builder = new BytesBuilder(); builder.add(\"tea\").add(32).add(Bytes.from(\"time\")); writeLn(builder.length); "
                + "writeLn(builder.toBytes().text()); builder.add(\"!\"); writeLn(builder.toBytes().text());", "8\ntea time\ntea time!\n")]
    [InlineData("extend Bytes { hex() { var s = \"\"; var i = 0; while (i < this.length) { s = s + $\"{this[i]:X2}\"; i = i + 1; } return s; } } "
                + "writeLn(Bytes.from(\"hi\").hex());", "6869\n")]
    [InlineData("writeLn([Bytes.from([0, 15, 16, 171, 255]).toHex(), Bytes.from([]).toHex()]);", "[\"000f10abff\", \"\"]\n")]
    [InlineData("writeLn([Bytes.fromHex(\"000F10abFF\"), Bytes.fromHex(\"\")]);", "[Bytes:[0, 15, 16, 171, 255], Bytes:[]]\n")]
    [InlineData("foreach (var t in [\"\", \"f\", \"fo\", \"foo\", \"foob\", \"fooba\", \"foobar\"]) { writeLn(Bytes.from(t).toBase64()); }",
        "\nZg==\nZm8=\nZm9v\nZm9vYg==\nZm9vYmE=\nZm9vYmFy\n")]
    [InlineData("foreach (var t in [\"\", \"Zg==\", \"Zm8=\", \"Zm9v\", \"Zm9vYg==\", \"Zm9vYmE=\", \"Zm9vYmFy\"]) { write(Bytes.fromBase64(t).text() + \"|\"); }",
        "|f|fo|foo|foob|fooba|foobar|")]
    [InlineData("var b = Bytes.from([251, 255, 191]); writeLn([b.toBase64(), b.toBase64(false), b.toBase64(true), Bytes.from([251, 255]).toBase64(true)]);",
        "[\"+/+/\", \"+/+/\", \"-_-_\", \"-_8\"]\n")]
    [InlineData("writeLn([Bytes.fromBase64(\"-_8\", true), Bytes.fromBase64(\"-_8=\", true), Bytes.fromBase64(\"+/8=\"), Bytes.fromBase64(\"+/8=\", false)]);",
        "[Bytes:[251, 255], Bytes:[251, 255], Bytes:[251, 255], Bytes:[251, 255]]\n")]
    [InlineData("var b = Bytes.from(\"tea time\"); writeLn([Bytes.fromHex(b.toHex()) == b, Bytes.fromBase64(b.toBase64()) == b, Bytes.fromBase64(b.toBase64(true), true) == b]);",
        "[true, true, true]\n")]
    public void BytesBehaveAlikeOnBothVms(string program, string output)
    {
        foreach (var stack in new[] { false, true })
        {
            var (text, error) = Run(program, stack);
            Assert.True(error is null, error?.Message);
            Assert.Equal(output, text);
        }
    }

    [Theory]
    [InlineData("var b = Bytes.from(\"x\"); b[0] = 1;", "WL3001", "Bytes cannot be changed: they are immutable; a BytesBuilder builds them.")]
    [InlineData("writeLn(Bytes.from(\"x\")[1]);", "WL3005", "The index 1 is outside 0 to 0.")]
    [InlineData("writeLn(Bytes.from(\"x\").slice(0, 2));", "WL3005", "The range 0 to 2 is not within the bytes, 0 to 1.")]
    [InlineData("writeLn(Bytes.from([256]));", "WL3001", "'from' takes an array of integers from 0 to 255, not the integer 256.")]
    [InlineData("writeLn(Bytes.from(5));", "WL3001", "'from' makes bytes from a string, an array of integers from 0 to 255, or bytes, not the integer 5.")]
    [InlineData("writeLn(Bytes.concat([\"a\"]));", "WL3001", "'concat' takes bytes and integers from 0 to 255, not a string.")]
    [InlineData("writeLn(Bytes.from(\"x\").text(\"klingon\"));", "WL3001",
        "'klingon' is not a charset WebLingo reads: utf-8, utf-16, utf-16be, us-ascii or iso-8859-1.")]
    [InlineData("writeLn(Bytes.fixedTimeEqual(\"a\", \"a\"));", "WL3001", "'fixedTimeEqual' compares bytes, not a string.")]
    [InlineData("var m = {}; m[Bytes.from(\"x\")] = 1;", "WL3001", "Bytes cannot be a key: keys are strings, integers, Booleans and null.")]
    [InlineData("writeLn(Bytes.from(\"a\") + Bytes.from(\"b\"));", "WL3001", "'+' needs two numbers or two strings, not bytes.")]
    [InlineData("writeLn(new Bytes());", "WL3001", "'new' cannot make a value of the class Bytes.")]
    [InlineData("new BytesBuilder().add(1.5);", "WL3001", "'concat' takes bytes and integers from 0 to 255, not the number 1.5.")]
    [InlineData("Bytes.fromHex(\"abc\");", "WL3001", "\"abc\" is not hexadecimal: two digits, 0 to 9 and a to f, for each byte.")]
    [InlineData("Bytes.fromHex(\"0g\");", "WL3001", "\"0g\" is not hexadecimal: two digits, 0 to 9 and a to f, for each byte.")]
    [InlineData("Bytes.fromHex(5);", "WL3001", "'fromHex' reads a string, not the integer 5.")]
    [InlineData("Bytes.fromBase64(\"Zg\");", "WL3001", "\"Zg\" is not Base64: A to Z, a to z, 0 to 9, + and /, padded with = to a multiple of four.")]
    [InlineData("Bytes.fromBase64(\"Zg=\");", "WL3001", "\"Zg=\" is not Base64: A to Z, a to z, 0 to 9, + and /, padded with = to a multiple of four.")]
    [InlineData("Bytes.fromBase64(\"Zm9v Yg==\");", "WL3001", "\"Zm9v Yg==\" is not Base64: A to Z, a to z, 0 to 9, + and /, padded with = to a multiple of four.")]
    [InlineData("Bytes.fromBase64(\"-_8=\");", "WL3001", "\"-_8=\" is not Base64: A to Z, a to z, 0 to 9, + and /, padded with = to a multiple of four.")]
    [InlineData("Bytes.fromBase64(\"+/8\", true);", "WL3001", "\"+/8\" is not URL-safe Base64: A to Z, a to z, 0 to 9, - and _, padded with = or not.")]
    [InlineData("Bytes.fromBase64(\"Z\", true);", "WL3001", "\"Z\" is not URL-safe Base64: A to Z, a to z, 0 to 9, - and _, padded with = or not.")]
    [InlineData("Bytes.from(\"x\").toBase64(1);", "WL3001", "'toBase64' takes whether the URL-safe alphabet is meant, a Boolean, not the integer 1.")]
    [InlineData("Bytes.from(\"x\").toHex(1);", "WL2006", "'toHex' takes 0 arguments, not 1.")]
    public void WrongUsesAreErrorsAlikeOnBothVms(string program, string code, string message)
    {
        foreach (var stack in new[] { false, true })
        {
            var (_, error) = Run(program, stack);
            Assert.NotNull(error);
            Assert.Equal((code, message), (error.Code, error.Message));
        }
    }

    private static (string Output, WebLingoDiagnostic? Error) Run(string program, bool stack)
    {
        var options = new WebLingoCompilerOptions([new("write", 1), new("writeLn", 1)]);
        var output = new WebLingoCollectedOutput();
        var context = new WebLingoExecutionContext(new WebLingoWorkerId(0)) { Services = new WebLingoBuiltinRegistry(WebLingoStandardBuiltins.Create(output)) };
        if (stack)
        {
            var stackCompiled = new WebLingoStackCompiler(options).Compile(program, "main.wl");
            Assert.Empty(stackCompiled.Diagnostics);
            var stackError = new WebLingoStackVirtualMachine().Execute(stackCompiled.Module!, context).Error;
            return (output.Text, stackError);
        }
        var compiled = new WebLingoCompiler(options).Compile(program, "main.wl");
        Assert.Empty(compiled.Diagnostics);
        // Run first: the output is read once the program has written it.
        var error = new WebLingoVirtualMachine().Execute(compiled.Module!, context).Error;
        return (output.Text, error);
    }
}
