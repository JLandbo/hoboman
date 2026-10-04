using System.Security.Cryptography;
using System.Text;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Auth;

// A secret can belong to an environment, such as an OAuth token fetched with its addresses; Guid.Empty is without an environment.
public sealed class SecretStore(AppFolder folder, ILogger<SecretStore> logger)
{
    readonly JsonFile<IReadOnlyDictionary<string, string>> _file = new(folder.Secrets, new Dictionary<string, string>(), logger);

    public Task<string?> OfAsync(Guid id, SecretKind kind, CancellationToken cancellationToken) => OfAsync(id, kind, Guid.Empty, cancellationToken);

    public async Task<string?> OfAsync(Guid id, SecretKind kind, Guid environment, CancellationToken cancellationToken)
    {
        if ((await _file.LoadAsync(cancellationToken).ConfigureAwait(false)).GetValueOrDefault(KeyOf(id, kind, environment)) is not { } secret)
        {
            logger.LogDebug("No {Kind} is saved for {Id} in {Environment}", kind, id, environment);
            return null;
        }
        return Decrypted(secret, id, kind);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> OfEachEnvironmentAsync(Guid id, SecretKind kind, CancellationToken cancellationToken)
    {
        var key = KeyOf(id, kind, Guid.Empty);
        var secrets = new Dictionary<Guid, string>();
        foreach (var (saved, secret) in await _file.LoadAsync(cancellationToken).ConfigureAwait(false))
        {
            Guid? environment = saved == key ? Guid.Empty : saved.StartsWith($"{key}/", StringComparison.Ordinal) && Guid.TryParse(saved[(key.Length + 1)..], out var parsed) ? parsed : null;
            // A hand-edited file can hold a null, which the dictionary type does not show.
            if (environment is { } found && secret is not null && Decrypted(secret, id, kind) is { } decrypted)
            {
                secrets[found] = decrypted;
            }
        }
        return secrets;
    }

    public Task SaveAsync(Guid id, SecretKind kind, string secret, CancellationToken cancellationToken) => SaveAsync(id, kind, Guid.Empty, secret, cancellationToken);

    public async Task SaveAsync(Guid id, SecretKind kind, Guid environment, string secret, CancellationToken cancellationToken)
    {
        // Requests without an id would otherwise share one secret.
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        await _file.UpdateAsync(
            secrets => new Dictionary<string, string>(secrets) { [KeyOf(id, kind, environment)] = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser)) },
            cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Saved the {Kind} for {Id} in {Environment}", kind, id, environment);
    }

    public async Task DeleteAsync(Guid id, SecretKind kind, Guid environment, CancellationToken cancellationToken)
    {
        var key = KeyOf(id, kind, environment);
        await _file.UpdateAsync(secrets => secrets.Where(secret => secret.Key != key).ToDictionary(), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Deleted the {Kind} for {Id} in {Environment}", kind, id, environment);
    }

    public async Task ForgetEnvironmentsAsync(IReadOnlySet<Guid> environments, CancellationToken cancellationToken)
    {
        if (environments.Count == 0)
        {
            return;
        }
        await ForgetAsync(environment => Guid.TryParse(environment, out var id) && environments.Contains(id), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Deleted the secrets of {Count} removed environments", environments.Count);
    }

    // Secrets were saved under the environment's name before environments had ids, and nothing reads them now.
    public Task ForgetEnvironmentNamesAsync(CancellationToken cancellationToken) => ForgetAsync(environment => !Guid.TryParse(environment, out _), cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => DeleteAsync(new HashSet<Guid> { id }, cancellationToken);

    public async Task DeleteAsync(IReadOnlySet<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }
        await _file.UpdateAsync(secrets => secrets.Where(secret => !Guid.TryParse(secret.Key.Split('/')[0], out var id) || !ids.Contains(id)).ToDictionary(), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Deleted the secrets for {Count} owners", ids.Count);
    }

    public async Task CopyAsync(Guid sourceId, Guid targetId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(targetId, Guid.Empty);
        if (sourceId == Guid.Empty || sourceId == targetId)
        {
            return;
        }
        await _file.UpdateAsync(saved =>
        {
            var copied = new Dictionary<string, string>(saved);
            foreach (var (key, secret) in saved.Where(secret => secret.Key.StartsWith($"{sourceId}/", StringComparison.Ordinal)))
            {
                copied[$"{targetId}{key[key.IndexOf('/')..]}"] = secret;
            }
            return copied;
        }, cancellationToken).ConfigureAwait(false);
    }

    string? Decrypted(string secret, Guid id, SecretKind kind)
    {
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(secret), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            logger.LogWarning(exception, "Could not decrypt the {Kind} for {Id}", kind, id);
            return null;
        }
    }

    Task ForgetAsync(Func<string, bool> forget, CancellationToken cancellationToken) =>
        _file.UpdateAsync(secrets => secrets.Where(secret => EnvironmentOf(secret.Key) is not { } environment || !forget(environment)).ToDictionary(), cancellationToken);

    static string KeyOf(Guid id, SecretKind kind, Guid environment) => environment == Guid.Empty ? $"{id}/{kind}" : $"{id}/{kind}/{environment}";

    // The environment is everything after the id and the kind, so an old name with a slash in it is still one name.
    static string? EnvironmentOf(string key)
    {
        var kind = key.IndexOf('/');
        var environment = kind < 0 ? -1 : key.IndexOf('/', kind + 1);
        return environment < 0 ? null : key[(environment + 1)..];
    }
}
