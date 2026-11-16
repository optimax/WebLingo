namespace WebLingo.Core;

/// <summary>
/// The built-in classes of the kinds of values, every kind but <c>null</c> (specification, section 3.7, "Classes of
/// values"): a module names which of its classes each is, and a VM tests a value's kind against them. The last,
/// <see cref="Iterator"/>, is no kind's: the class of the iterators an array's, a map's, a set's and a string's
/// <c>iterate()</c> give (section 3.6), which the runtime makes.
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
    Iterator
}
