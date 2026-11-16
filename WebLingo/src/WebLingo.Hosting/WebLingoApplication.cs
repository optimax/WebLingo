using WebLingo.Bytecode;
using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Hosting.Abstractions;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.Templates;
using WebLingo.VM;
namespace WebLingo.Hosting;

/// <summary>What a request came to: a page's response, a file the host serves as it is, no route, or a 500.</summary>
public enum WebLingoRequestOutcomeKind { Page, File, NotFound, Failed }

/// <summary>A request's outcome; for <see cref="WebLingoRequestOutcomeKind.File"/>, the file, from the application's root.</summary>
public sealed record WebLingoRequestOutcome(WebLingoRequestOutcomeKind Kind, string? File = null);

/// <summary>
/// A web application (specification, sections 6.3 to 6.6 and 6.12): its root's files, as a generation, its routes made when
/// it is loaded, which a request's path finds. A page is compiled when it is first requested, once on each worker, the
/// worker's own (a generation's state shared between workers is never changed, so that nothing needs a lock, WL-P013), and
/// cached, with the components it renders, for the generation's life, all in memory (section 5.1). A request runs on the
/// worker the host gives, its output kept until it has run, then written, as UTF-8, to the response; a page that does not
/// compile, or fails, is a 500, its errors shown in development only. <see cref="Handle"/> never throws for a script's error.
/// </summary>
public sealed class WebLingoApplication
{
    private readonly WebLingoApplicationOptions _options;
    private readonly WebLingoWorkerServices[] _services;
    private readonly IReadOnlyList<WebLingoBuiltinDeclaration>[] _declarations;
    private volatile Generation _current;

    private WebLingoApplication(WebLingoApplicationOptions options, Generation generation)
    {
        _options = options;
        _services = new WebLingoWorkerServices[options.Workers];
        _declarations = new IReadOnlyList<WebLingoBuiltinDeclaration>[options.Workers];
        for (var i = 0; i < options.Workers; i++)
        {
            _services[i] = options.WorkerServices(new WebLingoWorkerId(i));
            var declarations = new List<WebLingoBuiltinDeclaration>();
            foreach (var builtin in _services[i].Builtins.Builtins) declarations.Add(new WebLingoBuiltinDeclaration(builtin.Name, builtin.Arity));
            _declarations[i] = declarations;
        }
        _current = generation;
    }

    /// <summary>The application at <paramref name="options"/>' root, its routes made; null, with why, if they cannot be.</summary>
    public static WebLingoApplication? Load(WebLingoApplicationOptions options, out string? error)
    {
        if (options.Workers < 1)
        {
            error = "an application needs a worker or more";
            return null;
        }
        var routes = WebLingoRouteTable.Make(options.FileSystem, options.Root, out error);
        return routes is null ? null : new WebLingoApplication(options, new Generation(routes, options.Workers));
    }

    /// <summary>The routes of the generation requests are served from.</summary>
    public WebLingoRouteTable Routes => _current.Routes;

    /// <summary>
    /// Serves <paramref name="request"/> on <paramref name="worker"/>, from 0 to the host's workers − 1, writing and
    /// completing <paramref name="response"/>, but for a file the host serves as it is, which it leaves as it was.
    /// </summary>
    public WebLingoRequestOutcome Handle(IWebLingoHttpRequest request, IWebLingoHttpResponse response, WebLingoWorkerId worker,
        WebLingoCancellationSignal? cancellation = null)
    {
        var generation = _current;
        switch (generation.Routes.Find(request.Path))
        {
            case { Kind: WebLingoRouteKind.File } file:
                return new WebLingoRequestOutcome(WebLingoRequestOutcomeKind.File, file.File);
            case { Kind: WebLingoRouteKind.Page } page:
                return Run(generation.Page(worker, page.File, this), response, worker, cancellation);
            default:
                // A .wl's route is a handler's from M0.14.3 on.
                WebLingoErrorPages.NotFound(response);
                return new WebLingoRequestOutcome(WebLingoRequestOutcomeKind.NotFound);
        }
    }

    private WebLingoRequestOutcome Run(CompiledPage page, IWebLingoHttpResponse response, WebLingoWorkerId worker,
        WebLingoCancellationSignal? cancellation)
    {
        if (page.Module is null) return Failed(response, page.Errors);
        var output = new WebLingoResponseText();
        var context = new WebLingoExecutionContext(worker)
        {
            Services = new RequestServices(_services[worker.Value].Builtins, WebLingoTemplateBuiltins.Create(output)),
            Output = output,
            FileSystem = _options.FileSystem,
            Clock = _options.Clock,
            SchedulerHost = _options.Scheduler?.Invoke(),
            InstructionBudget = _options.InstructionBudget,
            Cancellation = cancellation
        };
        var result = new WebLingoVirtualMachine { Loader = page.Modules, Components = page.Components }.Execute(page.Module, context);
        if (!result.Succeeded) return Failed(response, [page.Describe(result.Error!)]);
        if (response.Headers.Get("Content-Type") is null) response.Headers.Set("Content-Type", WebLingoErrorPages.HtmlType);
        output.WriteTo(response);
        response.Complete();
        return new WebLingoRequestOutcome(WebLingoRequestOutcomeKind.Page);
    }

