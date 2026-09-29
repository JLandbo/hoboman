namespace Hoboman.Core.Auth;

public sealed record OAuthSettings(OAuthGrant Grant, string TokenUrl, string AuthorizeUrl, string ClientId, string Scope, int RedirectPort);
