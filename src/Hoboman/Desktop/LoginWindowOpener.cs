using Hoboman.Core.Auth;

namespace Hoboman.Desktop;

// The OAuth client is created at startup, so the app needs a login window to give it even before the window exists.
public sealed class LoginWindowOpener : ILoginWindow
{
    public Task<Uri?> GetRedirectAsync(Uri login, Uri callback, CancellationToken cancellationToken) => throw new OAuthException(OAuthProblem.LoginUnavailable);
}
