namespace Hoboman.Core.Auth;

public enum OAuthProblem
{
    MissingTokenUrl,
    MissingAuthorizeUrl,
    MissingCallbackUrl,
    MissingClientId,
    MissingClientSecret,
    InvalidAddress,
    InsecureAddress,
    PortUnavailable,
    Denied,
    TimedOut,
    LoginClosed,
    LoginUnavailable,
    Rejected,
    InvalidResponse,
    UnsupportedTokenType,
}
