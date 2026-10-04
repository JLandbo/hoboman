using System.Runtime.CompilerServices;
using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.Languages;
using Hoboman.Mvvm;
using Hoboman.Services;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

// The auth of a request or a folder. Both use the same editor, and the secrets are saved under the id of whichever it belongs to.
public sealed class AuthViewModel(SecretStore secrets, AuthRefreshService refreshes, EnvironmentsViewModel environments, CredentialsViewModel? credentials, Translator translator, TimeProvider clock, ILogger logger) : ObservableObject
{
    Guid _secretsId;
    string? _folder;
    OAuthSettings? _oauth;
    string _savedPassword = "";
    string _savedToken = "";
    string _savedClientSecret = "";
    Dictionary<Guid, OAuthToken> _tokens = [];
    Dictionary<Guid, OAuthToken> _savedTokens = [];
    CancellationTokenSource? _fetching;
    Task _fetchEnded = Task.CompletedTask;
    bool _loading;

    public event Action? Changed;

    public AuthKind Kind { get; set => Change(ref field, value); }

    public string UserName { get; set => Change(ref field, value); } = "";

    public string Password { get; set => Change(ref field, value); } = "";

    public string Token { get; set => Change(ref field, value); } = "";

    public OAuthGrant Grant { get; set => Change(ref field, value); }

    public string AuthorizeUrl { get; set => Change(ref field, value); } = "";

    public string TokenUrl { get; set => Change(ref field, value); } = "";

    public string ClientId { get; set => Change(ref field, value); } = "";

    public string ClientSecret { get; set => Change(ref field, value); } = "";

    public string Scope { get; set => Change(ref field, value); } = "";

    public OAuthClientAuthentication ClientAuthentication { get; set => Change(ref field, value); }

    public int? RedirectPort { get; set => Change(ref field, value); }

    // Each environment has its own token, because one fetched with another environment's addresses and client would be the wrong one.
    public OAuthToken? AccessToken => _tokens.GetValueOrDefault(ChosenEnvironment);

    public string TokenStatus => AccessToken switch
    {
        null => translator.Of("OAuth.NoToken"),
        { ExpiresAt: { } expiresAt } token when token.HasExpired(clock.GetUtcNow()) => translator.Format("OAuth.Expired", expiresAt.ToLocalTime()),
        { ExpiresAt: { } expiresAt } => translator.Format("OAuth.ValidUntil", expiresAt.ToLocalTime()),
        _ => translator.Of("OAuth.NoExpiry"),
    };

    public bool IsFetching { get; private set => Set(ref field, value); }

    // Left out where a credential itself is edited.
    public CredentialsViewModel? Credentials => credentials;

    public IReadOnlyList<CredentialChoice> FoundCredentials { get; private set => Set(ref field, value); } = [];

    // The credential the auth was filled in from, while nothing in it has changed since. One of another environment says which.
    public string? CredentialName => Matching() is { } match
        ? match.Credential.EnvironmentId == ChosenEnvironment ? match.Credential.Name : $"{match.Credential.Name} · {credentials?.EnvironmentNameOf(match.Credential.EnvironmentId)}"
        : null;

    public bool IsCredentialOfOtherEnvironment => Matching()?.Credential.EnvironmentId is { } environment && environment != ChosenEnvironment;

    // A saved credential belongs to one environment, so its token is fetched there whatever environment is chosen.
    public ApiEnvironment? OwnEnvironment { get; init; }

    public bool KeepsTokens { get; init; } = true;

    public string? TokenProblem { get; private set => Set(ref field, value); }

    // An owner that keeps secrets fetches the token itself, so it is saved as when sending or running and is no edit.
    public Func<Task>? OwnerFetch { get; set; }

    public AsyncCommand FetchToken => field ??= new(() => OwnerFetch?.Invoke() ?? FetchTokenAsync());

