namespace WebLingo.Bytecode;

/// <summary>What both verifiers check of a module's classes (specification, section 3.7).</summary>
internal static class WsClassTables
{
    /// <summary>
    /// The first fault of the classes, or null: each has a name, its methods have names, none twice, and its
    /// constructor and methods are functions that take <c>this</c> (<paramref name="isMethod"/>).
    /// </summary>
    public static string? Check(IReadOnlyList<WsClassInfo>? classes, Func<int, bool> isMethod)
    {
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
        }
        return null;
    }

    /// <summary>
    /// The functions of a class: its constructor, if it has one, then its methods. A list, not an iterator, whose
    /// generated code would use <c>System.Environment</c>, which the portable core does not.
    /// </summary>
    public static List<int> Functions(WsClassInfo info)
    {
        var functions = new List<int>();
        if (info.Constructor != -1) functions.Add(info.Constructor);
        foreach (var method in info.Methods) functions.Add(method.Function);
        return functions;
    }
}
