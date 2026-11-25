using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using WebLingo.Data.DotNet;
using WebLingo.Platform.Abstractions;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// A database server the tests run themselves, MySQL's or PostgreSQL's, from its download, the one zip file of the
/// repository's <c>Databases/mysql</c> or <c>Databases/postgresql</c> folder, which git ignores
/// (<c>Documents/implementation.md</c>, "Trying MySQL and PostgreSQL"): the server's own files unpacked into a folder of
/// the test process's, a database cluster made there, and the server started on a port of the system's choosing, listening
/// on 127.0.0.1 only, once for the process, when a test first asks for it. The server ends with the process, however it
/// ends, being in a job object of the process's that ends its processes when it closes; its folder, which a test host ended
/// before its cleanup has finished leaves behind, is removed by the next test process to start a server, once no process
/// holds the lock file in it, which its own holds while it runs. Its tests are skipped where its download is not there, and
/// on any platform but Windows, the downloads being Windows's. <c>WebLingo.Templates.Tests</c> shares this file.
/// </summary>
public sealed class DatabaseServer
{
    public static readonly DatabaseServer MySql = new("mysql", "MySQL", "mysql");
    public static readonly DatabaseServer PostgreSql = new("postgresql", "PostgreSQL", "postgres");

    /// <summary>
    /// How long a server may take to make its cluster, or to start, in seconds: seconds on an idle machine, more on a loaded
    /// one, as SQL Server's making of a database (<c>SqlServerTests.MakingTimeout</c>).
    /// </summary>
    private const int StartTimeout = 300;

    /// <summary>The folder that holds each test process's servers, a folder of its own for each, in the system's temporary folder.</summary>
    private static readonly string Servers = Path.Combine(Path.GetTempPath(), "weblingo-database-servers");

    /// <summary>The name of the file in a server's folder that the process running it holds open, and none other can.</summary>
    private const string LockFile = "running.lock";

    private readonly string _kind;
    private readonly string _name;
    private readonly Lazy<Running> _running;

