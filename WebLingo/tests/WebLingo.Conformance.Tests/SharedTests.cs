using WebLingo.Cli;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// <c>std:shared</c> (specification, section 6.12, "Memory a site's requests share"; the owner's answers, 27 November 2026,
/// A1 to A4 (a) of "Memory a Site's Requests Share"), through <c>weblingo</c>, one store for a program and its workers:
/// values copied in and out, <c>add</c> and <c>take</c> each one step, a value kept for some seconds gone after them, the
/// store's most <c>weblingo.config</c>'s <c>"limits"</c>' <c>"shared"</c>, and each misuse's error, its message tested.
/// </summary>
public sealed class SharedTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-shared-" + Guid.NewGuid().ToString("N"));

    private const string Imports = "import { writeLn } from \"std:console\"; import { Shared } from \"std:shared\";\n";

    public SharedTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void ValuesAreCopiedInAndOut_SoThatNoneChangesWhatIsKept()
    {
        var (output, error) = Run(Imports + """
            import { Bytes } from "std:bytes";
            import { DateTime } from "std:datetime";
            var kept = [null, true, 7, 1.5, 2.50m, "tea", Bytes.from("hi"), DateTime.utc(2026, 11, 27), { "a": #{1, "b"} }];
            Shared.set("all", kept);
            kept.push("later");
            var read = Shared.get("all");
            read[0] = "changed";
            writeLn(Shared.get("all"));
            writeLn(Shared.get("all") == Shared.get("all"));
            """);
        Assert.Equal("", error);
        Assert.Equal("""
            [null, true, 7, 1.5, 2.5m, "tea", Bytes:[104, 105], DateTime:2026-11-27T00:00:00Z, {"a": #{1, "b"}}]
            false

            """.ReplaceLineEndings("\n"), output);
    }

    [Fact]
    public void GetRemoveTakeAndAdd_EachSaysWhatWasThere()
    {
        var (output, error) = Run(Imports + """
            writeLn([Shared.get("none"), Shared.remove("none"), Shared.take("none")]);
            Shared.set("name", "Anna");
            Shared.set("name", "Ben");
            writeLn([Shared.get("name"), Shared.remove("name"), Shared.remove("name"), Shared.get("name")]);
            writeLn([Shared.add("views", 1), Shared.add("views", 2), Shared.add("views", -5), Shared.get("views")]);
            Shared.set("set", 10);
            writeLn([Shared.add("set", 1), Shared.take("set"), Shared.take("set"), Shared.add("set", 1)]);
            """);
        Assert.Equal("", error);
        Assert.Equal("""
            [null, false, null]
            ["Ben", true, false, null]
            [1, 3, -2, -2]
            [11, 11, null, 1]

            """.ReplaceLineEndings("\n"), output);
    }

    /// <summary>Under <c>weblingo</c>, a program's workers share its store: four adding 1,000 times each, all at once, count 4,000.</summary>
    [Fact]
    public void AProgramsWorkersShareItsStore_EachAddOneStep()
    {
        Write("adder.wlg", "import { Shared } from \"std:shared\"; import { parent } from \"std:workers\";\n"
                           + "for (var i = 0; i < 1000; i++) Shared.add(\"count\", 1);\nparent.post(Shared.get(\"from the program\"));\n");
        var (output, error) = Run(Imports + """
            import { Worker } from "std:workers";
            Shared.set("from the program", "tea");
            var adders = [];
            for (var i = 0; i < 4; i++) adders.push(new Worker("adder.wlg"));
            var heard = [];
            foreach (var adder in adders) heard.push(await adder.receive());
            writeLn([heard, Shared.get("count")]);
            """);
        Assert.Equal("", error);
        Assert.Equal("[[\"tea\", \"tea\", \"tea\", \"tea\"], 4000]\n", output);
    }

    [Fact]
    public void AValueKeptForSomeSecondsIsGoneAfterThem()
    {
        var (output, error) = Run(Imports + """
            import { delay } from "std:system";
            Shared.set("long", "kept", 60);
            Shared.set("brief", "gone", 0.05);
            Shared.set("forever", "kept", null);
            await delay(300);
            writeLn([Shared.get("long"), Shared.get("brief"), Shared.get("forever"), Shared.remove("brief")]);
            """);
        Assert.Equal("", error);
        Assert.Equal("[\"kept\", null, \"kept\", false]\n", output);
    }

    /// <summary>
    /// The store's most, <c>"limits"</c>' <c>"shared"</c>, in megabytes: a <c>set</c> past it a <c>SharedMemoryError</c>,
    /// WL3025, which a script catches, nothing kept; an <c>add</c> of a new key past it too; <c>null</c> for no most.
    /// </summary>
    [Fact]
    public void ASetPastTheStoresMostIsASharedMemoryError()
    {
        Write("weblingo.config", "{ \"limits\": { \"shared\": 1 } }");
        const string program = """
            Shared.set("small", "x".repeat(1000));
            try {
                Shared.set("large", "x".repeat(600000));
                writeLn("kept");
            } catch (SharedMemoryError e) {
                writeLn([e.code, e.message]);
            }
            writeLn([Shared.get("large") == null, Shared.get("small").length]);
            """;
        var (output, error) = Run(Imports + program);
        Assert.Equal("", error);
        Assert.Equal("""
            ["WL3025", "The shared memory is full: 'large''s value would make it hold more than its most, 1 MB."]
            [true, 1000]

            """.ReplaceLineEndings("\n"), output);

        Write("weblingo.config", "{ \"limits\": { \"shared\": null } }");
        (output, error) = Run(Imports + program);
        Assert.Equal("", error);
        Assert.Equal("kept\n[false, 1000]\n", output);
    }

    [Fact]
    public void TheConfigurationsMostIsChecked()
    {
        Write("weblingo.config", "{ \"limits\": { \"shared\": 0 } }");
        var (exit, _, error) = Weblingo("run", Main(Imports + "writeLn(1);\n"));
        Assert.NotEqual(WebLingoCommandLine.Success, exit);
        Assert.Contains("\"shared\": 64, the megabytes the memory the requests share holds at most, an integer from 1 to 1048576, or null "
                        + "for no most", error, StringComparison.Ordinal);
    }

    [Fact]
    public void EachMisuseIsAnErrorThatSaysWhy()
    {
        var (output, error) = Run(Imports + """
            function tried(f) { try { f(); writeLn("no error"); } catch (e) { writeLn([e.code, e.message]); } }
            tried(() => Shared.get(5));
            tried(() => Shared.set("k".repeat(1025), 1));
            tried(() => Shared.set("f", writeLn));
            tried(() => Shared.set("e", new Error("no")));
            tried(() => Shared.set("s", 1, 0));
            tried(() => Shared.set("s", 1, "soon"));
            tried(() => Shared.add("n", 1.5));
            Shared.set("text", "x");
            tried(() => Shared.add("text", 1));
            Shared.add("big", 9223372036854775807);
            tried(() => Shared.add("big", 1));
            writeLn(Shared.get("big"));
            """);
        Assert.Equal("", error);
        Assert.Equal("""
            ["WL3001", "The argument of 'get' must be a string of at most 1,024 characters, not the integer 5."]
            ["WL3001", "The first argument of 'set', the key, must be a string of at most 1,024 characters, not a string of 1,025 characters."]
            ["WL3001", "A shared value holds null, Booleans, numbers, decimals, strings, bytes, dates, and arrays, maps and sets of them, not a function."]
            ["WL3001", "A shared value holds null, Booleans, numbers, decimals, strings, bytes, dates, and arrays, maps and sets of them, not an instance of Error."]
            ["WL3001", "The third argument of 'set', the seconds, must be a number of seconds more than 0, or null to keep the value until it is removed, not the integer 0."]
            ["WL3001", "The third argument of 'set', the seconds, must be a number of seconds more than 0, or null to keep the value until it is removed, not a string."]
            ["WL3001", "The second argument of 'add', the amount, must be an integer, not the number 1.5."]
            ["WL3001", "'add' adds to an integer, and the key 'text' holds another kind of value."]
            ["WL3002", "'add' gives an integer beyond int64's range for the key 'big'."]
            9223372036854775807

            """.ReplaceLineEndings("\n"), output);
    }

    private void Write(string name, string text) => File.WriteAllText(Path.Combine(_folder, name), text);

    private string Main(string program)
    {
        var path = Path.Combine(_folder, "main.wlg");
        File.WriteAllText(path, program);
        return path;
    }

    private (string Output, string Error) Run(string program)
    {
        var (exit, output, error) = Weblingo("run", Main(program));
        Assert.True(exit == WebLingoCommandLine.Success, error);
        return (output, error);
    }

    private static (int Exit, string Output, string Error) Weblingo(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WebLingoCommandLine.Run(args, output, error);
        return (exit, output.ToString(), error.ToString());
    }
}
