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
            import { write } from "std:io";
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
            import { write } from "std:io";
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
            import { write } from "std:io";
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
            import { write } from "std:io";
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
    public void ManyOperationsRunAtOnce(string vm)
    {
        Assert.Equal(("4950", ""), Run(vm, """
            import { write } from "std:io";
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
            import { write } from "std:io";
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
    [InlineData("fs.File.openSync(\"x.txt\", \"rw\");", "WL3001 A file is opened to read it, \"r\", to write it, \"w\", or to append to it, \"a\", not \"rw\".")]
    [InlineData("fs.readTextSync();", "WL2006")]
    public void WrongArgumentsAreReported(string source, string error)
    {
        foreach (var vm in new[] { "register", "stack" }) Assert.StartsWith(error, Run(vm, source).Error);
    }

    /// <summary>Runs a program, given its folder as <c>folder</c>, with <c>std:fs</c> imported; its output and its error.</summary>
    private (string Output, string Error) Run(string vm, string body)
    {
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
