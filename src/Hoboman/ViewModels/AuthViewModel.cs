using System.Runtime.CompilerServices;
using Hoboman.Core.Auth;
using Hoboman.Mvvm;

namespace Hoboman.ViewModels;

// The auth of a request or a folder. Both use the same editor, and the secrets are saved under the id of whichever it belongs to.
public sealed class AuthViewModel(SecretStore secrets) : ObservableObject
{
    OAuthSettings? _oauth;
    string _savedPassword = "";
    string _savedToken = "";
    bool _loading;

    public event Action? Changed;

    public AuthKind Kind { get; set => Change(ref field, value); }

    public string UserName { get; set => Change(ref field, value); } = "";

    public string Password { get; set => Change(ref field, value); } = "";

    public string Token { get; set => Change(ref field, value); } = "";

    public bool HasUnsavedSecrets => Password != _savedPassword || Token != _savedToken;

    public void Load(AuthSettings settings)
    {
        _loading = true;
        Kind = settings.Kind;
        UserName = settings.UserName;
        _oauth = settings.OAuth;
        _loading = false;
    }

    public AuthSettings ToSettings() => new(Kind, UserName, _oauth);

    // Secrets that cannot be read are shown as empty, and the caller tells why.
    public async Task LoadSecretsAsync(Guid id, CancellationToken cancellationToken)
    {
        string? password = null;
        string? token = null;
        try
        {
            if (id != Guid.Empty)
            {
                password = await secrets.OfAsync(id, SecretKind.Password, cancellationToken);
                token = await secrets.OfAsync(id, SecretKind.Token, cancellationToken);
            }
        }
        finally
        {
            _loading = true;
            Password = _savedPassword = password ?? "";
            Token = _savedToken = token ?? "";
            _loading = false;
        }
    }

    // Each secret is saved on its own and only marked as saved afterwards, so a value typed while saving is saved next time.
    public async Task SaveSecretsAsync(Guid id, CancellationToken cancellationToken)
    {
        var (password, token) = (Password, Token);
        if (password != _savedPassword)
        {
            await secrets.SaveAsync(id, SecretKind.Password, password, cancellationToken);
            _savedPassword = password;
        }
        if (token != _savedToken)
        {
            await secrets.SaveAsync(id, SecretKind.Token, token, cancellationToken);
            _savedToken = token;
        }
    }

    // Once the secrets are deleted or the id is new, a later save writes the ones shown again.
    public void ForgetSavedSecrets() => _savedPassword = _savedToken = "";

    void Change<T>(ref T storage, T value, [CallerMemberName] string? name = null)
    {
        if (Set(ref storage, value, name) && !_loading)
        {
            Changed?.Invoke();
        }
    }
}
