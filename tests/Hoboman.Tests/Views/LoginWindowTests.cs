using System.Collections.Concurrent;
using System.Net;
using System.Windows;
using Hoboman.Desktop;
using Hoboman.Tests.Auth;
using Hoboman.Tests.ViewModels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace Hoboman.Tests.Views;

[Collection("Ui")]
public sealed class LoginWindowTests(LoginWindowTests.LoginServer server) : IClassFixture<LoginWindowTests.LoginServer>
{
    // The browser keeps the folder for the whole run, so every test here uses the same one, never the user's.
    static readonly string _folder = Path.Combine(Path.GetTempPath(), "Hoboman.Tests", $"WebView2Login-{Guid.NewGuid():N}");

    static LoginWindowTests() => LoginWindow.DataFolder = _folder;

    readonly LoginWindowOpener _opener = new();

    Uri Callback => new(server.Address, "/callback");

    [Fact]
    public async Task GetRedirectAsync_WhenTheLoginRedirectsToTheCallback_ThenReturnsTheCallbackWithTheFragmentAndCloses()
    {
        SkipWithoutRuntime();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            ShowMainWindow();

            // Act
            var redirect = await _opener.GetRedirectAsync(new(server.Address, "/login"), Callback, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(("/callback", "#access_token=abc&state=xyz"), (redirect?.AbsolutePath, redirect?.Fragment));
            Assert.Empty(LoginWindows());
        });
    }

    [Fact]
    public async Task GetRedirectAsync_WhenAnAddressOnlyLooksLikeTheCallback_ThenTheLoginGoesOn()
    {
        SkipWithoutRuntime();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            ShowMainWindow();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

            // Act
            var getting = _opener.GetRedirectAsync(new(server.Address, "/look-alike"), Callback, cancellation.Token);
            await Ui.UntilAsync(() => server.Paths.Contains("/callback/more"));

            // Assert
            Assert.Single(LoginWindows());
            cancellation.Cancel();
            await getting;
        });
    }

    [Fact]
    public async Task GetRedirectAsync_WhenTheUserClosesTheWindow_ThenReturnsNull()
    {
        SkipWithoutRuntime();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            ShowMainWindow();
            var getting = _opener.GetRedirectAsync(new(server.Address, "/page"), Callback, TestContext.Current.CancellationToken);
            await Ui.UntilAsync(() => LoginWindows().Any(window => window.IsVisible));

            // Act
            LoginWindows().Single().Close();

            // Assert
            Assert.Null(await getting);
        });
    }

    [Fact]
    public async Task GetRedirectAsync_WhenCancelled_ThenClosesTheWindowAndReturnsNull()
    {
        SkipWithoutRuntime();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            ShowMainWindow();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var getting = _opener.GetRedirectAsync(new(server.Address, "/page"), Callback, cancellation.Token);
            await Ui.UntilAsync(() => LoginWindows().Any(window => window.IsVisible));

            // Act
            cancellation.Cancel();
            var redirect = await getting;

            // Assert
            Assert.Null(redirect);
            Assert.Empty(LoginWindows());
        });
    }

    [Fact]
    public async Task GetRedirectAsync_WhenOpenedAgain_ThenKeepsTheCookiesInItsOwnFolder()
    {
        SkipWithoutRuntime();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            ShowMainWindow();
            await _opener.GetRedirectAsync(new(server.Address, "/set-cookie"), Callback, TestContext.Current.CancellationToken);

            // Act
            var redirect = await _opener.GetRedirectAsync(new(server.Address, "/cookie"), Callback, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(("#session=kept", true), (redirect?.Fragment, Directory.EnumerateFileSystemEntries(_folder).Any()));
        });
    }

    [Theory]
    [InlineData(null, "The token could not be fetched: the login window needs the Microsoft Edge WebView2 Runtime, which is not installed.")]
    [InlineData("Access is denied.", "The token could not be fetched: the login window could not start (Access is denied.).")]
    public async Task FetchTokenAsync_WhenTheLoginWindowCannotStart_ThenSaysWhyWithoutAToken(string? detail, string problem)
    {
        // Arrange
        using var harness = new Harness(oauth: new FakeOAuthClient(_ => throw new OAuthException(OAuthProblem.LoginUnavailable, detail)));
        var auth = harness.Tab().Auth;

        // Act
        await auth.FetchTokenAsync();

        // Assert
        Assert.Equal((problem, null), (auth.TokenProblem, auth.AccessToken));
    }

    // The login window lies on top of the app's own window.
    static void ShowMainWindow()
    {
        var main = new Window();
        Application.Current.MainWindow = main;
        Ui.Show(main);
    }

    static IEnumerable<LoginWindow> LoginWindows() => Application.Current.Windows.OfType<LoginWindow>();

    static void SkipWithoutRuntime()
    {
        try
        {
            CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            Assert.Skip("The Microsoft Edge WebView2 Runtime is not installed.");
        }
    }

    // A login provider that answers by path, as the redirects to the callback carry the answer in the fragment.
    public sealed class LoginServer : IAsyncLifetime
    {
        WebApplication? _app;

        public Uri Address { get; private set; } = null!;

        public ConcurrentBag<string> Paths { get; } = [];

        public async ValueTask InitializeAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
            _app = builder.Build();
            _app.Run(AnswerAsync);
            await _app.StartAsync();
            Address = new Uri(_app.Urls.Single());
        }

        public async ValueTask DisposeAsync()
        {
            if (_app is not null)
            {
                await _app.DisposeAsync();
            }
        }

        async Task AnswerAsync(HttpContext context)
        {
            Paths.Add(context.Request.Path);
            var location = context.Request.Path.Value switch
            {
                "/login" => "/callback#access_token=abc&state=xyz",
                "/look-alike" => "/callback/more#access_token=abc",
                "/set-cookie" => "/callback",
                "/cookie" => $"/callback#session={context.Request.Cookies["session"]}",
                _ => null,
            };
            if (context.Request.Path == "/set-cookie")
            {
                context.Response.Cookies.Append("session", "kept", new() { MaxAge = TimeSpan.FromHours(1) });
            }
            if (location is not null)
            {
                context.Response.Redirect(location);
                return;
            }
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync("<html><body>Log in</body></html>");
        }
    }
}
