using System.Windows;
using Hoboman.Core.Auth;
using Hoboman.Views;

namespace Hoboman.Desktop;

// The OAuth client is created at startup, so the app needs a login window to give it even before the window exists.
public sealed class LoginWindowOpener : ILoginWindow
{
    // Logins are started on the UI thread today, but the OAuth client may go on from another thread, so the window is always shown through the UI thread it belongs to.
    public Task<Uri?> GetRedirectAsync(Uri login, Uri callback, CancellationToken cancellationToken) =>
        Application.Current.Dispatcher.InvokeAsync(() => LoginWindow.ShowAsync(login, callback, cancellationToken)).Task.Unwrap();
}
