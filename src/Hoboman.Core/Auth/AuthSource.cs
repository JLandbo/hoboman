namespace Hoboman.Core.Auth;

public sealed record AuthSource(Guid SecretsId, AuthSettings Settings, string? Folder = null);
