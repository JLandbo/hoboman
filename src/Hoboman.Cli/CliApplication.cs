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
    WorkflowRunner workflowRunner, UnaskedTokens tokens, CliOutput output, VariableInput variables, RequestDeletion deletion, WorkflowDeletion workflowDeletion,
    Targets targets, ShowCommand show, UpdateCommand update, LogCommand log, EnvironmentChanges environmentChanges, HistoryCommand history)
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
            if (input.New is not null)
            {
                return await NewAsync(input.New, cancellationToken);
            }
            if (input.Change is not null)
            {
                return await ChangeAsync(input.Change, cancellationToken);
            }
            if (input.Show is not null)
            {
                return await show.RunAsync(input.Show, cancellationToken);
            }
            if (input.Update is not null)
            {
                return await update.RunAsync(input.Update, cancellationToken);
            }
            if (input.Log is not null)
            {
                return await log.RunAsync(input.Log, cancellationToken);
            }
            if (input.History is not null)
            {
                return await history.RunAsync(input.History, cancellationToken);
            }
            return input.IsList ? await ListAsync(input, cancellationToken) : await SendAsync(input.Send!, cancellationToken);
        }
        catch (Exception exception)
        {
            return await output.WriteErrorAsync(RequestProblem.Of(exception, cancellationToken));
        }
    }

    // The id first, as a name can hold anything but a tab, so a line splits at its first tab.
    // A file that cannot be read has no name, so it is listed by its id alone, and send or run with the id tells what is wrong with it.
    async Task<int> ListAsync(CommandInput input, CancellationToken cancellationToken)
    {
        if (input.ListsEnvironments)
        {
            return await ListEnvironmentsAsync(cancellationToken);
        }
        IEnumerable<(Guid Id, string Name)> listed;
        if (input.ListsWorkflows)
        {
            listed = (await workflows.ListAsync(cancellationToken)).Select(workflow => (workflow.Id, workflow.Name ?? ""));
        }
        else if (input.ListsFolders)
        {
            var folders = await library.LoadAllAsync(cancellationToken);
            listed = folders.Folders.Select(folder => (folder.Id, folders.FolderPathOf(folder.Id)));
        }
        else
        {
            var collection = await library.LoadAllAsync(cancellationToken);
            listed = collection.Requests.Select(request => (request.Id, collection.PathOf(request))).Concat(collection.Unreadable.Select(id => (id, "")));
        }
        await output.WriteNamesAsync(listed.OrderBy(line => line.Name, StringComparer.OrdinalIgnoreCase).ThenBy(line => line.Name, StringComparer.Ordinal).ThenBy(line => line.Id)
            .Select(line => $"{line.Id}\t{line.Name}"), cancellationToken);
        return 0;
    }

    // The environment chosen in the app is marked, as send, run and check use it without --env. Names are unique, so they sort alone.
    async Task<int> ListEnvironmentsAsync(CancellationToken cancellationToken)
    {
        Guid? chosen;
        IReadOnlyList<ApiEnvironment> all;
        try
        {
            chosen = (await settings.LoadAsync(cancellationToken)).EnvironmentId;
            all = await environments.AllAsync(cancellationToken);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            return await output.WriteErrorAsync("Environment settings could not be read.");
        }
        await output.WriteNamesAsync(all.OrderBy(environment => environment.Name, StringComparer.OrdinalIgnoreCase)
            .Select(environment => environment.Id == chosen ? $"{environment.Id}\t{environment.Name}\tselected" : $"{environment.Id}\t{environment.Name}"), cancellationToken);
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
        ApiRequest? request;
        try
        {
            if (input.IsDirect)
            {
                request = await RequestInput.CreateAsync(input, cancellationToken);
            }
            else
            {
                // A request found by its id is read alone, and one found by its path needs them all, as names are only in the files.
                IReadOnlyList<ApiRequest> found = RequestLibrary.TryIdOf(input.Target[0], out var id)
                    ? await library.LoadAsync(id, cancellationToken) is { } loaded ? [loaded] : []
                    : (await library.LoadAllAsync(cancellationToken)).Find(input.Target[0]);
                if (found.Count > 1)
                {
                    return await output.WriteErrorAsync("Saved request name is ambiguous. Use its id.");
                }
                request = found.SingleOrDefault();
            }
        }
        catch (InvalidFileException exception) when (!input.IsDirect && exception.InnerException is JsonException invalid)
        {
            return await output.WriteInvalidFileAsync("Saved request file is not valid.", exception, invalid);
        }
        catch (Exception exception) when (!input.IsDirect && FileProblem.Is(exception))
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
        var response = await runner.RunAsync(request, used, HistorySource.Cli, auth => tokens.FetchAsync(auth, environment, cancellationToken), cancellationToken);
        if (input.OutFile is null)
        {
            return await output.WriteResponseAsync(response, cancellationToken);
        }
        var file = Path.GetFullPath(input.OutFile);
        try
        {
            await File.WriteAllBytesAsync(file, response.Bytes ?? [], cancellationToken);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            return await output.WriteErrorAsync("Output file could not be written.");
        }
        return await output.WriteResponseAsync(response, file, cancellationToken);
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
            // A workflow found by its id is read alone, without reading the others for their names.
            var found = await targets.WorkflowsAsync(input.Workflow, cancellationToken);
            if (found.Count > 1)
            {
                return await output.WriteErrorAsync("Workflow name is ambiguous. Use its id.");
            }
            workflow = found.Count == 1 ? await workflows.LoadAsync(found[0], cancellationToken) : null;
        }
        catch (InvalidFileException exception) when (exception.InnerException is JsonException invalid)
        {
            return await output.WriteInvalidFileAsync("Workflow file is not valid.", exception, invalid);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            workflow = null;
        }
        if (workflow is null)
        {
            return await output.WriteErrorAsync("Workflow could not be loaded.");
        }
        var (environment, environmentProblem) = await EnvironmentAsync(input.EnvironmentName, cancellationToken);
        if (environment is null)
        {
            return await output.WriteErrorAsync(environmentProblem!);
        }
        var checkedWorkflow = await check.CheckAsync(workflow, environment, parameters, cancellationToken);
        if (checkedWorkflow.Problems.Count > 0)
        {
            return await output.WriteErrorAsync(new { error = "Workflow cannot run.", problems = checkedWorkflow.Problems });
        }
        if (input.CheckOnly)
        {
            return await output.WriteResultAsync(new { id = workflow.Id, name = workflow.Name });
        }
        // As for send, a token is saved for the environment, so it is fetched without the values of the run.
        var outcome = await workflowRunner.RunAsync(checkedWorkflow, environment, auth => tokens.FetchAsync(auth, environment, cancellationToken), output.WriteEventAsync, cancellationToken);
        return outcome == RunOutcome.Succeeded ? 0 : 1;
    }

    // Nothing is changed before the name, the target and the folder are known to be right, so a mistake changes nothing.
    async Task<int> NewAsync(NewInput input, CancellationToken cancellationToken)
    {
        var name = input.Name.Trim();
        if (!RequestLibrary.IsValidName(name))
        {
            return await output.WriteErrorAsync("Invalid name.");
        }
        if (input.Kind == SavedKind.Workflow)
        {
            return await SavedAsync(async () => (await workflows.CreateAsync(name, cancellationToken)).Id, name);
        }
        if (input.Kind == SavedKind.Environment)
        {
            return await environmentChanges.IsTakenAsync(name, null, cancellationToken)
                ? await output.WriteErrorAsync("Environment name is taken.")
                : await SavedAsync(() => environmentChanges.CreateAsync(name, cancellationToken), name);
        }
        var collection = await library.LoadAllAsync(cancellationToken);
        var (folder, problem) = FolderOf(input.Folder!, collection);
        if (problem is not null)
        {
            return await output.WriteErrorAsync(problem);
        }
        var request = input.Kind == SavedKind.Request ? await RequestInput.CreateAsync(input.Method, input.Url, input.Headers, input.JsonBody, input.TextBody, cancellationToken) : null;
        return await SavedAsync(async () =>
        {
            var id = Guid.NewGuid();
            await (request is null
                ? library.CreateFolderAsync(new() { Id = id, Name = name, ParentId = folder }, cancellationToken)
                : library.CreateAsync(request with { Id = id, Name = name, FolderId = folder }, cancellationToken));
            return id;
        }, PathIn(collection, folder, name));
    }

    async Task<int> ChangeAsync(ChangeInput input, CancellationToken cancellationToken)
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
        return input.Command switch
        {
            ChangeKind.Rename => await RenameAsync(found, input.Value!.Trim(), collection, cancellationToken),
            ChangeKind.Move => await MoveAsync(found, input.Value!, collection, cancellationToken),
            ChangeKind.Delete => await DeleteAsync(found, input.Yes, collection, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };
    }

    async Task<int> RenameAsync(Saved found, string name, RequestCollection collection, CancellationToken cancellationToken)
    {
        if (!RequestLibrary.IsValidName(name))
        {
            return await output.WriteErrorAsync("Invalid name.");
        }
        if (found.Kind == SavedKind.Environment && await environmentChanges.IsTakenAsync(name, found.Id, cancellationToken))
        {
            return await output.WriteErrorAsync("Environment name is taken.");
        }
        var parent = found.Kind switch
        {
            SavedKind.Request => collection.RequestOf(found.Id) is { } request ? collection.FolderIdOf(request) : null,
            SavedKind.Folder => collection.ParentOf(collection.FolderOf(found.Id)!),
            _ => null,
        };
        return await SavedAsync(async () =>
        {
            await (found.Kind switch
            {
                SavedKind.Request => library.RenameAsync(found.Id, name, cancellationToken),
                SavedKind.Folder => library.RenameFolderAsync(found.Id, name, cancellationToken),
                SavedKind.Environment => environmentChanges.RenameAsync(found.Id, name, cancellationToken),
                _ => workflows.RenameAsync(found.Id, name, cancellationToken),
            });
            return found.Id;
        }, found.Kind is SavedKind.Workflow or SavedKind.Environment ? name : PathIn(collection, parent, name));
    }

    async Task<int> MoveAsync(Saved found, string target, RequestCollection collection, CancellationToken cancellationToken)
    {
        if (found.Kind is SavedKind.Workflow or SavedKind.Environment)
        {
            return await output.WriteErrorAsync(found.Kind == SavedKind.Workflow ? "A workflow cannot be moved." : "An environment cannot be moved.");
        }
        var (folder, problem) = FolderOf(target, collection);
        if (problem is not null)
        {
            return await output.WriteErrorAsync(problem);
        }
        if (found.Kind == SavedKind.Folder && collection.FoldersDownTo(folder).Any(above => above.Id == found.Id))
        {
            return await output.WriteErrorAsync("A folder cannot be moved into itself.");
        }
        return await SavedAsync(async () =>
        {
            await (found.Kind == SavedKind.Request ? library.MoveAsync(found.Id, folder, cancellationToken) : library.MoveFolderAsync(found.Id, folder, cancellationToken));
            return found.Id;
        }, PathIn(collection, folder, found.Name));
    }

    async Task<int> DeleteAsync(Saved found, bool yes, RequestCollection collection, CancellationToken cancellationToken)
    {
        if (!yes)
        {
            // What would go is told, so the caller can ask the user before it adds --yes.
            var folders = found.Kind == SavedKind.Folder ? collection.FoldersIn(found.Id) : new HashSet<Guid>();
            return await output.WriteErrorAsync(new { error = "Deleting needs --yes.", path = found.Path, folders = folders.Count,
                requests = collection.Requests.Count(request => request.FolderId is { } inside && folders.Contains(inside)) });
        }
        return await SavedAsync(async () =>
        {
            await (found.Kind switch
            {
                SavedKind.Workflow => workflowDeletion.DeleteAsync(found.Id, cancellationToken),
                SavedKind.Environment => environmentChanges.DeleteAsync(found.Id, cancellationToken),
                _ => deletion.DeleteAsync(found.Id, found.Kind == SavedKind.Folder, cancellationToken),
            });
            return found.Id;
        }, found.Path);
    }

    // . is the top, as a shell such as Git Bash turns / into a path, and a folder is otherwise known by its id or its path.
    static (Guid? Id, string? Problem) FolderOf(string target, RequestCollection collection)
    {
        if (target == ".")
        {
            return (null, null);
        }
        var found = Targets.Matching(target, collection.Folders, folder => folder.Id, folder => collection.FolderPathOf(folder.Id));
        return found.Count switch { 0 => (null, "Folder could not be found."), 1 => (found[0].Id, null), _ => (null, "Folder is ambiguous. Use its id.") };
    }

    static string PathIn(RequestCollection collection, Guid? folder, string name) => folder is null ? name : $"{collection.FolderPathOf(folder)}/{name}";

    // The change is told by the id and path it has now, as list writes them.
    async Task<int> SavedAsync(Func<Task<Guid>> change, string path)
    {
        Guid id;
        try
        {
            id = await change();
        }
        catch (InvalidFileException exception) when (exception.InnerException is JsonException invalid)
        {
            return await output.WriteInvalidFileAsync("File is not valid.", exception, invalid);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            return await output.WriteErrorAsync("The change could not be saved.");
        }
        return await output.WriteResultAsync(new { id, path });
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
                { EnvironmentId: null } => ApiEnvironment.None,
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
