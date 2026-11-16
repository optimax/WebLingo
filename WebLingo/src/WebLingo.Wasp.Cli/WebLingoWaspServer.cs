using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebLingo.Cli;
using WebLingo.Hosting;
using WebLingo.Hosting.AspNetCore;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
namespace WebLingo.Wasp.Cli;

/// <summary>
/// <c>wasp serve</c> (specification, sections 6.12 and 8.4; section 9, "M0.14 parts", part 5): an application served by the
/// ASP.NET Core adapter, locally on Kestrel, or under IIS, whose ASP.NET Core Module starts it, named in the site's
/// <c>web.config</c>. Its root's <c>weblingo.config</c> gives its schemes and its environment, development locally and
/// production under IIS unless it names one. Each worker loads <c>std:system</c> and the native modules on its own.
/// </summary>
public static class WebLingoWaspServer
{
    /// <summary>The port <c>wasp serve</c> listens on locally unless given one.</summary>
    public const int DefaultPort = 8080;

    /// <summary>Whether the process runs under IIS's ASP.NET Core Module, which gives it its port and its site's folder.</summary>
    public static bool UnderIis =>
        Environment.GetEnvironmentVariable("ASPNETCORE_PORT") is not null || Environment.GetEnvironmentVariable("ASPNETCORE_IIS_PHYSICAL_PATH") is not null;

    /// <summary>
    /// The server for the application at <paramref name="root"/>, a folder from the current one, or, under IIS, from the
    /// site's; on <paramref name="port"/> (0 for one the system chooses) on the local machine only, unless under IIS; null,
    /// with why, if the application cannot be loaded.
    /// </summary>
    public static WebApplication? Create(string? root, int port, IReadOnlyDictionary<string, string> givenSchemes, out string? error,
        out WebLingoEnvironment environment)
    {
        environment = UnderIis ? WebLingoEnvironment.Production : WebLingoEnvironment.Development;
        var site = Environment.GetEnvironmentVariable("ASPNETCORE_IIS_PHYSICAL_PATH") ?? Directory.GetCurrentDirectory();
        var full = Path.GetFullPath(Path.Combine(site, root ?? "."));
        if (!WebLingoSchemes.TryLoadFolder(full, givenSchemes, out var schemes, out var configured, out error)) return null;
        if (configured is not null) environment = configured == "production" ? WebLingoEnvironment.Production : WebLingoEnvironment.Development;

        // std:system, loaded once here to fail early, then by each worker on its own.
        var importer = Path.Combine(full, "index.wasp");
        if (!new WebLingoNativeModuleLoader(schemes, new WebLingoBuiltinRegistry()).TryResolve(importer, "std:system", out _, out var why))
        {
            error = $"std:system cannot be loaded from {schemes["std"]}: {why}.";
            return null;
        }
        var workers = Math.Max(1, Environment.ProcessorCount);
        var application = WebLingoApplication.Load(new WebLingoApplicationOptions(full, new DotNetFileSystem())
        {
            Workers = workers,
            WorkerServices = _ => Services(schemes, importer),
            Environment = environment,
            Clock = new WebLingoExecutionClock(new DotNetClock()),
            Scheduler = () => new DotNetSchedulerHost()
        }, out error);
        if (application is null)
        {
            error = $"{full}: {error}.";
            return null;
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        // Kestrel chooses a port only for an address: 127.0.0.1 for port 0, localhost, IPv4's and IPv6's, otherwise.
        if (!UnderIis) builder.WebHost.UseUrls(port == 0 ? "http://127.0.0.1:0" : $"http://localhost:{port}");
        var server = builder.Build();
        var pool = new WebLingoWorkerPool(workers);
        server.Lifetime.ApplicationStopped.Register(pool.Dispose);
        server.UseWebLingo(application, full, pool);
        return server;
    }

    /// <summary>A worker's built-ins and native modules, its own: std:system's functions, and a loader of the others.</summary>
    private static WebLingoWorkerServices Services(IReadOnlyDictionary<string, string> schemes, string importer)
    {
        var builtins = new WebLingoBuiltinRegistry();
        var native = new WebLingoNativeModuleLoader(schemes, builtins);
        native.TryResolve(importer, "std:system", out var system, out _);
        foreach (var function in native.FunctionsOf(system!)) builtins.Register(function);
        return new WebLingoWorkerServices(builtins, native);
    }
}
