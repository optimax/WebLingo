using WebLingo.Runtime;
namespace WebLingo.StdLib.Path;

/// <summary>Lexical path operations accepting either separator and emitting its host's separator.</summary>
public sealed class WebLingoPathModule : IWebLingoNativeModule
{
    private static readonly char Separator = System.IO.Path.DirectorySeparatorChar;

    public void Register(IWebLingoNativeRegistry registry)
    {
        registry.RestFunction("join", 1, Join);
        RegisterPart(registry, "dirname", path =>
        {
            var root = RootLength(path);
            if (path.Length == root) return path;
            var at = path.LastIndexOf(Separator);
            return TrimEndUnlessRoot(path[..System.Math.Max(root, at)], root);
        });
        RegisterPart(registry, "basename", BaseName);
        RegisterPart(registry, "extension", path =>
        {
            var name = BaseName(path);
            var dot = name.LastIndexOf('.');
            return dot > 0 && dot < name.Length - 1 ? name[dot..] : "";
        });
    }

    private static void RegisterPart(IWebLingoNativeRegistry registry, string name, Func<string, string> part)
    {
        registry.Function(name, 1, (ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap,
            out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            if (!ReadPath(args[0], new(name, 0, 1, "the path", "a path string without NUL"), heap, out var path, out error))
                return false;
            return heap.TryAllocateString(part(Trim(path)), out result, out error);
        });
    }

    private static bool Join(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var parts = heap.GetArray(args[0]);
        var combined = "";
        for (var i = 0; i < parts.Count; i++)
        {
            if (!ReadPath(parts[i], new("join", i, parts.Count, "the path part", "a path string without NUL"),
                    heap, out var part, out error))
                return false;
            if (part.Length == 0) continue;
            if (combined.Length == 0 || RootLength(part) > 0)
            {
                combined = part;
                continue;
            }
            if (combined[^1] != Separator && !(combined.Length == 2 && IsDrivePath(combined))) combined += Separator;
            combined += part;
        }
        return heap.TryAllocateString(combined, out result, out error);
    }

    private static bool ReadPath(WebLingoValue value, WebLingoArgument argument, WebLingoHeap heap,
        out string path, out WebLingoRuntimeError? error)
    {
        value = heap.Held(value);
        if (value.Kind == WebLingoValueKind.String)
        {
            path = heap.GetString(value);
            if (!path.Contains('\0'))
            {
                path = path.Replace('/', Separator).Replace('\\', Separator);
                error = null;
                return true;
            }
        }
        path = "";
        error = argument.Wrong(value, heap);
        return false;
    }

    private static string BaseName(string path)
    {
        var start = System.Math.Max(RootLength(path), path.LastIndexOf(Separator) + 1);
        return path[start..];
    }

    private static string Trim(string path) => TrimEndUnlessRoot(path, RootLength(path));

    private static bool IsDrivePath(string path) => path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';

    private static int RootLength(string path)
    {
        if (IsDrivePath(path)) return path.Length > 2 && path[2] == Separator ? 3 : 2;
        if (path.Length == 0 || path[0] != Separator) return 0;
        if (path.Length < 3 || path[1] != Separator || path[2] == Separator) return 1;

        var serverEnd = path.IndexOf(Separator, 2);
        if (serverEnd < 0 || serverEnd + 1 == path.Length || path[serverEnd + 1] == Separator) return 1;
        var shareEnd = path.IndexOf(Separator, serverEnd + 1);
        return shareEnd < 0 ? path.Length : shareEnd;
    }
    private static string TrimEndUnlessRoot(string path, int root)
    {
        var end = path.Length;
        while (end > root && path[end - 1] == Separator) end--;
        return path[..end];
    }
}
