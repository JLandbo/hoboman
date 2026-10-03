using System.Collections.ObjectModel;
using Hoboman.Core.Storage;
using Hoboman.Core.Workflows;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class WorkflowsViewModel(WorkflowLibrary library, ILogger<WorkflowsViewModel> logger)
{
    public ObservableCollection<string> Names { get; } = [];

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var names = await library.NamesAsync(cancellationToken);
            Names.Clear();
            foreach (var name in names.Order(StringComparer.CurrentCultureIgnoreCase))
            {
                Names.Add(name);
            }
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not list the workflows");
        }
    }

    // Windows does not tell upper and lower case apart in folder names.
    public bool Contains(string name) => Names.Contains(name, StringComparer.OrdinalIgnoreCase);
}
