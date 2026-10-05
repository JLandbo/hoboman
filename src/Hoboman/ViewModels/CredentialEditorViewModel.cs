using System.Collections.ObjectModel;
using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.Languages;
using Hoboman.Core.Storage;
using Hoboman.Mvvm;
using Hoboman.Services;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

// The window where credentials are saved: each environment's, as many as are needed, each a whole auth.
public sealed class CredentialEditorViewModel(CredentialStore store, SecretStore secrets, AuthRefreshService refreshes, EnvironmentsViewModel environments, IClipboard clipboard, Translator translator,
    TimeProvider clock, ILogger<CredentialEditorViewModel> logger) : ObservableObject
{
    public ObservableCollection<CredentialGroupViewModel> Environments { get; } = [];

    public CredentialGroupViewModel? SelectedEnvironment
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                Find();
            }
        }
    }

    public string Search
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                Find();
            }
        }
    } = "";

    // The selected environment's credentials that match the search. One just added stays until the search changes, even when it does not match.
    public ObservableCollection<CredentialDraftViewModel> Shown { get; } = [];

    public CredentialDraftViewModel? Selected { get; set => Set(ref field, value); }

    public IClipboard Clipboard => clipboard;

    public string? Problem { get; private set => Set(ref field, value); }

    // Saving what could not be read would delete the credentials.
    public bool CanSave { get; private set => Set(ref field, value); }

    public event Func<Task>? Saved;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        Environments.Clear();
        Problem = null;
        CanSave = false;
        Search = "";
        try
        {
            var credentials = await store.AllAsync(cancellationToken);
            foreach (var environment in environments.Items)
            {
                var drafts = new List<CredentialDraftViewModel>();
                foreach (var credential in credentials.Where(credential => credential.EnvironmentId == environment.Id).OrderBy(credential => credential.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    var draft = Draft(credential.Id, environment, credential.Name, credential.Auth);
                    await draft.Auth.LoadSecretsAsync(credential.Id, cancellationToken);
                    drafts.Add(draft);
                }
                Environments.Add(new(environment, drafts));
            }
            CanSave = true;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not load the credentials for editing");
            Problem = translator.Format("Credentials.LoadFailed", translator.DetailsOf(exception));
        }
        SelectedEnvironment = Environments.FirstOrDefault(group => group.Environment.Id == environments.Selected?.Id) ?? Environments.FirstOrDefault();
        Find();
    }

    // A new credential is an OAuth client, as most are.
    public void Add()
    {
        if (SelectedEnvironment is not { } group)
        {
            return;
        }
        var draft = Draft(Guid.NewGuid(), group.Environment, translator.Of("Credentials.NewName"), new(AuthKind.OAuth2, OAuth: new()));
        group.Drafts.Add(draft);
        Shown.Add(draft);
        Selected = draft;
    }

    public void Remove()
    {
        if (SelectedEnvironment is not { } group || Selected is not { } selected)
        {
            return;
        }
        var index = Shown.IndexOf(selected);
        group.Drafts.Remove(selected);
        Shown.Remove(selected);
        Selected = Shown.ElementAtOrDefault(Math.Min(index, Shown.Count - 1));
    }

    // The window stays open, so the credentials can be tried in the main window while it is.
    public async Task<bool> SaveAsync()
    {
        if (!CanSave)
        {
            return false;
        }
        if (Environments.Any(group => group.Drafts.Select(draft => draft.Name.Trim()).ToList() is var names
            && (names.Any(name => name.Length == 0) || names.Distinct(StringComparer.CurrentCultureIgnoreCase).Count() != names.Count)))
        {
            Problem = translator.Of("Credentials.Invalid");
            return false;
        }
        try
        {
            var drafts = Environments.SelectMany(group => group.Drafts).ToList();
            foreach (var draft in drafts)
            {
                await draft.Auth.SaveSecretsAsync(draft.Id, CancellationToken.None);
            }
            await store.SaveAsync([.. drafts.Select(draft => new Credential(draft.Id, draft.EnvironmentId, draft.Name.Trim(), draft.Auth.ToSettings()))], CancellationToken.None);
            Problem = null;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not save the credentials");
            Problem = translator.Format("Credentials.SaveFailed", translator.DetailsOf(exception));
            return false;
        }
        if (Saved is { } saved)
        {
            await saved();
        }
        return true;
    }

    // A credential's token is fetched in its own environment to try it, and is never saved.
    CredentialDraftViewModel Draft(Guid id, ApiEnvironment environment, string name, AuthSettings settings)
    {
        var auth = new AuthViewModel(secrets, refreshes, environments, null, translator, clock, logger) { OwnEnvironment = environment, KeepsTokens = false };
        auth.Load(settings);
        return new(id, environment.Id, name, auth);
    }

    void Find()
    {
        Shown.Clear();
        foreach (var draft in SelectedEnvironment?.Drafts ?? [])
        {
            if (draft.Name.Contains(Search, StringComparison.CurrentCultureIgnoreCase) || draft.Auth.ClientId.Contains(Search, StringComparison.OrdinalIgnoreCase)
                || draft.Auth.UserName.Contains(Search, StringComparison.CurrentCultureIgnoreCase))
            {
                Shown.Add(draft);
            }
        }
        if (Selected is null || !Shown.Contains(Selected))
        {
            Selected = Shown.FirstOrDefault();
        }
    }
}
