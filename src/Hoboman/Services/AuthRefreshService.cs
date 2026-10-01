using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.Requests;

namespace Hoboman.Services;

public sealed class AuthRefreshService(IOAuthClient oauth, RequestLibrary library, SecretStore secrets)
{
    readonly List<Refresh> _refreshes = [];
    readonly SemaphoreSlim _savingTokens = new(1, 1);

    public event Action? Changed;

    public event Action<IReadOnlyDictionary<string, string?>>? EnvironmentsChanged;

    public static string OwnerOf(AuthSource source) => source.SecretsId == Guid.Empty && source.Folder is { } folder ? $"folder/{folder.ToUpperInvariant()}" : $"{source.SecretsId}";

    public bool IsRefreshing(string owner, string environment) => _refreshes.Any(refresh => (refresh.Owner == owner || OwnerOf(refresh.Source) == owner) && refresh.Environment == environment);

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

    public async Task<bool> FetchAsync(AuthSource source, string? clientSecret, ApiEnvironment environment, Func<OAuthToken, string, AuthSource, Task<bool>> accept, CancellationToken cancellationToken)
    {
        var owner = OwnerOf(source);
        source = _refreshes.FirstOrDefault(refresh => refresh.Owner == owner && refresh.Source.Folder == source.Folder && refresh.Source.Settings == source.Settings)?.Source ?? source;
        if (IsRefreshing(owner, environment.Name))
        {
            return false;
        }
        var refresh = new Refresh(owner, source, environment.Name);
        _refreshes.Add(refresh);
        Changed?.Invoke();
        try
        {
            clientSecret ??= await secrets.OfAsync(source.SecretsId, SecretKind.ClientSecret, cancellationToken) ?? "";
            var token = await oauth.GetTokenAsync(source.Settings.OAuth ?? new(), clientSecret, environment, cancellationToken);
            return await SaveAsync(async () => refresh.Environment is { } name && await accept(token, name, refresh.Source), cancellationToken);
        }
        finally
        {
            _refreshes.Remove(refresh);
            Changed?.Invoke();
        }
    }

    public Task<bool> RefreshFolderAsync(AuthSource source, ApiEnvironment environment, CancellationToken cancellationToken) =>
        FetchAsync(source, null, environment, (token, name, currentSource) => SaveFolderTokenAsync(currentSource, token, name, cancellationToken), cancellationToken);

    async Task<bool> SaveFolderTokenAsync(AuthSource source, OAuthToken token, string environment, CancellationToken cancellationToken)
    {
        var folder = source.Folder!;
        var expected = new FolderSettings { Id = source.SecretsId, Auth = source.Settings };
        if (!library.FolderExists(folder) || await library.LoadFolderAsync(folder, cancellationToken) != expected)
        {
            return false;
        }
        var shared = expected.Id != Guid.Empty && await library.SharesFolderIdAsync(folder, expected.Id, cancellationToken);
        var settings = expected.Id == Guid.Empty || shared ? expected with { Id = Guid.NewGuid() } : expected;
        if (settings.Id != expected.Id)
        {
            await secrets.CopyAsync(expected.Id, settings.Id, cancellationToken);
        }
        await secrets.SaveAsync(settings.Id, SecretKind.OAuthToken, environment, token.ToJson(), cancellationToken);
        if (settings != expected)
        {
            await library.SaveFolderAsync(folder, settings, cancellationToken, createDirectory: false);
            foreach (var refresh in _refreshes.Where(refresh => refresh.Source == source))
            {
                refresh.Source = source with { SecretsId = settings.Id };
            }
        }
        return true;
    }

    public async Task FollowEnvironmentsAsync(IReadOnlyDictionary<string, string?> changes, CancellationToken cancellationToken)
    {
        if (changes.Count == 0)
        {
            return;
        }
        await SaveAsync(async () =>
        {
            foreach (var refresh in _refreshes)
            {
                refresh.Environment = refresh.Environment is { } name ? changes.NameAfter(name) : null;
            }
            EnvironmentsChanged?.Invoke(changes);
            Changed?.Invoke();
            await secrets.FollowEnvironmentsAsync(changes, cancellationToken);
        }, cancellationToken);
    }

    sealed class Refresh(string owner, AuthSource source, string environment)
    {
        public string Owner { get; } = owner;

        public AuthSource Source { get; set; } = source;

        public string? Environment { get; set; } = environment;
    }
}
