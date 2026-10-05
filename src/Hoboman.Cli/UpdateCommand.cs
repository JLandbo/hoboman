using System.Text.Json;
using Hoboman.Core.Environments;
using Hoboman.Core.Requests;
using Hoboman.Core.Storage;
using Hoboman.Core.Workflows;

namespace Hoboman.Cli;

// Replaces what a request or workflow holds with what show wrote and a program changed. The id, the name and the place stay,
// so its secrets, history and runs stay with it, and a name or place is changed with rename and move, which check theirs.
sealed class UpdateCommand(RequestLibrary library, WorkflowLibrary workflows, WorkflowDeletion deletion, EnvironmentStore environments, EnvironmentChanges environmentChanges,
    Targets targets, VariableInput reader, CliOutput output)
{
    sealed record RequestUpdate(ApiRequest Request);

    // Scripts that are not given are left as they are, and one given as null is deleted.
    sealed record WorkflowUpdate(Workflow Workflow, IReadOnlyDictionary<string, string?>? Scripts = null);

    sealed record EnvironmentUpdate(ApiEnvironment Environment);

    public async Task<int> RunAsync(UpdateInput input, CancellationToken cancellationToken)
    {
        var collection = await library.LoadAllAsync(cancellationToken);
        Saved? found;
        string? problem;
        try
        {
            (found, problem) = await targets.FindAsync(input.Target, collection, withEnvironments: true, cancellationToken);
        }
        catch (InvalidFileException exception) when (exception.InnerException is JsonException invalid)
        {
            return await output.WriteInvalidFileAsync("File is not valid.", exception, invalid);
        }
        if (found is null)
        {
            return await output.WriteErrorAsync(problem!);
        }
        if (found.Kind == SavedKind.Folder)
        {
            return await output.WriteErrorAsync("A folder cannot be updated.");
        }
        string text;
        try
        {
            text = await reader.TextOfAsync(input.File, cancellationToken);
        }
        catch (Exception exception) when (exception is FormatException || FileProblem.Is(exception))
        {
            return await output.WriteErrorAsync("Input could not be read.");
        }
        try
        {
            problem = found.Kind switch
            {
                SavedKind.Request => await RequestAsync(found.Id, JsonFile<RequestUpdate>.Parse(text).Request, cancellationToken),
                SavedKind.Environment => await EnvironmentAsync(found.Id, JsonFile<EnvironmentUpdate>.Parse(text).Environment, cancellationToken),
                _ => await WorkflowAsync(found.Id, JsonFile<WorkflowUpdate>.Parse(text), cancellationToken),
            };
        }
        catch (InvalidFileException exception) when (exception.InnerException is JsonException invalid)
        {
            return await output.WriteInvalidFileAsync("File is not valid.", exception, invalid);
        }
        // Only where the input is wrong is told, as the message can quote a value from it.
        catch (JsonException invalid)
        {
            return await output.WriteErrorAsync(new { error = "Input is not valid.", path = invalid.Path, line = invalid.LineNumber + 1 });
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            return await output.WriteErrorAsync("The change could not be saved.");
        }
        return problem is null ? await output.WriteResultAsync(new { id = found.Id, path = found.Path }) : await output.WriteErrorAsync(problem);
    }

    async Task<string?> RequestAsync(Guid id, ApiRequest request, CancellationToken cancellationToken)
    {
        var saved = await library.LoadAsync(id, cancellationToken) ?? throw new FileNotFoundException("The request is gone.");
        if (Problem(request.Id, id, request.Name, saved.Name) is { } problem)
        {
            return problem;
        }
        if (request.FolderId != saved.FolderId)
        {
            return "Use move to change the folder.";
        }
        await library.SaveAsync(request with { Id = id }, cancellationToken);
        return null;
    }

    async Task<string?> EnvironmentAsync(Guid id, ApiEnvironment environment, CancellationToken cancellationToken)
    {
        var saved = (await environments.AllAsync(cancellationToken)).FirstOrDefault(each => each.Id == id) ?? throw new FileNotFoundException("The environment is gone.");
        if (Problem(environment.Id, id, environment.Name, saved.Name) is { } problem)
        {
            return problem;
        }
        await environmentChanges.UpdateAsync(id, environment.Variables, cancellationToken);
        return null;
    }

    // A step keeps the id its secrets are saved under, and a new step gets one of its own, as in the app.
    // An id from anywhere else would share the secrets of what has it, so only the workflow's own are taken.
    async Task<string?> WorkflowAsync(Guid id, WorkflowUpdate update, CancellationToken cancellationToken)
    {
        var saved = await workflows.LoadAsync(id, cancellationToken) ?? throw new FileNotFoundException("The workflow is gone.");
        if (Problem(update.Workflow.Id, id, update.Workflow.Name, saved.Name) is { } problem)
        {
            return problem;
        }
        var scripts = update.Scripts ?? new Dictionary<string, string?>();
        if (scripts.Keys.Any(script => !WorkflowLibrary.IsValidScriptName(script)))
        {
            return "Invalid script name.";
        }
        // A script a step still uses would leave the step without its code.
        if (scripts.Any(script => script.Value is null && update.Workflow.Steps.Any(step => string.Equals(step.Script, script.Key, StringComparison.OrdinalIgnoreCase))))
        {
            return "A script a step uses cannot be deleted.";
        }
        var owners = WorkflowLibrary.SecretOwnersOf(saved).Where(owner => owner != id).ToHashSet();
        var given = update.Workflow.Steps.Select(step => step.Request?.Id ?? Guid.Empty).Where(step => step != Guid.Empty).ToList();
        if (given.Count != given.Distinct().Count() || !given.All(owners.Contains))
        {
            return "Step id is not one of the workflow's.";
        }
        var workflow = update.Workflow with
        {
            Id = id,
            Steps = [.. update.Workflow.Steps.Select(step => step.Request is { } request && request.Id == Guid.Empty ? step with { Request = request with { Id = Guid.NewGuid() } } : step)],
        };
        // The scripts go first and the deleted ones last, so a workflow never points at one that could not be saved.
        foreach (var (script, code) in scripts.Where(script => script.Value is not null))
        {
            await workflows.SaveScriptAsync(id, script, code!, cancellationToken);
        }
        await workflows.SaveAsync(workflow, cancellationToken, createDirectory: false);
        foreach (var script in scripts.Where(script => script.Value is null).Select(script => script.Key))
        {
            await workflows.DeleteScriptAsync(id, script, cancellationToken);
        }
        // A step that is gone takes its secrets with it, unless something else uses them, as when the app saves.
        await deletion.ForgetSecretsAsync(WorkflowLibrary.SecretOwnersOf(saved).Except(WorkflowLibrary.SecretOwnersOf(workflow)), cancellationToken);
        return null;
    }

    static string? Problem(Guid given, Guid id, string name, string savedName) =>
        given != Guid.Empty && given != id ? "Target is not the one in the input." : name != savedName ? "Use rename to change the name." : null;
}
