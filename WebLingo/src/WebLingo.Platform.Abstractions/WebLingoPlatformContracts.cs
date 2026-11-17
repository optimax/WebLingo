namespace WebLingo.Platform.Abstractions;

public interface IWebLingoClock
{
    long GetUnixTimeNanoseconds();
    long GetMonotonicTicks();
    long MonotonicFrequency { get; }
}

/// <summary>
/// The server's own time zone (specification, section 7.6, WL-P006), a service apart from the clock: its offset from UTC,
/// in minutes, at an instant, and at a date and time on the server's own clocks, which a date made from its parts needs
/// (section 3.2, "Dates and times"). A time the zone skips, or has twice, as its clocks go forward or back, takes the
/// zone's standard offset.
/// </summary>
public interface IWebLingoTimeZone
{
    /// <summary>The offset at an instant, given in milliseconds since 1970-01-01T00:00:00Z.</summary>
    int OffsetMinutesAt(long unixMilliseconds);

    /// <summary>The offset of a date and time on the zone's clocks, given in milliseconds since 1970-01-01T00:00 on them.</summary>
    int OffsetMinutesOfLocal(long localMilliseconds);
}

public interface IWebLingoPlatformServices { IWebLingoClock Clock { get; } }
