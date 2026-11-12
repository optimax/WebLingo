namespace WebLingo.Platform.Abstractions;

public interface IWsClock
{
    long GetUnixTimeNanoseconds();
    long GetMonotonicTicks();
    long MonotonicFrequency { get; }
}
public interface IWsPlatformServices { IWsClock Clock { get; } }