    private WebLingoRequestOutcome Failed(IWebLingoHttpResponse response, IReadOnlyList<string> errors)
    {
        WebLingoErrorPages.Failed(response, _options.Environment, errors);
        return new WebLingoRequestOutcome(WebLingoRequestOutcomeKind.Failed);
    }

    /// <summary>A page compiled on a worker, with its files found from the application's root, or its errors, each as a line.</summary>
    private CompiledPage Compile(string file, WebLingoWorkerId worker)
    {
        var templates = new WebLingoSiteFiles(_options.FileSystem, _options.Root, ".wasp");
        var modules = new WebLingoSiteFiles(_options.FileSystem, _options.Root, ".wl");
        var options = new WebLingoCompilerOptions(_declarations[worker.Value]) { Resolver = modules, Native = _services[worker.Value].Native };
        var compiler = new WebLingoTemplateCompiler(options, templates);
        var text = new WebLingoSourceText(templates.Read(file) ?? "", file);
        var page = new CompiledPage(text, compiler, modules, new WebLingoModuleLoader(options));
        var result = compiler.Compile(text);
        page.Module = result.Module;
        foreach (var diagnostic in result.Diagnostics)
        {
            if (diagnostic.Severity == WebLingoDiagnosticSeverity.Error) page.Errors.Add(page.Describe(diagnostic));
        }
        return page;
    }

    /// <summary>
    /// A generation (section 6.5): its routes, and, for each worker, the pages compiled on it, which only that worker reads
    /// and adds to.
    /// </summary>
    private sealed class Generation(WebLingoRouteTable routes, int workers)
    {
        private readonly Dictionary<string, CompiledPage>[] _pages = MakePages(workers);

        public WebLingoRouteTable Routes { get; } = routes;

        public CompiledPage Page(WebLingoWorkerId worker, string file, WebLingoApplication application)
        {
            var pages = _pages[worker.Value];
            if (!pages.TryGetValue(file, out var page))
            {
                page = application.Compile(file, worker);
                pages.Add(file, page);
            }
            return page;
        }

        private static Dictionary<string, CompiledPage>[] MakePages(int workers)
        {
            var pages = new Dictionary<string, CompiledPage>[workers];
            for (var i = 0; i < workers; i++) pages[i] = new Dictionary<string, CompiledPage>(StringComparer.Ordinal);
            return pages;
        }
    }

    /// <summary>A page as its worker keeps it: its module, or its errors, and how to name where an error is.</summary>
    private sealed class CompiledPage(WebLingoSourceText text, WebLingoTemplateCompiler compiler, WebLingoSiteFiles modules,
        WebLingoModuleLoader imports)
    {
        public WebLingoCompiledModule? Module { get; set; }
        public List<string> Errors { get; } = [];

        /// <summary>What finds and compiles a module the page imports while it runs (section 3.9).</summary>
        public IWebLingoModuleLoader Modules { get; } = imports;

        /// <summary>The components the page renders (section 13), each compiled once, when first rendered.</summary>
        public IWebLingoModuleLoader Components { get; } = new CachedLoader(compiler.Components);

        /// <summary>An error as a line, <c>file(line,column): error CODE: message</c>, in the file it is in.</summary>
        public string Describe(WebLingoDiagnostic diagnostic)
        {
            var located = compiler.Sources.Locate(diagnostic);
            var file = located.Source is not { } name ? text
                : compiler.Sources.TextOf(name) ?? (modules.Texts.TryGetValue(name, out var module) ? module : text);
            var location = file.GetLocation(located.Span.Start);
            return $"{file.Name}({location.Line},{location.Column}): error {located.Code}: {located.Message}";
        }
    }

    /// <summary>A loader that compiles each module once, its result kept (WL-T007: a component is not compiled for every render).</summary>
    private sealed class CachedLoader(IWebLingoModuleLoader loader) : IWebLingoModuleLoader
    {
        private readonly Dictionary<string, (WebLingoCompiledModule? Module, WebLingoDiagnostic? Error)> _compiled = new(StringComparer.Ordinal);

        public bool TryResolve(string importer, string path, out string name, out string? error) =>
            loader.TryResolve(importer, path, out name, out error);

        public WebLingoCompiledModule? Compile(string name, out WebLingoDiagnostic? error)
        {
            if (!_compiled.TryGetValue(name, out var compiled))
            {
                compiled.Module = loader.Compile(name, out compiled.Error);
                _compiled.Add(name, compiled);
            }
            error = compiled.Error;
            return compiled.Module;
        }
    }

    /// <summary>A request's built-ins: the page's writers, writing to its output, then the host's.</summary>
    private sealed class RequestServices(WebLingoBuiltinRegistry host, IWebLingoBuiltinFunction[] request) : IWebLingoRuntimeServices
    {
        public IReadOnlyList<IWebLingoBuiltinFunction> Builtins { get; } = [.. request, .. host.Builtins];

        public bool TryGetBuiltin(string name, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out IWebLingoBuiltinFunction builtin)
        {
            foreach (var own in request)
            {
                if (own.Name == name)
                {
                    builtin = own;
                    return true;
                }
            }
            return host.TryGetBuiltin(name, out builtin);
        }
    }
}
