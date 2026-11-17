using WebLingo.Platform.Abstractions;
namespace WebLingo.Platform.DotNet;

/// <summary>
/// The server's own time zone, as .NET's <see cref="TimeZoneInfo.Local"/> has it, and the platform's other zones, found
/// by their names, Windows' and ICU's through .NET (specification, sections 3.2, "Time zones", and 7.6).
/// </summary>
public sealed class DotNetTimeZone : IWebLingoTimeZone
{
    // Null for the server's own, read as .NET has it when asked, so that a change of the server's zone is seen.
    private readonly TimeZoneInfo? _zone;

    public DotNetTimeZone()
    {
    }

    private DotNetTimeZone(TimeZoneInfo zone) => _zone = zone;

    private TimeZoneInfo Zone => _zone ?? TimeZoneInfo.Local;

    public int OffsetMinutesAt(long unixMilliseconds) =>
        (int)Zone.GetUtcOffset(DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds)).TotalMinutes;

    public int OffsetMinutesOfLocal(long localMilliseconds)
    {
        // A time skipped, or had twice, takes the standard offset, as .NET's GetUtcOffset gives it for an unspecified time.
        var local = DateTime.SpecifyKind(DateTime.UnixEpoch.AddMilliseconds(localMilliseconds), DateTimeKind.Unspecified);
        return (int)Zone.GetUtcOffset(local).TotalMinutes;
    }

    public IWebLingoTimeZone? Find(string name) =>
        name.Length > 0 && TimeZoneInfo.TryFindSystemTimeZoneById(name, out var zone) ? new DotNetTimeZone(zone) : null;
}
