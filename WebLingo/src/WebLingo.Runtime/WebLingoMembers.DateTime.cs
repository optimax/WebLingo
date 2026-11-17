using System.Globalization;
using WebLingo.Core;
namespace WebLingo.Runtime;

// Dates and times (specification, section 3.2, "Dates and times"; the owner's answers, 16 November 2026): a kind of value
// of its own, immutable, its class DateTime: an instant, to the millisecond, with its offset from UTC, as .NET's
// DateTimeOffset has them, in the years 1 to 9999, local time included. The class's DateTime.now(), the server's time with
// the server's offset, DateTime.utcNow(), DateTime.local(year, month, day, …) and DateTime.utc(year, month, day, …),
// DateTime.parse(text, format) and DateTime.fromUnixMilliseconds(ms); a date's year, month, day, hour, minute, second,
// millisecond, dayOfWeek, dayOfYear, offset and unixMilliseconds; its addYears(n) to addMilliseconds(n), toUtc(),
// toLocal() and format(format). The server's zone is the host's (IWebLingoTimeZone), UTC without one; the time is the
// host's wall clock's. Formats are .NET's date and time format strings, with the invariant culture.
public static partial class WebLingoMembers
{
    /// <summary>The first instant a date may have, 0001-01-01T00:00:00Z, in milliseconds since 1970-01-01T00:00:00Z.</summary>
    private const long FirstUnixMilliseconds = -62_135_596_800_000;

    /// <summary>The last instant a date may have, 9999-12-31T23:59:59.999Z.</summary>
    private const long LastUnixMilliseconds = 253_402_300_799_999;

    /// <summary>The largest offset from UTC, 14 hours, as .NET's DateTimeOffset allows.</summary>
    private const int LargestOffsetMinutes = 14 * 60;

    private const long MillisecondsPerMinute = 60_000;

