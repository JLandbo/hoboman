using Hoboman.Core.Environments;

namespace Hoboman.Core.Auth;

public interface IOAuthClient
{
    Task<OAuthToken> GetTokenAsync(OAuthSettings settings, string clientSecret, ApiEnvironment? environment, CancellationToken cancellationToken);
}
