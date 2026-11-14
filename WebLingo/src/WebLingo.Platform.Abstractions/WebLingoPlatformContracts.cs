namespace WebLingo.Platform.Abstractions;

public interface IWebLingoClock
{
    long GetUnixTimeNanoseconds();
    long GetMonotonicTicks();
    long MonotonicFrequency { get; }
}
public interface IWebLingoPlatformServices { IWebLingoClock Clock { get; } }
