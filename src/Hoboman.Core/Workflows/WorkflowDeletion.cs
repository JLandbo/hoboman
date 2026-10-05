using Hoboman.Core.Auth;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Workflows;

// The app and the CLI delete a workflow the same way, so its steps' secrets go with it in both.
public sealed class WorkflowDeletion(WorkflowLibrary workflows, SecretStore secrets, ILogger<WorkflowDeletion> logger)
{
    // The owners are read before the folder goes, and only a workflow that can be read tells them, so the secrets of one that cannot are kept.
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var owners = await OwnersOfAsync(id, cancellationToken).ConfigureAwait(false);
        await workflows.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        await ForgetSecretsAsync(owners, cancellationToken).ConfigureAwait(false);
    }

    // A secret is forgotten only when no workflow uses its owner any more, as a copy of a workflow shares the ids of its steps.
    // It does not come back to the calling thread, so the app can wait for it as it closes.
    public async Task ForgetSecretsAsync(IEnumerable<Guid> owners, CancellationToken cancellationToken)
    {
        var unused = owners.ToHashSet();
        if (unused.Count == 0)
        {
            return;
        }
        try
        {
            if (await workflows.SecretOwnersAsync(cancellationToken).ConfigureAwait(false) is { } used)
            {
                unused.ExceptWith(used);
                await secrets.DeleteAsync(unused, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogWarning(exception, "Could not forget the secrets of removed steps");
        }
    }

    async Task<IReadOnlyList<Guid>> OwnersOfAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await workflows.LoadAsync(id, cancellationToken).ConfigureAwait(false) is { } workflow ? [.. WorkflowLibrary.SecretOwnersOf(workflow)] : [];
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogWarning(exception, "Could not read the workflow {Id}, so its secrets are kept", id);
            return [];
        }
    }
}
