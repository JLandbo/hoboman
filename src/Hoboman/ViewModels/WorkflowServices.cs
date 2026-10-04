using Hoboman.Core.Auth;
using Hoboman.Core.Languages;
using Hoboman.Core.Storage;
using Hoboman.Core.Workflows;
using Hoboman.Services;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed record WorkflowServices(WorkflowLibrary Library, WorkflowCheck Check, WorkflowRunner Runner, EnvironmentsViewModel Environments, IDialogs Dialogs, Translator Translator, TimeProvider Clock, SecretStore Secrets,
    AuthRefreshService AuthRefresh, ILogger<WorkflowViewModel> Logger)
{
    // A secret is forgotten only when no workflow uses its owner any more, as a copy of a workflow shares the ids of its steps.
    // It does not come back to the calling thread, so the app can wait for it as it closes.
    public async Task ForgetSecretsAsync(IEnumerable<Guid> owners)
    {
        var unused = owners.ToHashSet();
        if (unused.Count == 0)
        {
            return;
        }
        try
        {
            if (await Library.SecretOwnersAsync(CancellationToken.None).ConfigureAwait(false) is { } used)
            {
                unused.ExceptWith(used);
                await Secrets.DeleteAsync(unused, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            Logger.LogWarning(exception, "Could not forget the secrets of removed steps");
        }
    }
}
