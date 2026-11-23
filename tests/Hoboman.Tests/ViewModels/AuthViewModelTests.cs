using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Hoboman.Tests.Auth;
using Hoboman.Tests.Views;

namespace Hoboman.Tests.ViewModels;

// In the UI collection, because the editor's fields are checked in the shared WPF thread too.
[Collection("Ui")]
public sealed class AuthViewModelTests
{
    static readonly ApiEnvironment _dev = new("Dev", []) { Id = Guid.NewGuid() };
    static readonly ApiEnvironment _prod = new("Prod", []) { Id = Guid.NewGuid() };

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FetchTokenAsync_WhenAnEnvironmentIsChosen_ThenAsksWithIt()
    {
        // Arrange
        using var harness = new Harness();
        var auth = harness.Tab().Auth;
        await harness.Environments.ChooseAsync(_dev);

        // Act
        await auth.FetchTokenAsync();

        // Assert
        Assert.Equal("Dev", harness.OAuth.Asked?.Environment?.Name);
    }

    [Fact]
    public async Task FetchTokenAsync_WhenAnotherEnvironmentIsChosenAfter_ThenItsTokenIsNotShown()
    {
        // Arrange
        using var harness = new Harness();
        var auth = harness.Tab().Auth;
        await harness.Environments.ChooseAsync(_dev);
        await auth.FetchTokenAsync();

        // Act
        await harness.Environments.ChooseAsync(_prod);

        // Assert
        Assert.Null(auth.AccessToken);
    }

    [Fact]
    public async Task FetchTokenAsync_WhenTheChosenEnvironmentIsRenamedAfter_ThenStillShowsItsToken()
    {
        // Arrange
        using var harness = new Harness();
        var auth = harness.Tab().Auth;
        await harness.Environments.ChooseAsync(_dev);
        await auth.FetchTokenAsync();

        // Act
        await harness.Environments.ChooseAsync(_dev with { Name = "Development" });

        // Assert
        Assert.Equal(FakeOAuthClient.Token, auth.AccessToken);
    }

    [Fact]
    public async Task SaveSecretsAsync_WhenATokenWasFetched_ThenSavesItForTheChosenEnvironment()
    {
        // Arrange
        using var harness = new Harness();
        var auth = harness.Tab().Auth;
        await harness.Environments.ChooseAsync(_dev);
        await auth.FetchTokenAsync();
        var id = Guid.NewGuid();

        // Act
        await auth.SaveSecretsAsync(id, Cancellation);

        // Assert
        Assert.NotNull(await harness.Secrets.OfAsync(id, SecretKind.OAuthToken, _dev.Id, Cancellation));
    }

    [Fact]
    public async Task FetchTokenAsync_WhenTheProviderGivesAToken_ThenShowsIt()
    {
        // Arrange
        using var harness = new Harness();
        var auth = harness.Tab().Auth;

        // Act
        await auth.FetchTokenAsync();

        // Assert
        Assert.Equal(FakeOAuthClient.Token, auth.AccessToken);
    }

    [Fact]
    public async Task FetchTokenAsync_WhenATokenIsFetched_ThenItIsNotSavedYet()
    {
        // Arrange
        using var harness = new Harness();
        var tab = harness.Tab();

        // Act
        await tab.Auth.FetchTokenAsync();

        // Assert
        Assert.Empty(await harness.Secrets.OfEachEnvironmentAsync(tab.Id, SecretKind.OAuthToken, Cancellation));
    }

    [Fact]
    public void FetchToken_WhenTheOwnerFetches_ThenLeavesItToTheOwner()
    {
        // Arrange
        using var harness = new Harness();
        var auth = harness.Tab().Auth;
        var asked = false;
        auth.OwnerFetch = () =>
        {
            asked = true;
            return Task.CompletedTask;
        };

        // Act
        auth.FetchToken.Execute(null);

        // Assert
        Assert.Equal((true, null), (asked, harness.OAuth.Asked));
    }

