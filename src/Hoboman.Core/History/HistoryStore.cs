using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.History;

public sealed class HistoryStore(AppFolder folder, ILogger<HistoryStore> logger)
{
    // One file per call, named by time, so the app and the CLI can add entries at the same time and the newest are found without reading the rest.
    public Task AddAsync(HistoryEntry entry, CancellationToken cancellationToken) =>
        FileOf($"{entry.At.UtcDateTime:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json").SaveAsync(entry, cancellationToken);

    public async Task<IReadOnlyList<HistoryFile>> LatestAsync(int count, string? newerThan, CancellationToken cancellationToken)
    {
        var names = await Task.Run<string[]>(() => Directory.Exists(folder.History)
            ? [.. Directory.EnumerateFiles(folder.History, "*.json").Select(Path.GetFileName).OfType<string>()
                .Where(name => newerThan is null || string.CompareOrdinal(name, newerThan) > 0).OrderDescending(StringComparer.Ordinal).Take(count)]
            : [], cancellationToken).ConfigureAwait(false);
        var files = new HistoryFile?[names.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, names.Length), cancellationToken, async (index, token) => files[index] = await LoadAsync(names[index], token).ConfigureAwait(false))
            .ConfigureAwait(false);
        return [.. files.OfType<HistoryFile>()];
    }

    async Task<HistoryFile?> LoadAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            return await FileOf(name).LoadAsync(cancellationToken).ConfigureAwait(false) is { } entry ? new(name, entry) : null;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogWarning(exception, "Skipped the history entry {Name}", name);
            return null;
        }
    }

    JsonFile<HistoryEntry?> FileOf(string name) => new(Path.Combine(folder.History, name), null, logger);
}
