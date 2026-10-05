using Hoboman.Core.Environments;
using Hoboman.Core.Requests;
using Hoboman.Core.Storage;
using Hoboman.Core.Workflows;

namespace Hoboman.Cli;

sealed record Saved(SavedKind Kind, Guid Id, string Name, string Path);

// Something saved is known by its id, or by its path or name when only one thing has it.
sealed class Targets(WorkflowLibrary workflows, EnvironmentStore environments)
{
    // An environment that has a name like something else's makes the name ambiguous, so a change never lands on the other one.
    // A broken environments file stops only a target that nothing else has, as it could be an environment.
    public async Task<(Saved? Found, string? Problem)> FindAsync(string target, RequestCollection collection, bool withEnvironments, CancellationToken cancellationToken)
    {
        var saved = collection.Requests.Select(request => new Saved(SavedKind.Request, request.Id, request.Name, collection.PathOf(request)))
            .Concat(collection.Unreadable.Select(id => new Saved(SavedKind.Request, id, "", "")))
            .Concat(collection.Folders.Select(folder => new Saved(SavedKind.Folder, folder.Id, folder.Name, collection.FolderPathOf(folder.Id))))
            .Concat((await workflows.ListAsync(cancellationToken)).Select(workflow => new Saved(SavedKind.Workflow, workflow.Id, workflow.Name ?? "", workflow.Name ?? "")));
        var found = Matching(target, saved, item => item.Id, item => item.Path);
        if (withEnvironments)
        {
            try
            {
                found.AddRange(Matching(target, (await environments.AllAsync(cancellationToken)).Select(environment => new Saved(SavedKind.Environment, environment.Id, environment.Name, environment.Name)),
                    item => item.Id, item => item.Path));
            }
            catch (Exception exception) when (FileProblem.Is(exception) && found.Count > 0)
            {
            }
        }
        return found.Count switch { 0 => (null, "Target could not be found."), 1 => (found[0], null), _ => (null, "Target is ambiguous. Use its id.") };
    }

    // A workflow found by its id is not looked up among the others, so its runs can be read even when its file cannot.
    public async Task<IReadOnlyList<Guid>> WorkflowsAsync(string target, CancellationToken cancellationToken) =>
        RequestLibrary.TryIdOf(target, out var id) ? [id]
            : Matching(target, await workflows.ListAsync(cancellationToken), named => named.Id, named => named.Name ?? "").Select(named => named.Id).ToList();

    // A path or name can be shared, so all that have it are given, and the caller tells when there is more than one.
    public static List<T> Matching<T>(string target, IEnumerable<T> items, Func<T, Guid> idOf, Func<T, string> pathOf) =>
        RequestLibrary.TryIdOf(target, out var id) ? [.. items.Where(item => idOf(item) == id)] : [.. items.Where(item => pathOf(item).Equals(target, StringComparison.OrdinalIgnoreCase))];
}
