using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM.Stack;
using Xunit;
namespace WebLingo.VM.Tests;

/// <summary>
/// Dates and times (specification, section 3.2, "Dates and times"), on both VMs: a kind of value of its own, an instant to
/// the millisecond with its offset from UTC, made by <c>DateTime</c>'s methods, read by its members, moved by its
/// <c>add…</c> methods, compared by their instants, formatted and parsed by .NET's formats, and written as
/// <c>DateTime:</c> and ISO 8601. The clock and the server's zone are the host's, here a fixed clock and a zone an hour
/// ahead of UTC, two from April to September; and the zones found through it (section 3.2, "Time zones"), here that zone,
/// <c>Test/Summer</c>, and one five and a half hours ahead, <c>Test/Kolkata</c>.
/// </summary>
public sealed class DateTimeTests
{
    // 2026-11-16T14:30:15.250Z.
    private const long Now = 1_794_839_415_250;

    [Theory]
    [InlineData("writeLn(DateTime.utc(2026, 11, 16, 14, 30));", "DateTime:2026-11-16T14:30:00Z\n")]
    [InlineData("writeLn(DateTime.utc(2026, 11, 16));", "DateTime:2026-11-16T00:00:00Z\n")]
    [InlineData("writeLn(DateTime.utcNow());", "DateTime:2026-11-16T14:30:15.250Z\n")]
    [InlineData("writeLn(DateTime.now());", "DateTime:2026-11-16T15:30:15.250+01:00\n")]
    [InlineData("writeLn(DateTime.now() == DateTime.utcNow());", "true\n")]
    [InlineData("writeLn(DateTime.local(2026, 11, 16, 9));", "DateTime:2026-11-16T09:00:00+01:00\n")]
    [InlineData("writeLn(DateTime.local(2026, 7, 1, 9));", "DateTime:2026-07-01T09:00:00+02:00\n")]
    [InlineData("writeLn(DateTime.fromUnixMilliseconds(0));", "DateTime:1970-01-01T00:00:00Z\n")]
    [InlineData("var d = DateTime.utc(2026, 11, 16, 14, 30, 15, 250); "
                + "writeLn([d.year, d.month, d.day, d.hour, d.minute, d.second, d.millisecond, d.dayOfWeek, d.dayOfYear, d.offset]);",
        "[2026, 11, 16, 14, 30, 15, 250, 1, 320, 0]\n")]
    [InlineData("writeLn(DateTime.utc(1970, 1, 1, 0, 0, 1).unixMilliseconds);", "1000\n")]
    [InlineData("var d = DateTime.utc(2026, 11, 16, 14, 30); writeLn([d.addDays(1.5), d.addHours(-15), d.addMinutes(30), d.addSeconds(0.25)]);",
        "[DateTime:2026-11-18T02:30:00Z, DateTime:2026-11-15T23:30:00Z, DateTime:2026-11-16T15:00:00Z, DateTime:2026-11-16T14:30:00.250Z]\n")]
    [InlineData("writeLn([DateTime.utc(2026, 1, 31).addMonths(1), DateTime.utc(2024, 2, 29).addYears(1), DateTime.utc(2026, 1, 1).addMilliseconds(1)]);",
        "[DateTime:2026-02-28T00:00:00Z, DateTime:2025-02-28T00:00:00Z, DateTime:2026-01-01T00:00:00.001Z]\n")]
    [InlineData("var d = DateTime.local(2026, 11, 16, 9); writeLn([d.toUtc(), d.toUtc().toLocal(), d.addMonths(-6), d.addMonths(-6).toLocal()]);",
        "[DateTime:2026-11-16T08:00:00Z, DateTime:2026-11-16T09:00:00+01:00, DateTime:2026-05-16T09:00:00+01:00, "
        + "DateTime:2026-05-16T10:00:00+02:00]\n")]
    [InlineData("var a = DateTime.utc(2026, 11, 16, 14); var b = DateTime.parse(\"2026-11-16T15:00:00+01:00\"); "
                + "writeLn([a == b, a != b, a < b, a <= b, b - a, a.addHours(1) - a, a > a.addDays(-1)]);",
        "[true, false, false, true, 0, 3600000, true]\n")]
    [InlineData("var d = DateTime.utc(2026, 11, 16, 14, 30); writeLn(d.format(\"dddd, d MMMM yyyy HH:mm\"));", "Monday, 16 November 2026 14:30\n")]
    [InlineData("var d = DateTime.utc(2026, 11, 16, 14, 30); writeLn($\"{d:yyyy-MM-dd} at {d:HH:mm}, [{d,32}]\");",
        "2026-11-16 at 14:30, [   DateTime:2026-11-16T14:30:00Z]\n")]
    [InlineData("writeLn(DateTime.local(2026, 11, 16, 9).format(\"o\"));", "2026-11-16T09:00:00.0000000+01:00\n")]
    [InlineData("writeLn([DateTime.parse(\"2026-11-16T14:30:00.5Z\"), DateTime.parse(\"2026-11-16T14:30-05:00\"), "
                + "DateTime.parse(\"2026-11-16\"), DateTime.parse(\"2026-07-01T12:00\")]);",
        "[DateTime:2026-11-16T14:30:00.500Z, DateTime:2026-11-16T14:30:00-05:00, DateTime:2026-11-16T00:00:00+01:00, "
        + "DateTime:2026-07-01T12:00:00+02:00]\n")]
    [InlineData("writeLn([DateTime.parse(\"16/11/2026 09:05\", \"dd/MM/yyyy HH:mm\"), DateTime.parse(\"16.11.2026 +03:00\", \"dd.MM.yyyy zzz\")]);",
        "[DateTime:2026-11-16T09:05:00+01:00, DateTime:2026-11-16T00:00:00+03:00]\n")]
    [InlineData("var d = DateTime.utcNow(); writeLn([d is DateTime, d is Object, \"x\" is DateTime, d.toString()]);",
        "[true, true, false, \"DateTime:2026-11-16T14:30:15.250Z\"]\n")]
    [InlineData("extend DateTime { isWeekend() { return this.dayOfWeek == 0 || this.dayOfWeek == 6; } } "
                + "writeLn([DateTime.utc(2026, 11, 15).isWeekend(), DateTime.utc(2026, 11, 16).isWeekend()]);", "[true, false]\n")]
    [InlineData("writeLn([DateTime.utc(1, 1, 1), DateTime.utc(9999, 12, 31, 23, 59, 59, 999)]);",
        "[DateTime:0001-01-01T00:00:00Z, DateTime:9999-12-31T23:59:59.999Z]\n")]
    [InlineData("writeLn(DateTime.utc(2026, 11, 16, 14, 30).toZone(\"Test/Kolkata\"));", "DateTime:2026-11-16T20:00:00+05:30\n")]
    [InlineData("writeLn([DateTime.inZone(\"Test/Kolkata\", 2026, 11, 16, 9, 30), DateTime.inZone(\"Test/Summer\", 2026, 7, 1)]);",
        "[DateTime:2026-11-16T09:30:00+05:30, DateTime:2026-07-01T00:00:00+02:00]\n")]
    [InlineData("var d = DateTime.inZone(\"Test/Kolkata\", 2026, 11, 16); "
                + "writeLn([d.toUtc(), d == DateTime.utc(2026, 11, 15, 18, 30), d.toZone(\"Test/Summer\"), d.addDays(1).offset]);",
        "[DateTime:2026-11-15T18:30:00Z, true, DateTime:2026-11-15T19:30:00+01:00, 330]\n")]
    public void DatesBehaveAlikeOnBothVms(string program, string output)
    {
        foreach (var stack in new[] { false, true })
        {
            var (text, error) = Run(program, stack);
            Assert.True(error is null, error?.Message);
            Assert.Equal(output, text);
        }
    }

