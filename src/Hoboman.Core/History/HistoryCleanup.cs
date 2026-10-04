using Hoboman.Core.Settings;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.History;

// The calls and the runs of workflows are kept for as many days as the settings say, or for ever.
// It runs when the app starts, so a number on its way to being typed in the settings deletes nothing.
public sealed class HistoryCleanup(AppFolder folder, SettingsStore settings, TimeProvider clock, ILogger<HistoryCleanup> logger)
{
    public async Task DeleteOldAsync(CancellationToken cancellationToken)
    {
        try
        {
            if ((await settings.LoadAsync(cancellationToken).ConfigureAwait(false)).DeleteHistoryAfterDays is not { } days)
            {
                return;
            }
            var oldest = clock.GetUtcNow().UtcDateTime.AddDays(-days);
            await Task.Run(() =>
            {
                var deleted = 0;
                foreach (var file in FilesIn(folder.History, "*.json").Concat(FilesIn(folder.Runs, "*.jsonl")).Where(file => File.GetLastWriteTimeUtc(file) < oldest))
                {
                    File.Delete(file);
                    deleted++;
                }
                logger.LogInformation("Deleted {Count} calls and runs older than {Days} days", deleted, days);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogWarning(exception, "Could not delete the old calls and runs");
        }
    }

    static IEnumerable<string> FilesIn(string path, string pattern) => Directory.Exists(path) ? Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories) : [];
}
