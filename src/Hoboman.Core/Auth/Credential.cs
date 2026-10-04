namespace Hoboman.Core.Auth;

// An auth saved once for an environment, so it can be picked instead of typed. Its secrets are kept in the secret store under the Id.
public sealed record Credential(Guid Id, Guid EnvironmentId, string Name, AuthSettings Auth);
