using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using WebLingo.Hosting;
using WebLingo.Hosting.DotNet;
using WebLingo.Hosting.Standalone;
namespace WebLingo.Wasp.Cli;

/// <summary>
/// <c>wasp serve</c>'s servers (specification, section 9, "M0.23 parts", parts 2 and 6; the owner's answer, A2 (c) of "M0.23:
/// A Standalone HTTP Host"): WebLingo's own HTTP server, without ASP.NET Core, its default since part 5 proved it, a site
/// loaded as ASP.NET Core's loads it, reloaded as its files change, on the local machine only; and ASP.NET Core's,
/// <c>--host aspnetcore</c>, which IIS runs.
/// </summary>
public static class WebLingoWaspStandalone
{
    /// <summary>The hosts <c>wasp serve --host</c> names: ASP.NET Core's, and WebLingo's own, its default but under IIS.</summary>
    public static readonly string[] Hosts = ["aspnetcore", "standalone"];

    /// <summary>
    /// The server for the application at <paramref name="root"/> on <paramref name="host"/>, one of <see cref="Hosts"/>, on
    /// <paramref name="port"/> of the local machine, 0 for one the system chooses; null, with why, if the application cannot be
    /// loaded, or the standalone server is asked for under IIS, which runs ASP.NET Core's.
    /// </summary>
    public static IWebLingoWaspServer? Create(string host, string? root, int port, IReadOnlyDictionary<string, string> givenSchemes,
        out string? error, out WebLingoMode mode, TextWriter? log = null)
    {
        if (host != "standalone")
        {
            var server = WebLingoWaspServer.Create(root, port, givenSchemes, out error, out mode, log);
            return server is null ? null : new AspNetCoreServer(server);
        }
        mode = WebLingoMode.Development;
        if (WebLingoWaspServer.UnderIis)
        {
            error = "Under IIS, wasp serve runs on ASP.NET Core's server: --host standalone serves a site on its own.";
            return null;
        }
        var full = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), root ?? "."));
        var application = WebLingoWaspServer.Load(full, givenSchemes, WebLingoMode.Development, log, out var workers, out mode, out error);
        return application is null ? null : new StandaloneServer(application, full, port, workers);
    }

    /// <summary>ASP.NET Core's server, its address the first it gives.</summary>
    private sealed class AspNetCoreServer(WebApplication server) : IWebLingoWaspServer
    {
        public string Address => server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault() ?? "";

        public Task StartAsync() => server.StartAsync();

        public Task StopAsync() => server.StopAsync();

        public ValueTask DisposeAsync() => server.DisposeAsync();
    }

    /// <summary>
    /// WebLingo's own server: listening on the local machine's IPv4 address, and its IPv6 one, as <c>localhost</c> is either,
    /// on the port given, or on 127.0.0.1 alone where the system chooses the port; its workers and its reloads its own.
    /// </summary>
    private sealed class StandaloneServer(WebLingoApplication application, string root, int port, int workers) : IWebLingoWaspServer
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly List<Socket> _listeners = [];
        private readonly List<Task> _serving = [];
        private WebLingoWorkerPool? _pool;
        private WebLingoReloader? _reloader;

        public string Address { get; private set; } = "";

        public Task StartAsync()
        {
            _pool = new WebLingoWorkerPool(workers);
            _reloader = WebLingoWaspServer.Reloader(application, root);
            var server = new WebLingoHttpServer(application, _pool);
            var first = WebLingoHttpServer.Listen(IPAddress.Loopback, port);
            _listeners.Add(first);
            var chosen = ((IPEndPoint)first.LocalEndPoint!).Port;
            Address = $"http://127.0.0.1:{chosen}";
            if (port != 0 && Socket.OSSupportsIPv6)
            {
                try
                {
                    _listeners.Add(WebLingoHttpServer.Listen(IPAddress.IPv6Loopback, chosen));
                }
                catch (SocketException)
                {
                    // No IPv6 here: IPv4's is enough.
                }
            }
            foreach (var listener in _listeners) _serving.Add(server.ServeAsync(listener, _stop.Token));
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            await _stop.CancelAsync();
            foreach (var listener in _listeners) listener.Dispose();
            await Task.WhenAll(_serving);
            _reloader?.Dispose();
            _pool?.Dispose();
            (application.Log as IDisposable)?.Dispose(); // Its log's file closed.
        }

        public async ValueTask DisposeAsync()
        {
            if (!_stop.IsCancellationRequested) await StopAsync();
            _stop.Dispose();
        }
    }
}
