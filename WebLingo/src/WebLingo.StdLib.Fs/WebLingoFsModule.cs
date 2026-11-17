using WebLingo.Core;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.StdLib.Fs;

/// <summary>
/// <c>std:fs</c>, a standard module (specification, sections 3.9 and 7.4), a native module installed with the runtime in
/// the <c>std</c> scheme's folder as <c>fs.dll</c>: files and folders through the platform's file system
/// (<see cref="WebLingoHeap.FileSystem"/>). Each operation is asynchronous, a task that the work, run off the worker,
/// completes, and has a synchronous form named with <c>Sync</c>, which blocks the worker (WL-P004): <c>readText</c>,
/// <c>writeText</c>, <c>appendText</c>, <c>exists</c>, <c>remove</c>, <c>list</c>, <c>makeFolder</c> and
/// <c>removeFolder</c>; and the class <c>File</c>, a file opened as a handle, <c>File.open(path, mode)</c>, its lines read
/// by <c>read()</c>, its text written by <c>write(text)</c>, closed by <c>dispose()</c>. Text is UTF-8 unless an encoding
/// is named. A failure is an <c>IOError</c> (WL3011) naming the path as the script gave it, never the host's own.
/// </summary>
public sealed class WebLingoFsModule : IWebLingoNativeModule
{
    private const string Utf8 = "utf-8";

