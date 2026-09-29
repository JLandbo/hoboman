using System.Security.Cryptography;
using System.Text;
using Hoboman.Core.Storage;

namespace Hoboman.Core.Auth;

public sealed class SecretStore(AppFolder folder)
{
    readonly JsonFile<IReadOnlyDictionary<Guid, string>> _file = new(folder.Secrets, new Dictionary<Guid, string>());

    public string Of(Guid id)
    {
        if (!_file.Load().TryGetValue(id, out var secret))
        {
            return "";
        }
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(secret), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return "";
        }
    }

    public void Save(Guid id, string secret) =>
        _file.Save(new Dictionary<Guid, string>(_file.Load()) { [id] = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser)) });
}
