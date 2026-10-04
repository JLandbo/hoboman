using Hoboman.Core.Auth;
using Hoboman.Core.Storage;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

// The saved credentials offered where an auth is filled in. They are read when the app starts and again when they are saved.
public sealed class CredentialsViewModel : ObservableObject
{
    readonly CredentialStore _store;
    readonly SecretStore _secrets;
    readonly EnvironmentsViewModel _environments;
    readonly CredentialEditorViewModel _editor;
    readonly IDialogs _dialogs;
    readonly ILogger<CredentialsViewModel> _logger;

    public CredentialsViewModel(CredentialStore store, SecretStore secrets, EnvironmentsViewModel environments, CredentialEditorViewModel editor, IDialogs dialogs, ILogger<CredentialsViewModel> logger)
    {
        (_store, _secrets, _environments, _editor, _dialogs, _logger) = (store, secrets, environments, editor, dialogs, logger);
        editor.Saved += () => LoadAsync(CancellationToken.None);
    }

    public IReadOnlyList<CredentialChoice> All { get; private set; } = [];

    public IReadOnlyList<CredentialChoice> OfChosenEnvironment => _environments.Selected is { } chosen
        ? [.. All.Where(choice => choice.Credential.EnvironmentId == chosen.Id).OrderBy(choice => choice.Credential.Name, StringComparer.CurrentCultureIgnoreCase)]
        : [];

    public bool IsEnvironmentChosen => _environments.Selected is not null;

    public string? EnvironmentNameOf(Guid id) => _environments.Items.FirstOrDefault(environment => environment.Id == id)?.Name;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var choices = new List<CredentialChoice>();
            foreach (var credential in await _store.AllAsync(cancellationToken))
            {
                choices.Add(new(credential, await SecretAsync(credential, SecretKind.Password), await SecretAsync(credential, SecretKind.Token), await SecretAsync(credential, SecretKind.ClientSecret)));
            }
            All = choices;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            _logger.LogError(exception, "Could not load the credentials");
        }
        Changed();

        async Task<string> SecretAsync(Credential credential, SecretKind kind) => await _secrets.OfAsync(credential.Id, kind, cancellationToken) ?? "";
    }

    public void EnvironmentChosen() => Changed();

    // The window can stay open beside the main window, so an open one is brought forward with what is typed in it.
    public async Task EditAsync()
    {
        if (_dialogs.ShowOpenCredentials())
        {
            return;
        }
        await _editor.LoadAsync(CancellationToken.None);
        _dialogs.ShowCredentials(_editor);
    }

    void Changed()
    {
        OnPropertyChanged(nameof(All));
        OnPropertyChanged(nameof(OfChosenEnvironment));
        OnPropertyChanged(nameof(IsEnvironmentChosen));
    }
}
