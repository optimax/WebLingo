using System.Collections;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WebLingo.Platform.DotNet;

internal sealed class LinuxProcessCapture(int pid, Stream output, Stream error) : IDisposable
{
    private const short SetProcessGroup = 2;
    private const int CloseOnExec = 0x80000;
    private const int DuplicateCloseOnExec = 1030;
    private const int ExecuteAccess = 1;
    private const int NoHang = 1;
    private const int Interrupted = 4;
    private const int NoChild = 10;
    private const int NoSuchFile = 2;
    private const int KillSignal = 9;
    private bool _reaped;

    public Stream Output { get; } = output;
    public Stream Error { get; } = error;

    public static LinuxProcessCapture Start(string program, IReadOnlyList<string> arguments)
    {
        var executable = ResolveExecutable(program);
        using var argv = new Utf8Vector(arguments.Prepend(program));
        using var environment = new Utf8Vector(Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .Select(static entry => $"{entry.Key}={entry.Value}"));
        var (outputReader, outputWriter) = OpenPipe();
        using var childOutput = outputWriter;
        SafeFileHandle? errorReader = null;
        FileStream? output = null;
        FileStream? error = null;
        try
        {
            var (reader, writer) = OpenPipe();
            errorReader = reader;
            using var childError = writer;
            output = new FileStream(outputReader, FileAccess.Read);
            error = new FileStream(errorReader, FileAccess.Read);
            var pid = Spawn(executable, argv.Entries, environment.Entries, childOutput, childError);
            return new LinuxProcessCapture(pid, output, error);
        }
        catch
        {
            output?.Dispose();
            error?.Dispose();
            outputReader.Dispose();
            errorReader?.Dispose();
            throw;
        }
    }

    private static int Spawn(string executable, nint[] args, nint[] environment, SafeFileHandle output, SafeFileHandle error)
    {
        // libc owns the layout. These aligned buffers cover the glibc and musl spawn types on supported Linux ABIs.
        var attributes = Marshal.AllocHGlobal(512);
        var actions = Marshal.AllocHGlobal(512);
        try
        {
            Check(posix_spawnattr_init(attributes));
            try
            {
                Check(posix_spawnattr_setflags(attributes, SetProcessGroup));
                Check(posix_spawnattr_setpgroup(attributes, 0));
                Check(posix_spawn_file_actions_init(actions));
                try
                {
                    Check(posix_spawn_file_actions_addopen(actions, 0, "/dev/null", 0, 0));
                    Check(posix_spawn_file_actions_adddup2(actions, (int)output.DangerousGetHandle(), 1));
                    Check(posix_spawn_file_actions_adddup2(actions, (int)error.DangerousGetHandle(), 2));
                    Check(posix_spawn(out var pid, executable, actions, attributes, args, environment));
                    return pid;
                }
                finally { _ = posix_spawn_file_actions_destroy(actions); }
            }
            finally { _ = posix_spawnattr_destroy(attributes); }
        }
        finally
        {
            Marshal.FreeHGlobal(actions);
            Marshal.FreeHGlobal(attributes);
        }
    }

