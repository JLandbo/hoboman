using Hoboman.Core.Requests;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Workflows;

// One folder per workflow, so files that belong to it can lie next to its workflow.json and move along with it.
public sealed class WorkflowLibrary(AppFolder folder, ILogger<WorkflowLibrary> logger)
{
    const string _workflowFile = "workflow.json";

    public static bool IsValidName(string name) => RequestLibrary.IsValidName(name) && !name.Contains('/');

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
