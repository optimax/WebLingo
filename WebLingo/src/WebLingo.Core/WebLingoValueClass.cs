namespace WebLingo.Core;

/// <summary>
/// The built-in classes of the kinds of values, every kind but <c>null</c> (specification, section 3.7, "Classes of
/// values"): a module names which of its classes each is, and a VM tests a value's kind against them. The last,
/// <see cref="Iterator"/>, is no kind's: the class of the iterators an array's, a map's, a set's and a string's
/// <c>iterate()</c> give (section 3.6), which the runtime makes. <see cref="Bytes"/> and <see cref="BytesBuilder"/> come
/// after it, so that a module's list of its value classes keeps its order: the bytes' kind's, and the class written in
/// WebLingo that builds bytes from pieces (section 3.2, "Bytes"); then <see cref="DateTime"/>, the dates' and times' kind's
/// (section 3.2, "Dates and times").
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
    Decimal
}
