using WebLingo.Bytecode;
using WebLingo.Core;
using WebLingo.Platform.Abstractions;
namespace WebLingo.VM;

/// <summary>A source debugger attached to a register VM. Callbacks run on the execution's worker.</summary>
public interface IWebLingoRegisterDebugger
{
    void ModuleLoaded(WebLingoCompiledModule module);
    bool ShouldStop(WebLingoDebugPoint point);

    /// <summary>
    /// Stopped where <see cref="ShouldStop"/> said, its frames given: true once the program may go on, the debugger having
    /// waited for it on the worker's thread, as a script's and a page's does; false where the program waits as at an
    /// <c>await</c> (specification, section 5.7, "Debugging WASP pages, parts", part 2), its VM returning waiting, its
    /// execution's context's <c>Suspends</c> set, and going on where it stopped once its host resumes it and
    /// <see cref="Waits"/> is false.
    /// </summary>
    bool Stopped(IReadOnlyList<WebLingoDebugFrame> frames);

    /// <summary>Whether a program that <see cref="Stopped"/> left waiting waits still.</summary>
    bool Waits { get; }
}

/// <summary>
/// The debugger of many executions at once, a site's requests' (specification, section 5.7, "Debugging WASP pages, parts",
/// part 2): each execution's its own, a thread of the debugger's, named for what it runs, given how a position of its
/// program is found in its files and the scheduler host through which the debugger resumes it where it stopped.
/// </summary>
public interface IWebLingoDebuggerThreads
{
    IWebLingoDebuggerThread Begin(string name, WebLingoDebugLocator locate, IWebLingoSchedulerHost scheduler);
}

/// <summary>One execution's debugger, a thread of an <see cref="IWebLingoDebuggerThreads"/>'s, disposed once its execution has ended.</summary>
public interface IWebLingoDebuggerThread : IWebLingoRegisterDebugger, IDisposable
{
    /// <summary>
    /// How many times it has stopped, and gone on, since it began, odd while it is stopped: so that a host's timeout
    /// leaves out the time its execution was stopped.
    /// </summary>
    int Stops { get; }
}

/// <summary>A position of a program, its source's name and its offset there, as the file it is in, and its offset in that file.</summary>
public delegate (WebLingoSourceText Text, int Offset) WebLingoDebugLocator(string source, int offset);

public readonly record struct WebLingoDebugPoint(string Source, WebLingoSourceSpan Span, int Depth, long Activation, int Instruction);
public sealed record WebLingoDebugFrame(string Name, string Source, WebLingoSourceSpan Span, IReadOnlyList<WebLingoDebugVariable> Variables);
public sealed record WebLingoDebugVariable(string Name, string Value, string Type)
{
    public IReadOnlyList<WebLingoDebugVariable> Children { get; init; } = [];
}
