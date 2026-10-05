namespace Hoboman.Tests.Workflows;

// Tests know a workflow by its name, as it is short to read. The app knows it by its id.
static class WorkflowNames
{
    public static async Task<Workflow> SaveAsync(this WorkflowLibrary library, string name, Workflow workflow, CancellationToken cancellationToken = default)
    {
        var saved = workflow with { Id = workflow.Id == Guid.Empty ? Guid.NewGuid() : workflow.Id, Name = name };
        await library.SaveAsync(saved, cancellationToken);
        return saved;
    }

    public static async Task<Guid> IdOfAsync(this WorkflowLibrary library, string name, CancellationToken cancellationToken = default) =>
        (await library.ListAsync(cancellationToken)).Single(workflow => workflow.Name == name).Id;

    public static async Task<Workflow?> LoadAsync(this WorkflowLibrary library, string name, CancellationToken cancellationToken = default) =>
        (await library.ListAsync(cancellationToken)).SingleOrDefault(workflow => workflow.Name == name) is { Id: var id } && id != Guid.Empty ? await library.LoadAsync(id, cancellationToken) : null;

    // The folder the workflow's scripts lie in.
    public static async Task<string> FolderAsync(this WorkflowLibrary library, string name, CancellationToken cancellationToken = default) =>
        Path.GetDirectoryName(library.ScriptPathOf(await library.IdOfAsync(name, cancellationToken), "any.js"))!;

    public static async Task RenameAsync(this WorkflowLibrary library, string name, string newName, CancellationToken cancellationToken = default) =>
        await library.RenameAsync(await library.IdOfAsync(name, cancellationToken), newName, cancellationToken);

    public static async Task OpenWorkflowAsync(this MainViewModel main, string name)
    {
        if (!main.Workflows.Items.Any(item => item.Name == name))
        {
            await main.Workflows.LoadAsync(CancellationToken.None);
        }
        await main.OpenWorkflowAsync(main.Workflows.Items.Single(item => item.Name == name).Id);
    }

    public static Task RenameWorkflowAsync(this MainViewModel main, string name) => main.RenameWorkflowAsync(main.Workflows.Items.Single(item => item.Name == name));

    public static Task DeleteWorkflowAsync(this MainViewModel main, string name) => main.DeleteWorkflowAsync(main.Workflows.Items.Single(item => item.Name == name));

    public static IReadOnlyList<string> Names(this WorkflowsViewModel workflows) => [.. workflows.Items.Select(item => item.Name)];
}
