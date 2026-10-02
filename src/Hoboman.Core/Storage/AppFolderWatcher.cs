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
        _watcher?.Dispose();
        _watcher = new(_folder.Root)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = 64 * 1024,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
        };
        _watcher.Changed += (_, e) => Settle(e.FullPath);
        _watcher.Created += (_, e) => Settle(e.FullPath);
        _watcher.Deleted += (_, e) => Settle(e.FullPath);
        _watcher.Renamed += (_, e) =>
        {
            Settle(e.OldFullPath);
            Settle(e.FullPath);
        };
        _watcher.Error += Watcher_Error;
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

    // Changes can be lost when the buffer overflows, and the watcher stops after other errors, so everything is reloaded and the watcher restarted.
    void Watcher_Error(object sender, ErrorEventArgs e)
    {
        _logger.LogWarning(e.GetException(), "Watching {Folder} failed, so everything is reloaded", _folder.Root);
        if (e.GetException() is not InternalBufferOverflowException)
        {
            try
            {
                Start();
            }
            catch (Exception exception) when (exception is IOException or ArgumentException)
            {
                _logger.LogError(exception, "Could not watch {Folder} again", _folder.Root);
            }
        }
        foreach (var timer in new[] { _requests, _history, _environments })
        {
            timer.Change(_settle, Timeout.InfiniteTimeSpan);
        }
    }

    void Settle(string path)
    {
        var timer = IsIn(path, _folder.Requests) || path.StartsWith(_folder.RequestOrder, StringComparison.OrdinalIgnoreCase) ? _requests
            : IsIn(path, _folder.History) ? _history
            : path.StartsWith(_folder.Environments, StringComparison.OrdinalIgnoreCase) ? _environments
            : null;
        timer?.Change(_settle, Timeout.InfiniteTimeSpan);
    }

    static bool IsIn(string path, string folder) =>
        path.Equals(folder, StringComparison.OrdinalIgnoreCase) || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
