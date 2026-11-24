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
