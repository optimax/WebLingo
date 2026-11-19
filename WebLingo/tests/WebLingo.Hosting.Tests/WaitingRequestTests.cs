using System.Text;
using WebLingo.Hosting.Abstractions;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// A request that waits without its worker's thread (specification, section 6.6; section 9, "M0.14.7 parts", part 2): given
/// its own scheduler host, a page or a handler that waits for a timer gives <see cref="WebLingoRequestOutcomeKind.Waiting"/>,
/// and its <see cref="WebLingoRequestOutcome.Resume"/> goes on with it.
/// </summary>
public sealed class WaitingRequestTests
{
    [Fact]
    public void APageAndAHandlerThatWaitAreResumed()
    {
        var clock = new ManualClock();
        var app = WebLingoApplication.Load(new WebLingoApplicationOptions("/site", new MemoryFileSystem()
            .Add("/site/index.wasp", "<% import { delay } from \"std:system\"; await delay(50); %>done")
            .Add("/site/api/later.wlg", "import { write, writeLn, delay } from \"std:system\"; export async function get(request, response) { await delay(20); write(\"later\"); }"))
        {
            WorkerServices = _ => new WebLingoWorkerServices(
                new WebLingoBuiltinRegistry(new WebLingoWriteFunction(), new WebLingoWriteLnFunction(), new WebLingoDelayFunction())),
            Clock = clock
        }, out var error);
        Assert.True(app is not null, error);

        foreach (var (path, wait, kind, body) in new[] { ("/", 50L, WebLingoRequestOutcomeKind.Page, "done"),
                     ("/api/later", 20L, WebLingoRequestOutcomeKind.Handler, "later") })
        {
            var response = new WebLingoBufferedResponse();
            var outcome = app.Handle(new WebLingoBufferedRequest("GET", path), response, new WebLingoWorkerId(0), scheduler: new NoWork());
            Assert.Equal((WebLingoRequestOutcomeKind.Waiting, wait), (outcome.Kind, outcome.WaitMilliseconds));
            Assert.Equal(WebLingoResponseState.Uncommitted, response.State);
            clock.Milliseconds += wait;
            outcome = outcome.Resume!();
            Assert.Equal((kind, body), (outcome.Kind, Encoding.UTF8.GetString(response.BodyBytes)));
            Assert.Equal(0, clock.Waits);
        }
    }

    private sealed class ManualClock : IWebLingoExecutionClock
    {
        public long Milliseconds { get; set; }
        public int Waits { get; private set; }
        public void WaitUntil(long milliseconds) => Waits++;
    }

    /// <summary>A scheduler host that is never posted work.</summary>
    private sealed class NoWork : IWebLingoSchedulerHost
    {
        public void RunBlocking(Action work) => throw new InvalidOperationException();
        public void Post(Action work) => throw new InvalidOperationException();
        public bool RunPosted(int milliseconds) => false;
    }
}
