using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using Microsoft.Data.Sqlite;
namespace WebLingo.Benchmarks;

/// <summary>
/// The load site, served (specification, section 5.6, "Under load"): copied with a SQLite database of its own into a folder
/// of its own, and served by <c>wasp serve</c>, or by <c>wasp fastcgi</c> behind nginx, each started beside the benchmark
/// and stopped after; or a site found at a URL, as IIS serves it.
/// </summary>
public sealed class WebLingoLoadSite : IAsyncDisposable
{
    private readonly string? _folder;
    private readonly List<Process> _started = [];
    private Process? _server;
    private WebLingoServerCounters? _counters;
    private string? _nginx;
    private Process? _nginxProcess;

    private WebLingoLoadSite(Uri address, string description, string? folder) =>
        (Address, Description, _folder) = (address, description, folder);

    public Uri Address { get; }
    public string Description { get; }

    /// <summary>A site served elsewhere, at <paramref name="url"/>, which the benchmark neither starts nor samples.</summary>
    public static WebLingoLoadSite At(Uri url) => new(url, $"the site at {url}", null);

    /// <summary>
    /// The load site made in <paramref name="folder"/> for IIS to serve (specification, section 5.6, "Under load, parts",
    /// part 3): its pages in its <c>site</c> folder, with a <c>web.config</c> that runs the installed <c>wasp</c>, which
    /// <c>add-wasp-site.wlg</c> needs, and its SQLite database in its <c>load-data</c> folder, where its <c>wasp.config</c>
    /// names it. Made again, its pages and its <c>web.config</c> are replaced, and its database kept.
    /// </summary>
    public static void Prepare(string folder)
    {
        var site = Path.Combine(folder, "site");
        Copy(WebLingoLoad.SiteFolder, site);
        File.WriteAllText(Path.Combine(site, "web.config"), WebConfig);
        var database = Path.Combine(folder, "load-data", "load.db");
        if (!File.Exists(database)) MakeDatabase(database);
    }

    /// <summary>The load site's <c>web.config</c> under IIS, as the examples' and the forum's are.</summary>
    private const string WebConfig = """
        <?xml version="1.0" encoding="utf-8"?>
        <!--
          IIS runs the load site through the ASP.NET Core Module, which starts wasp, installed in C:\Program Files\WebLingo,
          and passes it every request. Made by WebLingo.Benchmarks --load --prepare.
        -->
        <configuration>
          <location path="." inheritInChildApplications="false">
            <system.webServer>
              <handlers>
                <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
              </handlers>
              <aspNetCore processPath="C:\Program Files\WebLingo\wasp.exe" arguments="serve" hostingModel="outofprocess"
                          stdoutLogEnabled="false" stdoutLogFile="C:\inetpub\logs\WebLingo\LoadSite" />
            </system.webServer>
          </location>
        </configuration>

        """;

