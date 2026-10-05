using System.Text.Json;
using Hoboman.Core.Environments;
using Hoboman.Core.Requests;
using Hoboman.Core.Storage;
using Hoboman.Core.Workflows;

namespace Hoboman.Cli;

// Writes what is saved as update takes it, so a program reads and changes it without the files. No secret is in them.
sealed class ShowCommand(RequestLibrary library, WorkflowLibrary workflows, EnvironmentStore environments, Targets targets, CliOutput output)
{
    public async Task<int> RunAsync(string target, CancellationToken cancellationToken)
    {
        // Something that cannot be read, also the environments when they are looked through, is told with where it is wrong.
        try
        {
            var collection = await library.LoadAllAsync(cancellationToken);
            var (found, problem) = await targets.FindAsync(target, collection, withEnvironments: true, cancellationToken);
            if (found is null)
            {
                return await output.WriteErrorAsync(problem!);
            }
            return found.Kind switch
            {
                SavedKind.Request => await output.WriteResultAsync(new { request = await library.LoadAsync(found.Id, cancellationToken) ?? throw Gone() }),
                SavedKind.Folder => await output.WriteResultAsync(new { folder = collection.FolderOf(found.Id) }),
                SavedKind.Workflow => await WorkflowAsync(found.Id, cancellationToken),
                _ => await output.WriteResultAsync(new { environment = (await environments.AllAsync(cancellationToken)).FirstOrDefault(environment => environment.Id == found.Id) ?? throw Gone() }),
            };
        }
        catch (InvalidFileException exception) when (exception.InnerException is JsonException invalid)
        {
            return await output.WriteInvalidFileAsync("File is not valid.", exception, invalid);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            return await output.WriteErrorAsync("Target could not be read.");
        }
    }

    // Every script in the workflow's folder, also one no step uses yet.
    async Task<int> WorkflowAsync(Guid id, CancellationToken cancellationToken)
    {
        var workflow = await workflows.LoadAsync(id, cancellationToken) ?? throw Gone();
        var scripts = new Dictionary<string, string>();
        foreach (var script in await workflows.ScriptsAsync(id, cancellationToken))
        {
            scripts[script] = await workflows.LoadScriptAsync(id, script, cancellationToken) ?? throw Gone();
        }
        return await output.WriteResultAsync(new { workflow, scripts });
    }

    // Something deleted while it was looked up is as gone as a file that cannot be read.
    static FileNotFoundException Gone() => new("The target is gone.");
}
