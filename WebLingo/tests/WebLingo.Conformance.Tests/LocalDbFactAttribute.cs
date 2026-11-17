using Microsoft.Win32;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// A test that needs a SQL Server, SQL Server Express LocalDB's default instance, skipped where LocalDB is not installed:
/// on Linux, where there is none, and on a Windows machine without it (<c>Examples/README.md</c>, example3, says how to
/// install it). <c>WebLingo.Templates.Tests</c> shares this file.
/// </summary>
public sealed class LocalDbFactAttribute : FactAttribute
{
    /// <summary>LocalDB's default instance, which its installation makes.</summary>
    public const string Server = @"(localdb)\MSSQLLocalDB";

    /// <summary>Whether LocalDB is installed, as its installation records it.</summary>
    public static bool IsInstalled { get; } = Find();

    public LocalDbFactAttribute()
    {
        if (!IsInstalled) Skip = "SQL Server Express LocalDB is not installed.";
    }

    /// <summary>A connection string, as <c>std:db</c> takes it, to a database on LocalDB.</summary>
    public static string ConnectionString(string database) => $"sqlserver:Server={Server};Database={database};Integrated Security=true";

    private static bool Find()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var versions = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions");
        return versions is not null && versions.SubKeyCount > 0;
    }
}
