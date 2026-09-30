using System.Globalization;
using System.Text.RegularExpressions;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.History;

public sealed partial class HistoryStore(AppFolder folder, ILogger<HistoryStore> logger)
{
    // One file per call, named by time, so the app and the CLI can add entries at the same time and the newest are found without reading the rest.
    public Task AddAsync(HistoryEntry entry, CancellationToken cancellationToken) =>
        FileOf(string.Create(CultureInfo.InvariantCulture, $"{entry.At.UtcDateTime:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json")).SaveAsync(entry, cancellationToken);

    public async Task<IReadOnlyList<HistoryFile>> LatestAsync(int count, string? newerThan, CancellationToken cancellationToken)
    {
        var names = (await NamesAsync(cancellationToken).ConfigureAwait(false))
            .Where(name => newerThan is null || string.CompareOrdinal(name, newerThan) > 0).OrderDescending(StringComparer.Ordinal).Take(count).ToArray();
        var files = new HistoryFile?[names.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, names.Length), cancellationToken, async (index, token) => files[index] = await LoadAsync(names[index], token).ConfigureAwait(false))
            .ConfigureAwait(false);
        return [.. files.OfType<HistoryFile>()];
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken) => Task.Run(() =>
    {
        File.Delete(Path.Combine(folder.History, name));
        logger.LogInformation("Deleted the call {Name} from the history", name);
    }, cancellationToken);

    // Only the names, which is cheap next to reading the calls.
    public Task<IReadOnlySet<string>> NamesAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlySet<string>>(() => Directory.Exists(folder.History)
        ? Directory.EnumerateFiles(folder.History, "*.json").Select(Path.GetFileName).OfType<string>().Where(IsCall).ToHashSet(StringComparer.Ordinal)
        : new HashSet<string>(), cancellationToken);

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

    // Any other file would sort as the newest and hide the calls that come after it.
    bool IsCall(string name)
    {
        if (CallName().IsMatch(name))
        {
            return true;
        }
        logger.LogDebug("{Name} in the history folder is not a call and is left out", name);
        return false;
    }

    JsonFile<HistoryEntry?> FileOf(string name) => new(Path.Combine(folder.History, name), null, logger);

    [GeneratedRegex(@"^\d{8}-\d{6}-\d{3}-")]
    private static partial Regex CallName();
}
