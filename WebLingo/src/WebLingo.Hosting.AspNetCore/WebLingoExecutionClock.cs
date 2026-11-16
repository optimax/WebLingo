using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.Hosting.AspNetCore;

/// <summary>
/// The clock an execution's timers (<c>delay</c>) wait on (specification, section 3.8): the platform's monotonic clock, in
/// milliseconds, the worker's thread sleeping while its execution has nothing else to run.
/// </summary>
public sealed class WebLingoExecutionClock(IWebLingoClock clock) : IWebLingoExecutionClock
{
    public long Milliseconds
    {
        get
        {
            var (ticks, frequency) = (clock.GetMonotonicTicks(), clock.MonotonicFrequency);
            return ticks / frequency * 1000 + ticks % frequency * 1000 / frequency;
        }
    }

    public void WaitUntil(long milliseconds)
    {
        for (var now = Milliseconds; now < milliseconds; now = Milliseconds) Thread.Sleep((int)Math.Min(milliseconds - now, int.MaxValue));
    }
}
