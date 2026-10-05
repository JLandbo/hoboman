using Hoboman.Core.Requests;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Workflows;

// One folder per workflow, named by its id, so its scripts lie next to its workflow.json, and renaming it only changes its name.
public sealed class WorkflowLibrary(AppFolder folder, ILogger<WorkflowLibrary> logger)
{
    const string _workflowFile = "workflow.json";

    // A script is a file in the workflow's folder, so its name must work as a file name there and cannot lead out of it.
    public static bool IsValidScriptName(string name) => name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name.EndsWith(".js", StringComparison.OrdinalIgnoreCase);

    // A folder whose name is not an id, or that has no workflow.json, is not a workflow. One that cannot be read has no name.
    public async Task<IReadOnlyList<(Guid Id, string? Name)>> ListAsync(CancellationToken cancellationToken)
    {
        var ids = await IdsAsync(cancellationToken).ConfigureAwait(false);
        var names = new string?[ids.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, ids.Count), cancellationToken, async (index, token) =>
        {
            try
            {
                names[index] = (await LoadAsync(ids[index], token).ConfigureAwait(false))?.Name;
            }
            catch (Exception exception) when (FileProblem.Is(exception))
            {
                logger.LogWarning(exception, "Could not read the workflow {Id}", ids[index]);
            }
        }).ConfigureAwait(false);
        return [.. ids.Select((id, index) => (id, names[index]))];
    }

    public async Task<Workflow?> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await FileOf(id).LoadAsync(cancellationToken).ConfigureAwait(false) is { } workflow ? workflow with { Id = id } : null;

    // A workflow that is already there is saved without creating its folder, so one deleted on disk does not come back.
    public async Task SaveAsync(Workflow workflow, CancellationToken cancellationToken, bool createDirectory = true)
    {
        await FileOf(workflow.Id).SaveAsync(workflow, cancellationToken, createDirectory).ConfigureAwait(false);
        logger.LogInformation("Saved the workflow {Name} ({Id})", workflow.Name, workflow.Id);
    }

    public async Task<Workflow> CreateAsync(string name, CancellationToken cancellationToken)
    {
        var workflow = new Workflow { Id = Guid.NewGuid(), Name = name };
        await FileOf(workflow.Id).SaveAsync(workflow, cancellationToken, overwrite: false).ConfigureAwait(false);
        logger.LogInformation("Created the workflow {Name} ({Id})", name, workflow.Id);
        return workflow;
    }

    // Only the name changes, so edits open in the app are not saved along with it.
    public async Task RenameAsync(Guid id, string name, CancellationToken cancellationToken)
    {
        var workflow = await LoadAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new FileNotFoundException("The workflow is gone.", Path.Combine(FolderOf(id), _workflowFile));
        await SaveAsync(workflow with { Name = name }, cancellationToken, createDirectory: false).ConfigureAwait(false);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Retrying.RunAsync(() =>
    {
        Directory.Delete(FolderOf(id), recursive: true);
        logger.LogInformation("Deleted the workflow {Id}", id);
    }, logger, $"{id}", cancellationToken);

    // A script that is not there, cannot be read or has a name that leads out of the folder gives null, so the check tells of it.
    public async Task<string?> LoadScriptAsync(Guid id, string script, CancellationToken cancellationToken)
    {
        if (!IsValidScriptName(script))
        {
            logger.LogWarning("{Script} in the workflow {Id} is not a valid script name", script, id);
            return null;
        }
        try
        {
            return await File.ReadAllTextAsync(ScriptPathOf(id, script), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogWarning(exception, "Could not read the script {Script} of the workflow {Id}", script, id);
            return null;
        }
    }

    // A file that is already there is left as it is, so its code is never overwritten.
    public async Task CreateScriptAsync(Guid id, string script, string code, CancellationToken cancellationToken)
    {
        var path = ScriptPathOf(id, script);
        if (File.Exists(path))
        {
            return;
        }
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await using var writer = new StreamWriter(file);
        await writer.WriteAsync(code.AsMemory(), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Created the script {Script} of the workflow {Id}", script, id);
    }

    // Like a workflow that is already there, a script is not saved into a folder that was deleted on disk.
    public async Task SaveScriptAsync(Guid id, string script, string code, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(ScriptPathOf(id, script), code, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Saved the script {Script} of the workflow {Id}", script, id);
    }

    public Task<IReadOnlyList<string>> ScriptsAsync(Guid id, CancellationToken cancellationToken) => Task.Run<IReadOnlyList<string>>(() =>
        [.. Directory.EnumerateFiles(FolderOf(id), "*.js").Select(path => Path.GetFileName(path)).Order(StringComparer.OrdinalIgnoreCase)], cancellationToken);

    public Task DeleteScriptAsync(Guid id, string script, CancellationToken cancellationToken) => Retrying.RunAsync(() =>
    {
        File.Delete(ScriptPathOf(id, script));
        logger.LogInformation("Deleted the script {Script} of the workflow {Id}", script, id);
    }, logger, script, cancellationToken);

    // A request step's secrets are saved under its id, and the workflow's own auth under the workflow's id.
    public static IEnumerable<Guid> SecretOwnersOf(Workflow workflow) =>
        workflow.Steps.Select(step => step.Request?.Id ?? Guid.Empty).Append(workflow.Auth is null ? Guid.Empty : workflow.Id).Where(id => id != Guid.Empty);

    // A workflow that cannot be read may use any id, so then there is no answer.
    public async Task<IReadOnlySet<Guid>?> SecretOwnersAsync(CancellationToken cancellationToken)
    {
        var owners = new HashSet<Guid>();
        foreach (var id in await IdsAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                if (await LoadAsync(id, cancellationToken).ConfigureAwait(false) is not { } workflow)
                {
                    return null;
                }
                owners.UnionWith(SecretOwnersOf(workflow));
            }
            catch (Exception exception) when (FileProblem.Is(exception))
            {
                logger.LogWarning(exception, "Could not read the workflow {Id}, so no secrets are forgotten", id);
                return null;
            }
        }
        return owners;
    }

    public string ScriptPathOf(Guid id, string script) =>
        IsValidScriptName(script) ? Path.Combine(FolderOf(id), script) : throw new ArgumentException($"'{script}' is not a valid script name", nameof(script));

    Task<List<Guid>> IdsAsync(CancellationToken cancellationToken) => Task.Run(() => Directory.Exists(folder.Workflows)
        ? Directory.EnumerateDirectories(folder.Workflows).Where(path => File.Exists(Path.Combine(path, _workflowFile))).Select(IdOf).OfType<Guid>().ToList()
        : [], cancellationToken);

    Guid? IdOf(string path)
    {
        if (RequestLibrary.TryIdOf(Path.GetFileName(path), out var id))
        {
            return id;
        }
        logger.LogWarning("{Path} is left out, because its name is not an id", path);
        return null;
    }

    JsonFile<Workflow?> FileOf(Guid id) => new(Path.Combine(FolderOf(id), _workflowFile), null, logger);

    string FolderOf(Guid id) => Path.Combine(folder.Workflows, $"{id}");
}
