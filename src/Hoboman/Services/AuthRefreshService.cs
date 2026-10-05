using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.Requests;

namespace Hoboman.Services;

public sealed class AuthRefreshService(IOAuthClient oauth, RequestLibrary library, SecretStore secrets)
{
    readonly List<Refresh> _refreshes = [];
    readonly SemaphoreSlim _savingTokens = new(1, 1);

    public event Action? Changed;

    // A token belongs to the id its secrets are kept under, so a folder and the tabs that inherit from it share a refresh.
    public bool IsRefreshing(Guid owner, Guid environment) => _refreshes.Any(refresh => refresh.Owner == owner && refresh.Environment == environment);

    internal Task SaveAsync(Func<Task> save, CancellationToken cancellationToken) => SaveAsync(async () =>
    {
        await save();
        return true;
    }, cancellationToken);

    internal async Task<TResult> SaveAsync<TResult>(Func<Task<TResult>> save, CancellationToken cancellationToken)
    {
        await _savingTokens.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await save();
        }
        finally
        {
            _savingTokens.Release();
        }
    }

    public async Task<bool> FetchAsync(AuthSource source, string? clientSecret, ApiEnvironment environment, Func<OAuthToken, Guid, Task<bool>> accept, CancellationToken cancellationToken)
    {
        if (IsRefreshing(source.SecretsId, environment.Id))
        {
            return false;
        }
        var refresh = new Refresh(source.SecretsId, environment.Id);
        _refreshes.Add(refresh);
        Changed?.Invoke();
        try
        {
            clientSecret ??= await secrets.OfAsync(source.SecretsId, SecretKind.ClientSecret, cancellationToken) ?? "";
            var token = await oauth.GetTokenAsync(source.Settings.OAuth ?? new(), clientSecret, environment, cancellationToken);
            return await SaveAsync(() => accept(token, refresh.Environment), cancellationToken);
        }
        finally
        {
            _refreshes.Remove(refresh);
            Changed?.Invoke();
        }
    }

    public Task<bool> RefreshFolderAsync(AuthSource source, ApiEnvironment environment, CancellationToken cancellationToken) =>
        FetchAsync(source, null, environment, (token, environmentId) => SaveFolderTokenAsync(source, token, environmentId, cancellationToken), cancellationToken);

    async Task<bool> SaveFolderTokenAsync(AuthSource source, OAuthToken token, Guid environment, CancellationToken cancellationToken)
    {
        // The folder's id is its secrets' id. A folder that is gone or got other auth while the token was fetched does not get it.
        if ((await library.LoadFolderAsync(source.SecretsId, cancellationToken))?.Auth != source.Settings)
        {
            return false;
        }
        await secrets.SaveAsync(source.SecretsId, SecretKind.OAuthToken, environment, token.ToJson(), cancellationToken);
        return true;
    }

    sealed class Refresh(Guid owner, Guid environment)
    {
        public Guid Owner { get; } = owner;

        public Guid Environment { get; } = environment;
    }
}
