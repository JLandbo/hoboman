using System.Collections.ObjectModel;
using Hoboman.Core.Environments;
using Hoboman.Core.Settings;
using Hoboman.Core.Storage;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class EnvironmentsViewModel(EnvironmentStore store, SettingsStore settings, ILogger<EnvironmentsViewModel> logger) : ObservableObject
{
    public ObservableCollection<ApiEnvironment> Items { get; } = [];

    public ApiEnvironment? Selected { get; private set => Set(ref field, value); }

    public ApiEnvironment SelectedOrNone => Selected ?? ApiEnvironment.None;

    public async Task ChooseAsync(ApiEnvironment? environment)
    {
        Selected = environment;
        logger.LogInformation("Environment changed to {Environment}", environment?.Name);
        try
        {
            await settings.UpdateAsync(saved => saved with { EnvironmentId = environment?.Id }, CancellationToken.None);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not remember the chosen environment");
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var environments = await store.AllAsync(cancellationToken);
            var id = Selected?.Id;
            var saved = id is null ? await SavedAsync(cancellationToken) : null;
            Items.Clear();
            foreach (var environment in environments)
            {
                Items.Add(environment);
            }
            Selected = saved is null ? Items.FirstOrDefault(environment => environment.Id == id) : saved.EnvironmentIn(Items);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not load the environments");
        }
    }

    // Unreadable settings only lose the choice, not the environments.
    async Task<AppSettings> SavedAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await settings.LoadAsync(cancellationToken);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not read the chosen environment");
            return AppSettings.Default;
        }
    }
}
