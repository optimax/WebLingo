namespace WebLingo.Core;

/// <summary>
/// The built-in classes of the kinds of values, every kind but <c>null</c> (specification, section 3.7, "Classes of
/// values"): a module names which of its classes each is, and a VM tests a value's kind against them. The last,
/// <see cref="Iterator"/>, is no kind's: the class of the iterators an array's, a map's, a set's and a string's
/// <c>iterate()</c> give (section 3.6), which the runtime makes. <see cref="Bytes"/> and <see cref="BytesBuilder"/> come
/// after it, so that a module's list of its value classes keeps its order: the bytes' kind's, and the class written in
/// WebLingo that builds bytes from pieces (section 3.2, "Bytes"); then <see cref="DateTime"/>, the dates' and times' kind's
/// (section 3.2, "Dates and times"), the decimals' and the regular expressions' (section 3.6).
/// </summary>
public enum WebLingoValueClass
{
    Boolean,
    Integer,
    Number,
    String,
    Array,
    Map,
    Set,
    Function,
    Class,
    Task,
    Module,
    Iterator,
    Bytes,
    BytesBuilder,
    DateTime,
    Decimal,
    Regex,
    /// <summary>No kind's: the class of <c>Json.parse</c> and <c>Json.stringify</c> (section 3.6, "JSON").</summary>
    Json,
    /// <summary>No kind's: the class of the escapers a page's author calls, <c>Html.url(x)</c> and the like (section 13, "Context-aware escaping").</summary>
    Html
}