    public void FindCredentials(string text) => FoundCredentials =
        [.. (credentials?.OfChosenEnvironment ?? []).Where(choice => choice.Credential.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase))];

    // A credential is filled in as if it was typed, so it can be changed after. The tokens of the client before it are dropped, and an OAuth token is fetched at once.
    public async Task UseAsync(CredentialChoice choice)
    {
        CancelFetch();
        await _fetchEnded;
        var settings = choice.Credential.Auth;
        var oauth = settings.OAuth ?? new();
        Kind = settings.Kind;
        UserName = settings.UserName;
        Grant = oauth.Grant;
        AuthorizeUrl = oauth.AuthorizeUrl;
        TokenUrl = oauth.TokenUrl;
        ClientId = oauth.ClientId;
        Scope = oauth.Scope;
        ClientAuthentication = oauth.ClientAuthentication;
        RedirectPort = oauth.RedirectPort;
        Password = choice.Password;
        Token = choice.Token;
        ClientSecret = choice.ClientSecret;
        _tokens.Clear();
        Relabel();
        if (Kind == AuthKind.OAuth2)
        {
            await FetchTokenAsync();
        }
    }

    public static string HeaderOf(AuthKind? kind, Translator translator) => $"{translator.Of("Editor.Auth")} ({kind switch
    {
        AuthKind.None => translator.Of("Auth.None"),
        AuthKind.Basic => translator.Of("Auth.Basic"),
        AuthKind.Bearer => translator.Of("Auth.BearerShort"),
        AuthKind.OAuth2 => "OAuth",
        _ => "…",
    }})";

    // What is filled in like the rest of a request, so its user name and secret can use names too.
    public IEnumerable<string> Texts => Kind switch
    {
        AuthKind.Basic => [UserName, Password],
        AuthKind.Bearer => [Token],
        _ => [],
    };

    public bool HasUnsavedSecrets =>
        Password != _savedPassword || Token != _savedToken || ClientSecret != _savedClientSecret || _tokens.Any(token => _savedTokens.GetValueOrDefault(token.Key) != token.Value)
        || _savedTokens.Keys.Any(environment => !_tokens.ContainsKey(environment));

    public void Load(AuthSettings settings)
    {
        _loading = true;
        // An error from fetching belongs to what was shown before.
        TokenProblem = null;
        Kind = settings.Kind;
        UserName = settings.UserName;
        _oauth = settings.OAuth;
        var loaded = settings.OAuth ?? new();
        Grant = loaded.Grant;
        AuthorizeUrl = loaded.AuthorizeUrl;
        TokenUrl = loaded.TokenUrl;
        ClientId = loaded.ClientId;
        Scope = loaded.Scope;
        ClientAuthentication = loaded.ClientAuthentication;
        RedirectPort = loaded.RedirectPort;
        _loading = false;
    }

    // Settings nobody changed are given back as they were loaded, so a file without OAuth does not look changed.
    public AuthSettings ToSettings()
    {
        var edited = EditedOAuth();
        return new(Kind, UserName, edited == (_oauth ?? new()) ? _oauth : edited);
    }

    // Secrets that cannot be read are shown as empty, and the caller tells why.
    public async Task LoadSecretsAsync(Guid id, CancellationToken cancellationToken)
    {
        UseOwner(id, _folder);
        string? password = null;
        string? token = null;
        string? clientSecret = null;
        IReadOnlyDictionary<Guid, string> tokens = new Dictionary<Guid, string>();
        try
        {
            if (id != Guid.Empty)
            {
                password = await secrets.OfAsync(id, SecretKind.Password, cancellationToken);
                token = await secrets.OfAsync(id, SecretKind.Token, cancellationToken);
                clientSecret = await secrets.OfAsync(id, SecretKind.ClientSecret, cancellationToken);
                tokens = await secrets.OfEachEnvironmentAsync(id, SecretKind.OAuthToken, cancellationToken);
            }
        }
        finally
        {
            _loading = true;
            Password = _savedPassword = password ?? "";
            Token = _savedToken = token ?? "";
            ClientSecret = _savedClientSecret = clientSecret ?? "";
            _tokens = tokens.Select(saved => (saved.Key, Value: OAuthToken.FromJson(saved.Value))).Where(saved => saved.Value is not null).ToDictionary(saved => saved.Key, saved => saved.Value!);
            _savedTokens = new(_tokens);
            _loading = false;
            Relabel();
        }
    }

    public Task SaveSecretsAsync(Guid id, CancellationToken cancellationToken) => refreshes.SaveAsync(() => SaveSecretsUnderLockAsync(id, cancellationToken), cancellationToken);

    public Task CopySecretsAsync(Guid targetId, CancellationToken cancellationToken)
    {
        var sourceId = _secretsId;
        var edits = new[] { (SecretKind.Password, Password, _savedPassword), (SecretKind.Token, Token, _savedToken), (SecretKind.ClientSecret, ClientSecret, _savedClientSecret) }.Where(secret => secret.Item2 != secret.Item3).ToArray();
        return refreshes.SaveAsync(async () =>
        {
            var tokens = _tokens.ToArray();
            await secrets.CopyAsync(sourceId, targetId, cancellationToken);
            foreach (var (kind, value, _) in edits)
            {
                await secrets.SaveAsync(targetId, kind, value, cancellationToken);
            }
            foreach (var (environment, token) in tokens)
            {
                await secrets.SaveAsync(targetId, SecretKind.OAuthToken, environment, token.ToJson(), cancellationToken);
            }
        }, cancellationToken);
    }

    // Each secret is saved on its own and only marked as saved afterwards, so a value typed while saving is saved next time.
    internal async Task SaveSecretsUnderLockAsync(Guid id, CancellationToken cancellationToken)
    {
        var (password, token, clientSecret) = (Password, Token, ClientSecret);
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
        if (clientSecret != _savedClientSecret)
        {
            await secrets.SaveAsync(id, SecretKind.ClientSecret, clientSecret, cancellationToken);
            _savedClientSecret = clientSecret;
        }
        foreach (var (environment, fetched) in KeepsTokens ? _tokens.ToArray() : [])
        {
            if (_savedTokens.GetValueOrDefault(environment) != fetched)
            {
                await secrets.SaveAsync(id, SecretKind.OAuthToken, environment, fetched.ToJson(), cancellationToken);
                _savedTokens[environment] = fetched;
            }
        }
        // Tokens dropped when another credential was picked belong to the client before it.
        foreach (var environment in _savedTokens.Keys.Where(environment => !_tokens.ContainsKey(environment)).ToList())
        {
            await secrets.DeleteAsync(id, SecretKind.OAuthToken, environment, cancellationToken);
            _savedTokens.Remove(environment);
        }
    }

    // Once the secrets are deleted or the id is new, a later save writes the ones shown again.
    public void ForgetSavedSecrets()
    {
        _savedPassword = _savedToken = _savedClientSecret = "";
        _savedTokens.Clear();
    }

    // The token is kept with the other secrets, so it is saved the same way and under the same id, when the request is sent or saved.
    // It belongs to the environment chosen when the fetch started, even if another is chosen while the login is open.
    public Task<bool> FetchTokenAsync() => FetchTokenAsync(OwnEnvironment ?? environments.SelectedOrNone, saveSecrets: false, CancellationToken.None);

    public async Task<bool> FetchTokenAsync(ApiEnvironment environment, bool saveSecrets, CancellationToken cancellationToken)
    {
        if (IsFetching)
        {
            return false;
        }
        TokenProblem = null;
        IsFetching = true;
        var ended = new TaskCompletionSource();
        _fetchEnded = ended.Task;
        using var fetching = _fetching = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var settings = ToSettings();
        var clientSecret = ClientSecret;
        var source = new AuthSource(_secretsId, settings with { OAuth = EditedOAuth() }, _folder);
        try
        {
            return await refreshes.FetchAsync(source, clientSecret, environment, async (token, environmentId, _) =>
            {
                if (_secretsId != source.SecretsId || _folder != source.Folder || ToSettings() != settings || ClientSecret != clientSecret || fetching.IsCancellationRequested)
                {
                    return false;
                }
                _tokens[environmentId] = token;
                Relabel();
                Changed?.Invoke();
                if (saveSecrets)
                {
                    await SaveSecretsUnderLockAsync(source.SecretsId, fetching.Token);
                }
                return true;
            }, fetching.Token);
        }
        catch (OperationCanceledException) when (fetching.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not get an OAuth token");
            SetTokenProblem(exception);
        }
        finally
        {
            _fetching = null;
            IsFetching = false;
            ended.SetResult();
        }
        return false;
    }

    public void CancelFetch() => _fetching?.Cancel();

    internal void SetTokenProblem(Exception? exception) => TokenProblem = exception is null || Kind is not (AuthKind.Inherit or AuthKind.OAuth2) ? null : translator.Format("OAuth.Failed", ReasonOf(exception));

    internal void UseOwner(Guid id, string? folder = null)
    {
        if (_secretsId != id || _folder != folder)
        {
            CancelFetch();
        }
        _secretsId = id;
        _folder = folder;
    }

    // The texts follow the language, and the token follows the chosen environment.
    public void Relabel()
    {
        OnPropertyChanged(nameof(AccessToken));
        OnPropertyChanged(nameof(TokenStatus));
        RelabelCredential();
    }

    public void RelabelCredential()
    {
        OnPropertyChanged(nameof(CredentialName));
        OnPropertyChanged(nameof(IsCredentialOfOtherEnvironment));
    }

    Guid ChosenEnvironment => (OwnEnvironment ?? environments.SelectedOrNone).Id;

    // The chosen environment's credential first, when another environment has the same.
    CredentialChoice? Matching() => credentials?.All.Where(Matches).OrderBy(choice => choice.Credential.EnvironmentId != ChosenEnvironment).FirstOrDefault();

    bool Matches(CredentialChoice choice) => choice.Credential.Auth is var settings && settings.Kind == Kind && Kind switch
    {
        AuthKind.Basic => UserName == settings.UserName && Password == choice.Password,
        AuthKind.Bearer => Token == choice.Token,
        AuthKind.OAuth2 => EditedOAuth() == (settings.OAuth ?? new()) && ClientSecret == choice.ClientSecret,
        _ => false,
    };

    OAuthSettings EditedOAuth() => new()
    {
        Grant = Grant,
        AuthorizeUrl = AuthorizeUrl,
        TokenUrl = TokenUrl,
        ClientId = ClientId,
        Scope = Scope,
        ClientAuthentication = ClientAuthentication,
        RedirectPort = RedirectPort,
    };

    string ReasonOf(Exception exception) => exception switch
    {
        OAuthException { Problem: OAuthProblem.MissingTokenUrl } => translator.Of("OAuth.MissingTokenUrl"),
        OAuthException { Problem: OAuthProblem.MissingAuthorizeUrl } => translator.Of("OAuth.MissingAuthorizeUrl"),
        OAuthException { Problem: OAuthProblem.MissingClientId } => translator.Of("OAuth.MissingClientId"),
        OAuthException { Problem: OAuthProblem.MissingClientSecret } => translator.Of("OAuth.MissingClientSecret"),
        OAuthException { Problem: OAuthProblem.InvalidAddress } failure => translator.Format("OAuth.InvalidAddress", failure.Detail),
        OAuthException { Problem: OAuthProblem.InsecureAddress } failure => translator.Format("OAuth.InsecureAddress", failure.Detail),
        OAuthException { Problem: OAuthProblem.PortUnavailable } failure => translator.Format("OAuth.PortUnavailable", failure.Detail),
        OAuthException { Problem: OAuthProblem.Denied } failure => translator.Format("OAuth.Denied", failure.Detail),
        OAuthException { Problem: OAuthProblem.TimedOut } failure => translator.Format("OAuth.TimedOut", failure.Detail),
        OAuthException { Problem: OAuthProblem.Rejected } failure => translator.Format("OAuth.Rejected", failure.Detail),
        OAuthException { Problem: OAuthProblem.InvalidResponse } => translator.Of("OAuth.InvalidResponse"),
        OAuthException { Problem: OAuthProblem.UnsupportedTokenType } failure => translator.Format("OAuth.UnsupportedTokenType", failure.Detail),
        _ => NetworkProblem.Of(exception, translator) ?? exception.GetBaseException().Message,
    };

    void Change<T>(ref T storage, T value, [CallerMemberName] string? name = null)
    {
        if (!Set(ref storage, value, name))
        {
            return;
        }
        TokenProblem = null;
        CancelFetch();
        RelabelCredential();
        if (!_loading)
        {
            Changed?.Invoke();
        }
    }
}
