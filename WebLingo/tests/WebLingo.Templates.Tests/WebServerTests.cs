using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using WebLingo.Cli;
using WebLingo.Wasp.Cli;
using Xunit;
namespace WebLingo.Templates.Tests;

/// <summary>
/// <c>wasp fastcgi</c> behind the web servers the WASP User Manual's section 18 configures, as it configures them: nginx,
/// and Apache httpd through <c>mod_proxy_fcgi</c>, each to <c>wasp fastcgi</c> on a port, and Apache httpd through
/// <c>mod_fcgid</c>, starting <c>wasp</c> itself, on its standard input. Each server is unpacked from its download, in the
/// repository's <c>WebServers</c> folder, which git ignores (<c>Documents/implementation.md</c>, "Trying FastCGI with nginx
/// and Apache"), into a folder of its own, run on 127.0.0.1 only, and stopped; a test whose download is not there is
/// skipped, and so is each on a platform other than Windows, the downloads being Windows's.
/// </summary>
public sealed class WebServerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wasp-servers-" + Guid.NewGuid().ToString("N"));
    private readonly string _site;

    public WebServerTests()
    {
        _site = Path.Combine(_folder, "site");
        Write("index.wasp", "<% param request; %><p><%= request.method %> <%= request.path %> <%= request.query?[\"q\"] %> "
                            + "<%= request.headers?[\"Accept-Language\"] %> <%= request.scheme %></p>");
        Write("form.wasp", "<% param request; var form = await request.form(); %><%= form[\"tea\"] %>");
        Write("made.wasp", "<% param response; response.status = 201; response.headers[\"X-Tea\"] = \"Sencha\"; %>made");
        Write("big.wasp", "<% for (var i = 0; i < 20000; i++) { %>0123456789<% } %>");
        Write("teas/index.wasp", "<p>teas</p>");
        Write("css/site.css", "h1 { }");
        Write("lib/util.wlg", "export function f() { return 1; }");
        Write("wasp.config", "{ }");
    }

    public void Dispose()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(_folder, recursive: true);
                return;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException && attempt < 20)
            {
                // A server's process may hold its files a moment after it has gone: a program's own file, httpd.exe, is
                // then refused as a file in use is not (25 November 2026, under the soak's load).
                Thread.Sleep(250);
            }
        }
    }

    [WebServerFact("nginx")]
    public async Task NginxServesASiteFromWaspFastCgi()
    {
        var root = Path.Combine(Unpack("nginx"), Path.GetFileNameWithoutExtension(WebServerFactAttribute.Download("nginx")));
        var (waspPort, port) = (FreePort(), FreePort());
        await using var wasp = StartWasp(waspPort);
        File.WriteAllText(Path.Combine(root, "conf", "wasp.conf"), $$"""
            worker_processes 1;
            error_log logs/error.log;
            pid logs/nginx.pid;
            events { worker_connections 64; }
            http {
                access_log off;
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
        string[] arguments = ["-p", Slashed(root) + "/", "-c", "conf/wasp.conf"];
        using var nginx = Start(Path.Combine(root, "nginx.exe"), arguments, root);
        try
        {
            await ServesTheSiteAsync(port, nginx, () => Read(Path.Combine(root, "logs", "error.log")));
        }
        finally
        {
            using var stopping = Start(Path.Combine(root, "nginx.exe"), [.. arguments, "-s", "stop"], root);
            stopping.WaitForExit(20_000);
            if (!nginx.WaitForExit(20_000)) nginx.Kill(entireProcessTree: true);
        }
    }

    [WebServerFact("httpd")]
    public async Task ApacheServesASiteFromWaspFastCgiThroughModProxyFcgi()
    {
        var (waspPort, port) = (FreePort(), FreePort());
        await using var wasp = StartWasp(waspPort);
        await ApacheServesTheSiteAsync(port, $"""
            LoadModule proxy_module modules/mod_proxy.so
            LoadModule proxy_fcgi_module modules/mod_proxy_fcgi.so
            ProxyPass "/" "fcgi://127.0.0.1:{waspPort}/"
            """);
    }

    [WebServerFact("httpd", "mod_fcgid")]
    public async Task ApacheStartsWaspFastCgiThroughModFcgid()
    {
        var module = Slashed(Path.Combine(Unpack("mod_fcgid"), "mod_fcgid.so"));
        var (site, wasp) = (Slashed(_site), Slashed(Path.Combine(AppContext.BaseDirectory, "wasp.exe")));
        await ApacheServesTheSiteAsync(FreePort(), $"""
            LoadModule fcgid_module "{module}"
            DocumentRoot "{site}"
            <Directory "{site}">
                SetHandler fcgid-script
                Options +ExecCGI
                FcgidWrapper "\"{wasp}\" fastcgi --root {site}" virtual
                Require all granted
            </Directory>
            """);
    }

    /// <summary>Apache httpd, unpacked, configured to listen on <paramref name="port"/> with <paramref name="more"/>, serving the site, then stopped.</summary>
    private async Task ApacheServesTheSiteAsync(int port, string more)
    {
        var root = Slashed(Path.Combine(Unpack("httpd"), "Apache24"));
        var (configuration, log) = (Path.Combine(_folder, "httpd.conf"), Slashed(Path.Combine(_folder, "httpd-error.log")));
        File.WriteAllText(configuration, $"""
            Define SRVROOT "{root}"
            ServerRoot "${"{"}SRVROOT{"}"}"
            Listen 127.0.0.1:{port}
            ServerName 127.0.0.1
            LoadModule authz_core_module modules/mod_authz_core.so
            LoadModule log_config_module modules/mod_log_config.so
            ErrorLog "{log}"
            PidFile "{Slashed(Path.Combine(_folder, "httpd.pid"))}"
            {more}
            """);
        // Stopped by force, with the processes it started, wasp's under mod_fcgid among them: a console's httpd stops on
        // Ctrl+C alone, which no other process can send it, Ctrl+Break restarting it.
        using var httpd = Start(Path.Combine(root, "bin", "httpd.exe"), ["-f", Slashed(configuration)], root);
        try
        {
            await ServesTheSiteAsync(port, httpd, () => Read(log));
        }
        finally
        {
            httpd.Kill(entireProcessTree: true);
            httpd.WaitForExit(20_000);
        }
    }

    /// <summary>The site's pages, a form, a status and a header, a streamed page, a file, and what is never served, through the server on <paramref name="port"/>.</summary>
    private static async Task ServesTheSiteAsync(int port, Process server, Func<string> log)
    {
        if (!await ListensAsync(port, server))
        {
            Assert.Fail($"The web server did not listen{(server.HasExited ? $", and exited with {server.ExitCode}" : "")}: {log()}");
        }
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.Add("Accept-Language", "pl");
        Assert.Equal("<p>GET / Sencha pl http</p>", await client.GetStringAsync("/?q=Sencha"));
        Assert.Equal("<p>teas</p>", await client.GetStringAsync("/teas/"));
        using (var form = await client.PostAsync("/form", new FormUrlEncodedContent([new("tea", "Genmaicha")])))
        {
            Assert.Equal((HttpStatusCode.OK, "Genmaicha"), (form.StatusCode, await form.Content.ReadAsStringAsync()));
        }
        using (var made = await client.GetAsync("/made"))
        {
            Assert.Equal((HttpStatusCode.Created, "made", "Sencha"),
                (made.StatusCode, await made.Content.ReadAsStringAsync(), Assert.Single(made.Headers.GetValues("X-Tea"))));
        }
        Assert.Equal(200_000, (await client.GetStringAsync("/big")).Length);
        using (var css = await client.GetAsync("/css/site.css"))
        {
            Assert.Equal((HttpStatusCode.OK, "h1 { }", "text/css"),
                (css.StatusCode, await css.Content.ReadAsStringAsync(), css.Content.Headers.ContentType?.MediaType));
        }
        foreach (var never in new[] { "/lib/util.wlg", "/wasp.config", "/nothing" })
        {
            using var response = await client.GetAsync(never);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    /// <summary><c>wasp fastcgi --root site --port n</c>, run here, as its command line runs it, until disposed.</summary>
    private Running StartWasp(int port)
    {
        var stop = new CancellationTokenSource();
        var (output, error) = (new StringWriter(), new StringWriter());
        string[] arguments = ["fastcgi", "--root", _site, "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        var running = Task.Run(() => WebLingoCommandThread.Run(() => WebLingoWaspFastCgi.Run(arguments, output, error, stop.Token)));
        return new Running(stop, running);
    }

    private sealed class Running(CancellationTokenSource stop, Task<int> running) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            Assert.Equal(WebLingoCommandLine.Success, await running);
            stop.Dispose();
        }
    }

    private static Process Start(string program, string[] arguments, string folder)
    {
        var start = new ProcessStartInfo(program) { WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException($"'{program}' did not start.");
    }

    private static async Task<bool> ListensAsync(int port, Process server)
    {
        for (var i = 0; i < 200 && !server.HasExited; i++)
        {
            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(IPAddress.Loopback, port);
                return true;
            }
            catch (SocketException)
            {
                await Task.Delay(100);
            }
        }
        return false;
    }

    private static int FreePort()
    {
        using var free = new TcpListener(IPAddress.Loopback, 0);
        free.Start();
        return ((IPEndPoint)free.LocalEndpoint).Port;
    }

    /// <summary>A server's download unpacked into a new folder of its own.</summary>
    private string Unpack(string server)
    {
        var into = Path.Combine(_folder, server);
        ZipFile.ExtractToDirectory(WebServerFactAttribute.Download(server), into);
        return into;
    }

    private void Write(string name, string text)
    {
        var path = Path.Combine(_site, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    /// <summary>A server's log, which it may still be writing, or <c>""</c>.</summary>
    private static string Read(string log)
    {
        if (!File.Exists(log)) return "";
        using var reader = new StreamReader(new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        return reader.ReadToEnd();
    }

    /// <summary>A path as the servers' configurations write one, with <c>/</c>.</summary>
    private static string Slashed(string path) => path.Replace('\\', '/');
}

/// <summary>
/// A test that needs web servers' downloads, each the one zip file in its folder of the repository's <c>WebServers</c>
/// folder (<c>WebServers/nginx</c>, <c>WebServers/httpd</c>, <c>WebServers/mod_fcgid</c>), skipped where one is not there,
/// and on any platform but Windows.
/// </summary>
public sealed class WebServerFactAttribute : FactAttribute
{
    public WebServerFactAttribute(params string[] servers)
    {
        if (!OperatingSystem.IsWindows()) Skip = "The web servers' downloads are Windows's.";
        else if (servers.FirstOrDefault(server => Download(server).Length == 0) is { } missing)
        {
            Skip = $"No download of {missing} in the repository's WebServers/{missing} folder (Documents/implementation.md).";
        }
    }

    /// <summary>The one zip file in <c>WebServers/<paramref name="server"/></c>, or <c>""</c>.</summary>
    public static string Download(string server)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !Directory.Exists(Path.Combine(folder.FullName, "Examples.wasp"))) folder = folder.Parent;
        var downloads = folder is null ? null : Path.Combine(folder.FullName, "WebServers", server);
        return downloads is not null && Directory.Exists(downloads) && Directory.GetFiles(downloads, "*.zip") is [var zip] ? zip : "";
    }
}
