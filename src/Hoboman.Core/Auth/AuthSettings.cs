namespace Hoboman.Core.Auth;

public enum AuthKind { None, Basic, Bearer, OAuth2 }

public enum OAuthGrant { ClientCredentials, AuthorizationCode }

public sealed record OAuthSettings(OAuthGrant Grant, string TokenUrl, string AuthorizeUrl, string ClientId, string Scope, int RedirectPort);

public sealed record AuthSettings(AuthKind Kind, string UserName = "", OAuthSettings? OAuth = null)
{
    public static AuthSettings None { get; } = new(AuthKind.None);
}