    [Theory]
    [InlineData("writeLn(new DateTime());", "WL3001", "'new' cannot make a value of the class DateTime.")]
    [InlineData("writeLn(DateTime.utc(2026, 2, 29));", "WL3001", "A date's day is from 1 to 28, not 29.")]
    [InlineData("writeLn(DateTime.utc(2026, 13, 1));", "WL3001", "A date's month is from 1 to 12, not 13.")]
    [InlineData("writeLn(DateTime.utc(2026, 1));", "WL2006", "'utc' takes 3 to 7 arguments, not 2.")]
    [InlineData("writeLn(DateTime.utc(2026, 1, 1.5));", "WL3001", "A date's day is an integer, not the number 1.5.")]
    [InlineData("writeLn(DateTime.utc(9999, 12, 31).addDays(1));", "WL3001", "A date and time is within the years 1 to 9999.")]
    [InlineData("writeLn(DateTime.utc(9999, 12, 31, 23).toLocal());", "WL3001", "A date and time is within the years 1 to 9999.")]
    [InlineData("writeLn(DateTime.utc(2026, 1, 1).addMonths(1.5));", "WL3001", "'addMonths' takes an integer, not the number 1.5.")]
    [InlineData("writeLn(DateTime.utc(2026, 1, 1).addDays(\"1\"));", "WL3001", "'addDays' takes a number, not a string.")]
    [InlineData("writeLn(DateTime.parse(\"16 November 2026\"));", "WL3001",
        "\"16 November 2026\" is not a date and time as ISO 8601 writes it, such as 2026-11-16T14:30:00Z.")]
    [InlineData("writeLn(DateTime.parse(\"2026\", \"dd/MM/yyyy\"));", "WL3001", "\"2026\" is not a date and time of the format 'dd/MM/yyyy'.")]
    [InlineData("writeLn(DateTime.utc(2026, 1, 1).format(\"q\"));", "WL3001", "'q' is not a format of a date and time.")]
    [InlineData("writeLn(DateTime.utc(2026, 1, 1) + 1);", "WL3001", "'+' needs two numbers or two strings, not the date and time 2026-01-01T00:00:00Z.")]
    [InlineData("writeLn(DateTime.utc(2026, 1, 1) < 1);", "WL3001", "'<' compares two numbers, two decimals, two strings or two dates, not the date and time 2026-01-01T00:00:00Z and the integer 1.")]
    [InlineData("var m = {}; m[DateTime.utc(2026, 1, 1)] = 1;", "WL3001",
        "The date and time 2026-01-01T00:00:00Z cannot be a key: keys are strings, integers, Booleans and null.")]
    [InlineData("writeLn(DateTime.utc(2026, 1, 1).year());", "WL2005", "'year' of the date and time 2026-01-01T00:00:00Z is not a method, and cannot be called.")]
    [InlineData("writeLn(DateTime.utc(2026, 1, 1).toUtc);", "WL3001",
        "'toUtc' is a method of the date and time 2026-01-01T00:00:00Z, and can only be called.")]
    [InlineData("writeLn(DateTime.utc(2026, 1, 1).toZone(\"Mars/Olympus\"));", "WL3001",
        "\"Mars/Olympus\" is not a time zone this server knows: a zone is named as IANA names it, such as Europe/Warsaw.")]
    [InlineData("writeLn(DateTime.utc(2026, 1, 1).toZone(\"../Test/Kolkata\"));", "WL3001",
        "\"../Test/Kolkata\" is not a time zone this server knows: a zone is named as IANA names it, such as Europe/Warsaw.")]
    [InlineData("writeLn(DateTime.utc(2026, 1, 1).toZone(1));", "WL3001", "'toZone' takes a time zone's name, a string, not the integer 1.")]
    [InlineData("writeLn(DateTime.utc(2026, 1, 1).toZone());", "WL2006", "'toZone' takes 1 argument, not 0.")]
    [InlineData("writeLn(DateTime.inZone(\"Test/Kolkata\", 2026, 1));", "WL2006", "'inZone' takes 4 to 8 arguments, not 3.")]
    [InlineData("writeLn(DateTime.inZone(\"Test/Kolkata\", 2026, 1, 32));", "WL3001", "A date's day is from 1 to 31, not 32.")]
    public void WrongUsesAreErrorsAlikeOnBothVms(string program, string code, string message)
    {
        foreach (var stack in new[] { false, true })
        {
            var (_, error) = Run(program, stack);
            Assert.NotNull(error);
            Assert.Equal((code, message), (error.Code, error.Message));
        }
    }

