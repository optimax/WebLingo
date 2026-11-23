using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
namespace WebLingo.Benchmarks;

/// <summary>A program of the comparison, written in each language, and the one line every language must write.</summary>
public sealed record WebLingoComparisonProgram(string Name, string Exercises, string ExpectedOutput);

/// <summary>
/// A way of running the comparison's programs: a language's runtime, named on the command line by <see cref="Name"/>,
/// headed in the table by <see cref="Heading"/>, given a program's file of its extension after its own arguments, in its
/// folder, <see cref="Folder"/> for WebLingo's (a copy of the programs with its <c>weblingo.config</c>).
/// </summary>
public sealed record WebLingoComparisonRuntime(
    string Name, string Heading, string Language, string Extension, IReadOnlyList<string> Arguments, string? Folder = null);

/// <summary>
/// The comparison of WebLingo with JavaScript, Python and Lua (specification, section 5.6, "Against other languages"):
/// <c>--compare [--runs N] [--warm-ups N] [--program NAME]… [--runtime NAME]… [--weblingo PATH] [--node PATH]
/// [--python PATH] [--lua PATH]</c>. Each program of <c>Comparative/</c>, written alike in each language, is run as a
/// user runs it, a process of its runtime's, timed whole, start-up included, and <c>empty</c>, which only writes 0,
/// measures each runtime's start-up, which a second table takes away. WebLingo runs as <c>weblingo</c>, the one beside
/// the benchmark unless <c>--weblingo</c> names another, on both VMs, from source and compiled; JavaScript on Node.js,
/// with its JIT and without it (<c>--jitless</c>), Python on CPython and Lua on Lua 5.4, each found on the PATH unless
/// named. A runtime that is not found is left out, and said so. Exit codes as the benchmark's.
/// </summary>
public static class WebLingoComparison
{
    public static IReadOnlyList<WebLingoComparisonProgram> Programs { get; } =
    [
        new("empty", "nothing: the runtime's start-up, and one line written", "0\n"),
        new("loops", "nested for loops, integer arithmetic, comparisons and if", "513539\n"),
        new("calls", "a recursive fib(30), about 2.7 million calls", "832040\n"),
        new("closures", "closures made and called, sharing a captured variable", "3000000000\n"),
        new("strings", "strings made from integers, joined, compared, measured and searched", "2472766\n"),
        new("arrays", "an array filled, then read and written by index", "1498500000\n"),
        new("maps", "a map of string keys filled, then looked up", "8749975000\n"),
        new("objects", "instances made, their methods called and their fields read", "3000000\n"),
        new("sort", "pseudo-random integers sorted by a comparison function", "332303144\n"),
    ];

    public static IReadOnlyList<WebLingoComparisonRuntime> Runtimes { get; } =
    [
        new("weblingo-register", "WebLingo, register VM", "weblingo", ".wlg", ["run"], "register"),
        new("weblingo-register-compiled", "WebLingo, register VM, compiled", "weblingo", ".wlbr", ["run"], "register"),
        new("weblingo-stack", "WebLingo, stack VM", "weblingo", ".wlg", ["run"], "stack"),
        new("weblingo-stack-compiled", "WebLingo, stack VM, compiled", "weblingo", ".wlbs", ["run"], "stack"),
        new("node", "JavaScript, Node.js", "node", ".js", []),
        new("node-jitless", "JavaScript, Node.js without JIT", "node", ".js", ["--jitless"]),
        new("python", "Python, CPython", "python", ".py", []),
        new("lua", "Lua", "lua", ".lua", []),
    ];

