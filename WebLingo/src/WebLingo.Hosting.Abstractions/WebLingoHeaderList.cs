namespace WebLingo.Hosting.Abstractions;

/// <summary>
/// Headers held as a list (specification, section 6.7): every adapter's, and the buffered request's and response's. A
/// name is an HTTP token, compared without regard to ASCII case; a value holds visible ASCII, spaces and tabs only, so that
/// no value can end its header early (no CR or LF) or depend on an encoding. <see cref="MakeReadOnly"/> fixes them.
/// </summary>
public sealed class WebLingoHeaderList : IWebLingoHeaderCollection
{
    private readonly List<string> _names = [];
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);

    public bool IsReadOnly { get; private set; }

    public IReadOnlyList<string> Names => _names;

    /// <summary>Fixes them: from then on, every change is <see cref="WebLingoHeaderChange.ReadOnly"/>.</summary>
    public void MakeReadOnly() => IsReadOnly = true;

    public IReadOnlyList<string> GetValues(string name) => _values.TryGetValue(name, out var values) ? values : [];

    public string? Get(string name) => _values.TryGetValue(name, out var values) ? string.Join(", ", values) : null;

    public WebLingoHeaderChange Set(string name, string value)
    {
        var check = Check(name, value);
        if (check != WebLingoHeaderChange.Done) return check;
        if (_values.TryGetValue(name, out var values))
        {
            values.Clear();
            values.Add(value);
        }
        else
        {
            _values.Add(name, [value]);
            _names.Add(name);
        }
        return WebLingoHeaderChange.Done;
    }

    public WebLingoHeaderChange Add(string name, string value)
    {
        var check = Check(name, value);
        if (check != WebLingoHeaderChange.Done) return check;
        if (_values.TryGetValue(name, out var values))
        {
            values.Add(value);
        }
        else
        {
            _values.Add(name, [value]);
            _names.Add(name);
        }
        return WebLingoHeaderChange.Done;
    }

    public WebLingoHeaderChange Remove(string name)
    {
        if (IsReadOnly) return WebLingoHeaderChange.ReadOnly;
        if (!IsToken(name)) return WebLingoHeaderChange.InvalidName;
        if (_values.Remove(name))
        {
            _names.RemoveAt(_names.FindIndex(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase)));
        }
        return WebLingoHeaderChange.Done;
    }

    private WebLingoHeaderChange Check(string name, string value) =>
        IsReadOnly ? WebLingoHeaderChange.ReadOnly
        : !IsToken(name) ? WebLingoHeaderChange.InvalidName
        : !IsValue(value) ? WebLingoHeaderChange.InvalidValue
        : WebLingoHeaderChange.Done;

    /// <summary>An HTTP token: one or more of ASCII's letters and digits and <c>!#$%&amp;'*+-.^_`|~</c> (RFC 9110, section 5.6.2).</summary>
    public static bool IsToken(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var c in name)
        {
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' || "!#$%&'*+-.^_`|~".Contains(c))) return false;
        }
        return true;
    }

    /// <summary>A header's value as these headers take it: visible ASCII, spaces and tabs.</summary>
    public static bool IsValue(string value)
    {
        if (value is null) return false;
        foreach (var c in value)
        {
            if (c is not ('\t' or >= ' ' and <= '~')) return false;
        }
        return true;
    }
}
