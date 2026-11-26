using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebLingo.Bytecode;
using WebLingo.Cli;
using WebLingo.Data.DotNet;
using WebLingo.Hosting;
using WebLingo.Hosting.AspNetCore;
using WebLingo.Platform.Abstractions;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
using WebLingo.VM;
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
    /// each reload told to <paramref name="log"/>, if given, as is a page that fails after its response was sent. Under
    /// <paramref name="debugger"/>, each request is a thread of its, ASP.NET Core logs nothing, the debugger's protocol being
    /// on the standard output, and it stops within seconds, its requests no longer stopped (section 5.7).
    /// </summary>
    public static WebApplication? Create(string? root, int port, IReadOnlyDictionary<string, string> givenSchemes, out string? error,
        out WebLingoMode mode, TextWriter? log = null, IWebLingoDebuggerThreads? debugger = null)
    {
        var site = SiteFolder(Environment.GetEnvironmentVariable("ASPNETCORE_IIS_PHYSICAL_PATH"), Directory.GetCurrentDirectory());
        var full = Path.GetFullPath(Path.Combine(site, root ?? "."));
        var application = Load(full, givenSchemes, UnderIis ? WebLingoMode.Production : WebLingoMode.Development, log, out var workers,
            out mode, out error, debugger);
        if (application is null) return null;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        if (debugger is not null)
        {
            builder.Logging.ClearProviders();
            builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(3));
        }
        // Kestrel chooses a port only for an address: 127.0.0.1 for port 0, localhost, IPv4's and IPv6's, otherwise.
        if (!UnderIis) builder.WebHost.UseUrls(port == 0 ? "http://127.0.0.1:0" : $"http://localhost:{port}");
        var server = builder.Build();
        var pool = new WebLingoWorkerPool(workers);
        server.Lifetime.ApplicationStopped.Register(pool.Dispose);
        server.Lifetime.ApplicationStopped.Register(() => (application.Log as IDisposable)?.Dispose()); // Its log's file closed.
        var reloader = Reloader(application, full);
        server.Lifetime.ApplicationStopping.Register(reloader.Dispose);
        server.UseWebLingo(application, pool);
        return server;
    }

    /// <summary>
    /// A site served for a debugger (specification, section 5.7, "Debugging WASP pages, parts", part 2), <c>wasp
    /// --debug-adapter</c>'s: the site at <paramref name="root"/> served as <c>wasp serve</c> serves it, on Kestrel, on
    /// <paramref name="port"/> of the local machine, <see cref="DefaultPort"/> where it is null, 0 for one the system chooses,
    /// its pages compiled for the debugger and each request a thread of <paramref name="debugger"/>'s, until
    /// <paramref name="stop"/> is signalled; where it serves said to <paramref name="output"/> first, its reloads after.
    /// </summary>
    public static int ServeForDebugger(string root, int? port, IWebLingoDebuggerThreads debugger, TextWriter output, TextWriter error,
        CancellationToken stop)
    {
        var server = Create(root, port ?? DefaultPort, new Dictionary<string, string>(), out var why, out var mode, output, debugger);
        if (server is null)
        {
            error.WriteLine(why);
            return WebLingoCommandLine.FileUnreadable;
        }
        try
        {
            server.Start();
        }
        catch (IOException failure)
        {
            error.WriteLine($"WASP cannot serve on port {port ?? DefaultPort}: {failure.Message}");
            return WebLingoCommandLine.FileUnreadable;
        }
        // The address it serves at, the system's port where it chose one.
        var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
                      ?? $"http://localhost:{port ?? DefaultPort}";
        output.WriteLine($"WASP serving {address}/ ({mode.ToString().ToLowerInvariant()}), under the debugger.");
        output.Flush();
        stop.WaitHandle.WaitOne();
        server.StopAsync().Wait(TimeSpan.FromSeconds(10));
        return WebLingoCommandLine.Success;
    }

    /// <summary>
    /// What watches an application's files, and reloads it (section 6.5), each reload told to its log (section 6.12,
    /// "Logging"), a reload at <c>info</c>, one that fails at <c>error</c>.
    /// </summary>
    internal static WebLingoReloader Reloader(WebLingoApplication application, string full) =>
        new(application, full, (generation, why) =>
        {
            if (why is null) application.Log?.Write(WebLingoLogLevel.Info, null, $"WASP reloaded the application: generation {generation}.");
            else application.Log?.Write(WebLingoLogLevel.Error, null, $"WASP cannot reload the application, and serves generation {generation} still: {why}.");
        });

    /// <summary>
    /// The application at <paramref name="full"/>, as <c>wasp serve</c> and <c>wasp fastcgi</c> load it: its configuration,
    /// <c>wasp.config</c>, giving its schemes, its mode, <paramref name="defaultMode"/> unless it names one, how long a page
    /// waits for a slow client, the hosts, the folders and the COM objects its scripts may reach, what they may read of the
    /// machine's environment, and its databases; its workers, one for each processor; null, with why, if it cannot be loaded.
    /// Under <paramref name="debugger"/>, each request a thread of its (section 5.7).
    /// </summary>
    public static WebLingoApplication? Load(string full, IReadOnlyDictionary<string, string> givenSchemes, WebLingoMode defaultMode,
        TextWriter? log, out int workers, out WebLingoMode mode, out string? error, IWebLingoDebuggerThreads? debugger = null)
    {
        (workers, mode) = (Math.Max(1, Environment.ProcessorCount), defaultMode);
        if (!WebLingoSchemes.TryLoadFolder(full, WebLingoRouteTable.ConfigurationFile, givenSchemes, out var schemes, out var configured,
            out var sendTimeout, out var allowedHosts, out var databases, out var folders, out var allowed, out var env, out var mail,
            out var uploads, out var sessions, out var limits, out var logging, out var headers, out var served, out error)) return null;
        if (configured is not null) mode = configured == "production" ? WebLingoMode.Production : WebLingoMode.Development;
        // The site it lies over, where its wasp.config names one, whose files its own come before (section 6.12, "A site
        // over a base"); its settings, those the site's own does not give, read above.
        if (!WebLingoSchemes.TryFindBase(full, WebLingoRouteTable.ConfigurationFile, out var over, out _, out error)) return null;
        // The files a site does not serve by default it serves only by their extensions, each one of those (section 6.12).
        foreach (var extension in served)
        {
            if (WebLingoRouteTable.Sensitive.Contains(extension)) continue;
            error = $"{Path.Combine(full, WebLingoRouteTable.ConfigurationFile)}: \"serve\"'s \"allow\" names '{extension}', which a site serves "
                    + $"by default: it names those it does not, {string.Join(", ", WebLingoRouteTable.Sensitive)}.";
            return null;
        }

        // std:system, loaded once here to fail early, then by each worker on its own.
        var importer = Path.Combine(full, "index.wasp");
        if (!new WebLingoNativeModuleLoader(schemes, new WebLingoBuiltinRegistry()).TryResolve(importer, "std:system", out _, out var why))
        {
            error = $"std:system cannot be loaded from {schemes["std"]}: {why}.";
            return null;
        }
        // Its scripts reach only the folders wasp.config allows, a relative path from the root (section 7.13).
        var files = new DotNetFileCapabilities(full, folders.Read, folders.Write);
        // Those wasp.config names, their files from its folder (section 7.11); a script's own checked as its files are.
        var siteDatabases = new DotNetDatabases(databases, full, new DotNetSecrets()) { Files = files };
        if (sessions.Database is { } named && !databases.ContainsKey(named))
        {
            error = $"{Path.Combine(full, WebLingoRouteTable.ConfigurationFile)}: \"sessions\"'s \"database\" names '{named}', which \"databases\" does not.";
            return null;
        }
        // Its log: the host's own, log, or a file a day where wasp.config names one, outside the site, which would serve it
        // and reload at each line (section 6.12, "Logging").
        if (logging.File is { } logFile && (IsWithin(logFile, full) || (over is not null && IsWithin(logFile, over))))
        {
            error = $"{Path.Combine(full, WebLingoRouteTable.ConfigurationFile)}: \"log\"'s \"file\", {logFile}, is in the site's folder"
                    + (over is null ? "" : " or its base's") + ", where it would be served and the site reloaded at each line: name a file outside it.";
            return null;
        }
        DotNetLog? siteLog = logging.File is { } named2 ? new DotNetLog(named2, logging.Level, log)
            : log is null ? null : new DotNetLog(log, logging.Level);
        // Its sessions in the process's memory, kept while it reloads, or in a database (section 6.12, "Sessions and cookies").
        var (timeout, lockTimeout) = (TimeSpan.FromMinutes(sessions.TimeoutMinutes), TimeSpan.FromSeconds(sessions.LockTimeoutSeconds));
        IWebLingoSessionStore store = sessions.Database is { } database
            ? new WebLingoDatabaseSessions(siteDatabases, database, sessions.Table ?? WebLingoDatabaseSessions.DefaultTable, sessions.Create, timeout,
                lockTimeout, siteLog is null ? null : (Action<string>)(text => siteLog.Write(WebLingoLogLevel.Error, null, text)))
            : new WebLingoMemorySessions(timeout, lockTimeout);
        // A request's time limit, none where wasp.config says null, and the drain after a reload, 60 seconds or that limit by
        // default, whichever is longer (section 6.12, "Limits" and "Reloading").
        TimeSpan? requestTimeout = limits.NoTimeout ? null
            : limits.TimeoutSeconds is { } limit ? TimeSpan.FromSeconds(limit) : WebLingoApplicationOptions.DefaultRequestTimeout;
        var application = WebLingoApplication.Load(new WebLingoApplicationOptions(full, new DotNetFileSystem())
        {
            ScriptFileSystem = new DotNetFileSystem(files),
            Base = over,
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
            Databases = siteDatabases,
            // Those wasp.config names, their passwords read where it keeps them, outside the site (section 7.17).
            Mail = new DotNetMail(mail, new DotNetSecrets()),
            // Its body's largest, and its uploads', kept in temporary files where wasp.config says (section 6.12).
            Uploads = new WebLingoUploads(uploads.MaxBodySize, uploads.InMemory, uploads.MaxSize ?? uploads.MaxBodySize,
                new DotNetTemporaryFiles(uploads.Folder)),
            Sessions = store,
            Scheduler = () => new DotNetSchedulerHost(),
            Log = siteLog,
            // Its headers, nosniff and its kin unless wasp.config says otherwise, and the sensitive files it serves (section 6.12).
            Headers = headers.Headers(),
            Served = served,
            SendTimeout = sendTimeout is { } seconds ? TimeSpan.FromSeconds(seconds) : TimeSpan.FromSeconds(30),
            // A request's limits, wasp.config's or the defaults; its time none under a debugger, whose stops it must not
            // count (section 6.12, "Limits"; section 5.7, "Debugging WASP pages").
            RequestTimeout = debugger is not null ? null : requestTimeout,
            Drain = limits.DrainSeconds is { } drain ? TimeSpan.FromSeconds(drain) : WebLingoApplicationOptions.DefaultDrain(requestTimeout),
            InstructionBudget = limits.Instructions ?? WebLingoApplicationOptions.DefaultInstructionBudget,
            MaxHeapUnits = limits.MemoryMegabytes is { } megabytes ? megabytes * 512 * 1024 : WebLingoHeap.DefaultMaxUnits,
            Debugger = debugger
        }, out error);
        if (application is null)
        {
            error = $"{full}: {error}.";
            siteLog?.Dispose();
        }
        return application;
    }

    /// <summary>Whether <paramref name="path"/> is in <paramref name="folder"/> or below it, as the file system compares names.</summary>
    private static bool IsWithin(string path, string folder)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, comparison);
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
