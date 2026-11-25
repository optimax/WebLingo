using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WebLingo.Cli;
using WebLingo.Hosting;
using WebLingo.Hosting.AspNetCore;
using WebLingo.Hosting.FastCgi;
namespace WebLingo.Wasp.Cli;

/// <summary>
/// <c>wasp fastcgi</c> (specification, sections 6.1 and 6.2; section 9, M0.19): the application at the root served to a web
/// server, nginx or Apache, through FastCGI (<see cref="WebLingoFastCgiServer"/>), in one of two ways (the owner's answer,
/// 17 November 2026): a process the server's service manager starts and keeps running, listening on a port of the local
/// machine, 9000 unless given another, or on a Unix socket's path, as PHP-FPM listens; or a process the web server starts
/// itself, on demand, its listening socket given as its standard input, or, on Windows, its named pipe, as Apache's
/// mod_fcgid starts PHP, which <c>--stdin</c> asks for, and which is found without it where standard input is such a
/// socket or pipe and neither <c>--port</c> nor <c>--socket</c> is given. Its environment is production unless the root's
/// <c>wasp.config</c> names another; it reloads the application when its files change, as <c>wasp serve</c> does.
/// </summary>
public static class WebLingoWaspFastCgi
{
    /// <summary>The port <c>wasp fastcgi</c> listens on unless given one, PHP-FPM's.</summary>
    public const int DefaultPort = 9000;

    public const string Usage = "Usage: wasp fastcgi [--root <folder>] [--port <port> | --socket <path> | --stdin] [--scheme <name>=<folder> …]";

    public static int Run(string[] args, TextWriter output, TextWriter error, CancellationToken stop = default)
    {
        string? root = null, socketPath = null;
        int? port = null;
        var stdin = false;
        var schemes = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var at = 1; at < args.Length; at++)
        {
            if (args[at] == "--stdin" && !stdin)
            {
                stdin = true;
                continue;
            }
            var value = at + 1 < args.Length ? args[at + 1] : null;
            var equals = value?.IndexOf('=', StringComparison.Ordinal) ?? -1;
            var given = (args[at], value) switch
            {
                ("--root", { } folder) when root is null => (root = folder) is not null,
                ("--port", { } number) when port is null => int.TryParse(number, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed is > 0 and < 65536 && (port = parsed) is not null,
                ("--socket", { Length: > 0 } path) when socketPath is null => (socketPath = path) is not null,
                ("--scheme", { } scheme) => equals > 0 && WebLingoSchemes.IsName(scheme[..equals]) && equals + 1 < scheme.Length
                                            && schemes.TryAdd(scheme[..equals], scheme[(equals + 1)..]),
                _ => false
            };
            if (given)
            {
                at++;
                continue;
            }
            error.WriteLine($"'{args[at]}{(value is null ? "" : " " + value)}' is not --root <folder>, --port <port>, --socket <path>, --stdin "
                            + "or --scheme <name>=<folder>, or is given twice.");
            output.WriteLine(Usage);
            return WebLingoCommandLine.UsageError;
        }
        if ((port is not null ? 1 : 0) + (socketPath is not null ? 1 : 0) + (stdin ? 1 : 0) > 1)
        {
            error.WriteLine("wasp fastcgi listens on one of --port, --socket and --stdin.");
            output.WriteLine(Usage);
            return WebLingoCommandLine.UsageError;
        }
        // Started by a web server, its socket or pipe on standard input, as mod_fcgid starts PHP, unless told where to listen.
        stdin |= port is null && socketPath is null && FromWebServer();
        // Its log the standard error, which a service manager's journal keeps, and a web server that starts it reads,
        // where wasp.config names no file (section 6.12, "Logging").
        var log = error;
        var full = Path.GetFullPath(root ?? ".");
        var application = WebLingoWaspServer.Load(full, schemes, WebLingoMode.Production, log, out var workers, out var environment, out var why);
        if (application is null)
        {
            error.WriteLine(why);
            return WebLingoCommandLine.FileUnreadable;
        }
        using var pool = new WebLingoWorkerPool(workers);
        using var siteLog = application.Log as IDisposable; // Its log's file closed as it ends.
        using var reloader = WebLingoWaspServer.Reloader(application, full);
        var server = new WebLingoFastCgiServer(application, full, pool, log.WriteLine);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(stop);
        ConsoleCancelEventHandler cancel = (_, e) =>
        {
            e.Cancel = true;
            stopping.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            if (stdin)
            {
                log.WriteLine($"WASP serving FastCGI on its standard input ({environment}).");
                log.Flush();
                if (OperatingSystem.IsWindows()) server.ServePipeAsync(new SafePipeHandle(StandardInput(), ownsHandle: false), stopping.Token).GetAwaiter().GetResult();
                else server.ServeAsync(new Socket(new SafeSocketHandle(0, ownsHandle: false)), stopping.Token).GetAwaiter().GetResult();
                return WebLingoCommandLine.Success;
            }
            using var listener = Listen(port ?? DefaultPort, socketPath, out var where);
            output.WriteLine($"WASP serving FastCGI on {where} ({environment}); Ctrl+C stops it.");
            output.Flush();
            server.ServeAsync(listener, stopping.Token).GetAwaiter().GetResult();
            return WebLingoCommandLine.Success;
        }
        catch (SocketException exception)
        {
            error.WriteLine($"wasp fastcgi cannot listen: {exception.Message}");
            return WebLingoCommandLine.FileUnreadable;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
            if (socketPath is not null) File.Delete(socketPath);
        }
    }

    /// <summary>A listening socket: on the local machine's <paramref name="port"/>, or on a Unix socket's path, made anew.</summary>
    public static Socket Listen(int port, string? socketPath, out string where)
    {
        Socket listener;
        if (socketPath is not null)
        {
            File.Delete(socketPath);
            listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            where = socketPath;
        }
        else
        {
            listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, port));
            where = "127.0.0.1:" + ((IPEndPoint)listener.LocalEndPoint!).Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        listener.Listen(512);
        return listener;
    }

    /// <summary>
    /// Whether a web server started the process, its standard input a listening socket, FastCGI's <c>FCGI_LISTENSOCK_FILENO</c>,
    /// or, on Windows, a named pipe's server end.
    /// </summary>
    private static bool FromWebServer()
    {
        if (OperatingSystem.IsWindows())
        {
            var input = StandardInput();
            return GetFileType(input) == 3 && GetNamedPipeInfo(input, out var flags, 0, 0, 0) && (flags & 1) != 0; // FILE_TYPE_PIPE, PIPE_SERVER_END
        }
        try
        {
            using var socket = new Socket(new SafeSocketHandle(0, ownsHandle: false));
            return socket.LocalEndPoint is not null;
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException or NotSupportedException or ObjectDisposedException)
        {
            return false;
        }
    }

    private static nint StandardInput() => GetStdHandle(-10);

    [DllImport("kernel32.dll")]
    private static extern nint GetStdHandle(int which);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(nint file);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeInfo(nint pipe, out uint flags, nint outBufferSize, nint inBufferSize, nint maxInstances);
}
