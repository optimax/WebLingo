using WebLingo.Core;
namespace WebLingo.Compiler;

/// <summary>
/// The built-in classes besides <c>Object</c> and <c>Error</c> (specification, section 3.7): the kinds of values',
/// which extend <c>Object</c>, and the kinds of runtime errors', which extend <c>Error</c>. The compiler adds each to a
/// program that names it; a program that uses <c>Error</c> gets every runtime error's class, which its runtime errors
/// become.
/// </summary>
internal static class WebLingoPrelude
{
    public static readonly (string Name, WebLingoValueClass Kind)[] ValueClasses =
    [
        ("Boolean", WebLingoValueClass.Boolean), ("Integer", WebLingoValueClass.Integer), ("Number", WebLingoValueClass.Number),
        ("String", WebLingoValueClass.String), ("Array", WebLingoValueClass.Array), ("Map", WebLingoValueClass.Map), ("Set", WebLingoValueClass.Set),
        ("Function", WebLingoValueClass.Function), ("Class", WebLingoValueClass.Class), ("Task", WebLingoValueClass.Task),
        ("Module", WebLingoValueClass.Module), ("Iterator", WebLingoValueClass.Iterator), ("Bytes", WebLingoValueClass.Bytes),
        ("BytesBuilder", WebLingoValueClass.BytesBuilder), ("DateTime", WebLingoValueClass.DateTime),
        ("Decimal", WebLingoValueClass.Decimal), ("Regex", WebLingoValueClass.Regex), ("Json", WebLingoValueClass.Json),
        ("Html", WebLingoValueClass.Html)
    ];

    public static ReadOnlySpan<(string Name, string Code)> ErrorClasses => WebLingoErrorClasses.BuiltIn;

    public static bool IsValueClass(string name)
    {
        foreach (var (valueClass, _) in ValueClasses)
        {
            if (valueClass == name) return true;
        }
        return false;
    }

    public static bool IsErrorClass(string name)
    {
        foreach (var (errorClass, _) in ErrorClasses)
        {
            if (errorClass == name) return true;
        }
        return false;
    }

    /// <summary>
    /// A built-in class's source, its <c>.wlg</c> file's, <c>Builtins/</c>, made a constant at build time
    /// (<see cref="WebLingoSources"/>; the owner's answer, 22 November 2026, "Built-ins Written in WebLingo, in .wlg Files"):
    /// a kind of runtime error's, taking a message, each made from <c>RuntimeError.wlg</c>, its name the error's; a kind of
    /// value that a class may extend, holding a value of its kind, which its constructor gives (<c>hold</c>, section 3.7);
    /// any other, empty.
    /// </summary>
    public static string Source(string name) => name switch
    {
        _ when IsErrorClass(name) => "class " + name + WebLingoSources.RuntimeError["class RuntimeError".Length..],
        "Boolean" => WebLingoSources.Boolean,
        "Integer" => WebLingoSources.Integer,
        "Number" => WebLingoSources.Number,
        "Decimal" => WebLingoSources.Decimal,
        // new Regex(…) makes the value itself; a class extending Regex holds one, made by the class's own new.
        "Json" => WebLingoSources.Json,
        // A value as a script takes it, Html.js(value) (section 13, "Context-aware escaping"): its JSON, its <, >, & and line
        // separators, which JSON leaves only in strings, as escapes, so that neither the script nor its attribute ends; a
        // number or a keyword between spaces, so that it runs into no name or operator beside it. Html's other members are
        // the runtime's.
        "Html" => WebLingoSources.Html,
        "Regex" => WebLingoSources.Regex,
        // Bytes, a bytes builder and a date, each made by its class's own new, as Bytes.from(…), new BytesBuilder() and
        // DateTime.utc(…) make them, which a class extending one holds (section 3.7, "No sealed classes").
        "Bytes" => WebLingoSources.Bytes,
        "BytesBuilder" => WebLingoSources.BytesBuilder,
        "DateTime" => WebLingoSources.DateTime,
        "String" => WebLingoSources.String,
        "Function" => WebLingoSources.Function,
        // A module defined in code, its name given to new, and its body its exec(), which a class extending Module may
        // override (section 3.7, "No sealed classes").
        "Module" => WebLingoSources.Module,
        // A metaclass's super(name, base) names and bases the class it makes (section 3.7).
        "Class" => WebLingoSources.Class,
        // The iterator a collection's iterate() gives, its items and its position set by the runtime (section 3.6): next()
        // past the last item is an index out of range (WL3005).
        "Iterator" => WebLingoSources.Iterator,
        _ => $"class {name} {{}}"
    };
}