    [Fact]
    public void WithoutAHostsClockNowIsAnErrorAndTheServersZoneIsUtc()
    {
        foreach (var stack in new[] { false, true })
        {
            var (_, error) = Run("writeLn(DateTime.now());", stack, clock: false);
            Assert.NotNull(error);
            Assert.Equal(("WL3001", "'now' reads the host's clock, and this host gives none."), (error.Code, error.Message));
            Assert.Equal(("DateTime:2026-07-01T09:00:00Z\n", null), Run("writeLn(DateTime.local(2026, 7, 1, 9));", stack, clock: false));
            (_, error) = Run("writeLn(DateTime.inZone(\"Test/Kolkata\", 2026, 7, 1));", stack, clock: false);
            Assert.NotNull(error);
            Assert.Equal(("WL3001", "'inZone' reads the host's time zones, and this host gives none."), (error.Code, error.Message));
        }
    }

    private static (string Output, WebLingoDiagnostic? Error) Run(string program, bool stack, bool clock = true)
    {
        var options = new WebLingoCompilerOptions([new("write", 1), new("writeLn", 1)]);
        var output = new WebLingoCollectedOutput();
        var context = new WebLingoExecutionContext(new WebLingoWorkerId(0))
        {
            Services = new WebLingoBuiltinRegistry(WebLingoStandardBuiltins.Create(output)),
            WallClock = clock ? new FixedClock() : null,
            TimeZone = clock ? new SummerZone() : null
        };
        if (stack)
        {
            var stackCompiled = new WebLingoStackCompiler(options).Compile(program, "main.wl");
            Assert.Empty(stackCompiled.Diagnostics);
            var stackError = new WebLingoStackVirtualMachine().Execute(stackCompiled.Module!, context).Error;
            return (output.Text, stackError);
        }
        var compiled = new WebLingoCompiler(options).Compile(program, "main.wl");
        Assert.Empty(compiled.Diagnostics);
        // Run first: the output is read once the program has written it.
        var error = new WebLingoVirtualMachine().Execute(compiled.Module!, context).Error;
        return (output.Text, error);
    }

