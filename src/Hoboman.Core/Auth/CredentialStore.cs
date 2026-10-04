using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Auth;

public sealed class CredentialStore(AppFolder folder, SecretStore secrets, ILogger<CredentialStore> logger)
{
    readonly JsonFile<IReadOnlyList<SavedCredential>> _file = new(folder.Credentials, [], logger);

    public async Task<IReadOnlyList<Credential>> AllAsync(CancellationToken cancellationToken) =>
        [.. (await _file.LoadAsync(cancellationToken).ConfigureAwait(false)).Select(saved => saved.ToCredential())];

    // The secrets are saved by the caller under each credential's id. Those of removed credentials are deleted after the list is saved.
    public async Task SaveAsync(IReadOnlyList<Credential> credentials, CancellationToken cancellationToken)
    {
        var removed = (await AllAsync(cancellationToken).ConfigureAwait(false)).Select(credential => credential.Id).Except(credentials.Select(credential => credential.Id)).ToHashSet();
        await _file.SaveAsync([.. credentials.Select(credential => new SavedCredential(credential.Id, credential.EnvironmentId, credential.Name, credential.Auth))], cancellationToken).ConfigureAwait(false);
        await secrets.DeleteAsync(removed, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Saved {Count} credentials", credentials.Count);
    }

    public async Task ForgetEnvironmentsAsync(IReadOnlySet<Guid> environments, CancellationToken cancellationToken)
    {
        var forgotten = (await AllAsync(cancellationToken).ConfigureAwait(false)).Where(credential => environments.Contains(credential.EnvironmentId)).Select(credential => credential.Id).ToHashSet();
        if (forgotten.Count == 0)
        {
            return;
        }
        await _file.UpdateAsync(saved => [.. saved.Where(credential => !forgotten.Contains(credential.Id))], cancellationToken).ConfigureAwait(false);
        await secrets.DeleteAsync(forgotten, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Deleted the {Count} credentials of removed environments", forgotten.Count);
    }

    // ClientId is only read, from credentials saved before they held a whole auth. Those were OAuth client credentials, and their secret is already under the same id.
    sealed record SavedCredential(Guid Id, Guid EnvironmentId, string Name, AuthSettings? Auth = null, string? ClientId = null)
    {
        public Credential ToCredential() => new(Id, EnvironmentId, Name, Auth ?? new(AuthKind.OAuth2, OAuth: new() { ClientId = ClientId ?? "" }));
    }
}
