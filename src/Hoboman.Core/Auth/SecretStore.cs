using System.Security.Cryptography;
using System.Text;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Auth;

public sealed class SecretStore(AppFolder folder, ILogger<SecretStore> logger)
{
    readonly JsonFile<IReadOnlyDictionary<Guid, string>> _file = new(folder.Secrets, new Dictionary<Guid, string>(), logger);

    public async Task<string> OfAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!(await _file.LoadAsync(cancellationToken).ConfigureAwait(false)).TryGetValue(id, out var secret))
        {
            return "";
        }
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(secret), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            logger.LogWarning(exception, "Could not decrypt the secret for {Id}", id);
            return "";
        }
    }

    public async Task SaveAsync(Guid id, string secret, CancellationToken cancellationToken)
    {
        await _file.UpdateAsync(
            secrets => new Dictionary<Guid, string>(secrets) { [id] = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser)) },
            cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Saved the secret for {Id}", id);
    }
}
