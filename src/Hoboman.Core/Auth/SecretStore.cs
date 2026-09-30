using System.Security.Cryptography;
using System.Text;
using Hoboman.Core.Environments;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Auth;

// A secret can belong to an environment, such as an OAuth token fetched with its addresses; "" is without an environment.
public sealed class SecretStore(AppFolder folder, ILogger<SecretStore> logger)
{
    readonly JsonFile<IReadOnlyDictionary<string, string>> _file = new(folder.Secrets, new Dictionary<string, string>(), logger);

    public Task<string?> OfAsync(Guid id, SecretKind kind, CancellationToken cancellationToken) => OfAsync(id, kind, "", cancellationToken);

    public async Task<string?> OfAsync(Guid id, SecretKind kind, string environment, CancellationToken cancellationToken)
    {
        if ((await _file.LoadAsync(cancellationToken).ConfigureAwait(false)).GetValueOrDefault(KeyOf(id, kind, environment)) is not { } secret)
        {
            logger.LogDebug("No {Kind} is saved for {Id} in {Environment}", kind, id, environment);
            return null;
        }
        return Decrypted(secret, id, kind);
    }

    public async Task<IReadOnlyDictionary<string, string>> OfEachEnvironmentAsync(Guid id, SecretKind kind, CancellationToken cancellationToken)
    {
        var key = KeyOf(id, kind, "");
        var secrets = new Dictionary<string, string>();
        foreach (var (saved, secret) in await _file.LoadAsync(cancellationToken).ConfigureAwait(false))
        {
            var environment = saved == key ? "" : saved.StartsWith($"{key}/", StringComparison.Ordinal) ? saved[(key.Length + 1)..] : null;
            // A hand-edited file can hold a null, which the dictionary type does not show.
            if (environment is not null && secret is not null && Decrypted(secret, id, kind) is { } decrypted)
            {
                secrets[environment] = decrypted;
            }
        }
        return secrets;
    }

    public Task SaveAsync(Guid id, SecretKind kind, string secret, CancellationToken cancellationToken) => SaveAsync(id, kind, "", secret, cancellationToken);

    public async Task SaveAsync(Guid id, SecretKind kind, string environment, string secret, CancellationToken cancellationToken)
    {
        // Requests without an id would otherwise share one secret.
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        await _file.UpdateAsync(
            secrets => new Dictionary<string, string>(secrets) { [KeyOf(id, kind, environment)] = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser)) },
            cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Saved the {Kind} for {Id} in {Environment}", kind, id, environment);
    }

    // A renamed environment keeps its secrets under the new name, and a removed one loses them.
    public async Task FollowEnvironmentsAsync(IReadOnlyDictionary<string, string?> changes, CancellationToken cancellationToken)
    {
        if (changes.Count == 0)
        {
            return;
        }
        await _file.UpdateAsync(secrets =>
        {
            var followed = new Dictionary<string, string>();
            foreach (var (key, secret) in secrets)
            {
                if (KeyAfter(key, changes) is { } kept)
                {
                    followed[kept] = secret;
                }
            }
            return followed;
        }, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("The secrets followed {Count} renamed or removed environments", changes.Count);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await _file.UpdateAsync(secrets => secrets.Where(secret => !secret.Key.StartsWith($"{id}/", StringComparison.Ordinal)).ToDictionary(), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Deleted the secrets for {Id}", id);
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

    static string KeyOf(Guid id, SecretKind kind, string environment) => environment.Length == 0 ? $"{id}/{kind}" : $"{id}/{kind}/{environment}";

    // The environment is everything after the id and the kind, so a name with a slash in it is still one name.
    static string? KeyAfter(string key, IReadOnlyDictionary<string, string?> changes)
    {
        var kind = key.IndexOf('/');
        var environment = kind < 0 ? -1 : key.IndexOf('/', kind + 1);
        if (environment < 0)
        {
            return key;
        }
        return changes.NameAfter(key[(environment + 1)..]) is { } name ? $"{key[..environment]}/{name}" : null;
    }
}
