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
        ("Module", WebLingoValueClass.Module)
    ];

    public static readonly (string Name, string Code)[] ErrorClasses =
    [
        ("InitializationError", WebLingoDiagnosticCodes.ReadBeforeInitialization), ("CallError", WebLingoDiagnosticCodes.NotAFunction),
        ("ArgumentError", WebLingoDiagnosticCodes.WrongArgumentCount), ("TypeError", WebLingoDiagnosticCodes.InvalidOperation),
        ("OverflowError", WebLingoDiagnosticCodes.IntegerOverflow), ("IndexError", WebLingoDiagnosticCodes.IndexOutOfRange),
        ("MemberError", WebLingoDiagnosticCodes.NoSuchMember), ("KeyError", WebLingoDiagnosticCodes.NoSuchKey)
    ];

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
    /// A built-in class's source: a kind of runtime error's, taking a message; a kind of value that a class may extend,
    /// holding a value of its kind, which its constructor gives (<c>hold</c>, section 3.7); any other, empty.
    /// </summary>
    public static string Source(string name) => name switch
    {
        _ when IsErrorClass(name) => $"class {name} extends Error {{ constructor(message) {{ super(message); }} }}",
        "Boolean" => "class Boolean { constructor(value = false) { hold(this, value); } }",
        "Integer" => "class Integer { constructor(value = 0) { hold(this, value); } }",
        "Number" => "class Number { constructor(value = 0.0) { hold(this, value); } }",
        "String" => "class String { constructor(value = \"\") { hold(this, value); } }",
        "Function" => "class Function { constructor(f = function () {}) { hold(this, f); } }",
        // A metaclass's super(name, base) names and bases the class it makes (section 3.7).
        "Class" => "class Class { constructor(name = null, base = null) { hold(this, [name, base]); } }",
        _ => $"class {name} {{}}"
    };
}
