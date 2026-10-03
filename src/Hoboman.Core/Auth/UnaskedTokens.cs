using Hoboman.Core.Environments;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Auth;

// Client credentials need no login, so a token for them can be fetched and saved without the user.
public sealed class UnaskedTokens(IOAuthClient oauth, SecretStore secrets, ILogger<UnaskedTokens> logger)
{
    public static bool CanFetch(AuthSettings auth) => auth.Kind == AuthKind.OAuth2 && (auth.OAuth ?? new()).Grant == OAuthGrant.ClientCredentials;

    // A failed fetch is only logged, so the caller tells of the problem that made it fetch.
    public async Task<bool> FetchAsync(AuthSource auth, ApiEnvironment environment, CancellationToken cancellationToken)
    {
        // Secrets are saved by their owner's id, and an owner without one has nowhere to keep the token.
        if (auth.SecretsId == Guid.Empty)
        {
            return false;
        }
        try
        {
            var clientSecret = await secrets.OfAsync(auth.SecretsId, SecretKind.ClientSecret, cancellationToken).ConfigureAwait(false) ?? "";
            var token = await oauth.GetTokenAsync(auth.Settings.OAuth ?? new(), clientSecret, environment, cancellationToken).ConfigureAwait(false);
            await secrets.SaveAsync(auth.SecretsId, SecretKind.OAuthToken, environment.Name, token.ToJson(), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(exception, "Could not fetch an OAuth token without the user");
            return false;
        }
    }
}
