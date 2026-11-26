using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.Http;
using WebLingo.Hosting.AspNetCore;
using WebLingo.Hosting.DotNet;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// Old generations let go (specification, section 9, "M0.22 parts", part 2; the owner's answer, 26 November 2026, A2 (c) of
/// "M0.22: Old Generations Drained"): a site reloaded many times while clients ask for its pages, through ASP.NET Core's
/// host, its pages with a layout, a component, a module with a class, a handler, a session and a page that waits longer
/// than the drain; reloads in the middle of requests, drained requests, a page that does not compile and a reload whose
/// routes cannot be made among them. Once the clients have stopped and their requests have ended, every generation but the
/// one served is let go, and every request's heap with it, as weak references to them show.
/// </summary>
public sealed class GenerationsLetGoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "weblingo-generations-" + Guid.NewGuid().ToString("N"));
    private readonly WebLingoWorkerPool _workers = new(2);
    private readonly List<WeakReference> _heaps = [];

    /// <summary>The pages the clients ask for, each with what its answer begins with, its status 200 but where it says.</summary>
    private static readonly string[] Pages = ["/", "/component", "/imports", "/api/answer", "/session", "/slow", "/broken"];

    public GenerationsLetGoTests()
    {
        Write("layouts/main.wasp", "<% param title; %><html><title><%= title %></title><body><% renderBody(); %></body></html>");
        Write("components/value.wasp", "<% param n; import { delay } from \"std:system\"; await delay(5); %><%= n %>");
        Write("component.wasp", "<% remember(); await renderComponent(\"/components/value.wasp\", { n: 42 }); %>");
        Write("lib/cups.wlg", "export class Cup { constructor(n) { this.n = n; this.full = true; } size() { return this.n; } }\n"
                              + "export function pour(n) { var cup = new Cup(n); cup.full = false; return cup.size(); }");
        Write("imports.wasp", "<% import { Cup, pour } from \"/lib/cups.wlg\"; remember(); var cup = new Cup(3); %><%= cup.size() + pour(4) %>");
        Write("api/answer.wlg", "import { delay } from \"std:system\";\n"
                                + "export async function get(request, response) { remember(); await delay(5); response.write(42); }");
        Write("session.wasp", "<% param request; remember(); var n = (request.session?[\"n\"] ?? 0) + 1; request.session[\"n\"] = n;\n"
                              + "request.session[\"kinds\"] = { list: [1, \"two\", null, true] }; %><%= n %>");
        Write("slow.wasp", "<% import { delay } from \"std:system\"; remember(); await delay(2000); %>slow");
        Version(0);
    }

    public void Dispose()
    {
        _workers.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task EveryOldGenerationIsLetGo_AndEveryRequestsHeap()
    {
        var application = Load();
        var generations = new List<WeakReference> { Watch(application) };
        using var stop = new CancellationTokenSource();
        var answers = new int[2];
        var clients = Enumerable.Range(0, 4).Select(client => Ask(application, client, stop.Token, answers)).ToArray();
        var (reloads, refused) = (0, 0);
        // 24 versions, and more while no request has been drained yet, as on a loaded machine, for 30 seconds at most.
        var clock = Stopwatch.StartNew();
        for (var version = 1; version <= 24 || (Volatile.Read(ref answers[1]) == 0 && clock.Elapsed < TimeSpan.FromSeconds(30)); version++)
        {
            await Task.Delay(60);
            if (version % 8 == 0)
            {
                // A page and a handler for one path: its routes cannot be made, and the generation served stays.
                Write("api/answer.wasp", "clash");
                Assert.False(application.TryReload(out _));
                File.Delete(Path.Combine(_root, "api", "answer.wasp"));
                refused++;
                continue;
            }
            Version(version);
            Assert.True(application.TryReload(out var error), error);
            WebLingoDrain.Of(application).Reloaded(application.GenerationNumber);
            generations.Add(Watch(application));
            reloads++;
        }
        await stop.CancelAsync();
        await Task.WhenAll(clients);
        var drain = WebLingoDrain.Of(application);
        for (var waited = 0; drain.Running > 0 && waited < 30_000; waited += 10) await Task.Delay(10);
        Assert.Equal(0, drain.Running);

        Assert.True(reloads >= 21 && refused >= 3, $"{reloads} reloads, {refused} refused");
        Assert.True(answers[0] > 0, "no page was served");
        Assert.True(answers[1] > 0, "no request was drained");
        int heaps;
        lock (_heaps) heaps = _heaps.Count;
        Assert.True(heaps > 0);
        var (generationsHeld, heapsHeld) = Held(generations);
        // The generation served now, and nothing else.
        Assert.Equal((1, 0), (generationsHeld, heapsHeld));
        Assert.True(generations[^1].IsAlive);
        GC.KeepAlive(application);
    }

    /// <summary>How many of the generations, and of the requests' heaps, are still held, once every collection has run.</summary>
    private (int Generations, int Heaps) Held(List<WeakReference> generations)
    {
        for (var i = 0; i < 10; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            int heaps;
            lock (_heaps) heaps = _heaps.Count(heap => heap.IsAlive);
            var held = generations.Count(generation => generation.IsAlive);
            if (held <= 1 && heaps == 0) return (held, heaps);
            Thread.Sleep(50);
        }
        lock (_heaps) return (generations.Count(generation => generation.IsAlive), _heaps.Count(heap => heap.IsAlive));
    }

    /// <summary>A weak reference to the generation served now, made in a method of its own, so that no local of the test's holds it.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Watch(WebLingoApplication application) => new(application.CurrentGeneration);

    /// <summary>
    /// A client asking for each page in turn until <paramref name="stop"/>, each answer checked: counted in
    /// <paramref name="answers"/>' first, or, a 503 a reload's drain gave, its second.
    /// </summary>
    private async Task Ask(WebLingoApplication application, int client, CancellationToken stop, int[] answers)
    {
        for (var turn = client; !stop.IsCancellationRequested; turn++)
        {
            var path = Pages[turn % Pages.Length];
            var (status, body) = await Get(application, path);
            if (status == 503 && body.Contains("The site was reloaded", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref answers[1]);
                continue;
            }
            Assert.True(path == "/broken" ? status is 200 or 500 : status == 200, $"{path}: {status} {body}");
            Interlocked.Increment(ref answers[0]);
        }
    }

    /// <summary>
    /// The pages a reload changes: the front page's text, under the layout; and the page that does not compile in every
    /// third version, compiling in the others.
    /// </summary>
    private void Version(int version)
    {
        Write("index.wasp", $"<% layout \"/layouts/main.wasp\", {{ title: \"Home\" }}; remember(); %><p>version {version}</p>");
        Write("broken.wasp", version % 3 == 2 ? "<%= [1][2 %>" : $"<%= {version} %>");
    }

    private WebLingoApplication Load()
    {
        var application = WebLingoApplication.Load(new WebLingoApplicationOptions(_root, new DotNetFileSystem())
        {
            Workers = _workers.Count,
            WorkerServices = _ => new WebLingoWorkerServices(new WebLingoBuiltinRegistry(new WebLingoDelayFunction(), new Remember(_heaps))),
            Clock = new WebLingoExecutionClock(new DotNetClock()),
            Scheduler = () => new DotNetSchedulerHost(),
            Crypto = new DotNetCrypto(),
            Sessions = new WebLingoMemorySessions(TimeSpan.FromMinutes(20), TimeSpan.FromSeconds(10)),
            Mode = WebLingoMode.Development,
            Drain = TimeSpan.FromMilliseconds(200)
        }, out var error);
        Assert.True(application is not null, error);
        return application;
    }

    private async Task<(int Status, string Body)> Get(WebLingoApplication application, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Request.Host = new HostString("localhost");
        context.Response.Body = new MemoryStream();
        await WebLingoAspNetCore.Serve(context, application, _workers);
        return (context.Response.StatusCode, Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
    }

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary><c>remember()</c>: a weak reference to the heap of the request that calls it.</summary>
    private sealed class Remember(List<WeakReference> heaps) : IWebLingoBuiltinFunction
    {
        public string Name => "remember";
        public int Arity => 0;

        public bool TryInvoke(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
        {
            lock (heaps) heaps.Add(new WeakReference(heap));
            (result, error) = (WebLingoValue.Null, null);
            return true;
        }
    }
}
