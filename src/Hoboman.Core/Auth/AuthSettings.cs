namespace Hoboman.Core.Auth;

public sealed record AuthSettings(AuthKind Kind, string UserName = "", OAuthSettings? OAuth = null)
{
    public static AuthSettings None { get; } = new(AuthKind.None);
}
