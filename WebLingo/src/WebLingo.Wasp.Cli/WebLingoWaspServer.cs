using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebLingo.Bytecode;
using WebLingo.Cli;
using WebLingo.Data.DotNet;
using WebLingo.Hosting;
using WebLingo.Hosting.AspNetCore;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
namespace WebLingo.Wasp.Cli;

/// <summary>
/// <c>wasp serve</c> (specification, sections 6.12 and 8.4; section 9, "M0.14 parts", part 5): an application served by the
/// ASP.NET Core adapter, locally on Kestrel, or under IIS, whose ASP.NET Core Module starts it, named in the site's
/// <c>web.config</c>. Its root's <c>wasp.config</c> gives its schemes, its environment, development locally and
/// production under IIS unless it names one, and how long a page waits for a slow client, 30 seconds unless it gives
/// another (<c>"sendTimeout"</c>). Each worker loads <c>std:system</c> and the native modules on its own.
/// </summary>
public static class WebLingoWaspServer
{
    /// <summary>The port <c>wasp serve</c> listens on locally unless given one.</summary>
    public const int DefaultPort = 8080;

    /// <summary>Whether the process runs under IIS's ASP.NET Core Module, which gives it its port and its site's folder.</summary>
    public static bool UnderIis =>
        Environment.GetEnvironmentVariable("ASPNETCORE_PORT") is not null || Environment.GetEnvironmentVariable("ASPNETCORE_IIS_PHYSICAL_PATH") is not null;

    /// <summary>
    /// The site's folder, which an application's root is from: the ASP.NET Core Module's
    /// <c>ASPNETCORE_IIS_PHYSICAL_PATH</c>, <paramref name="physicalPath"/>, where it is a folder's full path; else
    /// <paramref name="currentFolder"/>, the folder the module starts <c>wasp</c> in, the site's, or, locally, the current
    /// one. Out of process, as a site's <c>web.config</c> runs <c>wasp</c>, the module gives the application's id there,
    /// <c>/LM/W3SVC/2/ROOT</c>, not its folder (found 18 November 2026, under IIS 10 with .NET 10's Hosting Bundle).
    /// </summary>
    public static string SiteFolder(string? physicalPath, string currentFolder) =>
        physicalPath is not null && Path.IsPathFullyQualified(physicalPath) && Directory.Exists(physicalPath)
            ? physicalPath
            : currentFolder;

    /// <summary>
    /// The server for the application at <paramref name="root"/>, a folder from the current one, or, under IIS, from the
    /// site's; on <paramref name="port"/> (0 for one the system chooses) on the local machine only, unless under IIS; null,
    /// with why, if the application cannot be loaded. It reloads the application when its files change (section 6.5),
    /// each reload told to <paramref name="log"/>, if given, as is a page that fails after its response was sent.
    /// </summary>
    public static WebApplication? Create(string? root, int port, IReadOnlyDictionary<string, string> givenSchemes, out string? error,
        out WebLingoMode mode, TextWriter? log = null)
    {
        var site = SiteFolder(Environment.GetEnvironmentVariable("ASPNETCORE_IIS_PHYSICAL_PATH"), Directory.GetCurrentDirectory());
        var full = Path.GetFullPath(Path.Combine(site, root ?? "."));
        var application = Load(full, givenSchemes, UnderIis ? WebLingoMode.Production : WebLingoMode.Development, log, out var workers,
            out mode, out error);
        if (application is null) return null;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        // Kestrel chooses a port only for an address: 127.0.0.1 for port 0, localhost, IPv4's and IPv6's, otherwise.
        if (!UnderIis) builder.WebHost.UseUrls(port == 0 ? "http://127.0.0.1:0" : $"http://localhost:{port}");
        var server = builder.Build();
        var pool = new WebLingoWorkerPool(workers);
        server.Lifetime.ApplicationStopped.Register(pool.Dispose);
        var reloader = Reloader(application, full, log);
        server.Lifetime.ApplicationStopping.Register(reloader.Dispose);
        server.UseWebLingo(application, full, pool);
        return server;
    }

