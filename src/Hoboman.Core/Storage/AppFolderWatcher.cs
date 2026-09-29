using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Storage;

public sealed class AppFolderWatcher : IDisposable
{
    // A save touches a file several times (temp file, move), so changes are gathered for a moment before anyone is told.
    static readonly TimeSpan _settle = TimeSpan.FromMilliseconds(300);

    readonly AppFolder _folder;
    readonly ILogger<AppFolderWatcher> _logger;
    readonly Timer _requests;
    readonly Timer _history;
    readonly Timer _environments;
    FileSystemWatcher? _watcher;

    public AppFolderWatcher(AppFolder folder, ILogger<AppFolderWatcher> logger)
    {
        _folder = folder;
        _logger = logger;
        _requests = new(_ => RequestsChanged?.Invoke());
        _history = new(_ => HistoryChanged?.Invoke());
        _environments = new(_ => EnvironmentsChanged?.Invoke());
    }

    public event Action? RequestsChanged;

    public event Action? HistoryChanged;

    public event Action? EnvironmentsChanged;

    public void Start()
    {
        _watcher = new(_folder.Root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite };
        _watcher.Changed += Watcher_Changed;
        _watcher.Created += Watcher_Changed;
        _watcher.Deleted += Watcher_Changed;
        _watcher.Renamed += Watcher_Changed;
        _watcher.Error += (_, e) => _logger.LogWarning(e.GetException(), "Watching {Folder} failed", _folder.Root);
        _watcher.EnableRaisingEvents = true;
        _logger.LogInformation("Watching {Folder} for changes", _folder.Root);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _requests.Dispose();
        _history.Dispose();
        _environments.Dispose();
    }

    void Watcher_Changed(object sender, FileSystemEventArgs e)
    {
        var timer = e.FullPath.StartsWith(_folder.Requests, StringComparison.OrdinalIgnoreCase) ? _requests
            : e.FullPath.StartsWith(_folder.History, StringComparison.OrdinalIgnoreCase) ? _history
            : e.FullPath.StartsWith(_folder.Environments, StringComparison.OrdinalIgnoreCase) ? _environments
            : null;
        timer?.Change(_settle, Timeout.InfiniteTimeSpan);
    }
}
