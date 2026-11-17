using WebLingo.Platform.Abstractions;
namespace WebLingo.Platform.DotNet;

/// <summary>The server's own time zone, as .NET's <see cref="TimeZoneInfo.Local"/> has it (specification, section 7.6).</summary>
public sealed class DotNetTimeZone : IWebLingoTimeZone
{
    public int OffsetMinutesAt(long unixMilliseconds) =>
        (int)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds)).TotalMinutes;

    public int OffsetMinutesOfLocal(long localMilliseconds)
    {
        // A time skipped, or had twice, takes the standard offset, as .NET's GetUtcOffset gives it for an unspecified time.
        var local = DateTime.SpecifyKind(DateTime.UnixEpoch.AddMilliseconds(localMilliseconds), DateTimeKind.Unspecified);
        return (int)TimeZoneInfo.Local.GetUtcOffset(local).TotalMinutes;
    }
}
