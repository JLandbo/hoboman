using System.Text.Json;
using Hoboman.Tests.Requests;
using Hoboman.Tests.Sending;
using Hoboman.Tests.Workflows;

namespace Hoboman.Tests.Cli;

// What only shows when the CLI runs as a program of its own: where it finds its data, its streams, its exit code and what it writes.
public sealed class CliProcessTests(CliTestServer server) : IClassFixture<CliTestServer>, IDisposable
{
    readonly CliProcess _process = new();

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    RequestLibrary Library => new(_process.Folder, NullLogger<RequestLibrary>.Instance);

    SecretStore Secrets => new(_process.Folder, NullLogger<SecretStore>.Instance);

    ApiRequest Request => ApiRequest.New() with { Url = $"{server.Http}", Auth = AuthSettings.None };

    WorkflowLibrary Workflows => new(_process.Folder, NullLogger<WorkflowLibrary>.Instance);

    Task SaveWorkflowAsync(params WorkflowStep[] steps) => Workflows.SaveAsync("Flow", new Workflow { Id = Guid.NewGuid(), Variables = [new("target")], Steps = steps }, Cancellation);

    WorkflowRequest Call => new() { Url = $"{server.Http}" };

    static WorkflowStep Step(WorkflowRequest request, params WorkflowSave[] saves) => new() { Request = request, Saves = saves };

    public void Dispose() => _process.Dispose();

    [Fact]
    public async Task RunAsync_WhenListingFromAnotherDirectory_ThenListsTheRequestsNextToTheProgram()
    {
        // Arrange
        var saved = await Library.SaveAtAsync("Ærø", Request, Cancellation);
        Directory.CreateDirectory(Path.Combine(_process.WorkingDirectory, "requests"));
        await File.WriteAllTextAsync(Path.Combine(_process.WorkingDirectory, "requests", $"{Guid.NewGuid()}.json"), """{ "name": "Wrong", "url": "" }""", Cancellation);

        // Act
        var result = await _process.RunAsync(["list"]);

        // Assert
        Assert.Equal($"{saved.Id}\tÆrø{Environment.NewLine}", result.Output);
    }

    [Fact]
    public async Task RunAsync_WhenListing_ThenChangesNoFiles()
    {
        // Arrange
        await Library.SaveAtAsync("Send", Request, Cancellation);
        var original = _process.Snapshot(includeOutput: true);

        // Act
        await _process.RunAsync(["list"]);

        // Assert
        _process.AssertUnchanged(original, includeOutput: true);
    }

    [Fact]
    public async Task RunAsync_WhenSending_ThenChangesNoFilesButTheHistory()
    {
        // Arrange
        await Library.SaveAtAsync("Send", Request, Cancellation);
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
        await Library.SaveAtAsync("Send", request, Cancellation);
        await Secrets.SaveAsync(request.Id, SecretKind.ClientSecret, "secret", Cancellation);

        // Act
        var result = await _process.RunAsync(["send", "Send"]);

        // Assert
        Assert.Equal("Bearer fetched", result.Echo().Headers["Authorization"]);
    }

    [Fact]
    public async Task RunAsync_WhenAWorkflowRuns_ThenGivesTheSavedValueToTheNextStepAndExitsZero()
    {
        // Arrange
        await SaveWorkflowAsync(
            Step(Call with { Url = $"{server.Http}first" }, new WorkflowSave("target", "$.target")),
            Step(Call with { Headers = [new("X", "{{target}}")] }));

        // Act
        var result = await _process.RunAsync(["run", "Flow"]);

        // Assert
        var lastStep = JsonSerializer.Deserialize<JsonElement>(result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^2]);
        var echo = JsonSerializer.Deserialize<Echo>(lastStep.GetProperty("body").GetString()!, JsonSerializerOptions.Web)!;
        Assert.Equal((0, "/first"), (result.ExitCode, echo.Headers["X"]));
    }

    [Fact]
    public async Task RunAsync_WhenAWorkflowRuns_ThenChangesNoFilesButTheRuns()
    {
        // Arrange
        await SaveWorkflowAsync(Step(Call));
        var original = _process.Snapshot();

        // Act
        await _process.RunAsync(["run", "Flow"]);

        // Assert
        _process.AssertUnchanged(original);
        Assert.Single(Directory.EnumerateFiles(_process.Folder.Runs, "*.jsonl", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RunAsync_WhenAStepOfAWorkflowFails_ThenEndsWithRunFinishedAndExitsOne()
    {
        // Arrange
        await SaveWorkflowAsync(Step(Call with { Url = $"{server.Http}abort" }), Step(Call));

        // Act
        var result = await _process.RunAsync(["run", "Flow"]);

        // Assert
        Assert.Equal((1, true), (result.ExitCode, result.Output.TrimEnd('\n').Split('\n')[^1].StartsWith("""{"type":"run.finished","outcome":"Failed",""", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task RunAsync_WhenAWorkflowCannotRun_ThenExitsTwoWithoutSendingOrARunFile()
    {
        // Arrange
        await SaveWorkflowAsync(new WorkflowStep());
        var requestCount = server.RequestCount;

        // Act
        var result = await _process.RunAsync(["run", "Flow"]);

        // Assert
        Assert.Equal((2, "Workflow cannot run.", "", false, requestCount), (result.ExitCode, result.Problem(), result.Output, Directory.Exists(_process.Folder.Runs), server.RequestCount));
    }
}
