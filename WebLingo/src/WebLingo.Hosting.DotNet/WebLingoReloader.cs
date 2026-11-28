namespace WebLingo.Hosting.DotNet;

/// <summary>
/// Reloads an application when its files change (specification, section 6.5, WL-T007; section 9, "M0.14 parts", part 6):
/// its root watched, folders below it included, and its base's (section 6.12, "A site over a base"), where it has one,
/// and, once its files have stopped changing for <see cref="Quiet"/>, a new
/// generation made, one for a burst of changes, never two at once; the application's drain told of each, which stops the
/// requests still running on an older generation once the site's drain has passed (section 6.12, "Reloading"); and
/// <paramref name="reloaded"/> told of each, with the generation's number, or why it could not be made, the generation
/// served then unchanged. Disposed, it stops watching.
/// </summary>
public sealed class WebLingoReloader : IDisposable
{
    /// <summary>How long the files must have stopped changing before the application is reloaded.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(250);

    private readonly WebLingoApplication _application;
    private readonly Action<int, string?> _reloaded;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Timer _timer;
    private readonly Lock _reloading = new();

    public WebLingoReloader(WebLingoApplication application, string root, Action<int, string?> reloaded)
    {
        (_application, _reloaded) = (application, reloaded);
        _timer = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
        foreach (var folder in application.Base is { } over ? [root, over] : new[] { root })
        {
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Changed += (_, _) => Changed();
            watcher.Created += (_, _) => Changed();
            watcher.Deleted += (_, _) => Changed();
            watcher.Renamed += (_, _) => Changed();
            // Too many changes at once for the watcher's buffer: reload anyway, so that none is missed.
            watcher.Error += (_, _) => Changed();
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    private void Changed() => _timer.Change(Quiet, Timeout.InfiniteTimeSpan);

    private void Reload()
    {
        lock (_reloading)
        {
            var reloaded = _application.TryReload(out var error);
            if (reloaded) WebLingoDrain.Of(_application).Reloaded(_application.GenerationNumber);
            _reloaded(_application.GenerationNumber, reloaded ? null : error);
        }
    }

    /// <summary>
    /// Attaches <paramref name="debugger"/> to the application, or detaches the one attached, where it is null (specification,
    /// section 5.7, "Debugging a site where it runs, parts", part 1): a new generation, made among the reloads, one at a time,
    /// whose pages are compiled for it, and the drain told, as of a reload; its number.
    /// </summary>
    public int Attach(WebLingo.VM.IWebLingoDebuggerThreads? debugger)
    {
        lock (_reloading)
        {
            var generation = _application.Attach(debugger);
            WebLingoDrain.Of(_application).Reloaded(generation);
            return generation;
        }
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
        _timer.Dispose();
    }
}
