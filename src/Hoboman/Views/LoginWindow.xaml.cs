using System.Windows;
using Hoboman.Core.Auth;
using Microsoft.Web.WebView2.Core;

namespace Hoboman.Views;

// Follows an implicit login in a browser of its own and stops at the callback, as only the browser sees the token in the callback's fragment.
// The window is not transparent like other dialogs, as the browser is a window of its own, which a transparent window does not show.
public partial class LoginWindow : DialogWindow
{
    // One environment for the app, as each starts browser processes of its own. A failed start is not kept, so the next login tries again, such as after the runtime is installed.
    static Task<CoreWebView2Environment>? _environment;

    readonly TaskCompletionSource<Uri?> _redirect = new();
    readonly Uri _callback;

    LoginWindow(Uri callback)
    {
        InitializeComponent();
        _callback = callback;
        // On top of the window the login started from, such as a folder's auth, or else the main window.
        Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive) ?? Application.Current.MainWindow;
        Closed += (_, _) =>
        {
            Browser.Dispose();
            _redirect.TrySetResult(null);
        };
    }

    // Where the login keeps its cookies, told by the app. They are kept apart from the responses', which run pages of anyone's.
    public static string? DataFolder { get; set; }

    // Null when the window closed before the callback, by the user or by the cancellation.
    public static async Task<Uri?> ShowAsync(Uri login, Uri callback, CancellationToken cancellationToken)
    {
        CoreWebView2Environment environment;
        try
        {
            environment = await (_environment ??= CoreWebView2Environment.CreateAsync(null, DataFolder));
        }
        catch (Exception exception)
        {
            _environment = null;
            throw UnavailableOf(exception);
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        var window = new LoginWindow(callback);
        using (cancellationToken.Register(() => window.Dispatcher.InvokeAsync(window.Close)))
        {
            window.Loaded += (_, _) => window.Start(environment, login);
            window.ShowDialog();
        }
        return await window._redirect.Task;
    }

    async void Start(CoreWebView2Environment environment, Uri login)
    {
        try
        {
            await Browser.EnsureCoreWebView2Async(environment);
            var browser = Browser.CoreWebView2;
            // The address is never logged, as the callback's fragment holds the token.
            browser.NavigationStarting += (_, e) =>
            {
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var address) && CallbackAddress.Matches(address, _callback))
                {
                    e.Cancel = true;
                    _redirect.TrySetResult(address);
                    Dispatcher.InvokeAsync(Close);
                }
            };
            // A new window opens here instead, so a callback in it is caught too.
            browser.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                browser.Navigate(e.Uri);
            };
            // The login saves nothing in the user's downloads, starts no other programs and asks for no camera, microphone, position or the like.
            browser.DownloadStarting += (_, e) => e.Cancel = true;
            browser.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
            browser.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            browser.Navigate(login.AbsoluteUri);
        }
        // A browser that cannot start, such as when its folder cannot be written, ends the login with the reason instead of stopping the app.
        catch (Exception exception)
        {
            _redirect.TrySetException(UnavailableOf(exception));
            Close();
        }
    }

    // No detail tells the missing runtime from other failures, so it can be told of in the user's language.
    internal static OAuthException UnavailableOf(Exception exception) =>
        exception is WebView2RuntimeNotFoundException ? new(OAuthProblem.LoginUnavailable) : new(OAuthProblem.LoginUnavailable, exception.Message);
}
