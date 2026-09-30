namespace Hoboman.Core.Auth;

// The detail is what the provider or the user gave, such as "access_denied: The user said no", and is shown as it is.
// It is kept out of the message, because the message is logged and an address in the detail can carry a key.
public sealed class OAuthException(OAuthProblem problem, string? detail = null) : Exception($"OAuth failed: {problem}")
{
    public OAuthProblem Problem => problem;

    public string? Detail => detail;
}
