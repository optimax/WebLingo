using System.Net.Sockets;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// A test that needs a SQL Server, the default instance on the machine the tests run on, reached by TCP/IP on its port,
/// 1433, and skipped where none answers there: on Linux, where there is none, and on a Windows machine without one, or
/// with TCP/IP off, as SQL Server's installation leaves it (<c>Examples/README.md</c>, example3, says how to set one up).
/// <c>WebLingo.Templates.Tests</c> shares this file.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    /// <summary>The machine's own default instance, by TCP/IP: SqlClient's managed network layer speaks nothing else to it.</summary>
    public const string Server = "localhost";

    /// <summary>Whether a server answers on SQL Server's port on this machine.</summary>
    public static bool IsAvailable { get; } = Find();

    public SqlServerFactAttribute()
    {
        if (!IsAvailable) Skip = "No SQL Server answers on this machine's port 1433, by TCP/IP.";
    }

    /// <summary>
    /// A connection string, as <c>std:db</c> takes it, to a database on that server, as the user running the tests, its
    /// certificate, which its installation made for itself, trusted.
    /// </summary>
    public static string ConnectionString(string database) =>
        $"sqlserver:Server={Server};Database={database};Integrated Security=true;TrustServerCertificate=true";

    private static bool Find()
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync("127.0.0.1", 1433).Wait(TimeSpan.FromSeconds(1)) && client.Connected;
        }
        catch (Exception exception) when (exception is SocketException or AggregateException)
        {
            return false;
        }
    }
}
