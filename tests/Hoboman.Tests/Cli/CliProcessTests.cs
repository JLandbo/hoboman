namespace Hoboman.Tests.Cli;

// What only shows when the CLI runs as a program of its own: where it finds its data, its streams, its exit code and what it writes.
public sealed class CliProcessTests(CliTestServer server) : IClassFixture<CliTestServer>, IDisposable
{
    readonly CliProcess _process = new();

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    RequestLibrary Library => new(_process.Folder, NullLogger<RequestLibrary>.Instance);

    SecretStore Secrets => new(_process.Folder, NullLogger<SecretStore>.Instance);

    ApiRequest Request => ApiRequest.New() with { Url = $"{server.Http}", Auth = AuthSettings.None };

    public void Dispose() => _process.Dispose();

    [Fact]
    public async Task RunAsync_WhenListingFromAnotherDirectory_ThenListsTheRequestsNextToTheProgram()
    {
        // Arrange
        await Library.SaveAsync("Ærø", Request, Cancellation);
        Directory.CreateDirectory(Path.Combine(_process.WorkingDirectory, "requests"));
        await File.WriteAllTextAsync(Path.Combine(_process.WorkingDirectory, "requests", "Wrong.json"), "{}", Cancellation);

        // Act
        var result = await _process.RunAsync(["list"]);

        // Assert
        Assert.Equal($"Ærø{Environment.NewLine}", result.Output);
    }

    [Fact]
    public async Task RunAsync_WhenListing_ThenChangesNoFiles()
    {
        // Arrange
        await Library.SaveAsync("Send", Request, Cancellation);
        var original = _process.Snapshot(includeHistory: true);

        // Act
        await _process.RunAsync(["list"]);

        // Assert
        _process.AssertUnchanged(original, includeHistory: true);
    }

    [Fact]
    public async Task RunAsync_WhenSending_ThenChangesNoFilesButTheHistory()
    {
        // Arrange
        await Library.SaveAsync("Send", Request, Cancellation);
        var original = _process.Snapshot();

        // Act
        await _process.RunAsync(["send", "Send"]);

        // Assert
        _process.AssertUnchanged(original);
    }

    [Fact]
    public async Task RunAsync_WhenAResponseArrives_ThenWritesItAsUtf8()
    {
        // Act
        var result = await _process.RunAsync(["send", "POST", $"{server.Http}", "--text", "Ærø 🚀"]);

        // Assert
        Assert.Equal((0, "Ærø 🚀"), (result.ExitCode, result.Echo().Body));
    }

    [Fact]
    public async Task RunAsync_WhenTheBodyFileIsRelative_ThenReadsItFromTheWorkingDirectory()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_process.WorkingDirectory, "body.txt"), "working directory", Cancellation);
        await File.WriteAllTextAsync(Path.Combine(_process.Folder.Root, "body.txt"), "program folder", Cancellation);

        // Act
        var result = await _process.RunAsync(["send", "POST", $"{server.Http}", "--text", "@body.txt"]);

        // Assert
        Assert.Equal("working directory", result.Echo().Body);
    }

    [Fact]
    public async Task RunAsync_WhenTheResponseIsLargerThanThePipe_ThenWritesAllOfIt()
    {
        // Act
        var result = await _process.RunAsync(["send", "GET", $"{server.Http}large"]);

        // Assert
        Assert.Equal(CliTestServer.LargeResponse, result.Body());
    }

    [Fact]
    public async Task RunAsync_WhenVariablesComeFromTheInput_ThenUsesThem()
    {
        // Act
        var result = await _process.RunAsync(["send", "GET", $"{server.Http}", "-H", "X: {{value}}", "--vars", "-"], """{"value":"from input"}""");

        // Assert
        Assert.Equal("from input", result.Echo().Headers["X"]);
    }

    [Fact]
    public async Task RunAsync_WhenTheConnectionIsLost_ThenSaysTheNetworkFailed()
    {
        // Act
        var result = await _process.RunAsync(["send", "GET", $"{server.Http}abort"]);

        // Assert
        Assert.Equal((2, "Network request failed."), (result.ExitCode, result.Problem()));
    }

    [Fact]
    public async Task RunAsync_WhenTheOAuthTokenIsMissing_ThenFetchesOneWithTheClientCredentials()
    {
        // Arrange
        var request = Request with { Auth = new(AuthKind.OAuth2, OAuth: new() { TokenUrl = $"{server.Http}token", ClientId = "cli" }) };
        await Library.SaveAsync("Send", request, Cancellation);
        await Secrets.SaveAsync(request.Id, SecretKind.ClientSecret, "secret", Cancellation);

        // Act
        var result = await _process.RunAsync(["send", "Send"]);

        // Assert
        Assert.Equal("Bearer fetched", result.Echo().Headers["Authorization"]);
    }
}
