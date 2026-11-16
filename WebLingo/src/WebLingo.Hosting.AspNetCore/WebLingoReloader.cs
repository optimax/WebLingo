namespace WebLingo.Hosting.AspNetCore;

/// <summary>
/// Reloads an application when its files change (specification, section 6.5, WL-T007; section 9, "M0.14 parts", part 6):
/// its root watched, folders below it included, and, once its files have stopped changing for <see cref="Quiet"/>, a new
/// generation made, one for a burst of changes, never two at once; <paramref name="reloaded"/> told of each, with the
/// generation's number, or why it could not be made, the generation served then unchanged. Disposed, it stops watching.
/// </summary>
public sealed class WebLingoReloader : IDisposable
{
    /// <summary>How long the files must have stopped changing before the application is reloaded.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(250);

    private readonly WebLingoApplication _application;
    private readonly Action<int, string?> _reloaded;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;
    private readonly Lock _reloading = new();

    public WebLingoReloader(WebLingoApplication application, string root, Action<int, string?> reloaded)
    {
        (_application, _reloaded) = (application, reloaded);
        _timer = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _watcher.Changed += (_, _) => Changed();
        _watcher.Created += (_, _) => Changed();
        _watcher.Deleted += (_, _) => Changed();
        _watcher.Renamed += (_, _) => Changed();
        // Too many changes at once for the watcher's buffer: reload anyway, so that none is missed.
        _watcher.Error += (_, _) => Changed();
        _watcher.EnableRaisingEvents = true;
    }

    private void Changed() => _timer.Change(Quiet, Timeout.InfiniteTimeSpan);

    private void Reload()
    {
        lock (_reloading)
        {
            var reloaded = _application.TryReload(out var error);
            _reloaded(_application.GenerationNumber, reloaded ? null : error);
        }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _timer.Dispose();
    }
}
