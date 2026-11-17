using WebLingo.Cli;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// <c>windows:com</c>, COM's objects on Windows (specification, section 3.10; section 9, M0.17), through <c>weblingo</c>, each
/// program run as a script, on the register VM, and as a <c>.wlbc</c> compiled for the stack VM, which must give the same,
/// on a single-threaded apartment's thread, as a Windows host's worker is. Its objects are Windows's own, which every
/// Windows has: <c>Scripting.Dictionary</c> and ADO's <c>Recordset</c>, in memory. Skipped elsewhere.
/// </summary>
public sealed class ComTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-com-" + Guid.NewGuid().ToString("N"));

    public ComTests()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "weblingo.config"),
            "{ \"com\": { \"allow\": [\"Scripting.Dictionary\", \"ADODB.Recordset\", \"ADODB.Connection\"] } }");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [WindowsFact]
    public void AnObjectsMembersAreReachedAsAnyMemberIs()
    {
        const string program = """
            import com from "windows:com";
            var d = com.create("Scripting.Dictionary");
            d.Add("tea", "Sencha");
            d.add("cups", 2);
            d["price"] = 6.5m;
            writeLn([d.Count, d.Item("tea"), d["cups"], d.Exists("tea"), d.exists("Tea"), d["price"]]);
            var keys = [];
            foreach (var key in d) keys.push(key);
            writeLn(keys);
            writeLn(d.Keys());
            var e = com.create("Scripting.Dictionary");
            e.CompareMode = 1;
            e.Add("A", [1, "two", true]);
            writeLn([e.CompareMode, e.Exists("a"), e["a"]]);
            writeLn([d.call("Exists", ["cups"]), d.get("Count")]);
            d.set("CompareMode", 0);
            writeLn(d?.Nonsense);
            using (var f = com.create("Scripting.Dictionary")) { f.Add(1, null); writeLn(f.Items()); }
            d.dispose();
            try { d.Count; } catch (e) { writeLn(e.message); }
            """;
        Assert.Equal(("""
            [3, "Sencha", 2, true, false, 6.5m]
            ["tea", "cups", "price"]
            ["tea", "cups", "price"]
            [1, true, [1, "two", true]]
            [true, 3]
            null
            [null]
            This ComObject has been disposed.

            """.ReplaceLineEndings("\n"), ""), Run(program));
    }

    [WindowsFact]
    public void ValuesGoOutAndComeBack()
    {
        // ADO's own types, in a recordset in memory: currency (6), decimal (14), date (7), bytes (204), and a big integer (20),
        // which ADO gives back as a decimal.
        const string program = """
            import com from "windows:com";
            var rs = com.create("ADODB.Recordset");
            rs.Fields.Append("Name", 202, 50);
            rs.Fields.Append("Price", 6);
            rs.Fields.Append("Exact", 14);
            rs.Fields.Append("When", 7);
            rs.Fields.Append("Data", 204, 16);
            rs.Fields.Append("Count", 20);
            rs.Fields["Exact"].Precision = 10;
            rs.Fields["Exact"].NumericScale = 4;
            rs.Open();
            rs.AddNew();
            var when = DateTime.utc(2026, 11, 17, 12, 30);
            rs["Name"] = "Sencha";
            rs["Price"] = 6.5m;
            rs["Exact"] = 1.2345m;
            rs["When"] = when;
            rs["Data"] = Bytes.from("hi");
            rs["Count"] = 9007199254740993;
            rs.Update();
            rs.MoveFirst();
            writeLn([rs["Name"].Value, rs["Price"].Value, rs["Exact"].Value, rs["Data"].Value, rs["Count"].Value, rs.EOF, rs.RecordCount]);
            writeLn([rs["When"].Value == when, rs["When"].Value.offset == when.toLocal().offset]);
            var names = [];
            foreach (var field in rs.Fields) names.push(field.Name);
            writeLn(names);
            var rows = rs.GetRows();
            writeLn([rows.length, rows[0], rows[2]]);
            rs.Close();
            """;
        Assert.Equal(("""
            ["Sencha", 6.5m, 1.2345m, Bytes:[104, 105], 9007199254740993m, false, 1]
            [true, true]
            ["Name", "Price", "Exact", "When", "Data", "Count"]
            [6, ["Sencha"], [1.2345m]]

            """.ReplaceLineEndings("\n"), ""), Run(program));
    }

    [WindowsFact]
    public void AFailureIsAComErrorAndAMissingMemberAMemberError()
    {
        const string program = """
            import com from "windows:com";
            var d = com.create("Scripting.Dictionary");
            d.Add("a", 1);
            try { d.Add("a", 2); } catch (ComError e) { writeLn([e.code, e.number, e.source, e.description, e is Error]); }
            try { d.Nonsense; } catch (MemberError e) { writeLn(e.message); }
            try { d.Nonsense(1); } catch (MemberError e) { writeLn(e.message); }
            var c = com.create("ADODB.Connection");
            try { c.Open("Provider=Nothing.Here"); } catch (ComError e) { writeLn([e.source, e.description.length > 0]); }
            try { d.Add(writeLn, 1); } catch (TypeError e) { writeLn(e.message); }
            try { com.create("Nothing.Here"); } catch (e) { writeLn(e.code); }
            """;
        Assert.Equal(("""
            ["WL3012", -2146827831, "", "", true]
            The COM object has no member 'Nonsense'.
            The COM object has no member 'Nonsense'.
            ["ADODB.Connection", true]
            A function cannot be given to a COM object.
            WL3001

            """.ReplaceLineEndings("\n"), ""), Run(program));
    }

    [WindowsFact]
    public void OnlyWhatTheConfigurationAllowsIsMade()
    {
        const string program = """
            import com from "windows:com";
            try { com.create("WScript.Shell"); } catch (TypeError e) { writeLn(e.message); }
            try { com.get("winmgmts:"); } catch (TypeError e) { writeLn(e.message); }
            """;
        Assert.Equal(("""
            'WScript.Shell' is not a COM object this application may make: its configuration's "com" does not allow it.
            'winmgmts:' is not a COM object this application may make: its configuration's "com" does not allow it.

            """.ReplaceLineEndings("\n"), ""), Run(program));
        File.Delete(Path.Combine(_folder, "weblingo.config"));
        Assert.Equal(("""
            'WScript.Shell' is not a COM object this application may make: its configuration's "com" does not allow it.
            'winmgmts:' is not a COM object this application may make: its configuration's "com" does not allow it.

            """.ReplaceLineEndings("\n"), ""), Run(program));
    }

    [Fact]
    public void ComIsAllowedByAListOfNames()
    {
        File.WriteAllText(Path.Combine(_folder, "weblingo.config"), "{ \"com\": [\"Scripting.Dictionary\"] }");
        File.WriteAllText(Path.Combine(_folder, "program.wl"), "writeLn(1);");
        var (exit, _, error) = Weblingo("run", Path.Combine(_folder, "program.wl"));
        Assert.Equal(WebLingoCommandLine.FileUnreadable, exit);
        Assert.Contains("\"com\" is an object, { \"allow\": [\"ADODB.Connection\"] }.", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The program run as a script, on the register VM, then compiled for the stack VM and run from its <c>.wlbc</c>, each on
    /// a single-threaded apartment's thread: what both write, which must be the same, and the first's errors.
    /// </summary>
    private (string Output, string Error) Run(string program)
    {
        var path = Path.Combine(_folder, "program.wl");
        File.WriteAllText(path, program);
        var register = OnApartment(() => Weblingo("run", path));
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", "--stack", path).Exit);
        var stack = OnApartment(() => Weblingo("run", Path.ChangeExtension(path, ".wlbc")));
        Assert.Equal((register.Output, register.Error.Replace("program.wl", "program", StringComparison.Ordinal)),
            (stack.Output, stack.Error.Replace("program.wl", "program", StringComparison.Ordinal)));
        return (register.Output, register.Error);
    }

    private static (int Exit, string Output, string Error) OnApartment(Func<(int, string, string)> run)
    {
        (int, string, string) result = default;
        var thread = new Thread(() => result = run());
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    private static (int Exit, string Output, string Error) Weblingo(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WebLingoCommandLine.Run(args, output, error);
        return (exit, output.ToString(), error.ToString());
    }
}

/// <summary>A test that needs Windows, which COM is (section 3.10), skipped elsewhere.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "COM is Windows's.";
    }
}
