namespace WebLingo.Platform.Abstractions;

/// <summary>
/// The memory a site's requests share, or a program's workers under <c>weblingo</c> (specification, section 6.12, "Memory a
/// site's requests share"; the owner's answers, 27 November 2026, A1 to A4 (a) of "Memory a Site's Requests Share"):
/// values by key, kept by the host outside every request's heap, each as its owner copied it, which the store does not read,
/// of the size its owner measured; an integer kept as itself, a <see cref="long"/>, so that <see cref="Add"/> adds to it.
/// Every operation is one step, which no other comes between; a value whose time has passed is gone. Its lock is the
/// host's, in an adapter (section 7.14).
/// </summary>
public interface IWebLingoSharedStore
{
    /// <summary>The value kept under <paramref name="key"/>, a <see cref="long"/> or its owner's copy; null where there is none.</summary>
    object? Get(string key);

    /// <summary>
    /// <paramref name="value"/>, of <paramref name="size"/> bytes, kept under <paramref name="key"/> in place of what was, for
    /// <paramref name="seconds"/> where they are given, else until it is removed; false, nothing changed, where the store
    /// would then hold more than its most.
    /// </summary>
    bool TrySet(string key, object value, long size, double? seconds);

    /// <summary>Whether <paramref name="key"/> kept a value, which it keeps no more.</summary>
    bool Remove(string key);

    /// <summary>The value kept under <paramref name="key"/>, which it keeps no more; null where there was none.</summary>
    object? Take(string key);

    /// <summary>
    /// <paramref name="amount"/> added to the integer kept under <paramref name="key"/>, or to 0 where there is none, its
    /// time kept; what came of it, and, where it added, the total, <paramref name="total"/>.
    /// </summary>
    WebLingoSharedAdded Add(string key, long amount, out long total);

    /// <summary>The most it holds, in bytes, its values' sizes, its keys' and its entries' own; null where it has no most.</summary>
    long? Most { get; }
}

/// <summary>What <see cref="IWebLingoSharedStore.Add"/> came to.</summary>
public enum WebLingoSharedAdded
{
    /// <summary>Added, the total given.</summary>
    Added,

    /// <summary>Nothing changed: the key holds a value that is no integer.</summary>
    NotAnInteger,

    /// <summary>Nothing changed: the total is beyond an integer's range.</summary>
    Overflow,

    /// <summary>Nothing changed: a new key would make the store hold more than its most.</summary>
    Full
}
