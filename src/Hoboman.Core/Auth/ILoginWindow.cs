namespace Hoboman.Core.Auth;

// The implicit grant puts the token in the callback's fragment, which never reaches a server, so only a window that follows the login can read it.
public interface ILoginWindow
{
    // Null means the window closed without reaching the callback, whether the user or the cancellation closed it.
    Task<Uri?> GetRedirectAsync(Uri login, Uri callback, CancellationToken cancellationToken);
}
