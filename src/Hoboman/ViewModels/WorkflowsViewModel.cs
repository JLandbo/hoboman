using System.Collections.ObjectModel;
using Hoboman.Core.Languages;
using Hoboman.Core.Storage;
using Hoboman.Core.Workflows;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class WorkflowsViewModel(WorkflowLibrary library, Translator translator, ILogger<WorkflowsViewModel> logger)
{
    public ObservableCollection<WorkflowItem> Items { get; } = [];

    // A workflow that cannot be read is shown by its id, and opening it tells what is wrong.
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var workflows = await library.ListAsync(cancellationToken);
            Items.Clear();
            foreach (var (id, name) in workflows.OrderBy(workflow => workflow.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                Items.Add(new(id, name ?? translator.Format("Tree.Unreadable", $"{id}"[..8])));
            }
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not list the workflows");
        }
    }
}
