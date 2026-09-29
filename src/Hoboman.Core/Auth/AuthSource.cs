namespace Hoboman.Core.Auth;

// The auth to send, and the id of the request or folder whose secrets it uses.
public sealed record AuthSource(Guid Id, AuthSettings Settings);