    // The ISO 8601 forms DateTime.parse(text) reads: with a zone, Z or an offset, and without one, the server's local time.
    private static readonly string[] IsoWithZone =
        ["yyyy-MM-dd'T'HH:mm:ss.FFF'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFzzz", "yyyy-MM-dd'T'HH:mm'Z'", "yyyy-MM-dd'T'HH:mmzzz"];
    private static readonly string[] IsoLocal = ["yyyy-MM-dd'T'HH:mm:ss.FFF", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd"];

    /// <summary>A date and time as .NET's <see cref="DateTimeOffset"/>, which its range keeps valid.</summary>
    internal static DateTimeOffset AsDateTimeOffset(WebLingoValue value) =>
        DateTimeOffset.FromUnixTimeMilliseconds(value.UnixMilliseconds).ToOffset(TimeSpan.FromMinutes(value.OffsetMinutes));

    /// <summary>A date and time's text, ISO 8601, its milliseconds only when there are any: <c>2026-11-16T14:30:00Z</c>.</summary>
    internal static string IsoText(WebLingoValue value)
    {
        var date = AsDateTimeOffset(value);
        var text = date.ToString(date.Millisecond == 0 ? "yyyy-MM-dd'T'HH:mm:ss" : "yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);
        return text + (value.OffsetMinutes == 0 ? "Z" : date.ToString("zzz", CultureInfo.InvariantCulture));
    }

    private static bool IsDateTimeProperty(WebLingoMember member) =>
        member is WebLingoMember.Year or WebLingoMember.Month or WebLingoMember.Day or WebLingoMember.Hour or WebLingoMember.Minute
            or WebLingoMember.Second or WebLingoMember.Millisecond or WebLingoMember.DayOfWeek or WebLingoMember.DayOfYear
            or WebLingoMember.Offset or WebLingoMember.UnixMilliseconds;

    private static bool IsDateTimeMethod(WebLingoMember member) =>
        member is WebLingoMember.AddYears or WebLingoMember.AddMonths or WebLingoMember.AddDays or WebLingoMember.AddHours
            or WebLingoMember.AddMinutes or WebLingoMember.AddSeconds or WebLingoMember.AddMilliseconds or WebLingoMember.ToUtc
            or WebLingoMember.ToLocal or WebLingoMember.FormatWith;

    private static bool IsDateTimeClassMethod(WebLingoMember member) =>
        member is WebLingoMember.Now or WebLingoMember.UtcNow or WebLingoMember.Local or WebLingoMember.Utc or WebLingoMember.Parse
            or WebLingoMember.FromUnixMilliseconds;

    /// <summary><c>d.year</c>, <c>d.month</c>, from 1, …, <c>d.dayOfWeek</c>, from 0 for Sunday, <c>d.offset</c>, in minutes.</summary>
    private static WebLingoValue GetDateTimeProperty(WebLingoValue target, WebLingoMember member)
    {
        var date = AsDateTimeOffset(target);
        return WebLingoValue.FromInt64(member switch
        {
            WebLingoMember.Year => date.Year,
            WebLingoMember.Month => date.Month,
            WebLingoMember.Day => date.Day,
            WebLingoMember.Hour => date.Hour,
            WebLingoMember.Minute => date.Minute,
            WebLingoMember.Second => date.Second,
            WebLingoMember.Millisecond => date.Millisecond,
            WebLingoMember.DayOfWeek => (int)date.DayOfWeek,
            WebLingoMember.DayOfYear => date.DayOfYear,
            WebLingoMember.Offset => target.OffsetMinutes,
            _ => target.UnixMilliseconds
        });
    }

    /// <summary><c>d.addYears(n)</c> to <c>d.addMilliseconds(n)</c>, <c>d.toUtc()</c>, <c>d.toLocal()</c> and <c>d.format(format)</c>.</summary>
    private static bool TryCallDateTime(WebLingoValue target, WebLingoMember member, string name, WebLingoValue[] values, int first, int count,
        WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var (instant, offset) = (target.UnixMilliseconds, target.OffsetMinutes);
        switch (member)
        {
            case WebLingoMember.ToUtc:
                if (count != 0) return WrongArgumentCount(name, 0, count, out error);
                return TryMakeDateTime(instant, 0, out result, out error);
            case WebLingoMember.ToLocal:
                if (count != 0) return WrongArgumentCount(name, 0, count, out error);
                return TryMakeDateTime(instant, heap.TimeZone?.OffsetMinutesAt(instant) ?? 0, out result, out error);
            case WebLingoMember.FormatWith:
                if (count != 1) return WrongArgumentCount(name, 1, count, out error);
                if (!TryFormatDateTime(target, values[first], heap, out var text, out error)) return false;
                return heap.TryAllocateString(text, out result, out error);
        }
        if (count != 1) return WrongArgumentCount(name, 1, count, out error);
        var amount = values[first];
        if (member is WebLingoMember.AddYears or WebLingoMember.AddMonths)
        {
            // Calendar months, the day kept, or the month's last where it has fewer; the offset kept, as .NET keeps it.
            if (amount.Kind != WebLingoValueKind.Int64)
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"'{name}' takes an integer, not {WebLingoValueFormatter.Describe(amount, heap)}.");
                return false;
            }
            var date = AsDateTimeOffset(target);
            var months = member == WebLingoMember.AddYears ? (decimal)amount.AsInt64 * 12 : amount.AsInt64;
            var month = date.Year * 12m + date.Month - 1 + months;
            if (month < 12 || month >= 10_000 * 12) return OutOfRange(out error);
            DateTimeOffset moved;
            try
            {
                moved = date.AddMonths((int)(month - (date.Year * 12m + date.Month - 1)));
            }
            catch (ArgumentOutOfRangeException)
            {
                return OutOfRange(out error); // Its local time within the years, its instant not.
            }
            return TryMakeDateTime(moved.ToUnixTimeMilliseconds(), offset, out result, out error);
        }
        var unit = member switch
        {
            WebLingoMember.AddDays => 86_400_000L,
            WebLingoMember.AddHours => 3_600_000L,
            WebLingoMember.AddMinutes => MillisecondsPerMinute,
            WebLingoMember.AddSeconds => 1_000L,
            _ => 1L
        };
        long delta;
        if (amount.Kind == WebLingoValueKind.Int64)
        {
            // Beyond the years 1 to 9999 either way, which no date can move by.
            if (Math.Abs((decimal)amount.AsInt64 * unit) > LastUnixMilliseconds - FirstUnixMilliseconds) return OutOfRange(out error);
            delta = amount.AsInt64 * unit;
        }
        else if (amount.Kind == WebLingoValueKind.Binary64)
        {
            // A number of units, rounded to the millisecond, half away from zero.
            var exact = amount.AsBinary64 * unit;
            if (double.IsNaN(exact) || Math.Abs(exact) > LastUnixMilliseconds - FirstUnixMilliseconds) return OutOfRange(out error);
            delta = (long)Math.Round(exact, MidpointRounding.AwayFromZero);
        }
        else
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"'{name}' takes a number, not {WebLingoValueFormatter.Describe(amount, heap)}.");
            return false;
        }
        return TryMakeDateTime(instant + delta, offset, out result, out error);
    }

    /// <summary>
    /// <c>DateTime.now()</c>, <c>DateTime.utcNow()</c>, <c>DateTime.local(year, month, day, hour, minute, second,
    /// millisecond)</c>, <c>DateTime.utc(…)</c>, <c>DateTime.parse(text, format)</c> and
    /// <c>DateTime.fromUnixMilliseconds(ms)</c>, the methods of the class <c>DateTime</c>.
    /// </summary>
    private static bool TryCallDateTimeClass(WebLingoMember member, string name, WebLingoValue[] values, int first, int count, WebLingoHeap heap,
        out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        switch (member)
        {
            case WebLingoMember.Now or WebLingoMember.UtcNow:
            {
                if (count != 0) return WrongArgumentCount(name, 0, count, out error);
                if (heap.WallClock is not { } clock)
                {
                    error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                        $"'{name}' reads the host's clock, and this host gives none.");
                    return false;
                }
                var instant = Math.Clamp(clock.GetUnixTimeNanoseconds() / 1_000_000, FirstUnixMilliseconds, LastUnixMilliseconds);
                var offset = member == WebLingoMember.Now ? heap.TimeZone?.OffsetMinutesAt(instant) ?? 0 : 0;
                return TryMakeDateTime(instant, offset, out result, out error);
            }
            case WebLingoMember.Local or WebLingoMember.Utc:
            {
                // Its parts, year, month and day, then hour, minute, second and millisecond, 0 when left out.
                if (count is < 3 or > 7) return WrongArgumentCount(name, 3, 7, count, out error);
                Span<long> parts = [0, 0, 0, 0, 0, 0, 0];
                ReadOnlySpan<string> names = ["year", "month", "day", "hour", "minute", "second", "millisecond"];
                for (var i = 0; i < count; i++)
                {
                    if (values[first + i].Kind != WebLingoValueKind.Int64)
                    {
                        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                            $"A date's {names[i]} is an integer, not {WebLingoValueFormatter.Describe(values[first + i], heap)}.");
                        return false;
                    }
                    parts[i] = values[first + i].AsInt64;
                }
                if (!TryWallClockMilliseconds(parts, names, out var wall, out error)) return false;
                var offset = member == WebLingoMember.Local ? heap.TimeZone?.OffsetMinutesOfLocal(wall) ?? 0 : 0;
                return TryMakeDateTime(wall - offset * MillisecondsPerMinute, offset, out result, out error);
            }
            case WebLingoMember.FromUnixMilliseconds:
                if (count != 1) return WrongArgumentCount(name, 1, count, out error);
                if (values[first].Kind != WebLingoValueKind.Int64)
                {
                    error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                        $"'{name}' takes an integer, not {WebLingoValueFormatter.Describe(values[first], heap)}.");
                    return false;
                }
                return TryMakeDateTime(values[first].AsInt64, 0, out result, out error);
            default:
            {
                // parse(text, format): ISO 8601 without a format, as a date's text gives it; else the format's.
                if (count is < 1 or > 2) return WrongArgumentCount(name, 1, 2, count, out error);
                var bad = values[first].Kind != WebLingoValueKind.String ? values[first]
                    : count == 2 && values[first + 1].Kind != WebLingoValueKind.String ? values[first + 1] : (WebLingoValue?)null;
                if (bad is { } notText)
                {
                    error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                        $"'parse' reads a string, with a format that is a string, not {WebLingoValueFormatter.Describe(notText, heap)}.");
                    return false;
                }
                var text = heap.GetString(values[first]);
                var format = count == 2 ? heap.GetString(values[first + 1]) : null;
                return TryParseDateTime(text, format, heap, out result, out error);
            }
        }
    }

    /// <summary>A date's text by a format of .NET's, standard or custom, with the invariant culture (section 3.2).</summary>
    internal static bool TryFormatDateTime(WebLingoValue date, WebLingoValue format, WebLingoHeap heap, out string text, out WebLingoRuntimeError? error)
    {
        text = "";
        if (format.Kind != WebLingoValueKind.String)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"A date's format is a string, not {WebLingoValueFormatter.Describe(format, heap)}.");
            return false;
        }
        return TryFormatDateTime(date, heap.GetString(format), out text, out error);
    }

    internal static bool TryFormatDateTime(WebLingoValue date, string format, out string text, out WebLingoRuntimeError? error)
    {
        error = null;
        try
        {
            text = AsDateTimeOffset(date).ToString(format, CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            text = "";
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, $"'{format}' is not a format of a date and time.");
            return false;
        }
    }

    /// <summary>
    /// A date read from text: by ISO 8601 without a format, with a zone, <c>Z</c> or an offset, or without one, the
    /// server's local time; by a format of .NET's otherwise, its zone its <c>z</c>, <c>zz</c>, <c>zzz</c> or <c>K</c>, the
    /// server's local time without one, and UTC when its <c>K</c> finds none.
    /// </summary>
    private static bool TryParseDateTime(string text, string? format, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        DateTimeOffset parsed;
        bool zoned;
        if (format is null)
        {
            zoned = DateTimeOffset.TryParseExact(text, IsoWithZone, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed);
            if (!zoned
                && !DateTimeOffset.TryParseExact(text, IsoLocal, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed))
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"\"{text}\" is not a date and time as ISO 8601 writes it, such as 2026-11-16T14:30:00Z.");
                return false;
            }
        }
        else
        {
            zoned = HasZone(format);
            try
            {
                if (!DateTimeOffset.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed))
                {
                    error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, $"\"{text}\" is not a date and time of the format '{format}'.");
                    return false;
                }
            }
            catch (FormatException)
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, $"'{format}' is not a format of a date and time.");
                return false;
            }
        }
        var instant = parsed.ToUnixTimeMilliseconds();
        if (zoned) return TryMakeDateTime(instant, (int)parsed.Offset.TotalMinutes, out result, out error);
        // Read as UTC, its time is the time on the server's clocks.
        var offset = heap.TimeZone?.OffsetMinutesOfLocal(instant) ?? 0;
        return TryMakeDateTime(instant - offset * MillisecondsPerMinute, offset, out result, out error);
    }

    /// <summary>Whether a format of .NET's reads a zone: a standard one that does, or a custom one with z or K outside quotes.</summary>
    private static bool HasZone(string format)
    {
        if (format.Length == 1) return format[0] is 'o' or 'O' or 'r' or 'R' or 'u';
        var quote = '\0';
        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
            }
            else if (c is '\'' or '"') quote = c;
            else if (c == '\\') i++;
            else if (c is 'z' or 'K') return true;
        }
        return false;
    }

    /// <summary>A date's parts as milliseconds since 1970-01-01T00:00 on the same clock; WL3001 for a part out of its range.</summary>
    private static bool TryWallClockMilliseconds(ReadOnlySpan<long> parts, ReadOnlySpan<string> names, out long milliseconds,
        out WebLingoRuntimeError? error)
    {
        milliseconds = 0;
        var (year, month) = (parts[0], parts[1]);
        long days = year is >= 1 and <= 9999 && month is >= 1 and <= 12 ? DateTime.DaysInMonth((int)year, (int)month) : 31;
        ReadOnlySpan<long> least = [1, 1, 1, 0, 0, 0, 0];
        ReadOnlySpan<long> most = [9999, 12, days, 23, 59, 59, 999];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i] >= least[i] && parts[i] <= most[i]) continue;
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"A date's {names[i]} is from {least[i].ToString(CultureInfo.InvariantCulture)} to {most[i].ToString(CultureInfo.InvariantCulture)}, "
                + $"not {parts[i].ToString(CultureInfo.InvariantCulture)}.");
            return false;
        }
        var date = new DateTimeOffset((int)year, (int)month, (int)parts[2], (int)parts[3], (int)parts[4], (int)parts[5], (int)parts[6], TimeSpan.Zero);
        milliseconds = date.ToUnixTimeMilliseconds();
        error = null;
        return true;
    }

    /// <summary>A date and time, if its instant and its local time are both within the years 1 to 9999; WL3001 otherwise.</summary>
    private static bool TryMakeDateTime(long instant, int offsetMinutes, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var local = instant + offsetMinutes * MillisecondsPerMinute;
        if (instant < FirstUnixMilliseconds || instant > LastUnixMilliseconds || local < FirstUnixMilliseconds || local > LastUnixMilliseconds
            || Math.Abs(offsetMinutes) > LargestOffsetMinutes)
        {
            return OutOfRange(out error);
        }
        result = WebLingoValue.FromDateTime(instant, offsetMinutes);
        error = null;
        return true;
    }

    private static bool OutOfRange(out WebLingoRuntimeError? error)
    {
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "A date and time is within the years 1 to 9999.");
        return false;
    }
}
