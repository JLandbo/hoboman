using System.Collections.ObjectModel;
using System.Text.Json;
using Hoboman.Core.Environments;
using Hoboman.Core.Languages;
using Hoboman.Core.Storage;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class EnvironmentEditorViewModel(EnvironmentStore store, EnvironmentsViewModel environments, Translator translator, ILogger<EnvironmentEditorViewModel> logger) : ObservableObject
{
    string _loadedJson = "";

    public ObservableCollection<EnvironmentDraftViewModel> Environments { get; } = [];

    public EnvironmentDraftViewModel? Selected { get; set => Set(ref field, value); }

    public string? Problem { get; private set => Set(ref field, value); }

    // Saving what could not be read would replace the file with an empty list.
    public bool CanSave { get; private set => Set(ref field, value); }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        Environments.Clear();
        Problem = null;
        CanSave = false;
        try
        {
            var loaded = await store.AllAsync(cancellationToken);
            _loadedJson = JsonSerializer.Serialize(loaded);
            foreach (var environment in loaded)
            {
                Environments.Add(new(environment));
            }
            Selected = Environments.FirstOrDefault();
            CanSave = true;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not load the environments for editing");
            Problem = translator.Format("Environments.LoadFailed", exception.Message);
        }
    }

    public void Add()
    {
        var draft = new EnvironmentDraftViewModel(new(translator.Of("Environments.NewName"), []), isNew: true);
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
        if (!CanSave)
        {
            return false;
        }
        var names = Environments.Select(environment => environment.Name.Trim()).ToList();
        if (names.Any(name => name.Length == 0) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
        {
            Problem = translator.Of("Environments.Invalid");
            return false;
        }
        try
        {
            // The file wins: if an agent changed it while the window was open, its change is not overwritten.
            if (JsonSerializer.Serialize(await store.AllAsync(CancellationToken.None)) != _loadedJson)
            {
                Problem = translator.Of("Environments.ChangedOnDisk");
                return false;
            }
            await store.SaveAsync([.. Environments.Select(environment => environment.ToEnvironment())], CancellationToken.None);
            logger.LogInformation("Saved {Count} environments", names.Count);
            foreach (var draft in Environments.Where(draft => draft.OriginalName is not null && draft.OriginalName != draft.Name.Trim()))
            {
                environments.Renamed(draft.OriginalName!, draft.Name.Trim());
            }
            await environments.LoadAsync(CancellationToken.None);
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
