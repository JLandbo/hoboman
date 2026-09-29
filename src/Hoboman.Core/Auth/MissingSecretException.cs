namespace Hoboman.Core.Auth;

public sealed class MissingSecretException(SecretKind kind) : Exception($"No {kind} is saved for this request")
{
    public SecretKind Kind => kind;
}