    public void Register(IWebLingoNativeRegistry registry)
    {
        Operation(registry, "readText", 2, 1, "read", (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out string path, out Func<IWebLingoFileSystem, string> work,
            out WebLingoRuntimeError? error) =>
        {
            work = null!;
            if (!TryPath(a, 0, heap, out path, out error) || !TryEncoding(a, 1, heap, out var encoding, out error)) return false;
            var at = path;
            work = fs => fs.ReadText(at, encoding);
            return true;
        }, Text);
        foreach (var (name, append) in new[] { ("writeText", false), ("appendText", true) })
        {
            Operation(registry, name, 3, 1, "written", (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out string path, out Func<IWebLingoFileSystem, bool> work,
                out WebLingoRuntimeError? error) =>
            {
                work = null!;
                if (!TryPath(a, 0, heap, out path, out error) || !TryString(a, 1, "text", heap, out var text, out error)
                    || !TryEncoding(a, 2, heap, out var encoding, out error))
                {
                    return false;
                }
                var at = path;
                work = fs =>
                {
                    fs.WriteText(at, text, encoding, append);
                    return true;
                };
                return true;
            }, Nothing);
        }
        PathOperation(registry, "exists", "looked for", (fs, path) => fs.Exists(path), Boolean);
        PathOperation(registry, "remove", "removed", (fs, path) =>
        {
            fs.Remove(path);
            return true;
        }, Nothing);
        PathOperation(registry, "list", "listed", (fs, path) => fs.List(path), Names);
        PathOperation(registry, "makeFolder", "made", (fs, path) =>
        {
            fs.MakeFolder(path);
            return true;
        }, Nothing);
        PathOperation(registry, "removeFolder", "removed", (fs, path) =>
        {
            fs.RemoveFolder(path);
            return true;
        }, Nothing);

        var file = registry.Class("File");
        foreach (var (name, sync) in new[] { ("open", false), ("openSync", true) })
        {
            file.DeclareFactory(name, 3, 1, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
                Open(file, sync, a, heap, out result, out error));
        }
        foreach (var (name, sync) in new[] { ("read", false), ("readSync", true) })
        {
            file.DeclareMethod(name, 0, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            {
                result = WebLingoValue.Null;
                if (!file.TryGetHostObject<OpenFile>(a[0], heap, out var open, out error)) return false;
                return Run(sync, heap, "read", open.Path, () => open.File.ReadLine(), Line, out result, out error);
            });
        }
        foreach (var (name, sync) in new[] { ("write", false), ("writeSync", true) })
        {
            file.DeclareMethod(name, 1, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            {
                result = WebLingoValue.Null;
                if (!file.TryGetHostObject<OpenFile>(a[0], heap, out var open, out error) || !TryString(a, 1, "text", heap, out var text, out error))
                {
                    return false;
                }
                return Run(sync, heap, "written", open.Path, () =>
                {
                    open.File.Write(text);
                    return true;
                }, Nothing, out result, out error);
            });
        }
        file.DeclareMethod("dispose", 0, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            // Closed once: disposing again does nothing (section 3.4).
            result = WebLingoValue.Null;
            if (!file.TryReleaseHostObject(a[0], heap, out var held, out error)) return false;
            (held as IDisposable)?.Dispose();
            return true;
        });
    }

    /// <summary>A file opened as a handle, and its path, as the script gave it, which its errors name.</summary>
    private sealed record OpenFile(IWebLingoFile File, string Path) : IDisposable
    {
        public void Dispose() => File.Dispose();
    }

    /// <summary>An operation's arguments checked: the path the script gave, and the work on the file system; or why not.</summary>
    private delegate bool Prepare<T>(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out string path, out Func<IWebLingoFileSystem, T> work,
        out WebLingoRuntimeError? error);

    /// <summary>An operation, asynchronous as <paramref name="name"/>, synchronous with <c>Sync</c> after it.</summary>
    private static void Operation<T>(IWebLingoNativeRegistry registry, string name, int arity, int optional, string doing, Prepare<T> prepare,
        WebLingoOffWorkerFinish<T> finish)
    {
        foreach (var sync in new[] { false, true })
        {
            registry.Function(sync ? name + "Sync" : name, arity, optional,
                (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
                {
                    result = WebLingoValue.Null;
                    if (!TryFileSystem(heap, out var fs, out error) || !prepare(a, heap, out var path, out var work, out error)) return false;
                    return Run(sync, heap, doing, path, () => work(fs), finish, out result, out error);
                });
        }
    }

    /// <summary>An operation on a path alone.</summary>
    private static void PathOperation<T>(IWebLingoNativeRegistry registry, string name, string doing, Func<IWebLingoFileSystem, string, T> operation,
        WebLingoOffWorkerFinish<T> finish) =>
        Operation(registry, name, 1, 0, doing, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out string path, out Func<IWebLingoFileSystem, T> work,
            out WebLingoRuntimeError? error) =>
        {
            work = null!;
            if (!TryPath(a, 0, heap, out path, out error)) return false;
            var at = path;
            work = fs => operation(fs, at);
            return true;
        }, finish);

    /// <summary>
    /// Runs the work: at once, blocking, for a synchronous form, its result made a value; or off the worker, a task the
    /// result completes. A file system's failure is an <c>IOError</c> naming the path as the script gave it.
    /// </summary>
    private static bool Run<T>(bool sync, WebLingoHeap heap, string doing, string path, Func<T> work, WebLingoOffWorkerFinish<T> finish,
        out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!sync)
        {
            if (heap.Scheduler is not { } scheduler)
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "std:fs's asynchronous functions need a VM that runs tasks.");
                return false;
            }
            return scheduler.TryRunOffWorker(work, finish, exception => exception is WebLingoFileSystemException failure ? Failed(doing, path, failure) : null,
                out result, out error);
        }
        T value;
        try
        {
            value = work();
        }
        catch (WebLingoFileSystemException failure)
        {
            error = Failed(doing, path, failure);
            return false;
        }
        return finish(value, heap, out result, out error);
    }

    /// <summary><c>File.open(path, mode)</c>: an instance of the class it was called on, holding the file, opened.</summary>
    private static bool Open(WebLingoNativeClass file, bool sync, ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var classValue = a[0];
        if (!TryFileSystem(heap, out var fs, out error) || !TryPath(a, 1, heap, out var path, out error)
            || !TryString(a, 2, "mode", heap, out var modeName, out error) || !TryEncoding(a, 3, heap, out var encoding, out error))
        {
            return false;
        }
        WebLingoFileMode mode;
        switch (modeName)
        {
            case "r": mode = WebLingoFileMode.Read; break;
            case "w": mode = WebLingoFileMode.Write; break;
            case "a": mode = WebLingoFileMode.Append; break;
            default:
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"A file is opened to read it, \"r\", to write it, \"w\", or to append to it, \"a\", not \"{modeName}\".");
                return false;
        }
        return Run(sync, heap, mode == WebLingoFileMode.Read ? "read" : "written", path, () => fs.Open(path, mode, encoding),
            (IWebLingoFile opened, WebLingoHeap h, out WebLingoValue made, out WebLingoRuntimeError? wrong) =>
            {
                if (file.TryMake(classValue, h, out made, out wrong) && file.TrySetHostObject(made, new OpenFile(opened, path), h, out wrong)) return true;
                opened.Dispose();
                return false;
            }, out result, out error);
    }

    /// <summary>
    /// A file system's failure as a runtime error: an encoding that is not there a <c>TypeError</c>; a path the site may not
    /// reach a <c>PermissionError</c> (WL3016, section 7.13, "File capabilities"); any other an <c>IOError</c>.
    /// </summary>
    private static WebLingoRuntimeError Failed(string doing, string path, WebLingoFileSystemException failure) => new(failure.Kind switch
    {
        WebLingoFileErrorKind.UnknownEncoding => WebLingoDiagnosticCodes.InvalidOperation,
        WebLingoFileErrorKind.NotAllowed => WebLingoDiagnosticCodes.PermissionDenied,
        _ => WebLingoDiagnosticCodes.IOFailure
    }, $"'{path}' cannot be {doing}: {failure.Message}.");

    private static bool TryFileSystem(WebLingoHeap heap, out IWebLingoFileSystem fs, out WebLingoRuntimeError? error)
    {
        fs = heap.FileSystem!;
        error = heap.FileSystem is null ? new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "This host gives scripts no files.") : null;
        return error is null;
    }

    private static bool TryPath(ReadOnlySpan<WebLingoValue> a, int index, WebLingoHeap heap, out string path, out WebLingoRuntimeError? error)
    {
        if (!TryString(a, index, "path", heap, out path, out error)) return false;
        if (path.Length > 0 && path.IndexOf('\0', StringComparison.Ordinal) < 0) return true;
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "A path is a string of one character or more, without '\\0'.");
        return false;
    }

    private static bool TryString(ReadOnlySpan<WebLingoValue> a, int index, string what, WebLingoHeap heap, out string text, out WebLingoRuntimeError? error)
    {
        text = "";
        error = null;
        if (a[index].Kind == WebLingoValueKind.String)
        {
            text = heap.GetString(a[index]);
            return true;
        }
        var article = what[0] is 'a' or 'e' or 'i' or 'o' or 'u' ? "An" : "A";
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
            $"{article} {what} is a string, not {WebLingoValueFormatter.Describe(a[index], heap)}.");
        return false;
    }

    /// <summary>An encoding's name, or UTF-8's for <c>null</c>, an argument left out.</summary>
    private static bool TryEncoding(ReadOnlySpan<WebLingoValue> a, int index, WebLingoHeap heap, out string encoding, out WebLingoRuntimeError? error)
    {
        encoding = Utf8;
        error = null;
        return a[index].Kind == WebLingoValueKind.Null || TryString(a, index, "encoding's name", heap, out encoding, out error);
    }

    private static bool Text(string text, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        heap.TryAllocateString(text, out result, out error);

    private static bool Line(string? line, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        if (line is not null) return heap.TryAllocateString(line, out result, out error);
        result = WebLingoValue.Null;
        error = null;
        return true;
    }

    private static bool Boolean(bool value, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.FromBoolean(value);
        error = null;
        return true;
    }

    private static bool Nothing(bool value, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        error = null;
        return true;
    }

    /// <summary>A folder's names, as an array of strings, each kept as a root while the next is made.</summary>
    private static bool Names(IReadOnlyList<string> names, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var values = new WebLingoValue[names.Count];
        heap.AddRoots(values);
        try
        {
            for (var i = 0; i < names.Count; i++)
            {
                if (!heap.TryAllocateString(names[i], out values[i], out error)) return false;
            }
            return heap.TryAllocateArray(values, 0, values.Length, out result, out error);
        }
        finally
        {
            heap.RemoveLastRoots(values);
        }
    }
}
