using WebLingo.Core;
namespace WebLingo.Bytecode;

/// <summary>What both verifiers check of a module's classes (specification, section 3.7).</summary>
internal static class WsClassTables
{
    /// <summary>
    /// The first fault of the classes, or null: each has a name, its methods have names, none twice, and its
    /// constructor and methods are functions that take <c>this</c>; its static fields and methods have names, none
    /// twice among them, and its static methods are functions that do not. <paramref name="kind"/> tells what a
    /// function is: 1 a method, 0 another function, -1 none.
    /// </summary>
    public static string? Check(IReadOnlyList<WsClassInfo>? classes, Func<int, int> kind)
    {
        bool isMethod(int function) => kind(function) == 1;
        if (classes is null) return "The module has no class table.";
        for (var i = 0; i < classes.Count; i++)
        {
            var info = classes[i];
            if (info is null || string.IsNullOrEmpty(info.Name) || info.Methods is null) return $"Class {i} is missing or has no name.";
            if (info.Constructor != -1 && !isMethod(info.Constructor))
            {
                return $"Class {i} ('{info.Name}'): its constructor, function {info.Constructor}, is not a method.";
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var method in info.Methods)
            {
                if (string.IsNullOrEmpty(method.Name) || !names.Add(method.Name))
                {
                    return $"Class {i} ('{info.Name}'): a method has no name, or one it shares.";
                }
                if (!isMethod(method.Function)) return $"Class {i} ('{info.Name}'): '{method.Name}', function {method.Function}, is not a method.";
            }
            if (info.StaticMethods is null || info.StaticFields is null) return $"Class {i} ('{info.Name}') has no static members.";
            var statics = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in info.StaticFields)
            {
                if (string.IsNullOrEmpty(field) || !statics.Add(field))
                {
                    return $"Class {i} ('{info.Name}'): a static field has no name, or one it shares.";
                }
            }
            foreach (var method in info.StaticMethods)
            {
                if (string.IsNullOrEmpty(method.Name) || !statics.Add(method.Name))
                {
                    return $"Class {i} ('{info.Name}'): a static method has no name, or one it shares.";
                }
                if (kind(method.Function) != 0)
                {
                    return $"Class {i} ('{info.Name}'): static '{method.Name}', function {method.Function}, is not a function without 'this'.";
                }
            }
        }
        return null;
    }

    /// <summary>
    /// The first fault of a module's tables of built-in classes, or null: the kinds of values', none or one for each kind,
    /// each -1 or a class; and the runtime errors', each a class, with a code (section 3.7).
    /// </summary>
    public static string? CheckRoles(int classCount, IReadOnlyList<int>? valueClasses, IReadOnlyList<WsErrorClass>? errorClasses)
    {
        if (valueClasses is null || errorClasses is null) return "The module has no tables of built-in classes.";
        if (valueClasses.Count != 0 && valueClasses.Count != (int)WsValueClass.Task + 1)
        {
            return $"The module has {valueClasses.Count} classes of values, not one for each kind.";
        }
        foreach (var index in valueClasses)
        {
            if (index < -1 || index >= classCount) return $"A class of values, {index}, is not -1 or one of its classes.";
        }
        foreach (var (code, index) in errorClasses)
        {
            if (string.IsNullOrEmpty(code) || index < 0 || index >= classCount)
            {
                return $"A runtime error's class, {index}, is not one of its classes, or has no code.";
            }
        }
        return null;
    }

    /// <summary>
    /// The functions of a class: its constructor, if it has one, then its methods, then its static methods. A list, not an iterator, whose
    /// generated code would use <c>System.Environment</c>, which the portable core does not.
    /// </summary>
    public static List<int> Functions(WsClassInfo info)
    {
        var functions = new List<int>();
        if (info.Constructor != -1) functions.Add(info.Constructor);
        foreach (var method in info.Methods) functions.Add(method.Function);
        foreach (var method in info.StaticMethods) functions.Add(method.Function);
        return functions;
    }
}
