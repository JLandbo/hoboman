using Hoboman.Core.Requests;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Workflows;

// One folder per workflow, so files that belong to it can lie next to its workflow.json and move along with it.
public sealed class WorkflowLibrary(AppFolder folder, ILogger<WorkflowLibrary> logger)
{
    const string _workflowFile = "workflow.json";

    public static bool IsValidName(string name) => RequestLibrary.IsValidName(name) && !name.Contains('/');

    // A script lies right in the workflow's folder, so its name cannot lead out of it.
    public static bool IsValidScriptName(string name) => IsValidName(name) && name.EndsWith(".js", StringComparison.OrdinalIgnoreCase);

    // A folder without a workflow.json is not a workflow.
    public Task<IReadOnlyList<string>> NamesAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<string>>(() => Directory.Exists(folder.Workflows)
        ? [.. Directory.EnumerateDirectories(folder.Workflows).Where(path => File.Exists(Path.Combine(path, _workflowFile))).Select(Path.GetFileName).OfType<string>().Where(IsUsable)]
        : [], cancellationToken);

    // These are async, so an invalid name fails the returned task instead of throwing before there is one.
    public async Task<Workflow?> LoadAsync(string name, CancellationToken cancellationToken) => await FileOf(name).LoadAsync(cancellationToken).ConfigureAwait(false);

    // A workflow that is already there is saved without creating its folder, so one renamed in Explorer does not come back under its old name.
    public async Task SaveAsync(string name, Workflow workflow, CancellationToken cancellationToken, bool createDirectory = true)
    {
        await FileOf(name).SaveAsync(workflow, cancellationToken, createDirectory).ConfigureAwait(false);
        logger.LogInformation("Saved the workflow {Name}", name);
    }

    public async Task<Workflow> CreateAsync(string name, CancellationToken cancellationToken)
    {
        var workflow = new Workflow { Id = Guid.NewGuid() };
        await FileOf(name).SaveAsync(workflow, cancellationToken, overwrite: false).ConfigureAwait(false);
        logger.LogInformation("Created the workflow {Name}", name);
        return workflow;
    }

    public Task RenameAsync(string name, string newName, CancellationToken cancellationToken) => Retrying.RunAsync(() =>
    {
        Directory.Move(FolderOf(name), FolderOf(newName));
        logger.LogInformation("Renamed the workflow {Name} to {NewName}", name, newName);
    }, logger, name, cancellationToken);

    public Task DeleteAsync(string name, CancellationToken cancellationToken) => Retrying.RunAsync(() =>
    {
        Directory.Delete(FolderOf(name), recursive: true);
        logger.LogInformation("Deleted the workflow {Name}", name);
    }, logger, name, cancellationToken);

    // A script that is not there, cannot be read or has a name that leads out of the folder gives null, so the check tells of it.
    public async Task<string?> LoadScriptAsync(string name, string script, CancellationToken cancellationToken)
    {
        if (!IsValidScriptName(script))
        {
            logger.LogWarning("{Script} in the workflow {Name} is not a valid script name", script, name);
            return null;
        }
        try
        {
            return await File.ReadAllTextAsync(ScriptPathOf(name, script), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogWarning(exception, "Could not read the script {Script} of the workflow {Name}", script, name);
            return null;
        }
    }

    // A file that is already there is left as it is, so its code is never overwritten.
    public async Task CreateScriptAsync(string name, string script, string code, CancellationToken cancellationToken)
    {
        var path = ScriptPathOf(name, script);
        if (File.Exists(path))
        {
            return;
        }
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await using var writer = new StreamWriter(file);
        await writer.WriteAsync(code.AsMemory(), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Created the script {Script} of the workflow {Name}", script, name);
    }

    // Like a workflow that is already there, a script is not saved into a folder that was renamed or deleted on disk.
    public async Task SaveScriptAsync(string name, string script, string code, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(ScriptPathOf(name, script), code, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Saved the script {Script} of the workflow {Name}", script, name);
    }

    // A request step's secrets are saved under its id.
    public static IEnumerable<Guid> SecretOwnersOf(Workflow workflow) => workflow.Steps.Select(step => step.Request?.Id ?? Guid.Empty).Where(id => id != Guid.Empty);

    // A workflow that cannot be read may use any id, so then there is no answer.
    public async Task<IReadOnlySet<Guid>?> SecretOwnersAsync(CancellationToken cancellationToken)
    {
        var owners = new HashSet<Guid>();
        foreach (var name in await NamesAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                if (await LoadAsync(name, cancellationToken).ConfigureAwait(false) is not { } workflow)
                {
                    return null;
                }
                owners.UnionWith(SecretOwnersOf(workflow));
            }
            catch (Exception exception) when (FileProblem.Is(exception))
            {
                logger.LogWarning(exception, "Could not read the workflow {Name}, so no secrets are forgotten", name);
                return null;
            }
        }
        return owners;
    }

    public string ScriptPathOf(string name, string script) =>
        IsValidScriptName(script) ? Path.Combine(FolderOf(name), script) : throw new ArgumentException($"'{script}' is not a valid script name", nameof(script));

    bool IsUsable(string name)
    {
        if (IsValidName(name))
        {
            return true;
        }
        logger.LogWarning("{Name} is left out, because it cannot be used as a workflow name", name);
        return false;
    }

    JsonFile<Workflow?> FileOf(string name) => new(Path.Combine(FolderOf(name), _workflowFile), null, logger);

    string FolderOf(string name) => IsValidName(name) ? Path.Combine(folder.Workflows, name) : throw new ArgumentException($"'{name}' is not a valid workflow name", nameof(name));
}
