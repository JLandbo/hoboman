using Hoboman.Core.Auth;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Requests;

public sealed class RequestDeletion(RequestLibrary library, SecretStore secrets, AppFolder folder, ILogger<RequestDeletion> logger)
{
    readonly JsonFile<IReadOnlyList<Guid>> _pending = new(folder.PendingSecretCleanup, [], logger);

    public async Task DeleteAsync(string name, bool isFolder, CancellationToken cancellationToken)
    {
        var requests = isFolder ? (await library.NamesAsync(cancellationToken).ConfigureAwait(false)).Where(path => IsInside(path, name)).ToList() : [name];
        var folders = isFolder ? (await library.FoldersAsync(cancellationToken).ConfigureAwait(false)).Where(path => IsInside(path, name)).Prepend(name).ToList() : [];
        var ids = await IdsOfAsync(requests, folders, cancellationToken).ConfigureAwait(false);
        if (ids.Count > 0)
        {
            // Remember ownership before deleting files, so cleanup can resume after a failure or restart.
            await _pending.UpdateAsync(pending => [.. pending.Concat(ids).Distinct()], cancellationToken).ConfigureAwait(false);
        }
        try
        {
            if (isFolder)
            {
                await library.DeleteFolderAsync(name, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await library.DeleteAsync(name, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await CleanupAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async Task CleanupAsync(CancellationToken cancellationToken)
    {
        var pending = await _pending.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return;
        }
        // An unreadable owner is unknown, not unused. No secrets are removed until every owner can be checked.
        var requests = await library.NamesAsync(cancellationToken).ConfigureAwait(false);
        var folders = await library.FoldersAsync(cancellationToken).ConfigureAwait(false);
        var used = await IdsOfAsync(requests, folders, cancellationToken).ConfigureAwait(false);
        await secrets.DeleteAsync(pending.Where(id => !used.Contains(id)).ToHashSet(), cancellationToken).ConfigureAwait(false);
        await _pending.SaveAsync([], cancellationToken).ConfigureAwait(false);
    }

    async Task<HashSet<Guid>> IdsOfAsync(IEnumerable<string> requests, IEnumerable<string> folders, CancellationToken cancellationToken)
    {
        var ids = new HashSet<Guid>();
        foreach (var name in requests)
        {
            if (await library.LoadAsync(name, cancellationToken).ConfigureAwait(false) is { } request)
            {
                ids.Add(request.Id);
            }
        }
        foreach (var name in folders)
        {
            if (await library.LoadFolderAsync(name, cancellationToken).ConfigureAwait(false) is { } settings)
            {
                ids.Add(settings.Id);
            }
        }
        ids.Remove(Guid.Empty);
        return ids;
    }

    static bool IsInside(string path, string folder) => path.StartsWith($"{folder}/", StringComparison.OrdinalIgnoreCase);
}