    /// <summary>What watches an application's files, and reloads it, each reload told to <paramref name="log"/> (section 6.5).</summary>
    internal static WebLingoReloader Reloader(WebLingoApplication application, string full, TextWriter? log) =>
        new(application, full, (generation, why) => log?.WriteLine(why is null
            ? $"WASP reloaded the application: generation {generation}."
            : $"WASP cannot reload the application, and serves generation {generation} still: {why}."));

    /// <summary>
    /// The application at <paramref name="full"/>, as <c>wasp serve</c> and <c>wasp fastcgi</c> load it: its configuration,
    /// <c>wasp.config</c>, giving its schemes, its mode, <paramref name="defaultMode"/> unless it names one, how long a page
    /// waits for a slow client, the hosts, the folders and the COM objects its scripts may reach, what they may read of the
    /// machine's environment, and its databases; its workers, one for each processor; null, with why, if it cannot be loaded.
    /// </summary>
    public static WebLingoApplication? Load(string full, IReadOnlyDictionary<string, string> givenSchemes, WebLingoMode defaultMode,
        TextWriter? log, out int workers, out WebLingoMode mode, out string? error)
    {
        (workers, mode) = (Math.Max(1, Environment.ProcessorCount), defaultMode);
        if (!WebLingoSchemes.TryLoadFolder(full, WebLingoRouteTable.ConfigurationFile, givenSchemes, out var schemes, out var configured,
            out var sendTimeout, out var allowedHosts, out var databases, out var folders, out var allowed, out var env, out var mail,
            out error)) return null;
        if (configured is not null) mode = configured == "production" ? WebLingoMode.Production : WebLingoMode.Development;

        // std:system, loaded once here to fail early, then by each worker on its own.
        var importer = Path.Combine(full, "index.wasp");
        if (!new WebLingoNativeModuleLoader(schemes, new WebLingoBuiltinRegistry()).TryResolve(importer, "std:system", out _, out var why))
        {
            error = $"std:system cannot be loaded from {schemes["std"]}: {why}.";
            return null;
        }
        // Its scripts reach only the folders wasp.config allows, a relative path from the root (section 7.13).
        var files = new DotNetFileCapabilities(full, folders.Read, folders.Write);
        var application = WebLingoApplication.Load(new WebLingoApplicationOptions(full, new DotNetFileSystem())
        {
            ScriptFileSystem = new DotNetFileSystem(files),
            Allowed = allowed, // The COM objects wasp.config allows (section 3.10).
            Crypto = new DotNetCrypto(),
            // Only what wasp.config's "env" allows of the machine's environment, and no variable set (section 7.15).
            Environment = new DotNetEnvironment(env.MachineName, env.Variables),
            Workers = workers,
            WorkerServices = _ => Services(schemes, importer),
            Mode = mode,
            Clock = new WebLingoExecutionClock(new DotNetClock()),
            WallClock = new DotNetClock(),
            TimeZone = new DotNetTimeZone(),
            Network = new DotNetNetwork(allowedHosts), // Only the hosts wasp.config allows (section 7.9).
            // Those wasp.config names, their files from its folder (section 7.11); a script's own checked as its files are.
            Databases = new DotNetDatabases(databases, full, new DotNetSecrets()) { Files = files },
            // Those wasp.config names, their passwords read where it keeps them, outside the site (section 7.17).
            Mail = new DotNetMail(mail, new DotNetSecrets()),
            Scheduler = () => new DotNetSchedulerHost(),
            Log = log is null ? null : log.WriteLine,
            SendTimeout = sendTimeout is { } seconds ? TimeSpan.FromSeconds(seconds) : TimeSpan.FromSeconds(30)
        }, out error);
        if (application is null) error = $"{full}: {error}.";
        return application;
    }

    /// <summary>
    /// A worker's built-ins and native modules, its own: std:system's functions, and a loader of the others, but std:console,
    /// a site having no console (section 3.9).
    /// </summary>
    private static WebLingoWorkerServices Services(IReadOnlyDictionary<string, string> schemes, string importer)
    {
        var builtins = new WebLingoBuiltinRegistry();
        var native = new WebLingoNativeModuleLoader(schemes, builtins) { Refused = WebLingoNativeModuleLoader.WithoutConsole };
        native.TryResolve(importer, "std:system", out var system, out _);
        foreach (var function in native.FunctionsOf(system!)) builtins.Register(function);
        return new WebLingoWorkerServices(builtins, native);
    }
}
