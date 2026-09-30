namespace Hoboman.Core.Auth;

// Each grant uses only some of the fields, so every field can be left out of the file.
public sealed record OAuthSettings
{
    public OAuthGrant Grant { get; init; }

    public string TokenUrl { get; init; } = "";

    // Only the authorization code grant logs in through the browser.
    public string AuthorizeUrl { get; init; } = "";

    public string ClientId { get; init; } = "";

    public string Scope { get; init; } = "";

    public OAuthClientAuthentication ClientAuthentication { get; init; }

    // Without a port, the login is caught on any free one, which most providers allow for a loopback address.
    public int? RedirectPort { get; init; }
}
