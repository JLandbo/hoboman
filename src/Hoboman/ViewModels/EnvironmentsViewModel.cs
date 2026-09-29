using System.Collections.ObjectModel;
using Hoboman.Core.Environments;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class EnvironmentsViewModel(EnvironmentStore store, SettingsViewModel settings, ILogger<EnvironmentsViewModel> logger) : ObservableObject
{
    public ObservableCollection<ApiEnvironment> All { get; } = [];

    public ApiEnvironment? Selected { get; private set => Set(ref field, value); }

    public void Choose(ApiEnvironment? environment)
    {
        Selected = environment;
        settings.EnvironmentName = environment?.Name;
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var environments = await store.AllAsync(cancellationToken);
            var name = Selected?.Name ?? settings.EnvironmentName;
            All.Clear();
            foreach (var environment in environments)
            {
                All.Add(environment);
            }
            Selected = All.FirstOrDefault(environment => environment.Name == name);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not load the environments");
        }
    }
}