    public async Task<int> WaitForExitAsync(CancellationToken cancellation)
    {
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            if (TryReap(NoHang, out var code)) return code;
            await Task.Delay(10, cancellation).ConfigureAwait(false);
        }
    }

    public void Stop()
    {
        if (_reaped) return;
        _ = kill(-pid, KillSignal);
        // Reap our direct child after signaling the entire group. A descendant may already have been reparented.
        try { _ = TryReap(0, out _); }
        catch (Win32Exception) { }
    }

    public void Dispose()
    {
        Output.Dispose();
        Error.Dispose();
    }

    private bool TryReap(int options, out int code)
    {
        code = 0;
        int result, status;
        do
        {
            result = waitpid(pid, out status, options);
        } while (result < 0 && Marshal.GetLastPInvokeError() == Interrupted);
        if (result == 0) return false;
        if (result == pid)
        {
            _reaped = true;
            var signal = status & 0x7f;
            code = signal == 0 ? (status >> 8) & 0xff : 128 + signal;
            return true;
        }
        var error = Marshal.GetLastPInvokeError();
        if (error == NoChild) _reaped = true; // Our PID can already have been recycled.
        throw new Win32Exception(error);
    }

    private static string ResolveExecutable(string program)
    {
        if (Path.IsPathRooted(program)) return program;
        if (Environment.ProcessPath is { } host && Path.GetDirectoryName(host) is { } directory)
        {
            var besideHost = Path.Combine(directory, program);
            if (File.Exists(besideHost)) return besideHost;
        }
        var local = Path.GetFullPath(program);
        if (File.Exists(local)) return local;
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(folder, program);
            if (access(candidate, ExecuteAccess) == 0 && !Directory.Exists(candidate)) return candidate;
        }
        throw new Win32Exception(NoSuchFile);
    }

    private static (SafeFileHandle Read, SafeFileHandle Write) OpenPipe()
    {
        var descriptors = new int[2];
        if (pipe2(descriptors, CloseOnExec) != 0) throw new Win32Exception();
        var read = new SafeFileHandle(descriptors[0], ownsHandle: true);
        var write = new SafeFileHandle(descriptors[1], ownsHandle: true);
        try { return (AboveStandardStreams(ref read), AboveStandardStreams(ref write)); }
        catch
        {
            read.Dispose();
            write.Dispose();
            throw;
        }
    }

    private static SafeFileHandle AboveStandardStreams(ref SafeFileHandle handle)
    {
        var descriptor = (int)handle.DangerousGetHandle();
        if (descriptor >= 3) return handle;
        var duplicate = fcntl(descriptor, DuplicateCloseOnExec, 3);
        if (duplicate < 0) throw new Win32Exception();
        handle.Dispose();
        return handle = new SafeFileHandle(duplicate, ownsHandle: true);
    }

    private static void Check(int result)
    {
        if (result != 0) throw new Win32Exception(result);
    }

    private sealed class Utf8Vector : IDisposable
    {
        public nint[] Entries { get; }

        public Utf8Vector(IEnumerable<string> values)
        {
            var strings = values.ToArray();
            Entries = new nint[strings.Length + 1];
            try
            {
                for (var index = 0; index < strings.Length; index++)
                    Entries[index] = Marshal.StringToCoTaskMemUTF8(strings[index]);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            foreach (var pointer in Entries) Marshal.FreeCoTaskMem(pointer);
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int pipe2([Out] int[] descriptors, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int fcntl(int descriptor, int command, int argument);

    [DllImport("libc")]
    private static extern int access([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);

    [DllImport("libc")]
    private static extern int posix_spawnattr_init(nint attributes);

    [DllImport("libc")]
    private static extern int posix_spawnattr_destroy(nint attributes);

    [DllImport("libc")]
    private static extern int posix_spawnattr_setflags(nint attributes, short flags);

    [DllImport("libc")]
    private static extern int posix_spawnattr_setpgroup(nint attributes, int group);

    [DllImport("libc")]
    private static extern int posix_spawn_file_actions_init(nint actions);

    [DllImport("libc")]
    private static extern int posix_spawn_file_actions_destroy(nint actions);

    [DllImport("libc")]
    private static extern int posix_spawn_file_actions_addopen(nint actions, int descriptor,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mode);

    [DllImport("libc")]
    private static extern int posix_spawn_file_actions_adddup2(nint actions, int source, int destination);

    [DllImport("libc")]
    private static extern int posix_spawn(out int pid, [MarshalAs(UnmanagedType.LPUTF8Str)] string executable,
        nint actions, nint attributes, nint[] arguments, nint[] environment);

    [DllImport("libc", SetLastError = true)]
    private static extern int waitpid(int pid, out int status, int options);

    [DllImport("libc")]
    private static extern int kill(int pid, int signal);
}
