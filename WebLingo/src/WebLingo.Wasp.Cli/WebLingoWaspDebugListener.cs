using System.Net;
using System.Net.Sockets;
using WebLingo.Cli;
using WebLingo.Hosting;
using WebLingo.Hosting.DotNet;
using WebLingo.Platform.Abstractions;
using WebLingo.Platform.DotNet;
namespace WebLingo.Wasp.Cli;

/// <summary>
/// A running site's way in for a debugger (specification, section 5.7, "Debugging a site where it runs, parts", part 2):
/// where its <c>wasp.config</c> names one, <c>"debug": { "port": 9230, "token": … }</c>, and it runs in development, it
/// listens on that port of this machine's alone, 127.0.0.1, under <c>wasp serve</c>, <c>wasp fastcgi</c> and IIS alike, and
/// runs a debug session for each connection, one at a time, the site's own adapter, which answers nothing until the debugger
/// attaches with the token; attached, the site's requests are the debugger's threads, until the session ends. A site in
/// production lets none in, and says so in its log; a port another process holds, one of the site's own while IIS recycles
/// its pool or where <c>mod_fcgid</c> runs several, is said so too, and the site serves without it.
/// </summary>
internal sealed class WebLingoWaspDebugListener : IDisposable
{
    /// <summary>The fewest characters a token may have.</summary>
    public const int ShortestToken = 16;

    private readonly TcpListener _listener;
    private readonly WebLingoRunningSite _site;
    private readonly Thread _accepting;
    private readonly Lock _lock = new();
    private TcpClient? _session;
    private bool _stopped;

    private WebLingoWaspDebugListener(TcpListener listener, WebLingoRunningSite site)
    {
        (_listener, _site) = (listener, site);
        _accepting = new Thread(Accept) { IsBackground = true, Name = "WASP's way in for a debugger" };
        _accepting.Start();
    }

    /// <summary>
    /// Listening for a debugger of <paramref name="application"/>'s, served from <paramref name="root"/> and reloaded by
    /// <paramref name="reloader"/>, which attaches it, as <paramref name="settings"/> say; null, its log saying why, where it
    /// runs in production, its token cannot be read or is too short, or its port is taken.
    /// </summary>
    public static WebLingoWaspDebugListener? Start(WebLingoApplication application, WebLingoReloader reloader, string root,
        WebLingoDebugSettings settings, WebLingoMode mode)
    {
        var log = application.Log;
        if (mode != WebLingoMode.Development)
        {
            log?.Write(WebLingoLogLevel.Warn, null,
                "WASP lets no debugger in: wasp.config's \"debug\" is for development, and the site runs in production.");
            return null;
        }
        string token;
        try
        {
            token = new DotNetSecrets().Read(settings.Token);
        }
        catch (WebLingoSecretException failure)
        {
            log?.Write(WebLingoLogLevel.Error, null, $"WASP lets no debugger in: \"debug\"'s \"token\" cannot be read: {failure.Message}");
            return null;
        }
        if (token.Length < ShortestToken)
        {
            log?.Write(WebLingoLogLevel.Error, null,
                $"WASP lets no debugger in: \"debug\"'s \"token\" has {token.Length} characters, fewer than {ShortestToken}.");
            return null;
        }
        var listener = new TcpListener(IPAddress.Loopback, settings.Port);
        try
        {
            listener.Start();
        }
        catch (SocketException failure)
        {
            log?.Write(WebLingoLogLevel.Warn, null, $"WASP lets no debugger in on port {settings.Port}: {failure.Message} Another of the "
                                                    + "site's processes may let one in there.");
            listener.Stop();
            return null;
        }
        log?.Write(WebLingoLogLevel.Info, null, $"WASP lets a debugger in on 127.0.0.1:{settings.Port}.");
        var site = new WebLingoRunningSite(root, debugger =>
        {
            reloader.Attach(debugger);
            return new Detaching(reloader);
        }) { Token = token };
        return new WebLingoWaspDebugListener(listener, site);
    }

    /// <summary>Each connection a session of its own, one at a time: a second while one runs is closed at once.</summary>
    private void Accept()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = _listener.AcceptTcpClient();
            }
            catch (Exception failure) when (failure is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                return; // Stopped.
            }
            lock (_lock)
            {
                if (_stopped || _session is not null)
                {
                    client.Dispose();
                    continue;
                }
                _session = client;
            }
            new Thread(() => Serve(client)) { IsBackground = true, Name = "WASP's attached debugger" }.Start();
        }
    }

    private void Serve(TcpClient client)
    {
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                WebLingoDebugAdapter.Run(WebLingoWaspCommandLine.Pages, stream, stream, running: _site);
            }
        }
        catch (Exception failure) when (failure is IOException or SocketException or ObjectDisposedException)
        {
            // The debugger's connection closed under its session, which has detached it.
        }
        finally
        {
            lock (_lock) _session = null;
        }
    }

    /// <summary>Stops listening, and ends the session there is, which detaches its debugger.</summary>
    public void Dispose()
    {
        TcpClient? session;
        lock (_lock) (_stopped, session) = (true, _session);
        _listener.Stop();
        session?.Dispose();
        _accepting.Join(TimeSpan.FromSeconds(5));
    }

    /// <summary>What detaches the debugger, once its session ends: a new generation without it.</summary>
    private sealed class Detaching(WebLingoReloader reloader) : IDisposable
    {
        public void Dispose() => reloader.Attach(null);
    }
}
