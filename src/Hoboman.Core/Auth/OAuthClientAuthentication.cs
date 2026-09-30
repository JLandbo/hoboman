namespace Hoboman.Core.Auth;

// The standard prefers the Basic header, but many providers only read the client id and secret from the form.
public enum OAuthClientAuthentication { BasicHeader, RequestBody }
