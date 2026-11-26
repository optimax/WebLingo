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
    private string? _nginx;
    private Process? _nginxProcess;

    private WebLingoLoadSite(Uri address, string description, string? folder) =>
        (Address, Description, _folder) = (address, description, folder);

    public Uri Address { get; }
    public string Description { get; }

    /// <summary>A site served elsewhere, at <paramref name="url"/>, which the benchmark neither starts nor samples.</summary>
    public static WebLingoLoadSite At(Uri url) => new(url, $"the site at {url}", null);

    /// <summary>The load site, served by <c>wasp serve</c> where <paramref name="host"/> is <c>serve</c>, else behind nginx.</summary>
    public static async Task<WebLingoLoadSite> Start(string host)
    {
        var folder = Path.Combine(Path.GetTempPath(), "weblingo-load-" + Guid.NewGuid().ToString("N"));
        var site = Path.Combine(folder, "site");
        Copy(WebLingoLoad.SiteFolder, site);
        MakeDatabase(Path.Combine(folder, "load-data", "load.db"));
        var wasp = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "wasp.exe" : "wasp");
        var port = FreePort();
        WebLingoLoadSite served;
        if (host == "serve")
        {
            served = new WebLingoLoadSite(new Uri($"http://127.0.0.1:{port}/"), "`wasp serve`", folder);
            served._server = served.Started(wasp, ["serve", "--root", site, "--port", Text(port)], site);
        }
        else
        {
            var waspPort = FreePort();
            served = new WebLingoLoadSite(new Uri($"http://127.0.0.1:{port}/"), "`wasp fastcgi` behind nginx", folder);
            served._server = served.Started(wasp, ["fastcgi", "--root", site, "--port", Text(waspPort)], site);
            var nginx = served._nginx = UnpackNginx(folder);
            File.WriteAllText(Path.Combine(nginx, "conf", "load.conf"), $$"""
                worker_processes 1;
                error_log logs/error.log;
                pid logs/nginx.pid;
                events { worker_connections 1024; }
                http {
                    access_log off;
                    client_max_body_size 30m;
                    upstream wasp {
                        server 127.0.0.1:{{waspPort}};
                        keepalive 16;
                    }
                    server {
                        listen 127.0.0.1:{{port}};
                        location / {
                            include fastcgi_params;
                            fastcgi_keep_conn on;
                            fastcgi_pass wasp;
                        }
                    }
                }
                """);
            served._nginxProcess = served.Started(Path.Combine(nginx, "nginx.exe"), NginxArguments(nginx), nginx);
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

    /// <summary>The server's process as it is now, where the benchmark started it; null where it did not.</summary>
    public WebLingoServerSample? Sample()
    {
        if (_server is not { HasExited: false } server) return null;
        server.Refresh();
        return new WebLingoServerSample(server.WorkingSet64, server.PrivateMemorySize64, server.HandleCount, server.Threads.Count);
    }

    /// <summary>
    /// The site's <c>wasp.config</c> given a setting more, <paramref name="setting"/>, beside its own, the site then reloaded
    /// and answering again, every request after it on the new configuration (section 9, "M0.24 parts", part 3).
    /// </summary>
    public async Task Configure(string setting)
    {
        if (_folder is null) throw new InvalidOperationException("--fair needs a site the benchmark serves itself.");
        var original = await File.ReadAllTextAsync(Path.Combine(WebLingoLoad.SiteFolder, "wasp.config"));
        var brace = original.IndexOf('{', StringComparison.Ordinal);
        await Replace("wasp.config", original[..(brace + 1)] + "\n  " + setting + "," + original[(brace + 1)..]);
        // The reload is seen by the next request: asked for until answered, then a moment more for the old generation's drain.
        using var client = Client();
        for (var tries = 0; tries < 100; tries++)
        {
            try
            {
                using var response = await client.GetAsync("/hello");
                if (response.IsSuccessStatusCode) break;
            }
            catch (HttpRequestException)
            {
            }
            await Task.Delay(100);
        }
        await Task.Delay(1000);
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
        // nginx stopped as its own command stops it, and waited for; wasp, which a service manager would stop, ended.
        if (_nginx is not null)
        {
            var stopping = new ProcessStartInfo(Path.Combine(_nginx, "nginx.exe"), NginxArguments(_nginx, "-s", "stop"))
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

    private static string[] NginxArguments(string nginx, params string[] more) =>
        ["-p", nginx.Replace('\\', '/') + "/", "-c", "conf/load.conf", .. more];

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
            File.Copy(file, target);
        }
    }

    /// <summary>
    /// The site's SQLite database, outside it, as its <c>wasp.config</c> names it: 100 teas, which <c>sqlite.wasp</c> reads
    /// ten of.
    /// </summary>
    private static void MakeDatabase(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = new SqliteConnection($"Data Source={path}");
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

    /// <summary>nginx, unpacked from its download in the repository's <c>WebServers</c> folder, as the tests unpack it.</summary>
    private static string UnpackNginx(string folder)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("--host fastcgi unpacks nginx for Windows, and runs on Windows only.");
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
