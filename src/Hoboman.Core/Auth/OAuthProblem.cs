namespace Hoboman.Core.Auth;

public enum OAuthProblem
{
    MissingTokenUrl,
    MissingAuthorizeUrl,
    MissingClientId,
    MissingClientSecret,
    InvalidAddress,
    InsecureAddress,
    PortUnavailable,
    Denied,
    TimedOut,
    Rejected,
    InvalidResponse,
    UnsupportedTokenType,
}
