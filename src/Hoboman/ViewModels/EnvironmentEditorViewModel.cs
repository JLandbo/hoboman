using System.Collections.ObjectModel;
using Hoboman.Core.Environments;
using Hoboman.Core.Languages;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class EnvironmentEditorViewModel(EnvironmentStore store, Translator translator, ILogger<EnvironmentEditorViewModel> logger) : ObservableObject
{
    public ObservableCollection<EnvironmentDraftViewModel> Environments { get; } = [];

    public EnvironmentDraftViewModel? Selected { get; set => Set(ref field, value); }

    public string? Problem { get; private set => Set(ref field, value); }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var environment in await store.AllAsync(cancellationToken))
            {
                Environments.Add(new(environment));
            }
            Selected = Environments.FirstOrDefault();
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not load the environments for editing");
            Problem = translator.Format("Environments.LoadFailed", exception.Message);
        }
    }

    public void Add()
    {
        var draft = new EnvironmentDraftViewModel(new(translator.Of("Environments.NewName"), []));
        Environments.Add(draft);
        Selected = draft;
    }

    public void Remove()
    {
        if (Selected is not { } selected)
        {
            return;
        }
        var index = Environments.IndexOf(selected);
        Environments.Remove(selected);
        Selected = Environments.ElementAtOrDefault(Math.Min(index, Environments.Count - 1));
    }

    public async Task<bool> SaveAsync()
    {
        var names = Environments.Select(environment => environment.Name.Trim()).ToList();
        if (names.Any(name => name.Length == 0) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
        {
            Problem = translator.Of("Environments.Invalid");
            return false;
        }
        try
        {
            await store.SaveAsync([.. Environments.Select(environment => environment.ToEnvironment())], CancellationToken.None);
            logger.LogInformation("Saved {Count} environments", names.Count);
            return true;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not save the environments");
            Problem = translator.Format("Environments.SaveFailed", exception.Message);
            return false;
        }
    }
}

public sealed class EnvironmentDraftViewModel : ObservableObject
{
    public EnvironmentDraftViewModel(ApiEnvironment environment)
    {
        Name = environment.Name;
        Variables.Load(environment.Variables);
    }

    public string Name { get; set => Set(ref field, value); }

    public KeyValueListViewModel Variables { get; } = new();

    public ApiEnvironment ToEnvironment() => new(Name.Trim(), Variables.ToList());
}
