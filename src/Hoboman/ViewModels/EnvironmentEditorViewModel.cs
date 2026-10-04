using System.Collections.ObjectModel;
using System.Text.Json;
using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.Languages;
using Hoboman.Core.Storage;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class EnvironmentEditorViewModel(EnvironmentStore store, EnvironmentsViewModel environments, SecretStore secrets, CredentialStore credentials, Translator translator, ILogger<EnvironmentEditorViewModel> logger) : ObservableObject
{
    string _loadedJson = "";
    IReadOnlySet<Guid> _loadedIds = new HashSet<Guid>();

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
            _loadedIds = loaded.Select(environment => environment.Id).ToHashSet();
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
        var draft = new EnvironmentDraftViewModel(new(translator.Of("Environments.NewName"), []) { Id = Guid.NewGuid() });
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
            try
            {
                var removed = _loadedIds.Except(Environments.Select(environment => environment.Id)).ToHashSet();
                await secrets.ForgetEnvironmentsAsync(removed, CancellationToken.None);
                await credentials.ForgetEnvironmentsAsync(removed, CancellationToken.None);
            }
            catch (Exception exception) when (FileProblem.Is(exception))
            {
                // The environments are saved, and the id of a removed one is never used again.
                logger.LogError(exception, "Could not delete the tokens and credentials of removed environments");
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
}
