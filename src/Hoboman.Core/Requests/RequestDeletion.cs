using System.Runtime.ExceptionServices;
using Hoboman.Core.Auth;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Requests;

public sealed class RequestDeletion(RequestLibrary library, SecretStore secrets, AppFolder folder, ILogger<RequestDeletion> logger)
{
    readonly JsonFile<IReadOnlyList<Guid>> _pending = new(folder.PendingSecretCleanup, [], logger);

    // A folder goes with every folder and request in it. What cannot be deleted stays, with the folders it lies in, so nothing is left without its folder.
    public async Task DeleteAsync(Guid id, bool isFolder, CancellationToken cancellationToken)
    {
        var collection = isFolder ? await library.LoadAllAsync(cancellationToken).ConfigureAwait(false) : RequestCollection.Empty;
        var folders = isFolder ? collection.FoldersIn(id).Append(id).ToHashSet() : [];
        IReadOnlyList<(Guid Id, Guid? FolderId)> requests = isFolder
            ? [.. collection.Requests.Where(request => request.FolderId is { } inside && folders.Contains(inside)).Select(request => (request.Id, request.FolderId))]
            : [(id, null)];
        // Remember ownership before deleting files, so cleanup can resume after a failure or restart.
        await _pending.UpdateAsync(pending => [.. pending.Concat(folders).Concat(requests.Select(request => request.Id)).Distinct()], cancellationToken).ConfigureAwait(false);
        ExceptionDispatchInfo? failure = null;
        try
        {
            var kept = new HashSet<Guid>();
            foreach (var request in requests)
            {
                try
                {
                    await library.DeleteAsync(request.Id, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (FileProblem.Is(exception))
                {
                    failure ??= ExceptionDispatchInfo.Capture(exception);
                    kept.UnionWith(collection.FoldersDownTo(request.FolderId).Select(above => above.Id));
                }
            }
            // The deepest folders go first, so a folder that cannot be deleted keeps the folders it lies in.
            foreach (var deleted in folders.OrderByDescending(deleted => collection.FoldersDownTo(deleted).Count).Where(deleted => !kept.Contains(deleted)))
            {
                try
                {
                    await library.DeleteFolderAsync(deleted, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (FileProblem.Is(exception))
                {
                    failure ??= ExceptionDispatchInfo.Capture(exception);
                    kept.UnionWith(collection.FoldersDownTo(deleted).Select(above => above.Id));
                }
            }
        }
        finally
        {
            await CleanupAsync(CancellationToken.None).ConfigureAwait(false);
        }
        failure?.Throw();
    }

    public async Task CleanupAsync(CancellationToken cancellationToken)
    {
        var pending = await _pending.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return;
        }
        // A file is named by its id, so a request or folder that is still there keeps its secrets, even when it cannot be read.
        var used = await library.IdsAsync(cancellationToken).ConfigureAwait(false);
        await secrets.DeleteAsync(pending.Where(id => !used.Contains(id)).ToHashSet(), cancellationToken).ConfigureAwait(false);
        await _pending.SaveAsync([], cancellationToken).ConfigureAwait(false);
    }
}
