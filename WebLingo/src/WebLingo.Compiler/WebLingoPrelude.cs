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

    public static readonly (string Name, string Code)[] ErrorClasses =
    [
        ("InitializationError", WebLingoDiagnosticCodes.ReadBeforeInitialization), ("CallError", WebLingoDiagnosticCodes.NotAFunction),
        ("ArgumentError", WebLingoDiagnosticCodes.WrongArgumentCount), ("TypeError", WebLingoDiagnosticCodes.InvalidOperation),
        ("OverflowError", WebLingoDiagnosticCodes.IntegerOverflow), ("IndexError", WebLingoDiagnosticCodes.IndexOutOfRange),
        ("MemberError", WebLingoDiagnosticCodes.NoSuchMember), ("KeyError", WebLingoDiagnosticCodes.NoSuchKey),
        ("TimeoutError", WebLingoDiagnosticCodes.Timeout), ("IOError", WebLingoDiagnosticCodes.IOFailure),
        ("DivisionByZeroError", WebLingoDiagnosticCodes.DivisionByZero), ("NetworkError", WebLingoDiagnosticCodes.NetworkFailure),
        ("DatabaseError", WebLingoDiagnosticCodes.DatabaseFailure), ("PermissionError", WebLingoDiagnosticCodes.PermissionDenied),
        ("ComError", WebLingoDiagnosticCodes.ComFailure),
        ("NetworkPermissionError", WebLingoDiagnosticCodes.NetworkPermissionDenied),
        ("ComPermissionError", WebLingoDiagnosticCodes.ComPermissionDenied)
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
        "Decimal" => "class Decimal { constructor(value = 0) { hold(this, value); } }",
        // new Regex(…) makes the value itself; a class extending Regex holds one, made by the class's own new.
        "Json" => "class Json { static stringify(value, indent = 0) { return toJson(value, indent); } }",
        // A value as a script takes it, Html.js(value) (section 13, "Context-aware escaping"): its JSON, its <, >, & and line
        // separators, which JSON leaves only in strings, as escapes, so that neither the script nor its attribute ends; a
        // number or a keyword between spaces, so that it runs into no name or operator beside it. Html's other members are
        // the runtime's.
        "Html" => """
            class Html {
                static js(value) {
                    var text = Json.stringify(value).replace("<", "\\u003c").replace(">", "\\u003e").replace("&", "\\u0026")
                        .replace("\u{2028}", "\\u2028").replace("\u{2029}", "\\u2029");
                    if (text.startsWith("\"") || text.startsWith("[") || text.startsWith("{")) return text;
                    return " " + text + " ";
                }
            }
            """,
        "Regex" => "class Regex { constructor(pattern, flags = \"\") { hold(this, new Regex(pattern, flags)); } }",
        "String" => "class String { constructor(value = \"\") { hold(this, value); } }",
        "Function" => "class Function { constructor(f = function () {}) { hold(this, f); } }",
        // A metaclass's super(name, base) names and bases the class it makes (section 3.7).
        "Class" => "class Class { constructor(name = null, base = null) { hold(this, [name, base]); } }",
        // The iterator a collection's iterate() gives, its items and its position set by the runtime (section 3.6): next()
        // past the last item is an index out of range (WL3005).
        "Iterator" => "class Iterator { hasNext() { return this.position < this.items.length; } "
            + "next() { var item = this.items[this.position]; this.position = this.position + 1; return item; } }",
        // Bytes built from pieces without copying what is built at each one (section 3.2, "Bytes"): the pieces are kept, and
        // joined once, by Bytes.concat, which also checks each piece where it is added.
        "BytesBuilder" => "class BytesBuilder { constructor() { this.parts = []; this.count = 0; } "
            + "add(value) { var part = null; if (value is String) part = Bytes.from(value); else part = Bytes.concat([value]); "
            + "this.parts.push(part); "
            + "this.count = this.count + part.length; return this; } "
            + "property length { get { return this.count; } } "
            + "toBytes() { var whole = Bytes.concat(this.parts); this.parts = [whole]; return whole; } }",
        _ => $"class {name} {{}}"
    };
}
