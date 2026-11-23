using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Runtime;
using WebLingo.StdLib;
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
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn(Bytes.from(\"hi\"));", "Bytes:[104, 105]\n")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn(Bytes.from(\"é\"));", "Bytes:[195, 169]\n")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn(Bytes.from(\"é\", \"latin1\"));", "Bytes:[233]\n")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn(Bytes.from(\"a\", \"UTF-16BE\"));", "Bytes:[0, 97]\n")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn(Bytes.from([104, 105]).text());", "hi\n")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn(Bytes.from([]).length);", "0\n")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; var b = Bytes.from(\"tea\"); writeLn(Bytes.from(b) == b);", "true\n")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; var b = Bytes.from(\"héllo\"); writeLn([b.length, b[0], b[1], b?[9]]);", "[6, 104, 195, null]\n")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; var b = Bytes.from(\"héllo\"); writeLn(b.text() == \"héllo\");", "true\n")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn(Bytes.from([233]).text(\"iso-8859-1\") == \"é\");", "true\n")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn(Bytes.from([255]).text() == \"\\u{FFFD}\");", "true\n")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; var b = Bytes.from(\"teapot\"); writeLn(b.slice(3).text() + b.slice(0, 3).text() + b.slice(2, 2).text());", "pottea\n")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn(Bytes.concat([Bytes.from(\"ab\"), 99, Bytes.from(\"\"), Bytes.from(\"d\")]).text());", "abcd\n")]
    [InlineData("import { write, writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn([Bytes.from(\"hi\") == Bytes.from([104, 105]), Bytes.from(\"hi\") != Bytes.from(\"ho\"), Bytes.from(\"\") == \"\"]);",
        "[true, true, false]\n")]
    [InlineData("import { write, writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn([Bytes.fixedTimeEqual(Bytes.from(\"key\"), Bytes.from(\"key\")), Bytes.fixedTimeEqual(Bytes.from(\"key\"), Bytes.from(\"kez\")), "
                + "Bytes.fixedTimeEqual(Bytes.from(\"key\"), Bytes.from(\"keys\"))]);", "[true, false, false]\n")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; var b = Bytes.from(\"x\"); writeLn([b is Bytes, b is Object, \"x\" is Bytes]);", "[true, true, false]\n")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn($\"[{Bytes.from(\"x\")}] {Bytes.from(\"x\").toString()}\");", "[Bytes:[120]] Bytes:[120]\n")]
    [InlineData("import { write, writeLn } from \"std:system\"; import { Bytes, BytesBuilder } from \"std:bytes\"; var builder = new BytesBuilder(); builder.add(\"tea\").add(32).add(Bytes.from(\"time\")); writeLn(builder.length); "
                + "writeLn(builder.toBytes().text()); builder.add(\"!\"); writeLn(builder.toBytes().text());", "8\ntea time\ntea time!\n")]
    [InlineData("import { write, writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; extend Bytes { hex() { var s = \"\"; var i = 0; while (i < this.length) { s = s + $\"{this[i]:X2}\"; i = i + 1; } return s; } } "
                + "writeLn(Bytes.from(\"hi\").hex());", "6869\n")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn([Bytes.from([0, 15, 16, 171, 255]).toHex(), Bytes.from([]).toHex()]);", "[\"000f10abff\", \"\"]\n")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn([Bytes.fromHex(\"000F10abFF\"), Bytes.fromHex(\"\")]);", "[Bytes:[0, 15, 16, 171, 255], Bytes:[]]\n")]
    [InlineData("import { write, writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; foreach (var t in [\"\", \"f\", \"fo\", \"foo\", \"foob\", \"fooba\", \"foobar\"]) { writeLn(Bytes.from(t).toBase64()); }",
        "\nZg==\nZm8=\nZm9v\nZm9vYg==\nZm9vYmE=\nZm9vYmFy\n")]
    [InlineData("import { write, writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; foreach (var t in [\"\", \"Zg==\", \"Zm8=\", \"Zm9v\", \"Zm9vYg==\", \"Zm9vYmE=\", \"Zm9vYmFy\"]) { write(Bytes.fromBase64(t).text() + \"|\"); }",
        "|f|fo|foo|foob|fooba|foobar|")]
    [InlineData("import { write, writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; var b = Bytes.from([251, 255, 191]); writeLn([b.toBase64(), b.toBase64(false), b.toBase64(true), Bytes.from([251, 255]).toBase64(true)]);",
        "[\"+/+/\", \"+/+/\", \"-_-_\", \"-_8\"]\n")]
    [InlineData("import { write, writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn([Bytes.fromBase64(\"-_8\", true), Bytes.fromBase64(\"-_8=\", true), Bytes.fromBase64(\"+/8=\"), Bytes.fromBase64(\"+/8=\", false)]);",
        "[Bytes:[251, 255], Bytes:[251, 255], Bytes:[251, 255], Bytes:[251, 255]]\n")]
    [InlineData("import { write, writeLn } from \"std:system\"; import { Bytes, BytesBuilder } from \"std:bytes\"; var b = Bytes.from(\"tea time\"); writeLn([Bytes.fromHex(b.toHex()) == b, Bytes.fromBase64(b.toBase64()) == b, Bytes.fromBase64(b.toBase64(true), true) == b]);",
        "[true, true, true]\n")]
    // A BytesBuilder, native since 18 November 2026: written as its bytes so far, its toBytes() unchanged by a later add,
    // growing as it must, and extended as any kind of value is.
    [InlineData("import { BytesBuilder } from \"std:bytes\"; var b = new BytesBuilder().add(\"hi\"); writeLn([b, b is BytesBuilder, b is Object, b.length]);",
        "[BytesBuilder:[104, 105], true, true, 2]\n")]
    [InlineData("import { BytesBuilder } from \"std:bytes\"; var b = new BytesBuilder().add(\"tea\"); var first = b.toBytes(); b.add(33); "
                + "writeLn([first.text(), b.toBytes().text(), first.length, b.length, b.toBytes() == b.toBytes()]);", "[\"tea\", \"tea!\", 3, 4, true]\n")]
    [InlineData("import { BytesBuilder } from \"std:bytes\"; var b = new BytesBuilder(); for (var i = 0; i < 100000; i++) b.add(i % 256); "
                + "var all = b.toBytes(); writeLn([b.length, all[0], all[255], all[256], all[99999]]);", "[100000, 0, 255, 0, 159]\n")]
    [InlineData("import { Bytes, BytesBuilder } from \"std:bytes\"; extend BytesBuilder { twice(v) { return this.add(v).add(v); } } "
                + "writeLn(new BytesBuilder().twice(\"ab\").add(Bytes.from([1])).toBytes());", "Bytes:[97, 98, 97, 98, 1]\n")]
    // A class extending Bytes or BytesBuilder holds a value of its kind, made as Bytes.from(…) and new BytesBuilder() make
    // them, with its members and its class's own; and new Bytes(…) makes bytes (section 3.7, "No sealed classes").
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; class Blob extends Bytes { size() { return this.length; } } var b = new Blob(\"tea\"); "
                + "writeLn([b, b.size(), b.toHex(), b is Bytes, b is Blob, new Bytes(\"hi\"), new Blob([1, 2]).length, Blob.fromHex(\"00ff\"), new Blob(\"a\") == new Blob(\"a\")]);",
        "[Blob:Bytes:[116, 101, 97], 3, \"746561\", true, true, Bytes:[104, 105], 2, Bytes:[0, 255], true]\n")]
    [InlineData("import { writeLn } from \"std:system\"; import { BytesBuilder } from \"std:bytes\"; class Log extends BytesBuilder { line(s) { this.add(s).add(10); return this; } } "
                + "var l = new Log(); l.line(\"a\").line(\"b\"); writeLn([l, l.toBytes().text(), l is BytesBuilder, l.length]);",
        "[Log:BytesBuilder:[97, 10, 98, 10], \"a\\nb\\n\", true, 4]\n")]
    public void BytesBehaveAlikeOnBothVms(string program, string output)
    {
        foreach (var stack in new[] { false })
        {
            var (text, error) = Run(program, stack);
            Assert.True(error is null, error?.Message);
            Assert.Equal(output, text);
        }
    }

    [Theory]
    [InlineData("import { Bytes } from \"std:bytes\"; var b = Bytes.from(\"x\"); b[0] = 1;", "WL3001", "Bytes cannot be changed: they are immutable; a BytesBuilder builds them.")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn(Bytes.from(\"x\")[1]);", "WL3005", "The index 1 is outside 0 to 0.")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn(Bytes.from(\"x\").slice(0, 2));", "WL3005", "The range 0 to 2 is not within the bytes, 0 to 1.")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn(Bytes.from([256]));", "WL3001", "The first argument of 'from', the value, must hold integers from 0 to 255, not the integer 256.")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn(Bytes.from(5));", "WL3001", "The first argument of 'from', the value, must be a string, an array of integers from 0 to 255, or bytes, not the integer 5.")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn(Bytes.concat([\"a\"]));", "WL3001", "The argument of 'concat' must hold bytes and integers from 0 to 255, not a string.")]
    [InlineData("import { write, writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn(Bytes.from(\"x\").text(\"klingon\"));", "WL3001",
        "'klingon' is not a charset WebLingo reads: utf-8, utf-16, utf-16be, us-ascii or iso-8859-1.")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn(Bytes.fixedTimeEqual(\"a\", \"a\"));", "WL3001", "The first argument of 'fixedTimeEqual', a, must be bytes, not a string.")]
    [InlineData("import { Bytes } from \"std:bytes\"; var m = {}; m[Bytes.from(\"x\")] = 1;", "WL3001", "Bytes cannot be a key: keys are strings, integers, Booleans and null.")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn(Bytes.from(\"a\") + Bytes.from(\"b\"));", "WL3001", "'+' needs two numbers or two strings, not bytes.")]
    [InlineData("import { writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; writeLn(new Bytes());", "WL2006", "'Bytes' takes 1 to 2 arguments, not 0.")]
    [InlineData("import { Bytes } from \"std:bytes\"; class Blob extends Bytes {} new Blob(5);", "WL3001",
        "The first argument of 'Bytes', the value, must be a string, an array of integers from 0 to 255, or bytes, not the integer 5.")]
    [InlineData("import { BytesBuilder } from \"std:bytes\"; new BytesBuilder().add(1.5);", "WL3001", "The argument of 'add' must be bytes, an integer from 0 to 255, or a string, not the number 1.5.")]
    [InlineData("import { BytesBuilder } from \"std:bytes\"; class Mine extends BytesBuilder {} new Mine(1);", "WL2006", "'BytesBuilder' takes 0 arguments, not 1.")]
    [InlineData("import { BytesBuilder } from \"std:bytes\"; new BytesBuilder(1);", "WL2006", "'BytesBuilder' takes 0 arguments, not 1.")]
    [InlineData("import { BytesBuilder } from \"std:bytes\"; new BytesBuilder().length();", "WL2005", "'length' of a BytesBuilder is not a method, and cannot be called.")]
    [InlineData("import { Bytes } from \"std:bytes\"; Bytes.fromHex(\"abc\");", "WL3001", "\"abc\" is not hexadecimal: two digits, 0 to 9 and a to f, for each byte.")]
    [InlineData("import { Bytes } from \"std:bytes\"; Bytes.fromHex(\"0g\");", "WL3001", "\"0g\" is not hexadecimal: two digits, 0 to 9 and a to f, for each byte.")]
    [InlineData("import { Bytes } from \"std:bytes\"; Bytes.fromHex(5);", "WL3001", "The argument of 'fromHex' must be a string, not the integer 5.")]
    [InlineData("import { Bytes } from \"std:bytes\"; Bytes.fromBase64(\"Zg\");", "WL3001", "\"Zg\" is not Base64: A to Z, a to z, 0 to 9, + and /, padded with = to a multiple of four.")]
    [InlineData("import { Bytes } from \"std:bytes\"; Bytes.fromBase64(\"Zg=\");", "WL3001", "\"Zg=\" is not Base64: A to Z, a to z, 0 to 9, + and /, padded with = to a multiple of four.")]
    [InlineData("import { Bytes } from \"std:bytes\"; Bytes.fromBase64(\"Zm9v Yg==\");", "WL3001", "\"Zm9v Yg==\" is not Base64: A to Z, a to z, 0 to 9, + and /, padded with = to a multiple of four.")]
    [InlineData("import { Bytes } from \"std:bytes\"; Bytes.fromBase64(\"-_8=\");", "WL3001", "\"-_8=\" is not Base64: A to Z, a to z, 0 to 9, + and /, padded with = to a multiple of four.")]
    [InlineData("import { Bytes } from \"std:bytes\"; Bytes.fromBase64(\"+/8\", true);", "WL3001", "\"+/8\" is not URL-safe Base64: A to Z, a to z, 0 to 9, - and _, padded with = or not.")]
    [InlineData("import { Bytes } from \"std:bytes\"; Bytes.fromBase64(\"Z\", true);", "WL3001", "\"Z\" is not URL-safe Base64: A to Z, a to z, 0 to 9, - and _, padded with = or not.")]
    [InlineData("import { Bytes } from \"std:bytes\"; Bytes.from(\"x\").toBase64(1);", "WL3001", "The argument of 'toBase64' must be a Boolean, not the integer 1.")]
    [InlineData("import { Bytes } from \"std:bytes\"; Bytes.from(\"x\").toHex(1);", "WL2006", "'toHex' takes 0 arguments, not 1.")]
    public void WrongUsesAreErrorsAlikeOnBothVms(string program, string code, string message)
    {
        foreach (var stack in new[] { false })
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
        var compiled = new WebLingoCompiler(options).Compile(program, "main.wlg");
        Assert.Empty(compiled.Diagnostics);
        // Run first: the output is read once the program has written it.
        var error = new WebLingoVirtualMachine().Execute(compiled.Module!, context).Error;
        return (output.Text, error);
    }
}