    [Fact]
    public async Task FetchTokenAsync_WhenTheSecretsAreReloadedDuringTheLogin_ThenKeepsTheToken()
    {
        // Arrange
        var login = new TaskCompletionSource<OAuthToken>();
        using var harness = new Harness(oauth: new FakeOAuthClient(cancellationToken => login.Task.WaitAsync(cancellationToken)));
        var tab = harness.Tab();
        var auth = tab.Auth;
        var fetching = auth.FetchTokenAsync();
        await auth.LoadSecretsAsync(tab.Id, Cancellation);

        // Act
        login.SetResult(FakeOAuthClient.Token);
        await fetching;

        // Assert
        Assert.Equal(FakeOAuthClient.Token, auth.AccessToken);
    }

    [Fact]
    public async Task FetchTokenAsync_WhenAnotherEnvironmentIsChosenDuringTheLogin_ThenTheTokenBelongsToTheFirst()
    {
        // Arrange
        var login = new TaskCompletionSource<OAuthToken>();
        using var harness = new Harness(oauth: new FakeOAuthClient(cancellationToken => login.Task.WaitAsync(cancellationToken)));
        var auth = harness.Tab().Auth;
        await harness.Environments.ChooseAsync(_dev);
        var fetching = auth.FetchTokenAsync();
        await harness.Environments.ChooseAsync(_prod);
        var id = Guid.NewGuid();

        // Act
        login.SetResult(FakeOAuthClient.Token);
        await fetching;
        await auth.SaveSecretsAsync(id, Cancellation);

        // Assert
        Assert.Equal([_dev.Id], (await harness.Secrets.OfEachEnvironmentAsync(id, SecretKind.OAuthToken, Cancellation)).Keys);
    }

    [Fact]
    public async Task FetchTokenAsync_WhenTheEnvironmentIsRenamedDuringTheLogin_ThenTheTokenFollows()
    {
        // Arrange
        var login = new TaskCompletionSource<OAuthToken>();
        using var harness = new Harness(oauth: new FakeOAuthClient(cancellationToken => login.Task.WaitAsync(cancellationToken)));
        var auth = harness.Tab().Auth;
        await harness.Environments.ChooseAsync(_dev);
        var fetching = auth.FetchTokenAsync();
        await harness.Environments.ChooseAsync(_dev with { Name = "Development" });

        // Act
        login.SetResult(FakeOAuthClient.Token);
        await fetching;

        // Assert
        Assert.Equal(FakeOAuthClient.Token, auth.AccessToken);
    }