    /// <summary>A wall clock that always says <see cref="Now"/>.</summary>
    private sealed class FixedClock : IWebLingoClock
    {
        public long GetUnixTimeNanoseconds() => Now * 1_000_000;
        public long GetMonotonicTicks() => 0;
        public long MonotonicFrequency => 1;
    }

    /// <summary>
    /// A zone an hour ahead of UTC, and two from April to September, by the month of the time asked about, which finds
    /// itself as <c>Test/Summer</c> and <see cref="KolkataZone"/> as <c>Test/Kolkata</c>.
    /// </summary>
    private sealed class SummerZone : IWebLingoTimeZone
    {
        public int OffsetMinutesAt(long unixMilliseconds) => OffsetIn(unixMilliseconds);
        public int OffsetMinutesOfLocal(long localMilliseconds) => OffsetIn(localMilliseconds);

        public IWebLingoTimeZone? Find(string name) => name switch
        {
            "Test/Summer" => this,
            "Test/Kolkata" => new KolkataZone(),
            _ => null
        };

        private static int OffsetIn(long milliseconds) => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).Month is >= 4 and <= 9 ? 120 : 60;
    }

    /// <summary>A zone five and a half hours ahead of UTC all year, which finds no other.</summary>
    private sealed class KolkataZone : IWebLingoTimeZone
    {
        public int OffsetMinutesAt(long unixMilliseconds) => 330;
        public int OffsetMinutesOfLocal(long localMilliseconds) => 330;
        public IWebLingoTimeZone? Find(string name) => null;
    }
}
