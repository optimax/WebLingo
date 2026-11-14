using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using WebLingo.Compiler;
using WebLingo.Runtime;
namespace WebLingo.Cli;

/// <summary>
/// The native modules a command line's programs import (specification, section 3.9, "Native modules"): by a scheme and a
/// module's name, <c>native:sqlite</c>, its assembly, <c>sqlite.dll</c>, being in that scheme's folder; or by the path of an
/// assembly, from the importing file's folder, which must be in a scheme's folder too, so that no script can write an
/// assembly and import it. An assembly is loaded once, into the host's own context, and never unloaded; its one public
/// class implementing <see cref="IWebLingoNativeModule"/> registers its functions, each then one of the host's built-ins,
/// named <c>scheme:module.function</c>, which no script can name. <c>std:system</c> a command line loads itself, its
/// functions the host's built-ins by their own names (<see cref="WebLingoCommandLine"/>); any other <c>std</c> module, such
/// as <c>std:tasks</c>, is imported as any native module is.
/// </summary>
public sealed class WebLingoNativeModuleLoader(IReadOnlyDictionary<string, string> schemes, WebLingoBuiltinRegistry builtins)
    : IWebLingoNativeResolver
{
    private const string Extension = ".dll";

    private readonly Dictionary<string, WebLingoNativeModuleInfo> _loaded = new(StringComparer.Ordinal);

    /// <summary>A native module's functions, as the host's built-ins, by the names it exports them by.</summary>
    public IReadOnlyList<IWebLingoBuiltinFunction> FunctionsOf(WebLingoNativeModuleInfo module)
    {
        var functions = new List<IWebLingoBuiltinFunction>();
        foreach (var export in module.Exports)
        {
            if (builtins.TryGetBuiltin(export.Builtin, out var function)) functions.Add(Named(export.Name, function));
        }
        return functions;
    }

    private static IWebLingoBuiltinFunction Named(string name, IWebLingoBuiltinFunction function) =>
        function is IWebLingoTextBuiltin ? new RenamedText(name, function) : new Renamed(name, function);

    private class Renamed(string name, IWebLingoBuiltinFunction function) : IWebLingoBuiltinFunction
    {
        public string Name => name;
        public int Arity => function.Arity;

        public bool TryInvoke(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            function.TryInvoke(arguments, heap, out result, out error);
    }

    private sealed class RenamedText(string name, IWebLingoBuiltinFunction function)
        : Renamed(name, function), IWebLingoTextBuiltin;

    /// <summary>
    /// A configured scheme's path, but <c>std:system</c>, which the compiler makes itself, or an assembly's; any other is a
    /// file's, as a script's import is.
    /// </summary>
    public bool Names(string path) =>
        (TrySplit(path, out var scheme, out _) && path != "std:system" && schemes.ContainsKey(scheme))
        || path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    public bool TryResolve(string importer, string path, [NotNullWhen(true)] out WebLingoNativeModuleInfo? module, out string? error)
    {
        module = null;
        error = null;
        string name, file;
        if (TrySplit(path, out var scheme, out var moduleName))
        {
            if (!schemes.TryGetValue(scheme, out var folder))
            {
                error = $"there is no scheme '{scheme}'";
                return false;
            }
            if (moduleName.Length == 0 || moduleName.IndexOfAny(['/', '\\', ':']) >= 0 || moduleName.StartsWith('.'))
            {
                error = "a native module's name is a file's name in its scheme's folder";
                return false;
            }
            name = scheme + ":" + moduleName;
            file = Path.Combine(folder, moduleName + Extension);
        }
        else
        {
            try
            {
                var from = Path.GetDirectoryName(Path.GetFullPath(importer)) ?? "";
                file = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(from, path));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                error = exception.Message;
                return false;
            }
            if (SchemeOf(file) is not { } found)
            {
                error = "it is not in a configured scheme's folder";
                return false;
            }
            name = found + ":" + Path.GetFileNameWithoutExtension(file);
        }
        if (_loaded.TryGetValue(name, out module)) return true;
        if (!File.Exists(file))
        {
            error = name.StartsWith("std:", StringComparison.Ordinal) ? "there is no such standard module" : "there is no such native module";
            return false;
        }
        if (!TryLoad(name, file, out module, out error)) return false;
        _loaded.Add(name, module);
        return true;
    }

    /// <summary><c>scheme:module</c>, a scheme being two or more letters and digits, so that <c>C:</c> is a drive's.</summary>
    private static bool TrySplit(string path, out string scheme, out string module)
    {
        var colon = path.IndexOf(':', StringComparison.Ordinal);
        scheme = colon > 0 ? path[..colon] : "";
        module = colon > 0 ? path[(colon + 1)..] : "";
        return colon > 0 && WebLingoSchemes.IsName(scheme);
    }

    /// <summary>The scheme whose folder holds <paramref name="file"/> itself, if any; <c>std</c>'s last.</summary>
    private string? SchemeOf(string file)
    {
        string? std = null;
        foreach (var (scheme, folder) in schemes)
        {
            var relative = Path.GetRelativePath(folder, file);
            if (Path.IsPathRooted(relative) || relative.IndexOfAny(['/', '\\']) >= 0 || relative.StartsWith("..", StringComparison.Ordinal))
            {
                continue;
            }
            if (scheme != "std") return scheme;
            std = scheme;
        }
        return std;
    }

    /// <summary>The assembly <paramref name="file"/>, loaded, and its module's functions registered as built-ins.</summary>
    private bool TryLoad(string name, string file, [NotNullWhen(true)] out WebLingoNativeModuleInfo? module, out string? error)
    {
        module = null;
        Type[] types;
        try
        {
            types = Assembly.LoadFrom(file).GetExportedTypes();
        }
        catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or IOException
                                              or TypeLoadException or ReflectionTypeLoadException or NotSupportedException)
        {
            error = $"it cannot be loaded: {exception.Message}";
            return false;
        }
        var found = new List<Type>();
        foreach (var type in types)
        {
            if (typeof(IWebLingoNativeModule).IsAssignableFrom(type) && type is { IsClass: true, IsAbstract: false }
                && type.GetConstructor(Type.EmptyTypes) is not null)
            {
                found.Add(type);
            }
        }
        if (found.Count != 1)
        {
            error = found.Count == 0
                ? "its assembly has no public class implementing IWebLingoNativeModule"
                : "its assembly has more than one public class implementing IWebLingoNativeModule";
            return false;
        }
        var registry = new Registry(name);
        try
        {
            ((IWebLingoNativeModule)Activator.CreateInstance(found[0])!).Register(registry);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            error = $"it failed to register its exports: {exception.InnerException?.Message ?? exception.Message}";
            return false;
        }
        if (registry.Error is { } wrong)
        {
            error = wrong;
            return false;
        }
        var exports = new List<WebLingoNativeExport>();
        foreach (var function in registry.Functions)
        {
            if (!builtins.TryGetBuiltin(function.Name, out _)) builtins.Register(function);
            exports.Add(new WebLingoNativeExport(function.Export, function.Name, function.Arity));
        }
        module = new WebLingoNativeModuleInfo(name, exports);
        error = null;
        return true;
    }

    /// <summary>What a native module registers its exports with: each function checked, the first error kept.</summary>
    private sealed class Registry(string module) : IWebLingoNativeRegistry
    {
        public List<NativeFunction> Functions { get; } = [];
        public string? Error { get; private set; }

        public void Function(string name, int arity, WebLingoNativeFunction function) => Add(name, arity, function, false);

        public void TextFunction(string name, int arity, WebLingoNativeFunction function) => Add(name, arity, function, true);

        private void Add(string name, int arity, WebLingoNativeFunction function, bool writesText)
        {
            if (Error is not null) return;
            if (!IsName(name)) Error = $"it registers a function named '{name}', which is not a name";
            else if (arity < 0) Error = $"it registers '{name}' with an arity of {arity}";
            else if (Functions.Exists(f => f.Export == name)) Error = $"it registers '{name}' twice";
            else if (writesText) Functions.Add(new NativeTextFunction(module + "." + name, name, arity, function));
            else Functions.Add(new NativeFunction(module + "." + name, name, arity, function));
        }

        private static bool IsName(string name)
        {
            if (string.IsNullOrEmpty(name) || char.IsAsciiDigit(name[0])) return false;
            foreach (var c in name)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '_') return false;
            }
            return true;
        }
    }

    /// <summary>A native module's function that writes text, given an instance's <c>toString()</c> (section 3.7).</summary>
    private sealed class NativeTextFunction(string name, string export, int arity, WebLingoNativeFunction function)
        : NativeFunction(name, export, arity, function), IWebLingoTextBuiltin;

    /// <summary>A native module's function, as one of the host's built-ins.</summary>
    private class NativeFunction(string name, string export, int arity, WebLingoNativeFunction function)
        : IWebLingoBuiltinFunction
    {
        public string Name => name;
        public string Export => export;
        public int Arity => arity;

        public bool TryInvoke(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            function(arguments, heap, out result, out error);
    }
}