    /// <summary>
    /// The load site, served by <c>wasp serve</c> where <paramref name="host"/> is <c>serve</c>, WebLingo's own HTTP server
    /// since part 6 of "M0.23 parts" (specification, section 9), by <c>wasp serve --host standalone</c> where it is
    /// <c>standalone</c>, by <c>wasp serve --host aspnetcore</c>, ASP.NET Core's server, where it is <c>aspnetcore</c>, else
    /// behind nginx; its <c>wasp.config</c> given <paramref name="setting"/> more, where one is given.
    /// </summary>
    public static async Task<WebLingoLoadSite> Start(string host, string? setting = null)
    {
        var folder = Path.Combine(Path.GetTempPath(), "weblingo-load-" + Guid.NewGuid().ToString("N"));
        var site = Path.Combine(folder, "site");
        Copy(WebLingoLoad.SiteFolder, site);
        if (setting is not null)
        {
            // A setting more in its wasp.config, beside its own, which its server reads as it starts (section 9, "M0.24 parts", part 3).
            var config = Path.Combine(site, "wasp.config");
            var original = File.ReadAllText(config);
            var brace = original.IndexOf('{', StringComparison.Ordinal);
            File.WriteAllText(config, original[..(brace + 1)] + "\n  " + setting + "," + original[(brace + 1)..]);
        }
        MakeDatabase(Path.Combine(folder, "load-data", "load.db"));
        var wasp = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "wasp.exe" : "wasp");
        var port = FreePort();
        WebLingoLoadSite served;
        if (host == "serve")
        {
            served = new WebLingoLoadSite(new Uri($"http://127.0.0.1:{port}/"), "`wasp serve`", folder);
            served._server = served.Started(wasp, ["serve", "--root", site, "--port", Text(port)], site);
        }
        else if (host == "standalone")
        {
            served = new WebLingoLoadSite(new Uri($"http://127.0.0.1:{port}/"), "`wasp serve --host standalone`, WebLingo's own HTTP server", folder);
            served._server = served.Started(wasp, ["serve", "--host", "standalone", "--root", site, "--port", Text(port)], site);
        }
        else if (host == "aspnetcore")
        {
            served = new WebLingoLoadSite(new Uri($"http://127.0.0.1:{port}/"), "`wasp serve --host aspnetcore`, ASP.NET Core's server", folder);
            served._server = served.Started(wasp, ["serve", "--host", "aspnetcore", "--root", site, "--port", Text(port)], site);
        }
        else
        {
            var waspPort = FreePort();
            served = new WebLingoLoadSite(new Uri($"http://127.0.0.1:{port}/"), "`wasp fastcgi` behind nginx", folder);
            // nginx found first, so that a machine without it leaves no wasp running.
            var nginx = UnpackNginx(folder);
            served._server = served.Started(wasp, ["fastcgi", "--root", site, "--port", Text(waspPort)], site);
            served._nginx = nginx;
            // Elsewhere than Windows, the system's nginx stays in the foreground, the benchmark's process, as Windows' does, and
            // keeps a request's body and the like in the run's folder, not its own.
            var foreground = OperatingSystem.IsWindows() ? "" : "daemon off;";
            var temps = OperatingSystem.IsWindows() ? "" : "client_body_temp_path temp/body; fastcgi_temp_path temp/fastcgi; "
                                                           + "proxy_temp_path temp/proxy; uwsgi_temp_path temp/uwsgi; scgi_temp_path temp/scgi;";
            File.WriteAllText(Path.Combine(nginx, "conf", "load.conf"), $$"""
                {{foreground}}
                worker_processes 1;
                error_log logs/error.log;
                pid logs/nginx.pid;
                events { worker_connections 1024; }
                http {
                    access_log off;
                    {{temps}}
                    client_max_body_size 30m;
                    upstream wasp {
                        server 127.0.0.1:{{waspPort}};
                        keepalive 256;
                    }
                    server {
                        listen 127.0.0.1:{{port}};
                        location / {
                            include fastcgi_params;
                            fastcgi_keep_conn on;
                            fastcgi_request_buffering off;
                            fastcgi_pass wasp;
                        }
                    }
                }
                """);
            served._nginxProcess = served.Started(NginxProgram(nginx), NginxArguments(nginx), nginx);
        }
        try
        {
            await served.Ready();
        }
        catch
        {
            await served.DisposeAsync(); // Its server stopped and its folder removed, not left running.
            throw;
        }
        return served;
    }

    /// <summary>A client of its own, with one connection and its own cookies, as a browser's tab is.</summary>
    public HttpClient Client() =>
        new(new SocketsHttpHandler { MaxConnectionsPerServer = 1, CookieContainer = new CookieContainer(), UseCookies = true })
        {
            BaseAddress = Address,
            Timeout = TimeSpan.FromSeconds(100)
        };

    /// <summary>The server's process as it is now, where the benchmark started it, its .NET heap where it is watched; null where it did not.</summary>
    public WebLingoServerSample? Sample()
    {
        if (_server is not { HasExited: false } server) return null;
        server.Refresh();
        return new WebLingoServerSample(server.WorkingSet64, server.PrivateMemorySize64, server.HandleCount, server.Threads.Count,
            _counters?.GcHeapBytes);
    }

    /// <summary>
    /// The server's <c>System.Runtime</c> counters read from now on, each every <paramref name="seconds"/>, its .NET heap
    /// among them (<see cref="WebLingoServerCounters"/>); null, or why they cannot be, where the benchmark did not start it.
    /// </summary>
    public string? WatchCounters(int seconds)
    {
        if (_server is not { HasExited: false } server) return "the benchmark did not start the server, whose process it would read.";
        if (_counters is not null) return null;
        _counters = WebLingoServerCounters.TryAttach(server.Id, seconds, out var why);
        return _counters is null ? why : null;
    }

    /// <summary>
    /// A page of the site replaced, whole, as a deployment replaces a file: written beside it, then moved over it, the move
    /// tried again every 10 ms where the server has the page open, as a deployment's tools try again; how many times it was
    /// refused so.
    /// </summary>
    public async Task<int> Replace(string page, string text)
    {
        if (_folder is null) throw new InvalidOperationException("--reload needs a site the benchmark serves itself.");
        var path = Path.Combine(_folder, "site", page);
        var written = path + ".new";
        await File.WriteAllTextAsync(written, text);
        for (var refused = 0; ; refused++)
        {
            try
            {
                File.Move(written, path, overwrite: true);
                return refused;
            }
            catch (UnauthorizedAccessException) when (refused < 500)
            {
                await Task.Delay(10); // The server reading the page, which Windows refuses to replace meanwhile.
            }
            catch (IOException) when (refused < 500)
            {
                await Task.Delay(10);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _counters?.Dispose(); // Its session stopped while its server still runs.
        // nginx stopped as its own command stops it, and waited for; wasp, which a service manager would stop, ended.
        if (_nginx is not null)
        {
            var stopping = new ProcessStartInfo(NginxProgram(_nginx), NginxArguments(_nginx, "-s", "stop"))
            {
                WorkingDirectory = _nginx, UseShellExecute = false, CreateNoWindow = true
            };
            using (var stop = Process.Start(stopping)) stop?.WaitForExit(20_000);
            _nginxProcess?.WaitForExit(20_000);
        }
        foreach (var process in _started)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            process.Dispose();
        }
        SqliteConnection.ClearAllPools();
        if (_folder is null) return;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(_folder, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                await Task.Delay(250); // A server's process may hold its files a moment after it has gone.
            }
        }
    }

    private const string SystemNginx = "/usr/sbin/nginx";
    private const string SystemFastCgiParams = "/etc/nginx/fastcgi_params";

    /// <summary>The nginx program for its prefix: the unpacked one on Windows, the system's elsewhere.</summary>
    private static string NginxProgram(string nginx) => OperatingSystem.IsWindows() ? Path.Combine(nginx, "nginx.exe") : SystemNginx;

    /// <summary>
    /// nginx's arguments for its prefix; elsewhere than Windows, its first error log there too, <c>-e</c>, in place of the
    /// system's, which an account that is not root cannot open.
    /// </summary>
    private static string[] NginxArguments(string nginx, params string[] more) => OperatingSystem.IsWindows()
        ? ["-p", nginx.Replace('\\', '/') + "/", "-c", "conf/load.conf", .. more]
        : ["-p", nginx + "/", "-e", Path.Combine(nginx, "logs", "error.log"), "-c", "conf/load.conf", .. more];

    private Process Started(string program, string[] arguments, string folder)
    {
        var process = Process.Start(new ProcessStartInfo(program, arguments)
        {
            WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        }) ?? throw new InvalidOperationException($"{program} could not be started.");
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _started.Add(process);
        return process;
    }

    /// <summary>The site answering its first page, within a minute, or why it did not.</summary>
    private async Task Ready()
    {
        using var client = Client();
        var deadline = DateTime.UtcNow.AddMinutes(1);
        var why = "nothing answered";
        while (true)
        {
            try
            {
                using var response = await client.GetAsync("/hello");
                if (response.StatusCode == HttpStatusCode.OK) return;
                why = $"/hello was a {(int)response.StatusCode}";
            }
            catch (HttpRequestException exception)
            {
                why = exception.Message; // Not listening yet.
            }
            if (_started.FirstOrDefault(process => process.HasExited) is { } ended)
            {
                throw new InvalidOperationException($"{Description} did not start: {Path.GetFileName(ended.StartInfo.FileName)} ended, "
                                                    + $"its exit code {ended.ExitCode}.");
            }
            if (DateTime.UtcNow >= deadline) throw new InvalidOperationException($"{Description} did not answer within a minute: {why}.");
            await Task.Delay(200);
        }
    }

    private static void Copy(string from, string to)
    {
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    /// <summary>
    /// The site's SQLite database, outside it, as its <c>wasp.config</c> names it: 100 teas, which <c>sqlite.wasp</c> reads
    /// ten of.
    /// </summary>
    private static void MakeDatabase(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Not pooled, so that the file is let go once it is made, for IIS's pool to open where --prepare made it.
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE teas (id INTEGER PRIMARY KEY, name TEXT NOT NULL)";
            create.ExecuteNonQuery();
        }
        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO teas (id, name) VALUES (@id, @name)";
        var id = insert.Parameters.Add("@id", SqliteType.Integer);
        var name = insert.Parameters.Add("@name", SqliteType.Text);
        for (var i = 1; i <= 100; i++)
        {
            id.Value = i;
            name.Value = "Tea " + Text(i);
            insert.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    /// <summary>
    /// nginx's folder, its prefix: on Windows, nginx unpacked from its download in the repository's <c>WebServers</c> folder,
    /// as the tests unpack it; elsewhere, a folder of the run's own for the system's nginx, its <c>conf</c> holding the
    /// system's <c>fastcgi_params</c>, with <c>logs</c> and <c>temp</c> beside it, which an account that is not root writes
    /// (specification, section 5.6, "Under load, parts", part 4).
    /// </summary>
    private static string UnpackNginx(string folder)
    {
        if (!OperatingSystem.IsWindows())
        {
            if (!File.Exists(SystemNginx) || !File.Exists(SystemFastCgiParams))
            {
                throw new InvalidOperationException($"--host fastcgi needs the system's nginx, {SystemNginx}, and its {SystemFastCgiParams}.");
            }
            var prefix = Path.Combine(folder, "nginx");
            foreach (var part in new[] { "conf", "logs", "temp" }) Directory.CreateDirectory(Path.Combine(prefix, part));
            File.Copy(SystemFastCgiParams, Path.Combine(prefix, "conf", "fastcgi_params"));
            return prefix;
        }
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        string Downloads(DirectoryInfo folder) => Path.Combine(folder.FullName, "WebServers", "nginx");
        while (repository is not null && !Directory.Exists(Downloads(repository))) repository = repository.Parent;
        var zip = repository is null ? null : Directory.GetFiles(Downloads(repository), "nginx-*.zip").FirstOrDefault();
        if (zip is null)
        {
            throw new InvalidOperationException("--host fastcgi needs nginx's download in the repository's WebServers/nginx folder.");
        }
        var unpacked = Path.Combine(folder, "nginx");
        ZipFile.ExtractToDirectory(zip, unpacked);
        return Path.Combine(unpacked, Path.GetFileNameWithoutExtension(zip));
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
