namespace Hoboman.Core.Auth;

// Each grant uses only some of the fields, so every field can be left out of the file.
public sealed record OAuthSettings
{
    public OAuthGrant Grant { get; init; }

    public string TokenUrl { get; init; } = "";

    // Only the grants that log the user in use it.
    public string AuthorizeUrl { get; init; } = "";

    // Only the implicit grant uses it. It is sent as written, because providers such as Cognito demand an exact match.
    public string CallbackUrl { get; init; } = "";

    public string ClientId { get; init; } = "";

    public string Scope { get; init; } = "";

    public OAuthClientAuthentication ClientAuthentication { get; init; }

    // Without a port, the login is caught on any free one, which most providers allow for a loopback address.
    public int? RedirectPort { get; init; }
}
