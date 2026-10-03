using System.Text.Json;
using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.History;
using Hoboman.Core.Requests;
using Hoboman.Core.Sending;
using Hoboman.Core.Settings;
using Hoboman.Core.Storage;
using Hoboman.Core.Workflows;

namespace Hoboman.Cli;

sealed class CliApplication(RequestLibrary library, SettingsStore settings, EnvironmentStore environments, RequestRunner runner, WorkflowLibrary workflows, WorkflowCheck check,
    WorkflowRunner workflowRunner, UnaskedTokens tokens, CliOutput output, VariableInput variables)
{
    public async Task<int> RunAsync(string[] arguments, CancellationToken cancellationToken)
    {
        try
        {
            var input = new CommandLine().Parse(arguments);
            if (input.Problem is not null)
            {
                return await output.WriteErrorAsync(input.Problem);
            }
            if (input.StandardOutput is not null)
            {
                return output.WriteStandardText(input.StandardOutput);
            }
            if (input.Run is not null)
            {
                return await RunWorkflowAsync(input.Run, cancellationToken);
            }
            return input.IsList ? await ListAsync(cancellationToken) : await SendAsync(input.Send!, cancellationToken);
        }
        catch (Exception exception)
        {
            return await output.WriteErrorAsync(RequestProblem.Of(exception, cancellationToken));
        }
    }

    async Task<int> ListAsync(CancellationToken cancellationToken)
    {
        var names = await library.NamesAsync(cancellationToken);
        await output.WriteNamesAsync(names.Order(StringComparer.OrdinalIgnoreCase).ThenBy(name => name, StringComparer.Ordinal), cancellationToken);
        return 0;
    }

    async Task<int> SendAsync(SendInput input, CancellationToken cancellationToken)
    {
        IReadOnlyList<KeyValue> overrides;
        try
        {
            overrides = await variables.ReadAsync(input, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            return await output.WriteErrorAsync("Invalid variable input.");
        }
        var name = input.IsDirect ? null : input.Target[0];
        ApiRequest? request;
        try
        {
            request = input.IsDirect ? await RequestInput.CreateAsync(input, cancellationToken) : await library.LoadAsync(name!, cancellationToken);
        }
        catch (Exception exception) when (!input.IsDirect && (FileProblem.Is(exception) || exception is ArgumentException))
        {
            request = null;
        }
        if (request is null)
        {
            return await output.WriteErrorAsync("Saved request could not be loaded.");
        }
        var (environment, problem) = await EnvironmentAsync(input.EnvironmentName, cancellationToken);
        if (environment is null)
        {
            return await output.WriteErrorAsync(problem!);
        }
        // The temporary values only live in this call, and a token is saved for the environment, so it is fetched without them.
        var used = overrides.Count > 0 ? environment.WithVariables(overrides) : environment;
        var response = await runner.RunAsync(request, name, used, HistorySource.Cli, auth => tokens.FetchAsync(auth, environment, cancellationToken), cancellationToken);
        return await output.WriteResponseAsync(response, cancellationToken);
    }

    // Everything is read and checked before the run starts, so a run that cannot start writes to stderr only and leaves no run log.
    async Task<int> RunWorkflowAsync(RunInput input, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, JsonElement> parameters;
        try
        {
            parameters = await variables.ReadAsync(input, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            return await output.WriteErrorAsync("Invalid parameter input.");
        }
        Workflow? workflow;
        try
        {
            workflow = await workflows.LoadAsync(input.Workflow, cancellationToken);
        }
        catch (InvalidFileException exception) when (exception.InnerException is JsonException invalid)
        {
            // Only where the file is wrong is told, as the message of the exception can quote a value from it.
            return await output.WriteErrorAsync(new { error = "Workflow file is not valid.", file = exception.FilePath, path = invalid.Path, line = invalid.LineNumber + 1 });
        }
        catch (Exception exception) when (FileProblem.Is(exception) || exception is ArgumentException)
        {
            workflow = null;
        }
        if (workflow is null)
        {
            return await output.WriteErrorAsync("Workflow could not be loaded.");
        }
        var (environment, problem) = await EnvironmentAsync(input.EnvironmentName, cancellationToken);
        if (environment is null)
        {
            return await output.WriteErrorAsync(problem!);
        }
        var checkedWorkflow = await check.CheckAsync(input.Workflow, workflow, environment, parameters, cancellationToken);
        if (checkedWorkflow.Problems.Count > 0)
        {
            return await output.WriteErrorAsync(new { error = "Workflow cannot run.", problems = checkedWorkflow.Problems });
        }
        // As for send, a token is saved for the environment, so it is fetched without the values of the run.
        var outcome = await workflowRunner.RunAsync(checkedWorkflow, environment, HistorySource.Cli, auth => tokens.FetchAsync(auth, environment, cancellationToken), output.WriteEventAsync, cancellationToken);
        return outcome == RunOutcome.Succeeded ? 0 : 1;
    }

    // --env wins over the environment selected in the app, which is used without reading the environments when there is none.
    async Task<(ApiEnvironment? Environment, string? Problem)> EnvironmentAsync(string? name, CancellationToken cancellationToken)
    {
        try
        {
            var chosen = name is null ? await settings.LoadAsync(cancellationToken) : null;
            var environment = chosen switch
            {
                null => await environments.FindAsync(name, cancellationToken),
                { EnvironmentId: null, EnvironmentName: null } => ApiEnvironment.None,
                _ => chosen.EnvironmentIn(await environments.AllAsync(cancellationToken)),
            };
            return environment is null ? (null, "Selected environment was not found.") : (environment, null);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            return (null, "Environment settings could not be read.");
        }
    }
}
