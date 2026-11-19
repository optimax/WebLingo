using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WebLingo.Platform.DotNet;

internal sealed class WindowsProcessCapture(SafeWaitHandle job, SafeWaitHandle process, Stream output, Stream error)
    : IDisposable
{
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateNoWindow = 0x08000000;
    private const uint UseStandardHandles = 0x00000100;
    private const uint InheritHandle = 1;
    private const uint WaitTimeout = 258;
    private const nuint HandleList = 0x00020002;
    private const nuint JobList = 0x0002000D;

    public Stream Output { get; } = output;
    public Stream Error { get; } = error;

    public static WindowsProcessCapture Start(string program, IReadOnlyList<string> arguments)
    {
        var pipeSecurity = new SecurityAttributes { Size = Marshal.SizeOf<SecurityAttributes>(), Inherit = 1 };
        Check(CreatePipe(out var stdin, out var unusedInput, ref pipeSecurity, 0));
        using var childInput = stdin;
        unusedInput.Dispose(); // EOF from the first read.
        Check(CreatePipe(out var stdout, out var childOutput, ref pipeSecurity, 0));
        using var childStdout = childOutput;
        SafeFileHandle? stderr = null;
        SafeWaitHandle? job = null;
        SafeWaitHandle? process = null;
        FileStream? output = null;
        FileStream? error = null;
        try
        {
            Check(CreatePipe(out stderr, out var childError, ref pipeSecurity, 0));
            using var childStderr = childError;
            Check(SetHandleInformation(stdout, InheritHandle, 0));
            Check(SetHandleInformation(stderr, InheritHandle, 0));
            output = new FileStream(stdout, FileAccess.Read);
            error = new FileStream(stderr, FileAccess.Read);

            job = CreateJobObject(0, 0);
            Check(!job.IsInvalid);
            var size = (nuint)0;
            _ = InitializeProcThreadAttributeList(0, 2, 0, ref size);
            var attributes = Marshal.AllocHGlobal(checked((nint)size));
            var inherited = Marshal.AllocHGlobal(3 * IntPtr.Size);
            var jobs = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Check(InitializeProcThreadAttributeList(attributes, 2, 0, ref size));
                try
                {
                    Marshal.WriteIntPtr(inherited, 0, childInput.DangerousGetHandle());
                    Marshal.WriteIntPtr(inherited, IntPtr.Size, childStdout.DangerousGetHandle());
                    Marshal.WriteIntPtr(inherited, 2 * IntPtr.Size, childStderr.DangerousGetHandle());
                    Marshal.WriteIntPtr(jobs, job.DangerousGetHandle());
                    Check(UpdateProcThreadAttribute(attributes, 0, HandleList, inherited, (nuint)(3 * IntPtr.Size), 0, 0));
                    Check(UpdateProcThreadAttribute(attributes, 0, JobList, jobs, (nuint)IntPtr.Size, 0, 0));
                    var start = new StartupInfoEx
                    {
                        Size = Marshal.SizeOf<StartupInfoEx>(),
                        Flags = UseStandardHandles,
                        Input = childInput.DangerousGetHandle(),
                        Output = childStdout.DangerousGetHandle(),
                        Error = childStderr.DangerousGetHandle(),
                        Attributes = attributes
                    };
                    var file = program.Trim();
                    var command = new StringBuilder();
                    if (file.StartsWith('"') && file.EndsWith('"')) command.Append(file);
                    else command.Append('"').Append(file).Append('"');
                    foreach (var argument in arguments) AppendArgument(command, argument);
                    Check(CreateProcess(0, command, 0, 0, true, ExtendedStartupInfoPresent | CreateNoWindow, 0, 0,
                        ref start, out var started));
                    process = new SafeWaitHandle(started.Process, ownsHandle: true);
                    using var thread = new SafeWaitHandle(started.Thread, ownsHandle: true);
                }
                finally { DeleteProcThreadAttributeList(attributes); }
            }
            finally
            {
                Marshal.FreeHGlobal(jobs);
                Marshal.FreeHGlobal(inherited);
                Marshal.FreeHGlobal(attributes);
            }
            return new WindowsProcessCapture(job, process, output, error);
        }
        catch
        {
            if (process is not null) TerminateJobObject(job!, 1);
            process?.Dispose();
            job?.Dispose();
            output?.Dispose();
            error?.Dispose();
            stdout.Dispose();
            stderr?.Dispose();
            throw;
        }
    }

    public async Task<int> WaitForExitAsync(CancellationToken cancellation)
    {
        uint wait;
        while ((wait = WaitForSingleObject(process, 0)) == WaitTimeout)
            await Task.Delay(10, cancellation).ConfigureAwait(false);
        Check(wait == 0);
        Check(GetExitCodeProcess(process, out var code));
        return code;
    }

    public void Stop()
    {
        // Job membership persists even if the launcher has exited and its descendants are orphaned.
        if (TerminateJobObject(job, 1)) _ = WaitForSingleObject(process, 1000);
    }

    public void Dispose()
    {
        Output.Dispose();
        Error.Dispose();
        process.Dispose();
        job.Dispose();
    }

    private static void AppendArgument(StringBuilder command, string value)
    {
        command.Append(' ');
        if (value.Length > 0 && !value.Any(static character => char.IsWhiteSpace(character) || character == '"'))
        {
            command.Append(value);
            return;
        }
        command.Append('"');
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }
            command.Append('\\', character == '"' ? 2 * backslashes + 1 : backslashes).Append(character);
            backslashes = 0;
        }
        command.Append('\\', 2 * backslashes).Append('"');
    }

    private static void Check(bool succeeded)
    {
        if (!succeeded) throw new Win32Exception();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Size;
        public nint Descriptor;
        public int Inherit;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public int Size;
        public nint Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, ReservedSize;
        public nint ReservedPointer, Input, Output, Error, Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process, Thread;
        public uint ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write,
        ref SecurityAttributes security, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true)]
    private static extern SafeWaitHandle CreateJobObject(nint security, nint name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(nint attributes, int count, uint flags, ref nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(nint attributes, uint flags, nuint attribute, nint value,
        nuint size, nint previous, nint returned);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(nint attributes);

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool CreateProcess(nint application, StringBuilder command, nint processSecurity,
        nint threadSecurity, bool inheritHandles, uint flags, nint environment, nint directory, ref StartupInfoEx start,
        out ProcessInformation process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(SafeWaitHandle job, uint code);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint timeout);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(SafeWaitHandle process, out int code);
}
