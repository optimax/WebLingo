using System.Diagnostics.Tracing;
using System.Globalization;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
namespace WebLingo.Benchmarks;

/// <summary>
/// A .NET process's <c>System.Runtime</c> counters, read from outside it as <c>dotnet-counters</c> reads them (specification,
/// section 9, "M0.22 parts", part 3; the owner's answer, 27 November 2026, A2 (a) of "M0.22, Part 3: The Soak with
/// Reloads"): an event session in the process, started through .NET's diagnostics client, Microsoft.Diagnostics.NETCore.Client,
/// its counters' events read as they come by Microsoft.Diagnostics.Tracing.TraceEvent, on a thread of its own; the GC
/// heap's size the last its counter, <c>gc-heap-size</c>, gave, in megabytes of a million bytes, as .NET gives it. Under
/// <c>wasp serve</c> and <c>wasp fastcgi</c>, where the benchmark started the server.
/// </summary>
public sealed class WebLingoServerCounters : IDisposable
{
    private readonly EventPipeSession _session;
    private readonly Thread _reading;
    private EventPipeEventSource? _source;
    private long _heap = -1;

    private WebLingoServerCounters(EventPipeSession session)
    {
        _session = session;
        _reading = new Thread(Read) { IsBackground = true, Name = "The server's counters" };
        _reading.Start();
    }

    /// <summary>
    /// The counters of the process <paramref name="processId"/>, each given every <paramref name="seconds"/>; null, with why,
    /// where its runtime cannot be reached.
    /// </summary>
    public static WebLingoServerCounters? TryAttach(int processId, int seconds, out string? why)
    {
        why = null;
        try
        {
            var provider = new EventPipeProvider("System.Runtime", EventLevel.Informational, 0,
                new Dictionary<string, string> { ["EventCounterIntervalSec"] = seconds.ToString(CultureInfo.InvariantCulture) });
            return new WebLingoServerCounters(new DiagnosticsClient(processId).StartEventPipeSession([provider], false, 16));
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            why = failure.Message;
            return null;
        }
    }

    /// <summary>The GC heap's size, in bytes, the last its counter gave; null before it has given one.</summary>
    public long? GcHeapBytes => Interlocked.Read(ref _heap) is var heap and >= 0 ? heap : null;

    private void Read()
    {
        try
        {
            _source = new EventPipeEventSource(_session.EventStream);
            _source.Dynamic.All += Counted;
            _source.Process(); // Until the session stops.
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // The server ended, or its stream with it: its counters are not read any more.
        }
    }

    /// <summary>A counter's event: its payload a map, whose <c>Payload</c> is the counter's name and its mean, as dotnet-counters reads it.</summary>
    private void Counted(TraceEvent counted)
    {
        if (counted.EventName != "EventCounters" || counted.PayloadValue(0) is not IDictionary<string, object> outer
            || !outer.TryGetValue("Payload", out var inner) || inner is not IDictionary<string, object> fields)
        {
            return;
        }
        if (fields.TryGetValue("Name", out var name) && name as string == "gc-heap-size" && fields.TryGetValue("Mean", out var mean))
        {
            Interlocked.Exchange(ref _heap, (long)(Convert.ToDouble(mean, CultureInfo.InvariantCulture) * 1_000_000));
        }
    }

    public void Dispose()
    {
        try
        {
            _session.Stop();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // The server has ended: its session with it.
        }
        _reading.Join(TimeSpan.FromSeconds(10));
        _source?.Dispose();
        _session.Dispose();
    }
}
