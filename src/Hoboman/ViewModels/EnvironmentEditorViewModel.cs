using System.Collections.ObjectModel;
using System.Text.Json;
using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.Languages;
using Hoboman.Core.Storage;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class EnvironmentEditorViewModel(EnvironmentStore store, EnvironmentsViewModel environments, SecretStore secrets, Translator translator, ILogger<EnvironmentEditorViewModel> logger) : ObservableObject
{
    string _loadedJson = "";
    IReadOnlyList<string> _loadedNames = [];

    public ObservableCollection<EnvironmentDraftViewModel> Environments { get; } = [];

    // The renames and removals of the last save, so the open tabs can move their tokens too.
    public IReadOnlyDictionary<string, string?> Changes { get; private set; } = new Dictionary<string, string?>();

    public EnvironmentDraftViewModel? Selected { get; set => Set(ref field, value); }

    public string? Problem { get; private set => Set(ref field, value); }

    // Saving what could not be read would replace the file with an empty list.
    public bool CanSave { get; private set => Set(ref field, value); }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        Environments.Clear();
        Problem = null;
        CanSave = false;
        Changes = new Dictionary<string, string?>();
        try
        {
            var loaded = await store.AllAsync(cancellationToken);
            _loadedJson = JsonSerializer.Serialize(loaded);
            _loadedNames = [.. loaded.Select(environment => environment.Name).Distinct()];
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
            Problem = translator.Format("Environments.LoadFailed", translator.DetailsOf(exception));
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
            Changes = ChangesOf();
            try
            {
                await secrets.FollowEnvironmentsAsync(Changes, CancellationToken.None);
            }
            catch (Exception exception) when (FileProblem.Is(exception))
            {
                // The environments are saved, and a token left behind can be fetched again.
                logger.LogError(exception, "Could not move the tokens of renamed or removed environments");
            }
            foreach (var draft in Environments.Where(draft => draft.OriginalName is not null && draft.OriginalName != draft.Name.Trim()))
            {
                await environments.RenamedAsync(draft.OriginalName!, draft.Name.Trim());
            }
            await environments.LoadAsync(CancellationToken.None);
            return true;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not save the environments");
            Problem = translator.Format("Environments.SaveFailed", translator.DetailsOf(exception));
            return false;
        }
    }

    IReadOnlyDictionary<string, string?> ChangesOf()
    {
        var now = new Dictionary<string, string>();
        foreach (var draft in Environments.Where(draft => draft.OriginalName is not null))
        {
            now[draft.OriginalName!] = draft.Name.Trim();
        }
        return _loadedNames.Where(name => now.GetValueOrDefault(name) != name).ToDictionary(name => name, name => now.GetValueOrDefault(name));
    }
}
