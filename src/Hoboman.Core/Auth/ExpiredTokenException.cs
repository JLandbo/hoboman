namespace Hoboman.Core.Auth;

public sealed class ExpiredTokenException(DateTimeOffset expiresAt) : Exception($"The OAuth token expired at {expiresAt:O}")
{
    public DateTimeOffset ExpiresAt => expiresAt;
}
