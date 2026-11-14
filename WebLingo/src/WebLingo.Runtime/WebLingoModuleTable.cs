using WebLingo.Core;
namespace WebLingo.Runtime;

/// <summary>
/// The instances of a program's files (specification, section 3.9): each file's, once it has begun to run; and for a
/// file with parameters, one for each set of values its imports give it, each run once; roots of the heap. A VM asks it
/// what an import gets (<see cref="TryFind"/>), and tells it of each instance a file's function makes (<see cref="Made"/>).
/// </summary>
public sealed class WebLingoModuleTable
{
    private readonly List<WebLingoValue> _instances = [];
    private readonly List<(int Unit, WebLingoValue[] Values, WebLingoValue Instance)> _variants = [];
    private readonly List<WebLingoValue> _roots = [];
    private readonly WebLingoHeap _heap;

    /// <summary>The values of the import about to run its file, whose instance <see cref="Made"/> is told of next.</summary>
    private WebLingoValue[]? _making;

    public WebLingoModuleTable(int units, WebLingoHeap heap)
    {
        _heap = heap;
        Grow(units);
        heap.AddRoots(_instances);
        heap.AddRoots(_roots);
    }

    /// <summary>Room for the files a module imported while the program runs brings (section 3.9).</summary>
    public void Grow(int units)
    {
        while (_instances.Count < units) _instances.Add(WebLingoValue.Null);
    }

    /// <summary>
    /// What an import of file <paramref name="unit"/> gets, given <paramref name="values"/>, a map, or <c>null</c> for
    /// none: the file's instance for those values, if one has begun; else <c>null</c>, for its function to run. Its
    /// values are each a parameter's, and give each one without a default (WL2016 otherwise).
    /// </summary>
    public bool TryFind(int unit, string name, IReadOnlyList<WebLingoModuleParameter> parameters, WebLingoValue values,
        out WebLingoValue instance, out WebLingoRuntimeError? error)
    {
        instance = WebLingoValue.Null;
        error = null;
        _making = null;
        if (values.Kind is not (WebLingoValueKind.Null or WebLingoValueKind.Map))
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"An import's values are a map, not {WebLingoValueFormatter.Describe(values, _heap)}.");
            return false;
        }
        var given = new WebLingoValue[parameters.Count];
        for (var i = 0; i < given.Length; i++) given[i] = WebLingoValue.Missing;
        var count = values.Kind == WebLingoValueKind.Map ? _heap.MapCount(values) : 0;
        for (var position = 0; position < count; position++)
        {
            var (key, value) = _heap.MapEntryAt(values, position);
            var index = -1;
            for (var i = 0; key.Kind == WebLingoValueKind.String && i < parameters.Count && index < 0; i++)
            {
                if (parameters[i].Name == _heap.GetString(key)) index = i;
            }
            if (index < 0)
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.ModuleParameter,
                    $"{WebLingoValueFormatter.Describe(key, _heap)} is not a parameter of '{name}'.");
                return false;
            }
            given[index] = value;
        }
        for (var i = 0; i < parameters.Count; i++)
        {
            if (given[i].Kind != WebLingoValueKind.Missing || parameters[i].HasDefault) continue;
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.ModuleParameter, $"'{name}''s parameter '{parameters[i].Name}' is not given.");
            return false;
        }
        if (parameters.Count == 0)
        {
            instance = _instances[unit];
            return true;
        }
        foreach (var (made, madeValues, madeInstance) in _variants)
        {
            if (made == unit && Same(madeValues, given))
            {
                instance = madeInstance;
                return true;
            }
        }
        _making = given;
        return true;
    }

    /// <summary>A file's instance, made where its function begins: the one its imports get, with the values it was run for.</summary>
    public void Made(int unit, WebLingoValue instance)
    {
        if (_making is not { } values)
        {
            _instances[unit] = instance;
            return;
        }
        _making = null;
        _variants.Add((unit, values, instance));
        _roots.Add(instance);
        _roots.AddRange(values);
    }

    /// <summary>Whether two imports' values are the same, as <c>==</c> compares them, a value left out only as one left out.</summary>
    private bool Same(WebLingoValue[] left, WebLingoValue[] right)
    {
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i].Kind == WebLingoValueKind.Missing || right[i].Kind == WebLingoValueKind.Missing)
            {
                if (left[i].Kind != right[i].Kind) return false;
                continue;
            }
            if (!WebLingoOperators.TryCompare(WebLingoComparisonOperator.Equal, left[i], right[i], _heap, out var equal, out _)
                || !equal.AsBoolean)
            {
                return false;
            }
        }
        return true;
    }
}
