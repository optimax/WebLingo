using System.Text;
using WebLingo.Cli;
using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM;
using WebLingo.VM.Stack;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// <c>std:fs</c> (specification, sections 3.9 and 7.4), on both VMs, in a folder of its own: each operation asynchronous,
/// and synchronous with <c>Sync</c>, text UTF-8 unless an encoding is named, files opened as handles, and every failure an
/// <c>IOError</c> (WL3011) naming the path as the script gave it.
/// </summary>
public sealed class FileSystemTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-fs-" + Guid.NewGuid().ToString("N")).Replace('\\', '/');

    public FileSystemTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A file a test left open, on a platform that keeps it: the temporary folder's to clean.
        }
    }

    public static TheoryData<string> Vms => new() { "register", "stack" };

    [Theory]
    [MemberData(nameof(Vms))]
    public void TextIsWrittenAppendedAndReadBothWays(string vm)
    {
        Assert.Equal(("héllo\nworld\n|12|true|false|done", ""), Run(vm, """
            import { write } from "std:system";
            await fs.writeText(folder + "/a.txt", "héllo\n");
            fs.appendTextSync(folder + "/a.txt", "world\n");
            write(await fs.readText(folder + "/a.txt"));
            write("|" + fs.readTextSync(folder + "/a.txt").length.toString());
            write("|" + (await fs.exists(folder + "/a.txt")).toString());
            fs.removeSync(folder + "/a.txt");
            write("|" + fs.existsSync(folder + "/a.txt").toString());
            write("|done");
            """));
    }

    [Theory]
    [MemberData(nameof(Vms))]
    public void FoldersAreMadeListedAndRemoved(string vm)
    {
        Assert.Equal(("[\"a.txt\", \"b\", \"c.txt\"] [] false", ""), Run(vm, """
            import { write } from "std:system";
            await fs.makeFolder(folder + "/x/b");
            fs.makeFolderSync(folder + "/x/b");
            await fs.writeText(folder + "/x/c.txt", "c");
            fs.writeTextSync(folder + "/x/a.txt", "a");
            write((await fs.list(folder + "/x")).toString() + " ");
            write(fs.listSync(folder + "/x/b").toString() + " ");
            fs.removeFolderSync(folder + "/x/b");
            await fs.remove(folder + "/x/a.txt");
            await fs.remove(folder + "/x/c.txt");
            await fs.removeFolder(folder + "/x");
            write(fs.existsSync(folder + "/x"));
            """));
    }

    [Theory]
    [MemberData(nameof(Vms))]
    public void AnEncodingMayBeNamed(string vm)
    {
        File.WriteAllText(Path.Combine(_folder, "bom.txt"), "marked", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Assert.Equal(("ü 6 6 4", ""), Run(vm, """
            import { write } from "std:system";
            fs.writeTextSync(folder + "/u.txt", "ü", "utf-16");
            write(fs.readTextSync(folder + "/u.txt", "utf-16") + " ");
            write(fs.readTextSync(folder + "/bom.txt").length.toString() + " ");
            write((await fs.readText(folder + "/bom.txt", "utf-8")).length.toString() + " ");
            write(fs.readTextSync(folder + "/u.txt", "iso-8859-1").length);
            """));
    }

    [Theory]
    [MemberData(nameof(Vms))]
    public void UtfEightIsWrittenWithoutAByteOrderMark(string vm)
    {
        Assert.Equal(("", ""), Run(vm, "fs.writeTextSync(folder + \"/plain.txt\", \"é\");"));
        Assert.Equal([0xC3, 0xA9], File.ReadAllBytes(Path.Combine(_folder, "plain.txt")));
    }

    [Theory]
    [MemberData(nameof(Vms))]
    public void AFileIsOpenedReadWrittenAndDisposed(string vm)
    {
        Assert.Equal(("[one][two][three]|WL3001 This File has been disposed.", ""), Run(vm, """
            import { write } from "std:system";
            using (var w = await fs.File.open(folder + "/f.txt", "w")) {
                await w.write("one\ntwo");
                w.writeSync("\n");
            }
            var a = fs.File.openSync(folder + "/f.txt", "a");
            a.writeSync("three\n");
            a.dispose();
            a.dispose();
            using (var r = fs.File.openSync(folder + "/f.txt", "r")) {
                var line = await r.read();
                while (line != null) {
                    write("[" + line + "]");
                    line = r.readSync();
                }
            }
            try { a.writeSync("x"); } catch (e) { write("|" + e.code + " " + e.message); }
            """));
    }

    [Theory]
    [MemberData(nameof(Vms))]
    public void BytesAreWrittenAppendedAndReadBothWays(string vm)
    {
        // Every byte as it is, 0, 255 and line breaks among them, never read as text (the owner's answers, 18 November 2026).
        Assert.Equal(("Bytes:[0, 255, 10, 13, 200]|Bytes:[0, 255, 10, 13, 200, 7]|true", ""), Run(vm, """
            import { write } from "std:system";
            var data = fs.readBytesSync(folder + "/given.bin");
            await fs.writeBytes(folder + "/b.bin", data);
            write(await fs.readBytes(folder + "/b.bin"));
            fs.appendBytesSync(folder + "/b.bin", fs.readBytesSync(folder + "/seven.bin"));
            write("|" + fs.readBytesSync(folder + "/b.bin").toString());
            write("|" + (fs.readBytesSync(folder + "/b.bin").slice(0, 5) == data).toString());
            """, ("given.bin", [0, 255, 10, 13, 200]), ("seven.bin", [7])));
        Assert.Equal(new byte[] { 0, 255, 10, 13, 200, 7 }, File.ReadAllBytes(Path.Combine(_folder, "b.bin")));
    }

    [Theory]
    [MemberData(nameof(Vms))]
    public void AFileIsOpenedForItsBytes(string vm)
    {
        // "wb" writes bytes anew; "rb" reads them, up to a count at a time, null at the end, however large the count.
        Assert.Equal(("[tea][ ti][me]|[tea time]|null", ""), Run(vm, """
            import { write } from "std:system";
            var tea = fs.readBytesSync(folder + "/tea.txt");
            using (var w = await fs.File.open(folder + "/f.bin", "wb")) {
                await w.write(tea.slice(0, 4));
                w.writeSync(tea.slice(4));
            }
            using (var r = fs.File.openSync(folder + "/f.bin", "rb")) {
                var part = await r.read(3);
                while (part != null) {
                    write("[" + part.text() + "]");
                    part = r.readSync(3);
                }
            }
            using (var r = fs.File.openSync(folder + "/f.bin", "rb")) {
                write("|[" + r.readSync(1099511627776).text() + "]|");
                write(r.readSync(1));
            }
            """, ("tea.txt", "tea time"u8.ToArray())));
        Assert.Equal("tea time"u8.ToArray(), File.ReadAllBytes(Path.Combine(_folder, "f.bin")));
    }

    [Theory]
    [MemberData(nameof(Vms))]
    public void AFolderIsToldFromAFileAndAFileIsCopied(string vm)
    {
        // A copy is the file as it is, replacing one there; a folder is not copied (the owner's answers, 18 November 2026).
        var (output, error) = Run(vm, """
            import { write } from "std:system";
            fs.makeFolderSync(folder + "/sub");
            write([fs.isFolderSync(folder + "/sub"), await fs.isFolder(folder + "/given.bin"), fs.isFolderSync(folder + "/missing")]);
            fs.writeTextSync(folder + "/copy.bin", "old, and longer than the copy");
            await fs.copy(folder + "/given.bin", folder + "/copy.bin");
            write("|" + (fs.readBytesSync(folder + "/copy.bin") == fs.readBytesSync(folder + "/given.bin")).toString());
            fs.copySync(folder + "/given.bin", folder + "/sub/copy.bin");
            write("|" + fs.listSync(folder + "/sub").toString() + "|");
            try { await fs.copy(folder + "/missing.bin", folder + "/x.bin"); } catch (IOError e) { write(e.code + " " + e.message + "|"); }
            fs.copySync(folder + "/sub", folder + "/x");
            """, ("given.bin", [0, 255, 10]));
        Assert.Equal("[true, false, false]|true|[\"copy.bin\"]|"
                     + $"WL3011 '{_folder}/missing.bin' cannot be copied to '{_folder}/x.bin': there is no such file or folder.|", output);
        Assert.Equal($"WL3011 '{_folder}/sub' cannot be copied to '{_folder}/x': it is a folder, and only a file is copied.", error);
        Assert.Equal(new byte[] { 0, 255, 10 }, File.ReadAllBytes(Path.Combine(_folder, "copy.bin")));
    }

    [Theory]
    [MemberData(nameof(Vms))]
    public void AFileIsReadAndWrittenAsItWasOpened(string vm)
    {
        // Text by a line, read(), and text written; bytes by a count, read(count), and bytes written.
        var (output, _) = Run(vm, """
            import { write } from "std:system";
            fs.writeTextSync(folder + "/t.txt", "tea\n");
            function t(f) { try { f(); } catch (e) { write(e.code + " " + e.message + "|"); } }
            using (var text = fs.File.openSync(folder + "/t.txt", "r")) { t(() => text.readSync(4)); }
            using (var bytes = fs.File.openSync(folder + "/t.txt", "rb")) {
                t(() => bytes.readSync());
                t(() => bytes.readSync(0));
                t(() => bytes.writeSync(fs.readBytesSync(folder + "/t.txt")));
            }
            using (var bytes = fs.File.openSync(folder + "/w.bin", "wb")) { t(() => bytes.writeSync("text")); t(() => bytes.readSync(1)); }
            """);
        Assert.Equal("WL3001 A file opened for its text is read a line at a time, by read(), which takes no count.|"
                     + "WL3001 A file opened for its bytes is read by read(count), how many at most, an integer from 1, not null.|"
                     + "WL3001 A file opened for its bytes is read by read(count), how many at most, an integer from 1, not the integer 0.|"
                     + $"WL3011 '{_folder}/t.txt' cannot be written: it was opened to be read, not written.|"
                     + "WL3001 A file opened for its bytes writes bytes, not a string.|"
                     + $"WL3011 '{_folder}/w.bin' cannot be read: it was opened to be written, not read.|", output);
    }

    [Theory]
    [MemberData(nameof(Vms))]
    public void ManyOperationsRunAtOnce(string vm)
    {
        Assert.Equal(("4950", ""), Run(vm, """
            import { write } from "std:system";
            var writes = [];
            for (var i = 0; i < 100; i = i + 1) writes.push(fs.writeText(folder + "/n" + i.toString() + ".txt", i.toString()));
            await Task.all(writes);
            var reads = [];
            for (var i = 0; i < 100; i = i + 1) reads.push(fs.readText(folder + "/n" + i.toString() + ".txt"));
            var sum = 0;
            foreach (var text in await Task.all(reads)) sum = sum + Integer.parse(text);
            write(sum);
            """));
    }

    [Theory]
    [MemberData(nameof(Vms))]
    public void AFailureIsAnIOErrorNamingTheScriptsPath(string vm)
    {
        var (output, error) = Run(vm, """
            import { write } from "std:system";
            try { await fs.readText(folder + "/missing.txt"); } catch (IOError e) { write(e.code + " " + e.message + "|"); }
            try { fs.removeSync("missing.txt"); } catch (IOError e) { write(e.message + "|"); }
            fs.makeFolderSync(folder + "/full");
            fs.writeTextSync(folder + "/full/x.txt", "x");
            try { await fs.removeFolder(folder + "/full"); } catch (e) { write(e.code + " " + e.message + "|"); }
            fs.readTextSync(folder + "/missing.txt");
            """);
        Assert.Equal($"WL3011 '{_folder}/missing.txt' cannot be read: there is no such file or folder.|"
                     + "'missing.txt' cannot be removed: there is no such file or folder.|"
                     + $"WL3011 '{_folder}/full' cannot be removed: it is not empty.|", output);
        Assert.Equal($"WL3011 '{_folder}/missing.txt' cannot be read: there is no such file or folder.", error);
    }

    [Theory]
    [MemberData(nameof(Vms))]
    public void TextThatIsNotValidInItsEncodingIsAnIOError(string vm)
    {
        File.WriteAllBytes(Path.Combine(_folder, "bad.txt"), [0x61, 0xFF, 0x62]);
        Assert.Equal(("", $"WL3011 '{_folder}/bad.txt' cannot be read: its text is not valid in its encoding."),
            Run(vm, "fs.readTextSync(folder + \"/bad.txt\");"));
    }

    [Theory]
    [InlineData("fs.readTextSync(5);", "WL3001 A path is a string, not the integer 5.")]
    [InlineData("fs.readTextSync(\"\");", "WL3001 A path is a string of one character or more, without '\\0'.")]
    [InlineData("fs.writeTextSync(\"x.txt\", 5);", "WL3001 A text is a string, not the integer 5.")]
    [InlineData("fs.readTextSync(\"x.txt\", 8);", "WL3001 An encoding's name is a string, not the integer 8.")]
    [InlineData("fs.readTextSync(\"x.txt\", \"klingon\");", "WL3001 'x.txt' cannot be read: there is no encoding 'klingon'.")]
    [InlineData("fs.File.openSync(\"x.txt\", \"rw\");",
        "WL3001 A file is opened to read it, \"r\", to write it, \"w\", or to append to it, \"a\", or for its bytes, \"rb\" or \"wb\", not \"rw\".")]
    [InlineData("fs.File.openSync(\"x.txt\", \"rb\", \"utf-8\");", "WL3001 A file opened for its bytes has no encoding.")]
    [InlineData("fs.writeBytesSync(\"x.bin\", \"text\");", "WL3001 'writeBytes' writes bytes, not a string.")]
    [InlineData("fs.appendBytesSync(\"x.bin\", 5);", "WL3001 'appendBytes' writes bytes, not the integer 5.")]
    [InlineData("fs.copySync(\"x.bin\", 5);", "WL3001 A path is a string, not the integer 5.")]
    [InlineData("fs.copySync(\"x.bin\");", "WL2006")]
    [InlineData("fs.readTextSync();", "WL2006")]
    public void WrongArgumentsAreReported(string source, string error)
    {
        foreach (var vm in new[] { "register", "stack" }) Assert.StartsWith(error, Run(vm, source).Error);
    }

    /// <summary>
    /// Runs a program, given its folder as <c>folder</c>, with <c>std:fs</c> imported, and <paramref name="files"/> written
    /// there first, each its bytes; its output and its error.
    /// </summary>
    private (string Output, string Error) Run(string vm, string body, params (string Name, byte[] Bytes)[] files)
    {
        foreach (var (name, bytes) in files) File.WriteAllBytes(Path.Combine(_folder, name), bytes);
        var source = "param folder;\nimport fs from \"std:fs\";\n" + body;
        var builtins = new WebLingoBuiltinRegistry();
        var loader = new WebLingoNativeModuleLoader(new Dictionary<string, string> { ["std"] = Path.Combine(AppContext.BaseDirectory, "std") }, builtins);
        var output = new WebLingoCollectedOutput();
        var services = new WebLingoBuiltinRegistry(WebLingoStandardBuiltins.Create(output));
        var options = new WebLingoCompilerOptions(services.Builtins.Select(b => new WebLingoBuiltinDeclaration(b.Name, b.Arity)).ToList()) { Native = loader };
        WebLingoExecutionResult result;
        if (vm == "register")
        {
            var compiled = new WebLingoCompiler(options).Compile(source, "program.wl");
            Assert.True(compiled.Module is not null, string.Join("; ", compiled.Diagnostics));
            foreach (var builtin in builtins.Builtins) services.Register(builtin);
            result = new WebLingoVirtualMachine().Execute(compiled.Module, Context(services, output));
        }
        else
        {
            var compiled = new WebLingoStackCompiler(options).Compile(source, "program.wl");
            Assert.True(compiled.Module is not null, string.Join("; ", compiled.Diagnostics));
            foreach (var builtin in builtins.Builtins) services.Register(builtin);
            result = new WebLingoStackVirtualMachine().Execute(compiled.Module, Context(services, output));
        }
        return (output.Text, result.Error is { } wrong ? $"{wrong.Code} {wrong.Message}" : "");
    }

    private WebLingoExecutionContext Context(WebLingoBuiltinRegistry services, IWebLingoTextOutput output) =>
        new(new WebLingoWorkerId(0))
        {
            Services = services,
            Output = output,
            Parameters = new Dictionary<string, string> { ["folder"] = _folder },
            FileSystem = new DotNetFileSystem(),
            SchedulerHost = new DotNetSchedulerHost()
        };
}
