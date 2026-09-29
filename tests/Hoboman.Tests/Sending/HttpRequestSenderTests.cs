using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Hoboman.Tests.Sending;

public sealed class HttpRequestSenderTests(EchoServer server) : IClassFixture<EchoServer>, IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    AppFolder Folder => new(_directory);

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    SecretStore Secrets() => new(Folder, NullLogger<SecretStore>.Instance);

    async Task<HttpRequestSender> SenderAsync(bool ignoreCertificateErrors = false)
    {
        var settings = new JsonFile<AppSettings>(Folder.Settings, AppSettings.Default, NullLogger.Instance);
        await settings.SaveAsync(new AppSettings(IgnoreCertificateErrors: ignoreCertificateErrors), Cancellation);
        return new(Secrets(), settings, NullLogger<HttpRequestSender>.Instance);
    }

    ApiRequest Request() => ApiRequest.New() with { Url = server.Http.ToString() };

    async Task<Echo> EchoOf(ApiRequest request, ApiEnvironment? environment = null)
    {
        using var sender = await SenderAsync();
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
    public async Task SendAsync_WhenTheAuthIsBasic_ThenSendsTheUserNameAndSavedPassword()
    {
        // Arrange
        var request = Request() with { Auth = new(AuthKind.Basic, "hobo") };
        await Secrets().SaveAsync(request.Id, "hemmelig", Cancellation);

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
        await Secrets().SaveAsync(request.Id, "token", Cancellation);

        // Act
        var echo = await EchoOf(request);

        // Assert
        Assert.Equal("Bearer token", echo.Headers["Authorization"]);
    }

    [Fact]
    public async Task SendAsync_WhenTheServerAnswers_ThenGivesStatusSizeHeadersAndBody()
    {
        // Arrange
        using var sender = await SenderAsync();

        // Act
        var response = await sender.SendAsync(Request(), null, Cancellation);

        // Assert
        Assert.Equal(200, response.StatusCode);
        Assert.Equal(Encoding.UTF8.GetByteCount(response.Body), response.Size);
        Assert.Contains(new KeyValue("Content-Type", "application/json; charset=utf-8"), response.Headers);
    }

    [Fact]
    public async Task SendAsync_WhenTheServerSetsACookie_ThenDoesNotSendItBack()
    {
        // Arrange
        using var sender = await SenderAsync();
        await sender.SendAsync(Request(), null, Cancellation);

        // Act
        var echo = EchoIn(await sender.SendAsync(Request(), null, Cancellation));

        // Assert
        Assert.DoesNotContain("Cookie", echo.Headers.Keys);
    }

    [Fact]
    public async Task SendAsync_WhenTheCertificateIsUntrusted_ThenFails()
    {
        // Arrange
        using var sender = await SenderAsync();

        // Act
        var sending = sender.SendAsync(ApiRequest.New() with { Url = server.Https.ToString() }, null, Cancellation);

        // Assert
        await Assert.ThrowsAsync<HttpRequestException>(() => sending);
    }

    [Fact]
    public async Task SendAsync_WhenCertificateErrorsAreIgnored_ThenGetsTheAnswer()
    {
        // Arrange
        using var sender = await SenderAsync(ignoreCertificateErrors: true);

        // Act
        var response = await sender.SendAsync(ApiRequest.New() with { Url = server.Https.ToString() }, null, Cancellation);

        // Assert
        Assert.Equal(200, response.StatusCode);
    }
}
