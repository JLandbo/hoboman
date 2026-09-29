using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.History;

public sealed class HistoryStore(AppFolder folder, ILogger<HistoryStore> logger)
{
    // One file per call, named by time, so the app and the CLI can add entries at the same time and the newest are found without reading the rest.
    public Task AddAsync(HistoryEntry entry, CancellationToken cancellationToken) =>
        FileOf(Path.Combine(folder.History, $"{entry.At.UtcDateTime:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json")).SaveAsync(entry, cancellationToken);

    public async Task<IReadOnlyList<HistoryEntry>> LatestAsync(int count, CancellationToken cancellationToken)
    {
        var files = await Task.Run<string[]>(() => Directory.Exists(folder.History) ? [.. Directory.EnumerateFiles(folder.History, "*.json").OrderDescending(StringComparer.Ordinal).Take(count)] : [], cancellationToken)
            .ConfigureAwait(false);
        var entries = new HistoryEntry?[files.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, files.Length), cancellationToken, async (index, token) => entries[index] = await LoadAsync(files[index], token).ConfigureAwait(false))
            .ConfigureAwait(false);
        return [.. entries.OfType<HistoryEntry>()];
    }

    async Task<HistoryEntry?> LoadAsync(string file, CancellationToken cancellationToken)
    {
        try
        {
            return await FileOf(file).LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogWarning(exception, "Skipped the history entry {File}", file);
            return null;
        }
    }

    JsonFile<HistoryEntry?> FileOf(string path) => new(path, null, logger);
}