    private DatabaseServer(string kind, string name, string administration)
    {
        (_kind, _name, Administration) = (kind, name, administration);
        _running = new Lazy<Running>(Start, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The database every such server has, to make others from: MySQL's <c>mysql</c>, PostgreSQL's <c>postgres</c>.</summary>
    public string Administration { get; }

    /// <summary>Why its tests are skipped, or <c>null</c> where its download is there.</summary>
    public string? Skip => !OperatingSystem.IsWindows() ? $"{_name}'s download is Windows's."
        : Download().Length == 0 ? $"No download of {_name} in the repository's Databases/{_kind} folder (Documents/implementation.md)."
        : null;

    /// <summary>
    /// A connection string, as <c>std:db</c> takes it, to the database <paramref name="database"/> on the server, which it
    /// starts if it has not, as its administrator, who needs no password there.
    /// </summary>
    public string ConnectionString(string database) => ConnectionStringAt(_running.Value.Port, database);

    /// <summary>A database of a test's own, made on the server, its name, in lower case, which MySQL on Windows keeps its names in.</summary>
    public string MakeDatabase()
    {
        var database = "weblingo_" + Guid.NewGuid().ToString("N");
        Administer($"CREATE DATABASE {database}");
        return database;
    }

    /// <summary>A test's database dropped, PostgreSQL's with the connections its provider keeps in its pool.</summary>
    public void DropDatabase(string database) =>
        Administer(_kind == "mysql" ? $"DROP DATABASE IF EXISTS {database}" : $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");

    private void Administer(string statement)
    {
        using var connection = new DotNetDatabases(new Dictionary<string, string>(), Path.GetTempPath()).Open(ConnectionString(Administration));
        connection.Execute(statement, [], StartTimeout);
    }

    /// <summary>The one zip file in the repository's <c>Databases/&lt;kind&gt;</c> folder, or <c>""</c>.</summary>
    private string Download()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !Directory.Exists(Path.Combine(folder.FullName, "Examples.wasp"))) folder = folder.Parent;
        var downloads = folder is null ? null : Path.Combine(folder.FullName, "Databases", _kind);
        return downloads is not null && Directory.Exists(downloads) && Directory.GetFiles(downloads, "*.zip") is [var zip] ? zip : "";
    }

    /// <summary>The server unpacked, its cluster made, and started, its administration's database answering.</summary>
    private Running Start()
    {
        if (Skip is { } skip) throw new InvalidOperationException(skip);
        RemoveLeftOver();
        var folder = Path.Combine(Servers, $"{_kind}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        // Held while the process runs, the server that holds it keeping it from the collector, which would close it.
        var held = new FileStream(Path.Combine(folder, LockFile), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        var output = new StringBuilder();
        Process? server = null;
        try
        {
            var root = Unpack(folder);
            var data = Path.Combine(folder, "data");
            var bin = Path.Combine(root, "bin");
            // The cluster: MySQL's administrator, root, and PostgreSQL's, postgres, without a password, on 127.0.0.1 only.
            Finish(_kind == "mysql"
                ? Launch(Path.Combine(bin, "mysqld.exe"), ["--no-defaults", $"--basedir={root}", $"--datadir={data}", "--initialize-insecure",
                    "--console", "--innodb-redo-log-capacity=8M"], output)
                : Launch(Path.Combine(bin, "initdb.exe"), ["-D", data, "-U", "postgres", "-A", "trust", "-E", "UTF8", "--no-locale", "--no-sync"], output),
                "make its cluster", output);
            var port = FreePort();
            server = _kind == "mysql"
                ? Launch(Path.Combine(bin, "mysqld.exe"), ["--no-defaults", $"--basedir={root}", $"--datadir={data}", $"--port={port}",
                    "--bind-address=127.0.0.1", "--mysqlx=OFF", "--console", "--innodb-redo-log-capacity=8M"], output)
                : Launch(Path.Combine(bin, "postgres.exe"), ["-D", data, "-p", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "-h", "127.0.0.1", "-k", "", "-c", "fsync=off"], output);
            var running = new Running(server, folder, port, held);
            WaitUntilAnswering(running, output);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => running.Stop();
            return running;
        }
        catch
        {
            new Running(server, folder, 0, held).Stop();
            throw;
        }
    }

    /// <summary>
    /// The servers' folders that earlier test processes left, each removed once no process holds its lock file, its process
    /// having ended, and its servers with it; one whose lock file is gone, being removed, or not made yet, once an hour old.
    /// </summary>
    private static void RemoveLeftOver()
    {
        if (!Directory.Exists(Servers)) return;
        foreach (var folder in Directory.GetDirectories(Servers))
        {
            var lockFile = Path.Combine(folder, LockFile);
            try
            {
                if (File.Exists(lockFile)) new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose();
                else if (Directory.GetCreationTimeUtc(folder) > DateTime.UtcNow.AddHours(-1)) continue;
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // Held by a process that runs its servers, or its files a moment after its servers have ended: left for later.
            }
        }
    }

    /// <summary>The server's files, and only those, unpacked from its download into <paramref name="folder"/>: its folder, holding <c>bin</c>.</summary>
    private string Unpack(string folder)
    {
        using var zip = ZipFile.OpenRead(Download());
        var into = Path.GetFullPath(Path.Combine(folder, "server")) + Path.DirectorySeparatorChar;
        foreach (var entry in zip.Entries)
        {
            // The zip's own folder first, mysql-8.4.13.1-winx64/ or pgsql/, then the server's: mysqld.exe, the libraries
            // beside it and its messages; PostgreSQL's programs, libraries and shared files.
            var parts = entry.FullName.Split('/');
            if (parts.Length < 3 || entry.Name.Length == 0) continue;
            var wanted = _kind == "mysql"
                ? parts[1] == "share" || parts is [_, "bin", var file] && (file == "mysqld.exe" || file.EndsWith(".dll", StringComparison.Ordinal)
                    && !file.Contains("-debug", StringComparison.Ordinal))
                : parts[1] is "bin" or "lib" or "share";
            if (!wanted) continue;
            var path = Path.GetFullPath(Path.Combine(into, string.Join(Path.DirectorySeparatorChar, parts[1..])));
            if (!path.StartsWith(into, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"'{entry.FullName}' is outside the server's folder.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path);
        }
        return into.TrimEnd(Path.DirectorySeparatorChar);
    }

    /// <summary>A program of the server's, its output kept, in the test process's job, so that it ends with the process.</summary>
    private static Process Launch(string program, IEnumerable<string> arguments, StringBuilder output)
    {
        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(program)!
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        var process = new Process { StartInfo = start };
        // Read as it comes, so that a full pipe never stops the server, and kept to say why it failed.
        void Keep(object _, DataReceivedEventArgs line)
        {
            if (line.Data is null) return;
            lock (output) output.AppendLine(line.Data);
        }
        process.OutputDataReceived += Keep;
        process.ErrorDataReceived += Keep;
        process.Start();
        ProcessJob.Add(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private void Finish(Process process, string what, StringBuilder output)
    {
        using (process)
        {
            if (!process.WaitForExit(StartTimeout * 1000))
            {
                process.Kill(entireProcessTree: true);
                throw new InvalidOperationException($"{_name} did not {what} in {StartTimeout} s:\n{Said(output)}");
            }
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException($"{_name} failed to {what}, exit code {process.ExitCode}:\n{Said(output)}");
        }
    }

    /// <summary>Waits until the server's administration's database opens, the server having started, or says why it did not.</summary>
    private void WaitUntilAnswering(Running running, StringBuilder output)
    {
        var deadline = Stopwatch.StartNew();
        var connection = ConnectionStringAt(running.Port, Administration);
        while (true)
        {
            try
            {
                using var opened = new DotNetDatabases(new Dictionary<string, string>(), Path.GetTempPath()).Open(connection);
                return;
            }
            catch (WebLingoDatabaseException failure)
            {
                if (running.Server!.HasExited) throw new InvalidOperationException($"{_name} stopped as it started, exit code {running.Server.ExitCode}:\n{Said(output)}");
                if (deadline.Elapsed.TotalSeconds > StartTimeout)
                {
                    throw new InvalidOperationException($"{_name} did not answer in {StartTimeout} s ({failure.Message}):\n{Said(output)}");
                }
                Thread.Sleep(200);
            }
        }
    }

    private string ConnectionStringAt(int port, string database) => _kind == "mysql"
        ? $"mysql:Server=127.0.0.1;Port={port};User ID=root;Database={database}"
        : $"postgresql:Host=127.0.0.1;Port={port};Username=postgres;Database={database}";

    private static string Said(StringBuilder output)
    {
        lock (output) return output.ToString();
    }

    private static int FreePort()
    {
        using var free = new TcpListener(IPAddress.Loopback, 0);
        free.Start();
        return ((IPEndPoint)free.LocalEndpoint).Port;
    }

    /// <summary>A server started: its process, its folder, its port, and the lock file it holds in its folder.</summary>
    private sealed class Running(Process? server, string folder, int port, FileStream held)
    {
        private int _stopped;

        public Process? Server => server;
        public int Port => port;

        /// <summary>The server ended, its files with it; its data is the test process's, so that nothing is lost by ending it at once.</summary>
        public void Stop()
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 1) return;
            try
            {
                if (server is { HasExited: false })
                {
                    server.Kill(entireProcessTree: true);
                    server.WaitForExit(10_000);
                }
            }
            catch (Exception failure) when (failure is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Gone already.
            }
            held.Dispose();
            for (var attempt = 0; attempt < 20 && Directory.Exists(folder); attempt++)
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
                {
                    Thread.Sleep(250); // Its files held a moment after its process has gone.
                }
            }
        }
    }

    /// <summary>
    /// A job object of the test process's, which ends the processes in it when it closes, as it does when the process
    /// ends, however it ends: so that a test host that crashes leaves no server running.
    /// </summary>
    private static class ProcessJob
    {
        private const uint KillOnJobClose = 0x2000;
        private const int ExtendedLimitInformation = 9;
        private static readonly nint Job = Make();

        public static void Add(Process process)
        {
            if (Job != 0 && !AssignProcessToJobObject(Job, process.Handle))
            {
                throw new InvalidOperationException($"The server's process could not join the tests' job: error {Marshal.GetLastPInvokeError()}.");
            }
        }

        private static nint Make()
        {
            var job = CreateJobObjectW(0, 0);
            if (job == 0) return 0;
            var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = KillOnJobClose } };
            return SetInformationJobObject(job, ExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()) ? job : 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public nuint MinimumWorkingSetSize;
            public nuint MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public nuint Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimits
        {
            public BasicLimits Basic;
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
            public nuint ProcessMemoryLimit;
            public nuint JobMemoryLimit;
            public nuint PeakProcessMemoryUsed;
            public nuint PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern nint CreateJobObjectW(nint attributes, nint name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(nint job, int infoClass, ref ExtendedLimits info, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(nint job, nint process);
    }
}

/// <summary>A test that needs MySQL, run by the tests from its download (<see cref="DatabaseServer"/>), skipped where the download is not there.</summary>
public sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute() => Skip = DatabaseServer.MySql.Skip;
}

/// <summary>A test that needs PostgreSQL, run by the tests from its download (<see cref="DatabaseServer"/>), skipped where the download is not there.</summary>
public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute() => Skip = DatabaseServer.PostgreSql.Skip;
}
