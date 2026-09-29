using System.Security.Cryptography;
using System.Text;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Auth;

public sealed class SecretStore(AppFolder folder, ILogger<SecretStore> logger)
{
    readonly JsonFile<IReadOnlyDictionary<string, string>> _file = new(folder.Secrets, new Dictionary<string, string>(), logger);

    public async Task<string?> OfAsync(Guid id, SecretKind kind, CancellationToken cancellationToken)
    {
        if (!(await _file.LoadAsync(cancellationToken).ConfigureAwait(false)).TryGetValue(KeyOf(id, kind), out var secret))
        {
            logger.LogDebug("No {Kind} is saved for {Id}", kind, id);
            return null;
        }
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

    public async Task SaveAsync(Guid id, SecretKind kind, string secret, CancellationToken cancellationToken)
    {
        // Requests without an id would otherwise share one secret.
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        await _file.UpdateAsync(
            secrets => new Dictionary<string, string>(secrets) { [KeyOf(id, kind)] = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser)) },
            cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Saved the {Kind} for {Id}", kind, id);
    }

    static string KeyOf(Guid id, SecretKind kind) => $"{id}/{kind}";
}
