using System.Buffers.Text;
using System.Collections.Specialized;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.Extensions.Time.Testing;

namespace Hoboman.Tests.Auth;

public sealed class OAuthClientTests(TokenServer server) : IClassFixture<TokenServer>, IDisposable
{
    readonly TemporaryFolder _temporary = new();
    readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
    HttpClients? _clients;

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _clients?.Dispose();
        _temporary.Dispose();
    }

    OAuthClient Client(IBrowser? browser = null) => new(
        _clients ??= new(new SettingsStore(new AppFolder(_temporary.Path), NullLogger<SettingsStore>.Instance)),
        browser ?? new FakeBrowser(_ => throw new InvalidOperationException("Only the authorization code grant opens a browser.")),
        new Translator(Translation.English),
        _clock,
        NullLogger<OAuthClient>.Instance);

    OAuthSettings ClientCredentials(string path = "/token") => new() { TokenUrl = new Uri(server.Address, path).ToString(), ClientId = "hoboman" };

    OAuthSettings AuthorizationCode() => new() { Grant = OAuthGrant.AuthorizationCode, TokenUrl = new Uri(server.Address, "/token").ToString(), AuthorizeUrl = "https://login.example/authorize", ClientId = "hoboman" };

    static NameValueCollection LoginOf(FakeBrowser browser) => HttpUtility.ParseQueryString(browser.Opened!.Query);

    static string Approved(NameValueCollection login) => $"code=abc&state={Uri.EscapeDataString(login["state"]!)}";

    // Plays the provider: after the login, it sends the browser back to the redirect address with the answer.
    static FakeBrowser Provider(Func<NameValueCollection, string> answerOf, Func<NameValueCollection, Task>? before = null) => new(address =>
    {
        var login = HttpUtility.ParseQueryString(address.Query);
        _ = Task.Run(async () =>
        {
            if (before is not null)
            {
                await before(login);
            }
            using var http = new HttpClient();
            await http.GetAsync($"{login["redirect_uri"]}/?{answerOf(login)}");
        });
    });

    [Fact]
    public async Task GetTokenAsync_WhenTheGrantIsClientCredentials_ThenAsksForThatGrant()
    {
        // Act
        await Client().GetTokenAsync(ClientCredentials(), "secret", null, Cancellation);

        // Assert
        Assert.Equal("client_credentials", server.Last?.Form["grant_type"]);
    }

    [Fact]
    public async Task GetTokenAsync_WhenAScopeIsGiven_ThenSendsIt()
    {
        // Act
        await Client().GetTokenAsync(ClientCredentials() with { Scope = "read write" }, "secret", null, Cancellation);

        // Assert
        Assert.Equal("read write", server.Last?.Form["scope"]);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheClientUsesTheBasicHeader_ThenSendsTheFormEncodedIdAndSecret()
    {
        // Act
        await Client().GetTokenAsync(ClientCredentials() with { ClientId = "hobo man" }, "s+cret", null, Cancellation);

        // Assert
        Assert.Equal($"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes("hobo+man:s%2Bcret"))}", server.Last?.Authorization);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheClientUsesTheBody_ThenSendsTheIdAndSecretInTheFormInstead()
    {
        // Act
        await Client().GetTokenAsync(ClientCredentials() with { ClientAuthentication = OAuthClientAuthentication.RequestBody }, "secret", null, Cancellation);

        // Assert
        Assert.Equal(("hoboman", "secret", ""), (server.Last!.Form["client_id"], server.Last.Form["client_secret"], server.Last.Authorization));
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheFieldsHaveVariables_ThenUsesTheEnvironmentsValues()
    {
        // Arrange
        var environment = new ApiEnvironment("Dev", [new("login", server.Address.ToString().TrimEnd('/')), new("client", "hoboman-dev"), new("secret", "dev secret")]);
        var settings = new OAuthSettings { TokenUrl = "{{login}}/token", ClientId = "{{client}}", ClientAuthentication = OAuthClientAuthentication.RequestBody };

        // Act
        await Client().GetTokenAsync(settings, "{{secret}}", environment, Cancellation);

        // Assert
        Assert.Equal(("hoboman-dev", "dev secret"), (server.Last!.Form["client_id"], server.Last.Form["client_secret"]));
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheServerAnswers_ThenGivesTheTokenWithItsExpiry()
    {
        // Act
        var token = await Client().GetTokenAsync(ClientCredentials(), "secret", null, Cancellation);

        // Assert
        Assert.Equal(new OAuthToken("access", "Bearer", _clock.GetUtcNow().AddHours(1), "read"), token);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheExpiryIsText_ThenStillReadsIt()
    {
        // Act
        var token = await Client().GetTokenAsync(ClientCredentials("/text-expiry"), "secret", null, Cancellation);

        // Assert
        Assert.Equal(_clock.GetUtcNow().AddHours(1), token.ExpiresAt);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheServerRejects_ThenThrowsWithItsError()
    {
        // Act
        var getting = Client().GetTokenAsync(ClientCredentials("/rejects"), "secret", null, Cancellation);

        // Assert
        Assert.Equal("invalid_client: Bad secret", (await Assert.ThrowsAsync<OAuthException>(() => getting)).Detail);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheTokenIsNotABearerToken_ThenThrows()
    {
        // Act
        var getting = Client().GetTokenAsync(ClientCredentials("/mac"), "secret", null, Cancellation);

        // Assert
        Assert.Equal(OAuthProblem.UnsupportedTokenType, (await Assert.ThrowsAsync<OAuthException>(() => getting)).Problem);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheAnswerHasNoAccessToken_ThenThrows()
    {
        // Act
        var getting = Client().GetTokenAsync(ClientCredentials("/no-token"), "secret", null, Cancellation);

        // Assert
        Assert.Equal(OAuthProblem.InvalidResponse, (await Assert.ThrowsAsync<OAuthException>(() => getting)).Problem);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheTokenUrlIsPlainHttpToAnotherComputer_ThenThrows()
    {
        // Act
        var getting = Client().GetTokenAsync(ClientCredentials() with { TokenUrl = "http://login.example/token" }, "secret", null, Cancellation);

        // Assert
        Assert.Equal(OAuthProblem.InsecureAddress, (await Assert.ThrowsAsync<OAuthException>(() => getting)).Problem);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheTokenUrlIsMissing_ThenThrows()
    {
        // Act
        var getting = Client().GetTokenAsync(ClientCredentials() with { TokenUrl = "" }, "secret", null, Cancellation);

        // Assert
        Assert.Equal(OAuthProblem.MissingTokenUrl, (await Assert.ThrowsAsync<OAuthException>(() => getting)).Problem);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheClientIdIsMissing_ThenThrows()
    {
        // Act
        var getting = Client().GetTokenAsync(ClientCredentials() with { ClientId = "" }, "secret", null, Cancellation);

        // Assert
        Assert.Equal(OAuthProblem.MissingClientId, (await Assert.ThrowsAsync<OAuthException>(() => getting)).Problem);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheClientCredentialsGrantHasNoSecret_ThenThrows()
    {
        // Act
        var getting = Client().GetTokenAsync(ClientCredentials(), "", null, Cancellation);

        // Assert
        Assert.Equal(OAuthProblem.MissingClientSecret, (await Assert.ThrowsAsync<OAuthException>(() => getting)).Problem);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheLoginSucceeds_ThenTradesTheCodeForAToken()
    {
        // Act
        await Client(Provider(Approved)).GetTokenAsync(AuthorizationCode(), "", null, Cancellation);

        // Assert
        Assert.Equal(("authorization_code", "abc"), (server.Last!.Form["grant_type"], server.Last.Form["code"]));
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheLoginSucceeds_ThenSendsTheSameRedirectAddressBothTimes()
    {
        // Arrange
        var browser = Provider(Approved);

        // Act
        await Client(browser).GetTokenAsync(AuthorizationCode(), "", null, Cancellation);

        // Assert
        Assert.Equal(LoginOf(browser)["redirect_uri"], server.Last?.Form["redirect_uri"]);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheLoginSucceeds_ThenProvesTheCodeWithTheVerifierOfTheS256Challenge()
    {
        // Arrange
        var browser = Provider(Approved);

        // Act
        await Client(browser).GetTokenAsync(AuthorizationCode(), "", null, Cancellation);

        // Assert
        var challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(server.Last!.Form["code_verifier"])));
        Assert.Equal((challenge, "S256"), (LoginOf(browser)["code_challenge"], LoginOf(browser)["code_challenge_method"]));
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheLoginStarts_ThenTheRedirectGoesToTheLoopbackAddress()
    {
        // Arrange
        var browser = Provider(Approved);

        // Act
        await Client(browser).GetTokenAsync(AuthorizationCode(), "", null, Cancellation);

        // Assert
        Assert.StartsWith("http://127.0.0.1:", LoginOf(browser)["redirect_uri"]);
    }

    [Fact]
    public async Task GetTokenAsync_WhenThereIsNoClientSecret_ThenOnlySendsTheClientId()
    {
        // Act
        await Client(Provider(Approved)).GetTokenAsync(AuthorizationCode(), "", null, Cancellation);

        // Assert
        Assert.Equal(("hoboman", ""), (server.Last!.Form["client_id"], server.Last.Authorization));
    }

    [Fact]
    public async Task GetTokenAsync_WhenARedirectWithTheWrongStateComesFirst_ThenWaitsForTheRealOne()
    {
        // Arrange
        var browser = Provider(Approved, async login =>
        {
            using var http = new HttpClient();
            await http.GetAsync($"{login["redirect_uri"]}/?code=forged&state=forged");
        });

        // Act
        await Client(browser).GetTokenAsync(AuthorizationCode(), "", null, Cancellation);

        // Assert
        Assert.Equal("abc", server.Last?.Form["code"]);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheUserSaysNo_ThenThrowsWithTheProvidersError()
    {
        // Arrange
        var browser = Provider(login => $"error=access_denied&error_description=The%20user%20said%20no&state={Uri.EscapeDataString(login["state"]!)}");

        // Act
        var getting = Client(browser).GetTokenAsync(AuthorizationCode(), "", null, Cancellation);

        // Assert
        Assert.Equal("access_denied: The user said no", (await Assert.ThrowsAsync<OAuthException>(() => getting)).Detail);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheBrowserConnectsAheadAndAsksForAnIcon_ThenStillCatchesTheRedirect()
    {
        // Arrange
        var browser = Provider(Approved, async login =>
        {
            // Browsers open connections before they need them and leave them silent.
            var idle = new TcpClient();
            await idle.ConnectAsync(IPAddress.Loopback, new Uri(login["redirect_uri"]!).Port);
            using var http = new HttpClient();
            await http.GetAsync($"{login["redirect_uri"]}/favicon.ico");
        });

        // Act
        var token = await Client(browser).GetTokenAsync(AuthorizationCode(), "", null, Cancellation);

        // Assert
        Assert.Equal("access", token.AccessToken);
    }

    [Fact]
    public async Task GetTokenAsync_WhenCancelled_ThenStopsListening()
    {
        // Arrange
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var browser = new FakeBrowser(_ => cancel.Cancel());

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(browser).GetTokenAsync(AuthorizationCode(), "", null, cancel.Token));

        // Assert
        using var again = new TcpListener(IPAddress.Loopback, new Uri(LoginOf(browser)["redirect_uri"]!).Port);
        Assert.Null(Record.Exception(again.Start));
    }

    [Fact]
    public async Task GetTokenAsync_WhenNobodyLogsIn_ThenTimesOut()
    {
        // Act
        var getting = Client(new FakeBrowser(_ => _clock.Advance(TimeSpan.FromMinutes(6)))).GetTokenAsync(AuthorizationCode(), "", null, Cancellation);

        // Assert
        Assert.Equal(OAuthProblem.TimedOut, (await Assert.ThrowsAsync<OAuthException>(() => getting)).Problem);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheRedirectPortIsTaken_ThenThrows()
    {
        // Arrange
        using var taken = new TcpListener(IPAddress.Loopback, 0);
        taken.Start();

        // Act
        var getting = Client(Provider(Approved)).GetTokenAsync(AuthorizationCode() with { RedirectPort = ((IPEndPoint)taken.LocalEndpoint).Port }, "", null, Cancellation);

        // Assert
        Assert.Equal(OAuthProblem.PortUnavailable, (await Assert.ThrowsAsync<OAuthException>(() => getting)).Problem);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheRedirectPortIsOutOfRange_ThenThrows()
    {
        // Act
        var getting = Client(Provider(Approved)).GetTokenAsync(AuthorizationCode() with { RedirectPort = 70000 }, "", null, Cancellation);

        // Assert
        Assert.Equal(OAuthProblem.PortUnavailable, (await Assert.ThrowsAsync<OAuthException>(() => getting)).Problem);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheTokenServerRedirects_ThenDoesNotFollow()
    {
        // Act
        var getting = Client().GetTokenAsync(ClientCredentials("/redirects"), "secret", null, Cancellation);

        // Assert
        Assert.Equal(OAuthProblem.Rejected, (await Assert.ThrowsAsync<OAuthException>(() => getting)).Problem);
    }

    [Fact]
    public async Task GetTokenAsync_WhenAnErrorComesWithStatus200_ThenThrowsWithItsError()
    {
        // Act
        var getting = Client().GetTokenAsync(ClientCredentials("/error-with-200"), "secret", null, Cancellation);

        // Assert
        Assert.Equal("bad_verification_code: The code is wrong", (await Assert.ThrowsAsync<OAuthException>(() => getting)).Detail);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheLifetimeIsBeyondReason_ThenGivesTheTokenWithoutAnExpiry()
    {
        // Act
        var token = await Client().GetTokenAsync(ClientCredentials("/huge-expiry"), "secret", null, Cancellation);

        // Assert
        Assert.Null(token.ExpiresAt);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTheAddressIsRefused_ThenLeavesItOutOfTheMessageThatIsLogged()
    {
        // Act
        var getting = Client().GetTokenAsync(ClientCredentials() with { TokenUrl = "http://login.example/token?key=secret" }, "secret", null, Cancellation);

        // Assert
        Assert.DoesNotContain("key=secret", (await Assert.ThrowsAsync<OAuthException>(() => getting)).Message);
    }
}
