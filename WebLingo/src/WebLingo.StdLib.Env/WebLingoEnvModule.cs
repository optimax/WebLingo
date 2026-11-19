using WebLingo.Core;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.StdLib.Env;

/// <summary>
/// <c>std:env</c>, a standard module (specification, section 7.15), a native module installed with the runtime in the
/// <c>std</c> scheme's folder as <c>env.dll</c>, through the platform's environment (<see cref="WebLingoHeap.Environment"/>),
/// so that the portable core reads no machine's of its own (the owner's answers, 18 November 2026): its class
/// <c>Environment</c>, whose members are static: <c>machineName</c>, the machine's name; <c>architecture</c>,
/// <c>processArchitecture</c> and <c>os</c>, the machine's architecture, the running program's, and the operating system
/// (the owner's answers, 19 November 2026, "The Machine's Architecture and System"); <c>variable(name)</c>, an
/// environment variable's value; <c>variables</c>, a map of the variables, each its name and its value; and
/// <c>setVariable(name, value)</c>, which sets one, for the process alone, or removes it, given <c>null</c>. Under
/// <c>weblingo</c> a script reaches all of it; under <c>wasp</c> a site's scripts reach what its <c>wasp.config</c>'s
/// <c>"env"</c> allows, anything else <c>null</c>, as if it were not there, and set nothing, an
/// <c>EnvironmentPermissionError</c> (WL3019).
/// </summary>
public sealed class WebLingoEnvModule : IWebLingoNativeModule
{
    public void Register(IWebLingoNativeRegistry registry)
    {
        registry.Class("Environment")
            .DeclareStaticProperty("machineName", MachineName)
            .DeclareStaticProperty("architecture", (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result,
                out WebLingoRuntimeError? error) => Text(heap, environment => environment.Architecture, out result, out error))
            .DeclareStaticProperty("processArchitecture", (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result,
                out WebLingoRuntimeError? error) => Text(heap, environment => environment.ProcessArchitecture, out result, out error))
            .DeclareStaticProperty("os", (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result,
                out WebLingoRuntimeError? error) => Text(heap, environment => environment.OperatingSystemName, out result, out error))
            .DeclareStaticProperty("variables", Variables)
            .DeclareStaticMethod("variable", 1, Variable)
            .DeclareStaticMethod("setVariable", 2, SetVariable);
    }

    private static bool MachineName(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryEnvironment(heap, out var environment, out error)) return false;
        return environment.MachineName is not { } name || heap.TryAllocateString(name, out result, out error);
    }

    /// <summary>A string the environment gives, or <c>null</c> where the application may not read it.</summary>
    private static bool Text(WebLingoHeap heap, Func<IWebLingoEnvironment, string?> read, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryEnvironment(heap, out var environment, out error)) return false;
        return read(environment) is not { } text || heap.TryAllocateString(text, out result, out error);
    }

    private static bool Variable(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryEnvironment(heap, out var environment, out error)) return false;
        if (!TryName(new("variable", 0, 1, "the name", "a variable's name, a string"), a[0], heap, out var name, out error)) return false;
        return environment.GetVariable(name) is not { } value || heap.TryAllocateString(value, out result, out error);
    }

    /// <summary>A map of the variables the application may read, each its name and its value, the strings kept as roots until it is made.</summary>
    private static bool Variables(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryEnvironment(heap, out var environment, out error)) return false;
        var variables = environment.GetVariables();
        var entries = new WebLingoValue[2 * variables.Count];
        heap.AddRoots(entries);
        try
        {
            var at = 0;
            foreach (var (name, value) in variables)
            {
                if (!heap.TryAllocateString(name, out entries[at++], out error)) return false;
                if (!heap.TryAllocateString(value, out entries[at++], out error)) return false;
            }
            return heap.TryAllocateMap(entries, 0, variables.Count, out result, out error);
        }
        finally
        {
            heap.RemoveLastRoots(entries);
        }
    }

    /// <summary>
    /// <c>setVariable(name, value)</c>: a variable set, for the process alone, or removed, given <c>null</c>; an
    /// <c>EnvironmentPermissionError</c> where the application may not set one, <c>wasp</c>'s sites' (section 7.15).
    /// </summary>
    private static bool SetVariable(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryEnvironment(heap, out var environment, out error)) return false;
        if (!TryName(new("setVariable", 0, 2, "the name", "a variable's name, a string"), a[0], heap, out var name, out error)) return false;
        string? value = null;
        if (a[1].Kind == WebLingoValueKind.String) value = heap.GetString(a[1]);
        else if (a[1].Kind != WebLingoValueKind.Null)
        {
            error = new WebLingoArgument("setVariable", 1, 2, "the value", "a string, or null, which removes the variable").Wrong(a[1], heap);
            return false;
        }
        if (!environment.MaySetVariables)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.EnvironmentPermissionDenied,
                $"This application may not set the environment variable '{name}'.");
            return false;
        }
        if (!IsName(name) || value is not null && value.Contains('\0'))
        {
            error = Invalid(IsName(name)
                ? $"'setVariable' cannot set '{name}' to a value holding U+0000."
                : $"'setVariable' cannot set '{name}': a variable's name is not empty, and holds neither '=' nor U+0000.");
            return false;
        }
        environment.SetVariable(name, value);
        return true;
    }

    /// <summary>A variable's name, a string; WL3001 for anything else.</summary>
    private static bool TryName(WebLingoArgument argument, WebLingoValue value, WebLingoHeap heap, out string name, out WebLingoRuntimeError? error)
    {
        (name, error) = ("", null);
        if (value.Kind == WebLingoValueKind.String)
        {
            name = heap.GetString(value);
            return true;
        }
        error = argument.Wrong(value, heap);
        return false;
    }

    private static bool IsName(string name) => name.Length > 0 && !name.Contains('=') && !name.Contains('\0');

    private static bool TryEnvironment(WebLingoHeap heap, out IWebLingoEnvironment environment, out WebLingoRuntimeError? error)
    {
        (environment, error) = (heap.Environment!, null);
        if (heap.Environment is not null) return true;
        error = Invalid("This host gives no environment.");
        return false;
    }

    private static WebLingoRuntimeError Invalid(string message) => new(WebLingoDiagnosticCodes.InvalidOperation, message);
}
