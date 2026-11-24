using System.Text.Json;
using WebLingo.Conformance.Tests;
using Xunit;
namespace WebLingo.Templates.Tests;

/// <summary>
/// A page under the debugger (specification, section 5.7, "Debugging WASP pages, parts", part 1): <c>wasp --debug-adapter</c>
/// runs a page as <c>wasp run</c> does, its parameters from its launch, and stops at breakpoints in the page, the templates it
/// includes, its layout, the components it renders and the modules it imports, each in its own file at its own line.
/// </summary>
public sealed class WaspDebugTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wasp-debug-" + Guid.NewGuid().ToString("N"));

    public WaspDebugTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public async Task APageStopsInEveryFileItIsMadeOf()
    {
        var tea = Write("lib/tea.wlg", "export function brew(minutes) {\n    var steeped = minutes * 60;\n    return steeped;\n}\n");
        var greeting = Write("includes/greeting.wasp", "<p>\n<% var greeting = \"Hello, \" + name; %>\n<%= greeting %></p>\n");
        var layout = Write("layouts/main.wasp", "<% param title; %><html><body>\n<% var year = 2026; %>\n<% await renderBody(); %>\n</body></html>\n");
        var cup = Write("components/cup.wasp", "<% param size; %>\n<% var doubled = size * 2; %>\n<span><%= doubled %></span>\n");
        var page = Write("index.wasp", "<% param name = \"world\"; %>\n"
                                       + "<% import { brew } from \"/lib/tea.wlg\"; %>\n"
                                       + "<% layout \"/layouts/main.wasp\", { title: \"Tea\" }; %>\n"
                                       + "<% var seconds = brew(3); %>\n"
                                       + "<% include \"/includes/greeting.wasp\"; %>\n"
                                       + "<% await renderComponent(\"/components/cup.wasp\", { size: 4 }); %>\n"
                                       + "<p><%= seconds %></p>\n");

        await using var dap = new DebugAdapterSession("wasp");
        await dap.Start(page, args: ["name=Anna"], root: _root);
        foreach (var (file, line) in new[] { (page, 4), (greeting, 2), (layout, 2), (cup, 2) })
        {
            await dap.Request("setBreakpoints", new { source = new { path = file }, breakpoints = new[] { new { line } } });
        }
        await dap.Request("configurationDone");

        // The layout first, its body after: the page's code runs as the layout renders it.
        var frames = await Stopped(dap);
        AssertFrame(frames[0], layout, 2);
        frames = await Continue(dap);
        AssertFrame(frames[0], page, 4);
        Assert.Equal("\"Anna\"", (await dap.Locals(frames[0]))["name"]);
        await dap.Request("stepIn", new { threadId = 1 });
        frames = await Stopped(dap);
        AssertFrame(frames[0], tea, 2);
        Assert.Equal("3", (await dap.Locals(frames[0]))["minutes"]);
        frames = await Continue(dap);
        AssertFrame(frames[0], greeting, 2);
        Assert.Equal("\"Anna\"", (await dap.Locals(frames[0]))["name"]);
        frames = await Continue(dap);
        AssertFrame(frames[0], cup, 2);
        Assert.Equal("4", (await dap.Locals(frames[0]))["size"]);

        await dap.Request("continue", new { threadId = 1 });
        Assert.Equal(0, (await dap.Event("exited")).GetProperty("exitCode").GetInt32());
        await dap.Event("terminated");
        await dap.Request("disconnect");
    }

    /// <summary>
    /// A site under the debugger (specification, section 5.7, "Debugging WASP pages, parts", part 2): served as <c>wasp
    /// serve</c> serves it, each request a thread, named by its method, its path and its number; a request at a breakpoint
    /// alone stops, others served meanwhile; stepped and continued, it ends; and the debugger's pause stops the requests that
    /// come after it, until the debugger goes on.
    /// </summary>
    [Fact]
    public async Task ASiteStopsOnlyTheRequestAtABreakpoint()
    {
        var slow = Write("slow.wasp", "<% var a = 1; %>\n<% var b = a + 1; %>\n<p>slow <%= b %></p>\n");
        Write("other.wasp", "<p>other</p>\n");

        await using var dap = new DebugAdapterSession("wasp");
        await dap.Serve(_root);
        await dap.Request("setBreakpoints", new { source = new { path = slow }, breakpoints = new[] { new { line = 1 } } });
        await dap.Request("configurationDone");
        using var client = new HttpClient { BaseAddress = new Uri(await Address(dap)), Timeout = TimeSpan.FromSeconds(30) };

        var slowly = client.GetStringAsync("/slow");
        var stopped = await dap.Event("stopped");
        Assert.Equal(("breakpoint", false), (stopped.GetProperty("reason").GetString(), stopped.GetProperty("allThreadsStopped").GetBoolean()));
        var thread = stopped.GetProperty("threadId").GetInt32();
        var threads = (await dap.Request("threads")).GetProperty("threads").EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetInt32(), item => item.GetProperty("name").GetString());
        Assert.Equal($"GET /slow #{thread}", threads[thread]);
        AssertFrame((await dap.Frames(thread))[0], slow, 1);

        // Another request is served while it is stopped.
        Assert.Equal("<p>other</p>", (await client.GetStringAsync("/other")).Trim());
        Assert.False(slowly.IsCompleted);

        // Stepped, it stops at its next line; continued, it ends.
        await dap.Request("next", new { threadId = thread });
        Assert.Equal("step", (await dap.Event("stopped")).GetProperty("reason").GetString());
        var frames = await dap.Frames(thread);
        AssertFrame(frames[0], slow, 2);
        Assert.Equal("1", (await dap.Locals(frames[0]))["a"]);
        await dap.Request("continue", new { threadId = thread });
        Assert.Equal("<p>slow 2</p>", (await slowly).Trim());

        // Paused, the requests that come stop, until the debugger goes on.
        await dap.Request("pause", new { threadId = thread });
        var paused = client.GetStringAsync("/other");
        stopped = await dap.Event("stopped");
        Assert.Equal("pause", stopped.GetProperty("reason").GetString());
        Assert.False(paused.IsCompleted);
        await dap.Request("continue", new { threadId = stopped.GetProperty("threadId").GetInt32() });
        Assert.Equal("<p>other</p>", (await paused).Trim());

        await dap.Request("disconnect");
    }

    /// <summary>
    /// A breakpoint's condition in a site (section 5.7, "Debugging WASP pages, parts", part 3): in code every page shares,
    /// it stops only the requests it names, a native class's property, <c>request.path</c>, read without running a page's code.
    /// </summary>
    [Fact]
    public async Task AConditionStopsOnlyTheRequestsItNames()
    {
        var shared = Write("shared.wasp", "<% var seen = request.path; %>\n<p><%= seen %></p>\n");
        Write("a.wasp", "<% param request; %>\n<% include \"/shared.wasp\"; %>\n");
        Write("b.wasp", "<% param request; %>\n<% include \"/shared.wasp\"; %>\n");

        await using var dap = new DebugAdapterSession("wasp");
        await dap.Serve(_root);
        await dap.Request("setBreakpoints", new { source = new { path = shared }, breakpoints = new[] { new { line = 2, condition = "request.path == \"/b\"" } } });
        await dap.Request("configurationDone");
        using var client = new HttpClient { BaseAddress = new Uri(await Address(dap)), Timeout = TimeSpan.FromSeconds(30) };

        Assert.Equal("<p>/a</p>", (await client.GetStringAsync("/a")).Trim());
        var named = client.GetStringAsync("/b");
        var stopped = await dap.Event("stopped");
        var thread = stopped.GetProperty("threadId").GetInt32();
        var frame = (await dap.Frames(thread))[0];
        AssertFrame(frame, shared, 2);
        Assert.Equal("\"/b\"", (await dap.Locals(frame))["seen"]);
        await dap.Request("continue", new { threadId = thread });
        Assert.Equal("<p>/b</p>", (await named).Trim());
        await dap.Request("disconnect");
    }

    /// <summary>Where the site is served, as the adapter says it first.</summary>
    private static async Task<string> Address(DebugAdapterSession dap)
    {
        while (true)
        {
            var said = (await dap.Event("output")).GetProperty("output").GetString()!;
            var at = said.IndexOf("http://", StringComparison.Ordinal);
            if (said.StartsWith("WASP serving ", StringComparison.Ordinal) && at >= 0) return said[at..said.IndexOf(' ', at)];
        }
    }

    private static async Task<JsonElement> Stopped(DebugAdapterSession dap)
    {
        await dap.Event("stopped");
        return await dap.Frames();
    }

    private static async Task<JsonElement> Continue(DebugAdapterSession dap)
    {
        await dap.Request("continue", new { threadId = 1 });
        return await Stopped(dap);
    }

    private static void AssertFrame(JsonElement frame, string path, int line) =>
        Assert.Equal((path, line), (frame.GetProperty("source").GetProperty("path").GetString(), frame.GetProperty("line").GetInt32()));
}
