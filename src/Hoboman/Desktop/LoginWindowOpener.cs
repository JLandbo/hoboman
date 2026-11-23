using System.Windows;
using Hoboman.Core.Auth;
using Hoboman.Views;

namespace Hoboman.Desktop;

// The OAuth client is created at startup, so the app needs a login window to give it even before the window exists.
public sealed class LoginWindowOpener : ILoginWindow
{
    // The OAuth client waits off the UI thread, which the window belongs to.
    public Task<Uri?> GetRedirectAsync(Uri login, Uri callback, CancellationToken cancellationToken) =>
        Application.Current.Dispatcher.InvokeAsync(() => LoginWindow.ShowAsync(login, callback, cancellationToken)).Task.Unwrap();
}
