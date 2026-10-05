using Hoboman.Core.Auth;
using Hoboman.Core.Languages;
using Hoboman.Core.Workflows;
using Hoboman.Services;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed record WorkflowServices(WorkflowLibrary Library, WorkflowCheck Check, WorkflowRunner Runner, EnvironmentsViewModel Environments, CredentialsViewModel Credentials, IDialogs Dialogs, Translator Translator, TimeProvider Clock, SecretStore Secrets,
    AuthRefreshService AuthRefresh, ILogger<WorkflowViewModel> Logger, WorkflowDeletion Deletion)
{
    public Task ForgetSecretsAsync(IEnumerable<Guid> owners) => Deletion.ForgetSecretsAsync(owners, CancellationToken.None);
}