    /// <summary>The folder of the programs, copied beside the benchmark.</summary>
    public static string ProgramsFolder { get; } = Path.Combine(AppContext.BaseDirectory, "Comparative");

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        var runs = 5;
        var warmUps = 1;
        var names = new List<string>();
        var chosen = new List<string>();
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var value = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--runs" when TryCount(value, 1, out runs):
                case "--warm-ups" when TryCount(value, 0, out warmUps):
                    i++;
                    break;
                case "--program" when Programs.Any(program => program.Name == value):
                    names.Add(value!);
                    i++;
                    break;
                case "--runtime" when Runtimes.Any(runtime => runtime.Name == value):
                    chosen.Add(value!);
                    i++;
                    break;
                case "--weblingo" or "--node" or "--python" or "--lua" when value is not null:
                    paths[args[i][2..]] = value;
                    i++;
                    break;
                default:
                    return Usage(error);
            }
        }
        // empty always, since the second table takes each runtime's start-up away.
        var programs = Programs.Where(program => names.Count == 0 || program.Name == "empty" || names.Contains(program.Name)).ToList();
        var runtimes = Runtimes.Where(runtime => chosen.Count == 0 || chosen.Contains(runtime.Name)).ToList();

        var executables = new Dictionary<string, string>(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var language in runtimes.Select(runtime => runtime.Language).Distinct())
        {
            var found = paths.TryGetValue(language, out var given) ? (File.Exists(given) ? Path.GetFullPath(given) : null)
                : language == "weblingo" ? Beside("weblingo") : OnPath(language);
            if (found is null) missing.Add(language);
            else executables[language] = found;
        }
        runtimes = runtimes.Where(runtime => executables.ContainsKey(runtime.Language)).ToList();
        if (runtimes.Count == 0)
        {
            error.WriteLine($"No runtime was found: {string.Join(", ", missing)}.");
            return WebLingoBenchmarkCommand.MeasurementFailed;
        }

        var work = Path.Combine(Path.GetTempPath(), "weblingo-comparison-" + Guid.NewGuid().ToString("N"));
        try
        {
            Prepare(work, programs, runtimes, executables);
            output.WriteLine($"WebLingo comparison: {runs} timed runs of each program after {warmUps} warm-ups, each a whole run");
            output.WriteLine("of its runtime, as a user runs it, start-up included.");
            foreach (var language in executables.Keys) output.WriteLine($"- {Version(language, executables[language])}");
            foreach (var language in missing) output.WriteLine($"- {language}: not found, left out; --{language} PATH names it.");
            output.WriteLine($"- {RuntimeInformation.OSDescription.Trim()}, {RuntimeInformation.OSArchitecture}, " +
                             $"{Environment.ProcessorCount} logical processors.");
            output.WriteLine();

            var medians = new Dictionary<(string, string), double>();
            foreach (var program in programs)
            {
                foreach (var runtime in runtimes)
                    medians[(program.Name, runtime.Name)] = Measure(program, runtime, executables[runtime.Language], work, runs, warmUps);
            }
            WriteTable(output, "Median time of a run, start-up included", programs, runtimes, (program, runtime) =>
                medians[(program, runtime)]);
            output.WriteLine();
            WriteTable(output, "Median time of a run, less its runtime's start-up (empty's)", programs.Where(p => p.Name != "empty").ToList(),
                runtimes, (program, runtime) => Math.Max(0, medians[(program, runtime)] - medians[("empty", runtime)]));
            return WebLingoBenchmarkCommand.Success;
        }
        catch (InvalidOperationException exception)
        {
            output.Flush();
            error.WriteLine(exception.Message);
            return WebLingoBenchmarkCommand.MeasurementFailed;
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// A copy of the WebLingo programs for each VM, its <c>weblingo.config</c> naming it, each compiled there for the
    /// runtimes that run bytecode; the other languages' programs are run where they are.
    /// </summary>
    private static void Prepare(string work, IReadOnlyList<WebLingoComparisonProgram> programs,
        IReadOnlyList<WebLingoComparisonRuntime> runtimes, IReadOnlyDictionary<string, string> executables)
    {
        foreach (var folder in runtimes.Select(runtime => runtime.Folder).OfType<string>().Distinct())
        {
            var target = Path.Combine(work, folder);
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "weblingo.config"), $"{{ \"vm\": \"{folder}\" }}\n");
            foreach (var program in programs)
            {
                File.Copy(Path.Combine(ProgramsFolder, program.Name + ".wlg"), Path.Combine(target, program.Name + ".wlg"));
                if (!runtimes.Any(runtime => runtime.Folder == folder && runtime.Extension != ".wlg")) continue;
                var (exit, text) = RunOnce(executables["weblingo"], ["compile", program.Name + ".wlg"], target);
                if (exit != 0) throw new InvalidOperationException($"{program.Name}.wlg did not compile for the {folder} VM: {text}");
            }
        }
    }

    /// <summary>The median, in milliseconds, of a program's timed runs on a runtime, each checked for its output.</summary>
    private static double Measure(WebLingoComparisonProgram program, WebLingoComparisonRuntime runtime, string executable,
        string work, int runs, int warmUps)
    {
        var folder = runtime.Folder is { } own ? Path.Combine(work, own) : ProgramsFolder;
        List<string> arguments = [.. runtime.Arguments, program.Name + runtime.Extension];
        var times = new List<double>();
        for (var i = 0; i < warmUps + runs; i++)
        {
            var watch = Stopwatch.StartNew();
            var (exit, text) = RunOnce(executable, arguments, folder);
            watch.Stop();
            if (exit != 0 || text != program.ExpectedOutput)
            {
                throw new InvalidOperationException(
                    $"{program.Name} on {runtime.Heading} exited with {exit} and wrote '{text.TrimEnd()}', not '{program.ExpectedOutput.TrimEnd()}'.");
            }
            if (i >= warmUps) times.Add(watch.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        return times.Count % 2 == 1 ? times[times.Count / 2] : (times[times.Count / 2 - 1] + times[times.Count / 2]) / 2;
    }

    /// <summary>A process run to its end, its output read as it is written, its line breaks LF, its errors after it.</summary>
    private static (int Exit, string Output) RunOnce(string executable, IReadOnlyList<string> arguments, string folder)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{executable} did not start.");
        process.StandardInput.Close();
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        var text = output.Replace("\r\n", "\n", StringComparison.Ordinal);
        var errorText = errors.GetAwaiter().GetResult();
        return (process.ExitCode, errorText.Length == 0 ? text : text + errorText.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>A runtime's name and version as it gives them, its first line.</summary>
    private static string Version(string language, string executable)
    {
        var (_, text) = RunOnce(executable, language == "lua" ? ["-v"] : ["--version"], AppContext.BaseDirectory);
        var line = text.Split('\n')[0].Trim();
        return language switch
        {
            "node" => $"Node.js {line}, {executable}",
            _ => $"{line}, {executable}",
        };
    }

    private static string? Beside(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? name + ".exe" : name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>A program found on the PATH, as a shell finds it; on Windows by its extensions, PATHEXT's.</summary>
    private static string? OnPath(string name)
    {
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""];
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var path = Path.Combine(folder.Trim('"'), name + extension);
                if (File.Exists(path)) return path;
            }
        }
        return null;
    }

    private static void WriteTable(TextWriter output, string title, IReadOnlyList<WebLingoComparisonProgram> programs,
        IReadOnlyList<WebLingoComparisonRuntime> runtimes, Func<string, string, double> value)
    {
        output.WriteLine(title + ", in milliseconds:");
        output.WriteLine();
        output.WriteLine("| Program | " + string.Join(" | ", runtimes.Select(runtime => runtime.Heading)) + " |");
        output.WriteLine("|---|" + string.Concat(runtimes.Select(_ => "---:|")));
        foreach (var program in programs)
        {
            output.WriteLine($"| {program.Name} | " + string.Join(" | ",
                runtimes.Select(runtime => value(program.Name, runtime.Name).ToString("0", CultureInfo.InvariantCulture))) + " |");
        }
    }

    private static bool TryCount(string? text, int minimum, out int count) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out count) && count >= minimum;

    private static int Usage(TextWriter error)
    {
        error.WriteLine("Usage: WebLingo.Benchmarks --compare [--runs N] [--warm-ups N] [--program NAME]... [--runtime NAME]...");
        error.WriteLine("         [--weblingo PATH] [--node PATH] [--python PATH] [--lua PATH]");
        error.WriteLine($"  programs: {string.Join(", ", Programs.Select(program => program.Name))}");
        error.WriteLine($"  runtimes: {string.Join(", ", Runtimes.Select(runtime => runtime.Name))}");
        return WebLingoBenchmarkCommand.UsageError;
    }
}
