using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Hoboman.Tests.Sending;

public sealed class HttpRequestSenderTests(EchoServer server) : IClassFixture<EchoServer>, IDisposable
{
    readonly TemporaryFolder _temporary = new();
    HttpClients? _clients;

    AppFolder Folder => new(_temporary.Path);

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _clients?.Dispose();
        _temporary.Dispose();
    }

    SecretStore Secrets() => new(Folder, NullLogger<SecretStore>.Instance);

    SettingsStore Settings() => new(Folder, NullLogger<SettingsStore>.Instance);

    async Task<HttpRequestSender> SenderAsync(bool ignoreCertificateErrors = false, ILogger<HttpRequestSender>? logger = null, TimeProvider? clock = null)
    {
        await Settings().UpdateAsync(_ => new(IgnoreCertificateErrors: ignoreCertificateErrors), Cancellation);
        return new(Secrets(), _clients ??= new(Settings()), clock ?? TimeProvider.System, logger ?? NullLogger<HttpRequestSender>.Instance);
    }

    ApiRequest Request() => ApiRequest.New() with { Url = server.Http.ToString() };

    async Task<Echo> SendAndEchoAsync(ApiRequest request, ApiEnvironment? environment = null)
    {
        var sender = await SenderAsync();
        return EchoOf(await sender.SendAsync(request, OwnAuth(request), environment, Cancellation));
    }

    static AuthSource OwnAuth(ApiRequest request) => new(request.Id, request.Auth);

    static Task<ApiResponse> SendAsync(HttpRequestSender sender, ApiRequest request, CancellationToken cancellationToken) => sender.SendAsync(request, OwnAuth(request), null, cancellationToken);

    static Echo EchoOf(ApiResponse response) => JsonSerializer.Deserialize<Echo>(response.Body, JsonSerializerOptions.Web)!;

    [Fact]
    public async Task SendAsync_WhenTheBodyIsEmpty_ThenSendsNoBodyAndNoContentType()
    {
        // Act
        var echo = await SendAndEchoAsync(Request() with { Method = "GET", BodyKind = BodyKind.Json, Body = "" });

        // Assert
        Assert.Equal(("", false), (echo.Body, echo.Headers.ContainsKey("Content-Type")));
    }

    [Fact]
    public async Task SendAsync_WhenTheAnswerIsAFile_ThenKeepsItsBytesAsSent()
    {
        // Arrange
        var sender = await SenderAsync();

        // Act
        var response = await SendAsync(sender, ApiRequest.New() with { Url = new Uri(server.Http, "file").ToString() }, Cancellation);

        // Assert
        Assert.Equal(EchoServer.File, response.Bytes);
        Assert.Equal(EchoServer.File.Length, response.Size);
    }

    [Fact]
    public async Task SendAsync_WhenTheUrlHasVariablesAndQuery_ThenSendsTheResolvedAddress()
    {
        // Arrange
        var environment = new ApiEnvironment("Test", [new("base", server.Http.ToString())]);
        var request = Request() with { Url = "{{base}}items?x=1", Query = [new("a", "b c"), new("off", "1", Enabled: false)] };

        // Act
        var echo = await SendAndEchoAsync(request, environment);

        // Assert
        Assert.Equal("/items?x=1&a=b%20c", echo.Target);
    }

    [Fact]
    public async Task SendAsync_WhenTheUrlHasAFragment_ThenStillSendsTheQuery()
    {
        // Arrange
        var request = Request() with { Url = $"{server.Http}items#top", Query = [new("page", "2")] };

        // Act
        var echo = await SendAndEchoAsync(request);

        // Assert
        Assert.Equal("/items?page=2", echo.Target);
    }

    [Fact]
    public async Task SendAsync_WhenTheMethodIsGiven_ThenUsesIt()
    {
        // Act
        var echo = await SendAndEchoAsync(Request() with { Method = "PUT" });

        // Assert
        Assert.Equal("PUT", echo.Method);
    }

    [Fact]
    public async Task SendAsync_WhenTheBodyIsJson_ThenSendsIt()
    {
        // Arrange
        var request = Request() with { Method = "POST", BodyKind = BodyKind.Json, Body = """{"name":"Hobo"}""" };

        // Act
        var echo = await SendAndEchoAsync(request);

        // Assert
        Assert.Equal("""{"name":"Hobo"}""", echo.Body);
    }

    [Fact]
    public async Task SendAsync_WhenTheBodyIsJson_ThenSaysItIsJson()
    {
        // Arrange
        var request = Request() with { Method = "POST", BodyKind = BodyKind.Json, Body = """{"name":"Hobo"}""" };

        // Act
        var echo = await SendAndEchoAsync(request);

        // Assert
        Assert.Equal("application/json; charset=utf-8", echo.Headers["Content-Type"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendAsync_WhenPropertiesAreChosenForBase64_ThenResolvesVariablesOnlyWhenEnabled(bool useVariables)
    {
        // Arrange
        var request = Request() with
        {
            Method = "POST",
            BodyKind = BodyKind.Json,
            Body = """{"html":"<p>{{place}}</p>","data":{"name":"Hobo"},"rendered":false}""",
            UseEnvironmentVariablesInBody = useVariables,
            Base64 = new() { Encode = ["$.html", "$.data"] },
        };

        // Act
        var echo = await SendAndEchoAsync(request, new ApiEnvironment("dev", [new("place", "Ærø")]));

        // Assert
        using var body = JsonDocument.Parse(echo.Body);
        Assert.Equal(useVariables ? "<p>Ærø</p>" : "<p>{{place}}</p>", Base64Text.Decode(body.RootElement.GetProperty("html").GetString()!));
        Assert.Equal("""{"name":"Hobo"}""", Base64Text.Decode(body.RootElement.GetProperty("data").GetString()!));
        Assert.False(body.RootElement.GetProperty("rendered").GetBoolean());
    }

    [Fact]
    public async Task SendAsync_WhenTheBodyIsXml_ThenSendsItAsXml()
    {
        // Arrange
        var request = Request() with { Method = "POST", BodyKind = BodyKind.Xml, Body = "<order id=\"{{id}}\" />", UseEnvironmentVariablesInBody = true };

        // Act
        var echo = await SendAndEchoAsync(request, new ApiEnvironment("dev", [new("id", "17")]));

        // Assert
        Assert.Equal(("<order id=\"17\" />", "application/xml; charset=utf-8"), (echo.Body, echo.Headers["Content-Type"]));
    }

    [Fact]
    public async Task SendAsync_WhenTheWholeJsonBodyIsChosenForBase64_ThenSendsItEncodedAsJson()
    {
        // Arrange
        var request = Request() with { Method = "POST", BodyKind = BodyKind.Json, Body = """{"a": 1}""", Base64 = new() { Encode = [JsonPath.Root] } };

        // Act
        var echo = await SendAndEchoAsync(request);

        // Assert
        Assert.Equal((Base64Text.Encode("""{"a": 1}"""), "application/json; charset=utf-8"), (echo.Body, echo.Headers["Content-Type"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendAsync_WhenTheWholeTextBodyIsChosenForBase64_ThenResolvesVariablesOnlyWhenEnabled(bool useVariables)
    {
        // Arrange
        var request = Request() with { Method = "POST", BodyKind = BodyKind.Text, Body = "Hej {{place}}", UseEnvironmentVariablesInBody = useVariables, Base64 = new() { Encode = [JsonPath.Root] } };

        // Act
        var echo = await SendAndEchoAsync(request, new ApiEnvironment("dev", [new("place", "Ærø")]));

        // Assert
        Assert.Equal((Base64Text.Encode(useVariables ? "Hej Ærø" : "Hej {{place}}"), "text/plain; charset=utf-8"), (echo.Body, echo.Headers["Content-Type"]));
    }

    [Theory]
    [InlineData(BodyKind.Json, "{\"html\":\"<h1>{{a}}</h1>\"}", false)]
    [InlineData(BodyKind.Json, "{\"html\":\"<h1>{{a}}</h1>\"}", true)]
    [InlineData(BodyKind.Xml, "<name>{{a}}</name>", false)]
    [InlineData(BodyKind.Xml, "<name>{{a}}</name>", true)]
    [InlineData(BodyKind.Text, "Hej {{a}}", false)]
    [InlineData(BodyKind.Text, "Hej {{a}}", true)]
    public async Task SendAsync_WhenBodyVariablesAreToggled_ThenOnlyResolvesThemWhenEnabled(BodyKind kind, string body, bool useVariables)
    {
        var request = Request() with { Method = "POST", BodyKind = kind, Body = body, UseEnvironmentVariablesInBody = useVariables };

        var echo = await SendAndEchoAsync(request, new("dev", [new("a", "Jacob")]));

        Assert.Equal(useVariables ? body.Replace("{{a}}", "Jacob", StringComparison.Ordinal) : body, echo.Body);
    }

    [Fact]
    public async Task SendAsync_WhenBodyVariablesAreDisabledByDefault_ThenStillResolvesTheOtherFields()
    {
        var request = Request() with
        {
            Method = "POST",
            Url = "{{base}}items",
            Query = [new("{{a}}", "{{a}}")],
            Headers = [new("X-{{a}}", "{{a}}")],
            BodyKind = BodyKind.Text,
            Body = "{{a}}",
            Auth = new(AuthKind.Bearer),
        };
        await Secrets().SaveAsync(request.Id, SecretKind.Token, "{{a}}", Cancellation);

        var echo = await SendAndEchoAsync(request, new("dev", [new("base", $"{server.Http}"), new("a", "resolved")]));

        Assert.False(request.UseEnvironmentVariablesInBody);
        Assert.Equal("{{a}}", echo.Body);
        Assert.Equal("/items?resolved=resolved", echo.Target);
        Assert.Equal("resolved", echo.Headers["X-resolved"]);
        Assert.Equal("Bearer resolved", echo.Headers["Authorization"]);
    }

    [Fact]
    public async Task SendAsync_WhenATextBodyHasPropertiesChosenForBase64_ThenSendsItAsItIs()
    {
        // Arrange
        var request = Request() with { Method = "POST", BodyKind = BodyKind.Text, Body = "not json", Base64 = new() { Encode = ["$.html"] } };

        // Act
        var echo = await SendAndEchoAsync(request);

        // Assert
        Assert.Equal("not json", echo.Body);
    }

    [Fact]
    public async Task SendAsync_WhenAChosenPropertyIsNotInTheBody_ThenRejectsIt()
    {
        // Arrange
        var request = Request() with { Method = "POST", BodyKind = BodyKind.Json, Body = """{"a": 1}""", Base64 = new() { Encode = ["$.html"] } };

        // Act
        var sending = SendAndEchoAsync(request);

        // Assert
        await Assert.ThrowsAsync<MissingBase64PathException>(() => sending);
    }

    [Fact]
    public async Task SendAsync_WhenPropertiesAreChosenForBase64AndTheBodyIsNotJson_ThenRejectsIt()
    {
        // Arrange
        var request = Request() with { Method = "POST", BodyKind = BodyKind.Json, Body = "not json", Base64 = new() { Encode = ["$.html"] } };

        // Act
        var sending = SendAndEchoAsync(request);

        // Assert
        await Assert.ThrowsAsync<InvalidBase64RequestBodyException>(() => sending);
    }

    [Fact]
    public async Task SendAsync_WhenAHeaderHasAVariable_ThenSendsItResolved()
    {
        // Arrange
        var environment = new ApiEnvironment("Test", [new("value", "resolved")]);
        var request = Request() with { Headers = [new("X-On", "{{value}}")] };

        // Act
        var echo = await SendAndEchoAsync(request, environment);

        // Assert
        Assert.Equal("resolved", echo.Headers["X-On"]);
    }

    [Fact]
    public async Task SendAsync_WhenAHeaderIsDisabled_ThenLeavesItOut()
    {
        // Arrange
        var request = Request() with { Headers = [new("X-Off", "1", Enabled: false)] };

        // Act
        var echo = await SendAndEchoAsync(request);

        // Assert
        Assert.DoesNotContain("X-Off", echo.Headers.Keys);
    }

    [Fact]
    public async Task SendAsync_WhenAContentTypeHeaderIsGiven_ThenItReplacesTheDefault()
    {
        // Arrange
        var request = Request() with { Method = "POST", BodyKind = BodyKind.Text, Body = "<a />", Headers = [new("Content-Type", "application/xml")] };

        // Act
        var echo = await SendAndEchoAsync(request);

        // Assert
        Assert.Equal("application/xml", echo.Headers["Content-Type"]);
    }

    [Fact]
    public async Task SendAsync_WhenAHeaderHasNoName_ThenSkipsIt()
    {
        // Arrange
        var request = Request() with { Method = "POST", BodyKind = BodyKind.Text, Body = "hej", Headers = [new("", "")] };

        // Act
        var echo = await SendAndEchoAsync(request);

        // Assert
        Assert.Equal("hej", echo.Body);
    }

    [Fact]
    public async Task SendAsync_WhenAHeaderNameIsInvalid_ThenThrows()
    {
        // Arrange
        var sender = await SenderAsync();

        // Act
        var sending = SendAsync(sender, Request() with { Headers = [new("X Key", "1")] }, Cancellation);

        // Assert
        await Assert.ThrowsAsync<InvalidHeaderException>(() => sending);
    }

    [Fact]
    public async Task SendAsync_WhenTheMethodIsInvalid_ThenThrows()
    {
        // Arrange
        var sender = await SenderAsync();

        // Act
        var sending = SendAsync(sender, Request() with { Method = "GE T" }, Cancellation);

        // Assert
        await Assert.ThrowsAsync<InvalidMethodException>(() => sending);
    }

    [Fact]
    public async Task SendAsync_WhenTheAuthIsBasic_ThenSendsTheUserNameAndSavedPassword()
    {
        // Arrange
        var request = Request() with { Auth = new(AuthKind.Basic, "hobo") };
        await Secrets().SaveAsync(request.Id, SecretKind.Password, "hemmelig", Cancellation);

        // Act
        var echo = await SendAndEchoAsync(request);

        // Assert
        Assert.Equal($"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes("hobo:hemmelig"))}", echo.Headers["Authorization"]);
    }

    [Fact]
    public async Task SendAsync_WhenTheAuthIsBearer_ThenSendsTheSavedToken()
    {
        // Arrange
        var request = Request() with { Auth = new(AuthKind.Bearer) };
        await Secrets().SaveAsync(request.Id, SecretKind.Token, "token", Cancellation);

        // Act
        var echo = await SendAndEchoAsync(request);

        // Assert
        Assert.Equal("Bearer token", echo.Headers["Authorization"]);
    }

    [Fact]
    public async Task SendAsync_WhenTheAuthComesFromAFolder_ThenSendsTheFoldersToken()
    {
        // Arrange
        var folder = Guid.NewGuid();
        await Secrets().SaveAsync(folder, SecretKind.Token, "folder token", Cancellation);
        var sender = await SenderAsync();

        // Act
        var response = await sender.SendAsync(Request(), new(folder, new(AuthKind.Bearer)), null, Cancellation);

        // Assert
        Assert.Equal("Bearer folder token", EchoOf(response).Headers["Authorization"]);
    }

    [Fact]
    public async Task SendAsync_WhenTheTokenIsMissing_ThenThrows()
    {
        // Arrange
        var sender = await SenderAsync();

        // Act
        var sending = SendAsync(sender, Request() with { Auth = new(AuthKind.Bearer) }, Cancellation);

        // Assert
        Assert.Equal(SecretKind.Token, (await Assert.ThrowsAsync<MissingSecretException>(() => sending)).Kind);
    }

    [Fact]
    public async Task SendAsync_WhenTheAuthIsOAuthWithoutAToken_ThenThrows()
    {
        // Arrange
        var sender = await SenderAsync();

        // Act
        var sending = SendAsync(sender, Request() with { Auth = new(AuthKind.OAuth2) }, Cancellation);

        // Assert
        Assert.Equal(SecretKind.OAuthToken, (await Assert.ThrowsAsync<MissingSecretException>(() => sending)).Kind);
    }

    [Fact]
    public async Task SendAsync_WhenTheAuthIsOAuth_ThenSendsTheSavedTokenAsBearer()
    {
        // Arrange
        var request = Request() with { Auth = new(AuthKind.OAuth2) };
        await Secrets().SaveAsync(request.Id, SecretKind.OAuthToken, new OAuthToken("access", "bearer", null, null).ToJson(), Cancellation);

        // Act
        var echo = await SendAndEchoAsync(request);

        // Assert
        Assert.Equal("Bearer access", echo.Headers["Authorization"]);
    }

    [Fact]
    public async Task SendAsync_WhenTheOAuthTokenIsForTheChosenEnvironment_ThenSendsIt()
    {
        // Arrange
        var request = Request() with { Auth = new(AuthKind.OAuth2) };
        var environment = new ApiEnvironment("Dev", []) { Id = Guid.NewGuid() };
        await Secrets().SaveAsync(request.Id, SecretKind.OAuthToken, environment.Id, new OAuthToken("dev access", "Bearer", null, null).ToJson(), Cancellation);

        // Act
        var echo = await SendAndEchoAsync(request, environment);

        // Assert
        Assert.Equal("Bearer dev access", echo.Headers["Authorization"]);
    }

    [Fact]
    public async Task SendAsync_WhenTheOAuthTokenIsForAnotherEnvironmentWithTheSameName_ThenThrows()
    {
        // Arrange
        var request = Request() with { Auth = new(AuthKind.OAuth2) };
        await Secrets().SaveAsync(request.Id, SecretKind.OAuthToken, Guid.NewGuid(), new OAuthToken("dev access", "Bearer", null, null).ToJson(), Cancellation);
        var sender = await SenderAsync();

        // Act
        var sending = sender.SendAsync(request, OwnAuth(request), new ApiEnvironment("Dev", []) { Id = Guid.NewGuid() }, Cancellation);

        // Assert
        Assert.Equal(SecretKind.OAuthToken, (await Assert.ThrowsAsync<MissingSecretException>(() => sending)).Kind);
    }

    [Fact]
    public async Task SendAsync_WhenTheOAuthTokenComesFromAFolder_ThenSendsTheFoldersToken()
    {
        // Arrange
        var folder = Guid.NewGuid();
        await Secrets().SaveAsync(folder, SecretKind.OAuthToken, new OAuthToken("folder access", "Bearer", null, null).ToJson(), Cancellation);
        var sender = await SenderAsync();

        // Act
        var response = await sender.SendAsync(Request(), new(folder, new(AuthKind.OAuth2)), null, Cancellation);

        // Assert
        Assert.Equal("Bearer folder access", EchoOf(response).Headers["Authorization"]);
    }

    [Fact]
    public async Task SendAsync_WhenTheOAuthTokenHasExpired_ThenThrows()
    {
        // Arrange
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var request = Request() with { Auth = new(AuthKind.OAuth2) };
        await Secrets().SaveAsync(request.Id, SecretKind.OAuthToken, new OAuthToken("access", "Bearer", clock.GetUtcNow(), null).ToJson(), Cancellation);
        var sender = await SenderAsync(clock: clock);

        // Act
        var sending = SendAsync(sender, request, Cancellation);

        // Assert
        await Assert.ThrowsAsync<ExpiredTokenException>(() => sending);
    }

    [Fact]
    public async Task SendAsync_WhenTheServerAnswers_ThenGivesTheStatus()
    {
        // Arrange
        var sender = await SenderAsync();

        // Act
        var response = await SendAsync(sender, Request(), Cancellation);

        // Assert
        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_WhenTheServerAnswers_ThenGivesTheHeaders()
    {
        // Arrange
        var sender = await SenderAsync();

        // Act
        var response = await SendAsync(sender, Request(), Cancellation);

        // Assert
        Assert.Contains(new ResponseHeader("Content-Type", "application/json; charset=utf-8"), response.Headers);
    }

    [Fact]
    public async Task SendAsync_WhenTheAnswerHasLettersBeyondAscii_ThenGivesTheSizeInBytes()
    {
        // Arrange
        var sender = await SenderAsync();

        // Act
        var response = await SendAsync(sender, Request() with { Method = "POST", BodyKind = BodyKind.Text, Body = "Ærø" }, Cancellation);

        // Assert
        Assert.Equal(Encoding.UTF8.GetByteCount(response.Body), response.Size);
    }

    [Fact]
    public async Task SendAsync_WhenTheServerSetsACookie_ThenDoesNotSendItBack()
    {
        // Arrange
        var sender = await SenderAsync();
        await SendAsync(sender, Request(), Cancellation);

        // Act
        var echo = EchoOf(await SendAsync(sender, Request(), Cancellation));

        // Assert
        Assert.DoesNotContain("Cookie", echo.Headers.Keys);
    }

    [Fact]
    public async Task SendAsync_WhenTheUrlHoldsSecrets_ThenLeavesThemOutOfTheLog()
    {
        // Arrange
        var logger = new RecordingLogger<HttpRequestSender>();
        var sender = await SenderAsync(logger: logger);
        var request = Request() with { Url = $"http://bob:pa55@{server.Http.Authority}/items?api_key=k3y" };

        // Act
        await SendAsync(sender, request, Cancellation);

        // Assert
        Assert.NotEmpty(logger.Entries);
        Assert.DoesNotContain(logger.Entries, entry => entry.Message.Contains("pa55") || entry.Message.Contains("k3y"));
    }

    [Fact]
    public async Task SendAsync_WhenCancelledWhileWaiting_ThenDoesNotLogItAsAFailure()
    {
        // Arrange
        var logger = new RecordingLogger<HttpRequestSender>();
        var sender = await SenderAsync(logger: logger);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var request = Request() with { Url = $"{server.Http}slow" };

        // Act
        await Record.ExceptionAsync(() => SendAsync(sender, request, cancellation.Token));

        // Assert
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task SendAsync_WhenTheCertificateIsUntrusted_ThenFails()
    {
        // Arrange
        var sender = await SenderAsync();

        // Act
        var sending = SendAsync(sender, ApiRequest.New() with { Url = server.Https.ToString() }, Cancellation);

        // Assert
        await Assert.ThrowsAsync<HttpRequestException>(() => sending);
    }

    [Fact]
    public async Task SendAsync_WhenCertificateErrorsAreIgnored_ThenGetsTheAnswer()
    {
        // Arrange
        var sender = await SenderAsync(ignoreCertificateErrors: true);

        // Act
        var response = await SendAsync(sender, ApiRequest.New() with { Url = server.Https.ToString() }, Cancellation);

        // Assert
        Assert.Equal(200, response.StatusCode);
    }
}
