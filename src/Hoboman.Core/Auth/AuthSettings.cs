namespace Hoboman.Core.Auth;

public sealed record AuthSettings(AuthKind Kind, string UserName = "", OAuthSettings? OAuth = null)
{
    public static AuthSettings Inherit { get; } = new(AuthKind.Inherit);

    public static AuthSettings None { get; } = new(AuthKind.None);
}
