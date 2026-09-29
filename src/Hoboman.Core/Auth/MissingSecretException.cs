namespace Hoboman.Core.Auth;

public sealed class MissingSecretException(SecretKind kind) : Exception($"No {kind} is saved for the request or its folder")
{
    public SecretKind Kind => kind;
}
