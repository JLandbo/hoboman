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

    public async Task ChooseAsync(ApiEnvironment? environment)
    {
        Selected = environment;
        logger.LogInformation("Environment changed to {Environment}", environment?.Name);
        try
        {
            await settings.UpdateAsync(saved => saved with { EnvironmentName = environment?.Name }, CancellationToken.None);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not remember the chosen environment");
        }
    }

    public Task RenamedAsync(string name, string newName) => Selected?.Name == name ? ChooseAsync(Selected with { Name = newName }) : Task.CompletedTask;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var environments = await store.AllAsync(cancellationToken);
            var name = Selected?.Name ?? await ChosenNameAsync(cancellationToken);
            Items.Clear();
            foreach (var environment in environments)
            {
                Items.Add(environment);
            }
            Selected = Items.FirstOrDefault(environment => environment.Name == name);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not load the environments");
        }
    }

    // Unreadable settings only lose the choice, not the environments.
    async Task<string?> ChosenNameAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await settings.LoadAsync(cancellationToken)).EnvironmentName;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not read the chosen environment");
            return null;
        }
    }
}
