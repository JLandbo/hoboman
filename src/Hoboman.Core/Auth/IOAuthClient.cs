namespace Hoboman.Core.Auth;

public interface IOAuthClient
{
    Task<string> GetTokenAsync(OAuthSettings settings, string clientSecret, CancellationToken cancellationToken);
}
