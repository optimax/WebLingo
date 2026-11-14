using System.Diagnostics;
using WebLingo.Platform.Abstractions;
namespace WebLingo.Platform.DotNet;

public sealed class DotNetClock : IWebLingoClock
{
    public long GetUnixTimeNanoseconds() =>
        checked(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L);
    public long GetMonotonicTicks() => Stopwatch.GetTimestamp();
    public long MonotonicFrequency => Stopwatch.Frequency;
}
