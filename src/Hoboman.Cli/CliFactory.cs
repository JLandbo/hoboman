using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.History;
using Hoboman.Core.Requests;
using Hoboman.Core.Sending;
using Hoboman.Core.Settings;
using Hoboman.Core.Storage;
using Hoboman.Core.Workflows;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoboman.Cli;

// Builds the application around an output and an input, sharing everything else, so the CLI and each MCP call work alike,
// and calls that come at the same time share the stores and their locks.
sealed class CliFactory(AppFolder folder, SecretStore secrets, IRequestSender sender, IOAuthClient oauth)
{
    readonly SettingsStore _settings = new(folder, NullLogger<SettingsStore>.Instance);
    readonly EnvironmentStore _environments = new(folder, NullLogger<EnvironmentStore>.Instance);
    readonly RequestLibrary _library = new(folder, NullLogger<RequestLibrary>.Instance);
    readonly HistoryStore _history = new(folder, NullLogger<HistoryStore>.Instance);
    readonly WorkflowLibrary _workflows = new(folder, NullLogger<WorkflowLibrary>.Instance);
    readonly OwnFiles _files = new(folder);

    public CliApplication Create(Stream outputStream, Stream errorStream, TextReader input, bool inputRedirected)
    {
        var output = new CliOutput(outputStream, errorStream);
        var variables = new VariableInput(input, inputRedirected, _files);
        var targets = new Targets(_workflows, _environments);
        var workflowDeletion = new WorkflowDeletion(_workflows, secrets, NullLogger<WorkflowDeletion>.Instance);
        var environmentChanges = new EnvironmentChanges(_environments, secrets, new(folder, secrets, NullLogger<CredentialStore>.Instance), _settings);
        return new(_library, _settings, _environments, new(sender, _library, _history, NullLogger<RequestRunner>.Instance), _workflows,
            new(_workflows, secrets, NullLogger<WorkflowCheck>.Instance), new(sender, folder, TimeProvider.System, NullLogger<WorkflowRunner>.Instance),
            new(oauth, secrets, NullLogger<UnaskedTokens>.Instance), output, variables, new(_library, secrets, folder, NullLogger<RequestDeletion>.Instance), workflowDeletion, targets,
            new(_library, _workflows, _environments, targets, output), new(_library, _workflows, workflowDeletion, _environments, environmentChanges, targets, variables, output),
            new(folder, _workflows, targets, output), environmentChanges, new(_history, output), _files);
    }
}
