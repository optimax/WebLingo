using WebLingo.Core;
namespace WebLingo.Compiler;

/// <summary>
/// The built-in classes besides <c>Object</c> and <c>Error</c> (specification, section 3.7): the kinds of values',
/// which extend <c>Object</c>, and the kinds of runtime errors', which extend <c>Error</c>. The compiler adds each to a
/// program that names it; a program that uses <c>Error</c> gets every runtime error's class, which its runtime errors
/// become.
/// </summary>
internal static class WsPrelude
{
    public static readonly (string Name, WsValueClass Kind)[] ValueClasses =
    [
        ("Boolean", WsValueClass.Boolean), ("Integer", WsValueClass.Integer), ("Number", WsValueClass.Number),
        ("String", WsValueClass.String), ("Array", WsValueClass.Array), ("Map", WsValueClass.Map), ("Set", WsValueClass.Set),
        ("Function", WsValueClass.Function), ("Class", WsValueClass.Class), ("Task", WsValueClass.Task)
    ];

    public static readonly (string Name, string Code)[] ErrorClasses =
    [
        ("InitializationError", WsDiagnosticCodes.ReadBeforeInitialization), ("CallError", WsDiagnosticCodes.NotAFunction),
        ("ArgumentError", WsDiagnosticCodes.WrongArgumentCount), ("TypeError", WsDiagnosticCodes.InvalidOperation),
        ("OverflowError", WsDiagnosticCodes.IntegerOverflow), ("IndexError", WsDiagnosticCodes.IndexOutOfRange),
        ("MemberError", WsDiagnosticCodes.NoSuchMember), ("KeyError", WsDiagnosticCodes.NoSuchKey)
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
        _ => $"class {name} {{}}"
    };
}
