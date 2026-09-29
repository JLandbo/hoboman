using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Hoboman.Tests.Sending;

public sealed class HttpRequestSenderTests(EchoServer server) : IClassFixture<EchoServer>, IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    HttpClients? _clients;

    AppFolder Folder => new(_directory);

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _clients?.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    SecretStore Secrets() => new(Folder, NullLogger<SecretStore>.Instance);

    JsonFile<AppSettings> Settings() => new(Folder.Settings, AppSettings.Default, NullLogger.Instance);

    async Task<HttpRequestSender> SenderAsync(bool ignoreCertificateErrors = false, ILogger<HttpRequestSender>? logger = null)
    {
        await Settings().SaveAsync(new AppSettings(IgnoreCertificateErrors: ignoreCertificateErrors), Cancellation);
        return new(Secrets(), _clients ??= new(Settings()), logger ?? NullLogger<HttpRequestSender>.Instance);
    }

    ApiRequest Request() => ApiRequest.New() with { Url = server.Http.ToString() };

    async Task<Echo> EchoOf(ApiRequest request, ApiEnvironment? environment = null)
    {
        var sender = await SenderAsync();
        return EchoIn(await sender.SendAsync(request, environment, Cancellation));
    }

    static Echo EchoIn(ApiResponse response)
    {
        return JsonSerializer.Deserialize<Echo>(response.Body, JsonSerializerOptions.Web)!;
    }

    [Fact]
    public async Task SendAsync_WhenTheUrlHasVariablesAndQuery_ThenSendsTheResolvedAddress()
    {
        // Arrange
        var environment = new ApiEnvironment("Test", [new("base", server.Http.ToString())]);
        var request = Request() with { Url = "{{base}}items?x=1", Query = [new("a", "b c"), new("off", "1", Enabled: false)] };

        // Act
        var echo = await EchoOf(request, environment);

        // Assert
        Assert.Equal("/items?x=1&a=b%20c", echo.Target);
    }

    [Fact]
    public async Task SendAsync_WhenTheUrlHasAFragment_ThenStillSendsTheQuery()
    {
        // Arrange
        var request = Request() with { Url = $"{server.Http}items#top", Query = [new("page", "2")] };

        // Act
        var echo = await EchoOf(request);

        // Assert
        Assert.Equal("/items?page=2", echo.Target);
    }

    [Fact]
    public async Task SendAsync_WhenTheBodyIsJson_ThenSendsItAsJson()
    {
        // Arrange
        var request = Request() with { Method = "POST", BodyKind = BodyKind.Json, Body = """{"name":"Hobo"}""" };

        // Act
        var echo = await EchoOf(request);

        // Assert
        Assert.Equal("POST", echo.Method);
        Assert.Equal("""{"name":"Hobo"}""", echo.Body);
        Assert.Equal("application/json; charset=utf-8", echo.Headers["Content-Type"]);
    }

    [Fact]
    public async Task SendAsync_WhenHeadersAreGiven_ThenSendsTheEnabledOnesResolved()
    {
        // Arrange
        var environment = new ApiEnvironment("Test", [new("value", "resolved")]);
        var request = Request() with { Headers = [new("X-On", "{{value}}"), new("X-Off", "1", Enabled: false)] };

        // Act
        var echo = await EchoOf(request, environment);

        // Assert
        Assert.Equal("resolved", echo.Headers["X-On"]);
        Assert.DoesNotContain("X-Off", echo.Headers.Keys);
    }

    [Fact]
    public async Task SendAsync_WhenAContentTypeHeaderIsGiven_ThenItReplacesTheDefault()
    {
        // Arrange
        var request = Request() with { Method = "POST", BodyKind = BodyKind.Text, Body = "<a />", Headers = [new("Content-Type", "application/xml")] };

        // Act
        var echo = await EchoOf(request);

        // Assert
        Assert.Equal("application/xml", echo.Headers["Content-Type"]);
    }

    [Fact]
    public async Task SendAsync_WhenAHeaderHasNoName_ThenSkipsIt()
    {
        // Arrange
        var request = Request() with { Method = "POST", BodyKind = BodyKind.Text, Body = "hej", Headers = [new("", "")] };

        // Act
        var echo = await EchoOf(request);

        // Assert
        Assert.Equal("hej", echo.Body);
    }

    [Fact]
    public async Task SendAsync_WhenAHeaderNameIsInvalid_ThenThrows()
    {
        // Arrange
        var sender = await SenderAsync();

        // Act
        var sending = sender.SendAsync(Request() with { Headers = [new("X Key", "1")] }, null, Cancellation);

        // Assert
        await Assert.ThrowsAsync<InvalidHeaderException>(() => sending);
    }

    [Fact]
    public async Task SendAsync_WhenTheMethodIsInvalid_ThenThrows()
    {
        // Arrange
        var sender = await SenderAsync();

        // Act
        var sending = sender.SendAsync(Request() with { Method = "GE T" }, null, Cancellation);

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
        var echo = await EchoOf(request);

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
        var echo = await EchoOf(request);

        // Assert
        Assert.Equal("Bearer token", echo.Headers["Authorization"]);
    }

    [Fact]
    public async Task SendAsync_WhenTheTokenIsMissing_ThenThrows()
    {
        // Arrange
        var sender = await SenderAsync();

        // Act
        var sending = sender.SendAsync(Request() with { Auth = new(AuthKind.Bearer) }, null, Cancellation);

        // Assert
        Assert.Equal(SecretKind.Token, (await Assert.ThrowsAsync<MissingSecretException>(() => sending)).Kind);
    }

    [Fact]
    public async Task SendAsync_WhenTheServerAnswers_ThenGivesStatusSizeHeadersAndBody()
    {
        // Arrange
        var sender = await SenderAsync();

        // Act
        var response = await sender.SendAsync(Request(), null, Cancellation);

        // Assert
        Assert.Equal(200, response.StatusCode);
        Assert.Equal(Encoding.UTF8.GetByteCount(response.Body), response.Size);
        Assert.Contains(new ResponseHeader("Content-Type", "application/json; charset=utf-8"), response.Headers);
    }

    [Fact]
    public async Task SendAsync_WhenTheServerSetsACookie_ThenDoesNotSendItBack()
    {
        // Arrange
        var sender = await SenderAsync();
        await sender.SendAsync(Request(), null, Cancellation);

        // Act
        var echo = EchoIn(await sender.SendAsync(Request(), null, Cancellation));

        // Assert
        Assert.DoesNotContain("Cookie", echo.Headers.Keys);
    }

    [Fact]
    public async Task SendAsync_WhenTheUrlHoldsSecrets_ThenLeavesThemOutOfTheLog()
    {
        // Arrange
        var logger = new RecordingLogger<HttpRequestSender>();
        var sender = await SenderAsync(logger: logger);

        // Act
        await sender.SendAsync(Request() with { Url = $"http://bob:pa55@{server.Http.Authority}/items?api_key=k3y" }, null, Cancellation);

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

        // Act
        var sending = sender.SendAsync(Request() with { Url = $"{server.Http}slow" }, null, cancellation.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task SendAsync_WhenTheCertificateIsUntrusted_ThenFails()
    {
        // Arrange
        var sender = await SenderAsync();

        // Act
        var sending = sender.SendAsync(ApiRequest.New() with { Url = server.Https.ToString() }, null, Cancellation);

        // Assert
        await Assert.ThrowsAsync<HttpRequestException>(() => sending);
    }

    [Fact]
    public async Task SendAsync_WhenCertificateErrorsAreIgnored_ThenGetsTheAnswer()
    {
        // Arrange
        var sender = await SenderAsync(ignoreCertificateErrors: true);

        // Act
        var response = await sender.SendAsync(ApiRequest.New() with { Url = server.Https.ToString() }, null, Cancellation);

        // Assert
        Assert.Equal(200, response.StatusCode);
    }
}
