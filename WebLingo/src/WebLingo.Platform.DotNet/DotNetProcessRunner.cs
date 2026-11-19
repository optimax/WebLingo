using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using WebLingo.Platform.Abstractions;
namespace WebLingo.Platform.DotNet;

/// <summary>Executes a local program without invoking a shell and captures both of its output streams.</summary>
public sealed class DotNetProcessRunner : IWebLingoProcessRunner
{
    public WebLingoProcessResult Run(string program, IReadOnlyList<string> arguments, int timeoutMs, int maxOutputBytes)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var child = WindowsProcessCapture.Start(program, arguments);
                return Capture(child.Output, child.Error, child.WaitForExitAsync, child.Stop, timeoutMs, maxOutputBytes);
            }
            if (OperatingSystem.IsLinux())
            {
                using var child = LinuxProcessCapture.Start(program, arguments);
                return Capture(child.Output, child.Error, child.WaitForExitAsync, child.Stop, timeoutMs, maxOutputBytes);
            }
            return RunManaged(program, arguments, timeoutMs, maxOutputBytes);
        }
        catch (Exception failure) when (failure is Win32Exception or IOException or InvalidOperationException or NotSupportedException)
        {
            throw new WebLingoProcessException($"Could not run '{program}': {failure.Message}");
        }
    }

    private static WebLingoProcessResult RunManaged(string program, IReadOnlyList<string> arguments,
        int timeoutMs, int maxOutputBytes)
    {
        var start = new ProcessStartInfo(program, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new WebLingoProcessException($"Could not start '{program}'.");
        process.StandardInput.Close();
        return Capture(process.StandardOutput.BaseStream, process.StandardError.BaseStream, async cancellation =>
        {
            await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
            return process.ExitCode;
        }, () => StopIfRunning(process), timeoutMs, maxOutputBytes);
    }

    private static WebLingoProcessResult Capture(Stream output, Stream error,
        Func<CancellationToken, Task<int>> waitForExit, Action stop, int timeoutMs, int maxOutputBytes)
    {
        try
        {
            return CaptureAsync(output, error, waitForExit, timeoutMs, maxOutputBytes).GetAwaiter().GetResult();
        }
        catch
        {
            stop();
            throw;
        }
    }

    private static async Task<WebLingoProcessResult> CaptureAsync(Stream output, Stream error,
        Func<CancellationToken, Task<int>> waitForExit, int timeoutMs, int maxOutputBytes)
    {
        using var cancellation = new CancellationTokenSource(timeoutMs);
        var budget = new OutputBudget(maxOutputBytes, cancellation);
        var stdout = ReadOutputAsync(output, budget, cancellation.Token);
        var stderr = ReadOutputAsync(error, budget, cancellation.Token);
        var complete = Task.WhenAll(stdout, stderr);
        try
        {
            await complete.WaitAsync(cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            // On Linux the unreaped launcher reserves the process group ID until capture is complete.
            var exitCode = await waitForExit(cancellation.Token).ConfigureAwait(false);
            return new WebLingoProcessResult(exitCode, stdout.Result, stderr.Result);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // WaitAsync can finish before a pending pipe read. Observe failures as closing the process ends those reads.
            _ = complete.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            if (budget.Exceeded)
                throw new WebLingoProcessException(FormattableString.Invariant($"Process output exceeded {maxOutputBytes} bytes."));
            throw new WebLingoProcessException(FormattableString.Invariant($"The process exceeded its {timeoutMs} ms timeout."),
                timedOut: true);
        }
    }

    private static async Task<string> ReadOutputAsync(Stream input, OutputBudget budget, CancellationToken cancellation)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await input.ReadAsync(buffer, cancellation).ConfigureAwait(false);
            if (count == 0) return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
            if (!budget.Charge(count)) return "";
            output.Write(buffer, 0, count);
        }
    }

    private static void StopIfRunning(Process process)
    {
        try
        {
            if (process.HasExited) return;
            process.Kill(entireProcessTree: true);
            process.WaitForExit(1000);
        }
        catch (Exception failure) when (failure is InvalidOperationException or Win32Exception or AggregateException)
        {
            // Exit can race with termination, and an already exited child can have detached descendants.
        }
    }

    private sealed class OutputBudget(int maxBytes, CancellationTokenSource cancellation)
    {
        private long _bytes;
        public bool Exceeded => Interlocked.Read(ref _bytes) > maxBytes;

        public bool Charge(int count)
        {
            if (Interlocked.Add(ref _bytes, count) <= maxBytes) return true;
            cancellation.Cancel();
            return false;
        }
    }
}