    [Fact]
    public async Task FetchTokenAsync_WhenTheTokenServerCannotBeFound_ThenSaysSoLikeSending()
    {
        // Arrange
        using var harness = new Harness(oauth: new FakeOAuthClient(_ => throw new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known.")));
        var auth = harness.Tab().Auth;

        // Act
        await auth.FetchTokenAsync();

        // Assert
        Assert.Equal($"The token could not be fetched: The server could not be found.{Environment.NewLine}No such host is known.", auth.TokenProblem);
    }

    [Fact]
    public async Task Load_WhenAnEarlierFetchFailed_ThenClearsTheProblem()
    {
        // Arrange
        using var harness = new Harness(oauth: new FakeOAuthClient(_ => throw new OAuthException(OAuthProblem.Denied, "access_denied")));
        var auth = harness.Tab().Auth;
        await auth.FetchTokenAsync();

        // Act
        auth.Load(new(AuthKind.OAuth2));

        // Assert
        Assert.Null(auth.TokenProblem);
    }

    [Fact]
    public async Task TokenStatus_WhenTheTokenHasExpired_ThenSaysWhen()
    {
        // Arrange
        using var harness = new Harness();
        var auth = harness.Tab().Auth;
        await auth.FetchTokenAsync();

        // Act
        harness.Clock.Advance(TimeSpan.FromHours(2));

        // Assert
        Assert.Equal($"The token expired {FakeOAuthClient.Token.ExpiresAt!.Value.ToLocalTime().ToString("g", Translation.English.Culture)}.", auth.TokenStatus);
    }

    [Fact]
    public async Task FetchTokenAsync_WhenFetching_ThenAsksWithTheSettingsAndClientSecretShown()
    {
        // Arrange
        using var harness = new Harness();
        var auth = harness.Tab().Auth;
        auth.TokenUrl = "https://login.local/token";
        auth.ClientSecret = "secret";

        // Act
        await auth.FetchTokenAsync();

        // Assert
        Assert.Equal(("https://login.local/token", "secret"), (harness.OAuth.Asked!.Value.Settings.TokenUrl, harness.OAuth.Asked.Value.ClientSecret));
    }

    [Fact]
    public async Task FetchTokenAsync_WhenTheProviderFails_ThenSaysWhy()
    {
        // Arrange
        using var harness = new Harness(oauth: new FakeOAuthClient(_ => throw new OAuthException(OAuthProblem.Denied, "access_denied")));
        var auth = harness.Tab().Auth;

        // Act
        await auth.FetchTokenAsync();

        // Assert
        Assert.Equal("The token could not be fetched: the login was refused (access_denied).", auth.TokenProblem);
    }

    [Fact]
    public async Task FetchTokenAsync_WhenCancelled_ThenStopsWithoutAProblem()
    {
        // Arrange
        using var harness = new Harness(oauth: new FakeOAuthClient(async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return FakeOAuthClient.Token;
        }));
        var auth = harness.Tab().Auth;
        var fetching = auth.FetchTokenAsync();

        // Act
        auth.CancelFetch();
        await fetching;

        // Assert
        Assert.Null(auth.TokenProblem);
    }

    [Fact]
    public async Task SaveSecretsAsync_WhenATokenWasFetched_ThenSavesIt()
    {
        // Arrange
        using var harness = new Harness();
        var auth = harness.Tab().Auth;
        await auth.FetchTokenAsync();
        var id = Guid.NewGuid();

        // Act
        await auth.SaveSecretsAsync(id, Cancellation);

        // Assert
        Assert.Equal(FakeOAuthClient.Token, OAuthToken.FromJson((await harness.Secrets.OfAsync(id, SecretKind.OAuthToken, Cancellation))!));
    }

    [Fact]
    public async Task LoadSecretsAsync_WhenATokenAndAClientSecretAreSaved_ThenShowsThem()
    {
        // Arrange
        using var harness = new Harness();
        var id = Guid.NewGuid();
        await harness.Secrets.SaveAsync(id, SecretKind.ClientSecret, "secret", Cancellation);
        await harness.Secrets.SaveAsync(id, SecretKind.OAuthToken, FakeOAuthClient.Token.ToJson(), Cancellation);
        var auth = harness.Tab().Auth;

        // Act
        await auth.LoadSecretsAsync(id, Cancellation);

        // Assert
        Assert.Equal(("secret", FakeOAuthClient.Token), (auth.ClientSecret, auth.AccessToken!));
    }

    [Fact]
    public void ToSettings_WhenOAuthIsUntouched_ThenLeavesItOutOfTheFile()
    {
        // Arrange
        using var harness = new Harness();
        var auth = harness.Tab().Auth;

        // Act
        auth.Load(new(AuthKind.Bearer));

        // Assert
        Assert.Null(auth.ToSettings().OAuth);
    }

    [Fact]
    public void ToSettings_WhenOAuthIsFilledInAndLoadedAgain_ThenKeepsIt()
    {
        // Arrange
        using var harness = new Harness();
        var auth = harness.Tab().Auth;
        auth.Grant = OAuthGrant.Implicit;
        auth.AuthorizeUrl = "https://login.local/authorize";
        auth.CallbackUrl = "https://{{site}}/";
        auth.TokenUrl = "https://login.local/token";
        auth.ClientId = "hoboman";
        auth.Scope = "read";
        auth.ClientAuthentication = OAuthClientAuthentication.RequestBody;
        auth.RedirectPort = 5000;
        var reloaded = harness.Tab().Auth;

        // Act
        reloaded.Load(auth.ToSettings());

        // Assert
        Assert.Equal(
            new OAuthSettings
            {
                Grant = OAuthGrant.Implicit,
                AuthorizeUrl = "https://login.local/authorize",
                CallbackUrl = "https://{{site}}/",
                TokenUrl = "https://login.local/token",
                ClientId = "hoboman",
                Scope = "read",
                ClientAuthentication = OAuthClientAuthentication.RequestBody,
                RedirectPort = 5000,
            },
            reloaded.ToSettings().OAuth);
    }

    [Fact]
    public async Task UseAsync_WhenAnImplicitClientIsPicked_ThenFillsInItsCallbackUrl()
    {
        // Arrange
        using var harness = new Harness();
        var choice = await SaveImplicitClientAsync(harness);
        var auth = harness.Tab().Auth;

        // Act
        await auth.UseAsync(choice);

        // Assert
        Assert.Equal((OAuthGrant.Implicit, "https://{{site}}/"), (auth.Grant, auth.CallbackUrl));
    }

    [Fact]
    public async Task CredentialName_WhenTheCallbackUrlIsChangedAfterPicking_ThenIsNone()
    {
        // Arrange
        using var harness = new Harness();
        var choice = await SaveImplicitClientAsync(harness);
        var auth = harness.Tab().Auth;
        await auth.UseAsync(choice);
        var picked = auth.CredentialName;

        // Act
        auth.CallbackUrl = "https://other.local/";

        // Assert
        Assert.Equal(("Acies Core", null), (picked, auth.CredentialName));
    }

    [Theory]
    [InlineData("English", "The token could not be fetched: fill in the callback URL.")]
    [InlineData("Dansk", "Tokenet kunne ikke hentes: udfyld callback-URL'en.")]
    public async Task FetchTokenAsync_WhenTheCallbackUrlIsMissing_ThenSaysSoInTheLanguage(string language, string expected)
    {
        // Arrange
        using var harness = new Harness(oauth: new FakeOAuthClient(_ => throw new OAuthException(OAuthProblem.MissingCallbackUrl)));
        harness.Translator.Use(Translation.Find(language));
        var auth = harness.Tab().Auth;

        // Act
        await auth.FetchTokenAsync();

        // Assert
        Assert.Equal(expected, auth.TokenProblem);
    }

    [Theory]
    [InlineData(OAuthGrant.Implicit, new[] { "Auth URL", "Callback URL", "Client ID", "Scope" })]
    [InlineData(OAuthGrant.ClientCredentials, new[] { "Token URL", "Client ID", "Client secret", "Scope", "Send the client ID and secret" })]
    [InlineData(OAuthGrant.AuthorizationCode, new[] { "Auth URL", "Token URL", "Client ID", "Client secret", "Scope", "Send the client ID and secret", "Redirect port" })]
    public async Task AuthEditor_WhenTheGrantIsChosen_ThenShowsOnlyItsFields(OAuthGrant grant, string[] expected)
    {
        using var harness = new Harness();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var auth = harness.Tab().Auth;
            auth.Kind = AuthKind.OAuth2;
            var editor = new AuthEditor { DataContext = auth, Width = 900 };
            Ui.Show(new Window { Content = editor, SizeToContent = SizeToContent.WidthAndHeight });

            // Act
            auth.Grant = grant;
            await Ui.IdleAsync();

            // Assert
            var label = editor.FindResource("Label");
            Assert.Equal(expected, Ui.Descendants<TextBlock>(editor).Where(text => text.IsVisible && text.Style == label).Select(text => text.Text));
        });
    }

    static async Task<CredentialChoice> SaveImplicitClientAsync(Harness harness)
    {
        Credential client = new(Guid.NewGuid(), ApiEnvironment.None.Id, "Acies Core",
            new(AuthKind.OAuth2, OAuth: new() { Grant = OAuthGrant.Implicit, AuthorizeUrl = "https://auth.{{site}}/login", CallbackUrl = "https://{{site}}/", ClientId = "core-client" }));
        await harness.CredentialStore.SaveAsync([client], TestContext.Current.CancellationToken);
        await harness.Credentials.LoadAsync(TestContext.Current.CancellationToken);
        return harness.Credentials.All.Single();
    }
}
