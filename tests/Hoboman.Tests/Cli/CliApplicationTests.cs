using System.Net.Http;
using System.Text;
using System.Text.Json;
using Hoboman.Cli;
using Hoboman.Tests.Auth;
using Hoboman.Tests.Requests;
using Hoboman.Tests.Sending;
using Hoboman.Tests.Workflows;

namespace Hoboman.Tests.Cli;

public sealed class CliApplicationTests(EchoServer server) : IClassFixture<EchoServer>, IDisposable
{
    static readonly OAuthToken _fetched = new("fetched", "Bearer", null, null);

    readonly TemporaryFolder _temporary = new();
    readonly MemoryStream _output = new();
    readonly MemoryStream _error = new();
    HttpClients? _clients;

    AppFolder Folder => new(_temporary.Path);

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    RequestLibrary Library => new(Folder, NullLogger<RequestLibrary>.Instance);

    SettingsStore Settings => new(Folder, NullLogger<SettingsStore>.Instance);

    EnvironmentStore Environments => new(Folder, NullLogger<EnvironmentStore>.Instance);

    SecretStore Secrets => new(Folder, NullLogger<SecretStore>.Instance);

    HistoryStore History => new(Folder, NullLogger<HistoryStore>.Instance);

    WorkflowLibrary Workflows => new(Folder, NullLogger<WorkflowLibrary>.Instance);

    ApiRequest Request => ApiRequest.New() with { Url = $"{server.Http}", Auth = AuthSettings.None };

    string Output => Encoding.UTF8.GetString(_output.ToArray());

    IEnumerable<JsonElement> Events => Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonSerializer.Deserialize<JsonElement>(line));

    string Error => Encoding.UTF8.GetString(_error.ToArray());

    string? Problem
    {
        get
        {
            using var error = JsonDocument.Parse(_error.ToArray());
            return error.RootElement.GetProperty("error").GetString();
        }
    }

    Echo Echo
    {
        get
        {
            using var response = JsonDocument.Parse(_output.ToArray());
            return JsonSerializer.Deserialize<Echo>(response.RootElement.GetProperty("body").GetString()!, JsonSerializerOptions.Web)!;
        }
    }

    public void Dispose()
    {
        _clients?.Dispose();
        _output.Dispose();
        _error.Dispose();
        _temporary.Dispose();
    }

    // Without a token client, a token cannot be fetched, as when the server refuses.
    Task<int> RunAsync(string[] arguments, IRequestSender? sender = null, FakeOAuthClient? oauth = null, TextReader? input = null, CancellationToken? cancellationToken = null)
    {
        sender ??= new HttpRequestSender(Secrets, _clients ??= new(Settings), TimeProvider.System, NullLogger<HttpRequestSender>.Instance);
        var runner = new RequestRunner(sender, Library, History, NullLogger<RequestRunner>.Instance);
        var check = new WorkflowCheck(Workflows, Secrets, NullLogger<WorkflowCheck>.Instance);
        var workflowRunner = new WorkflowRunner(sender, Folder, TimeProvider.System, NullLogger<WorkflowRunner>.Instance);
        var tokens = new UnaskedTokens(oauth ?? new(_ => throw new OAuthException(OAuthProblem.Denied, "access_denied")), Secrets, NullLogger<UnaskedTokens>.Instance);
        return new CliApplication(Library, Settings, Environments, runner, Workflows, check, workflowRunner, tokens, new(_output, _error), new(input ?? TextReader.Null, input is not null))
            .RunAsync(arguments, cancellationToken ?? Cancellation);
    }

    async Task<IReadOnlyList<HistoryFile>> CallsAsync() => await History.ReadAsync(await History.LatestAsync(10, Cancellation), Cancellation);

    static FakeSender Answering(int status = 200) => new(() => Task.FromResult(new ApiResponse(status, "OK", 0, 0, [], "")));

    static FakeSender Failing(Exception exception) => new(() => Task.FromException<ApiResponse>(exception));

    WorkflowRequest Call => new() { Url = $"{server.Http}" };

    // A workflow named Flow with one step per request.
    Task SaveWorkflowAsync(params WorkflowRequest[] requests) =>
        SaveWorkflowAsync(new Workflow { Id = Guid.NewGuid(), Steps = [.. requests.Select(request => new WorkflowStep { Request = request })] });

    Task SaveWorkflowAsync(Workflow workflow) => Workflows.SaveAsync("Flow", workflow, Cancellation);

    JsonElement Event(string type) => Events.Single(element => element.GetProperty("type").GetString() == type);

    [Fact]
    public async Task RunAsync_WhenListing_ThenWritesTheNamesSorted()
    {
        // Arrange
        var last = await Library.SaveAtAsync("z-last", Request, Cancellation);
        var nested = await Library.SaveAtAsync("Nested/b", Request, Cancellation);
        var first = await Library.SaveAtAsync("a-first", Request, Cancellation);

        // Act
        await RunAsync(["list"]);

        // Assert
        Assert.Equal($"{first.Id}\ta-first{Environment.NewLine}{nested.Id}\tNested/b{Environment.NewLine}{last.Id}\tz-last{Environment.NewLine}", Output);
    }

    [Fact]
    public async Task RunAsync_WhenSendingById_ThenSendsThatRequest()
    {
        // Arrange
        var saved = await Library.SaveAtAsync("Folder/Send", Request with { Url = $"{server.Http}by-id" }, Cancellation);

        // Act
        await RunAsync(["send", $"{saved.Id}"]);

        // Assert
        Assert.Equal("/by-id", Echo.Target);
    }

    [Fact]
    public async Task RunAsync_WhenTwoRequestsHaveThePath_ThenAsksForTheId()
    {
        // Arrange
        await Library.SaveAtAsync("a/b/c", Request, Cancellation);
        var folder = await Library.FolderAtAsync("a", Cancellation);
        await Library.SaveAsync(Request with { Name = "b/c", FolderId = folder }, Cancellation);

        // Act
        var exitCode = await RunAsync(["send", "a/b/c"]);

        // Assert
        Assert.Equal((2, "Saved request name is ambiguous. Use its id."), (exitCode, Problem));
    }

    [Fact]
    public async Task RunAsync_WhenListingWorkflows_ThenWritesTheirNamesSorted()
    {
        // Arrange
        var b = await Workflows.SaveAsync("b", new() { Id = Guid.NewGuid() }, Cancellation);
        var a = await Workflows.SaveAsync("a", new() { Id = Guid.NewGuid() }, Cancellation);
        await Library.SaveAtAsync("request", Request, Cancellation);

        // Act
        await RunAsync(["list", "workflows"]);

        // Assert
        Assert.Equal($"{a.Id}\ta{Environment.NewLine}{b.Id}\tb{Environment.NewLine}", Output);
    }

    [Fact]
    public async Task RunAsync_WhenTwoWorkflowsHaveTheName_ThenAsksForTheId()
    {
        // Arrange
        await Workflows.SaveAsync("Flow", new() { Id = Guid.NewGuid() }, Cancellation);
        await Workflows.SaveAsync("Flow", new() { Id = Guid.NewGuid() }, Cancellation);

        // Act
        var exitCode = await RunAsync(["run", "Flow"]);

        // Assert
        Assert.Equal((2, "Workflow name is ambiguous. Use its id."), (exitCode, Problem));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_WhenListingAndAFileCannotBeRead_ThenListsItByItsIdAlone(bool workflow)
    {
        // Arrange
        var id = Guid.NewGuid();
        var file = workflow ? Path.Combine(Folder.Workflows, $"{id}", "workflow.json") : Path.Combine(Folder.Requests, $"{id}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "{", Cancellation);

        // Act
        await RunAsync(workflow ? ["list", "workflows"] : ["list"]);

        // Assert
        Assert.Equal($"{id}\t{Environment.NewLine}", Output);
    }

    [Fact]
    public async Task RunAsync_WhenSendingAPathInAnotherCase_ThenSendsThatRequest()
    {
        // Arrange
        await Library.SaveAtAsync("Folder/Send", Request with { Url = $"{server.Http}by-path" }, Cancellation);

        // Act
        await RunAsync(["send", "folder/send"]);

        // Assert
        Assert.Equal("/by-path", Echo.Target);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_WhenRunByIdOrByTheNameInAnotherCase_ThenRunsTheWorkflow(bool byId)
    {
        // Arrange
        var workflow = await Workflows.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Call }] }, Cancellation);

        // Act
        var exitCode = await RunAsync(["run", byId ? $"{workflow.Id}" : "flow"]);

        // Assert
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task RunAsync_WhenSentWithOut_ThenWritesTheBytesToTheFileAndNotTheBody()
    {
        // Arrange
        var file = Path.Combine(_temporary.Path, "svar.pdf");

        // Act
        var exitCode = await RunAsync(["send", "GET", $"{server.Http}file", "--out", file]);

        // Assert
        using var response = JsonDocument.Parse(_output.ToArray());
        Assert.Equal((0, Convert.ToHexString(EchoServer.File), file, false),
            (exitCode, Convert.ToHexString(await File.ReadAllBytesAsync(file, Cancellation)), response.RootElement.GetProperty("file").GetString(), response.RootElement.TryGetProperty("body", out _)));
    }

    [Fact]
    public async Task RunAsync_WhenTheOutFileCannotBeWritten_ThenSaysSo()
    {
        // Act
        var exitCode = await RunAsync(["send", "GET", $"{server.Http}file", "--out", Path.Combine(_temporary.Path, "mangler", "svar.pdf")]);

        // Assert
        Assert.Equal((2, "Output file could not be written."), (exitCode, Problem));
    }

    [Fact]
    public async Task RunAsync_WhenListing_ThenReadsNoOtherData()
    {
        // Arrange
        await Library.SaveAtAsync("a", Request, Cancellation);
        foreach (var path in new[] { Folder.Settings, Folder.Environments, Folder.Secrets, Folder.RequestOrder })
        {
            await File.WriteAllTextAsync(path, "{", Cancellation);
        }

        // Act
        var exitCode = await RunAsync(["list"]);

        // Assert
        Assert.Equal(0, exitCode);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("--help")]
    [InlineData("--version")]
    public async Task RunAsync_WhenNothingIsSent_ThenCreatesNoData(string command)
    {
        // Act
        var exitCode = await RunAsync([command]);

        // Assert
        Assert.Equal((0, false), (exitCode, Directory.Exists(Folder.Root)));
    }

    [Theory]
    [InlineData("--help", "Commands:")]
    [InlineData("--version", "1.0.0")]
    public async Task RunAsync_WhenHelpOrVersionIsAsked_ThenWritesItAsText(string option, string expected)
    {
        // Act
        await RunAsync([option]);

        // Assert
        Assert.Contains(expected, Output);
    }

    [Fact]
    public async Task RunAsync_WhenAResponseArrives_ThenWritesItAsOneLineOfJson()
    {
        // Arrange
        var sender = new FakeSender(() => Task.FromResult(new ApiResponse(503, "Reason", 12, 42, [new("X", "one"), new("X", "two")], "Ærø")));

        // Act
        await RunAsync(["send", "GET", "https://localhost/"], sender);

        // Assert
        Assert.Equal($$"""{"status":503,"reason":"Reason","elapsedMs":12,"size":42,"headers":[{"name":"X","value":"one"},{"name":"X","value":"two"}],"body":"Ærø"}{{Environment.NewLine}}""", Output);
    }

    [Theory]
    [InlineData(200, 0)]
    [InlineData(299, 0)]
    [InlineData(400, 1)]
    [InlineData(503, 1)]
    public async Task RunAsync_WhenAResponseArrives_ThenExitsByItsStatus(int status, int expected)
    {
        // Act
        var exitCode = await RunAsync(["send", "GET", "https://localhost/"], Answering(status));

        // Assert
        Assert.Equal(expected, exitCode);
    }

    [Fact]
    public async Task RunAsync_WhenSending_ThenRemembersTheCallAsFromTheCli()
    {
        // Act
        await RunAsync(["send", "GET", "https://localhost/"], Answering());

        // Assert
        Assert.Equal(HistorySource.Cli, Assert.Single(await CallsAsync()).Entry.Source);
    }

    [Fact]
    public async Task RunAsync_WhenSendingDirectlyWithoutABody_ThenSendsNone()
    {
        // Act
        await RunAsync(["send", "GET", "https://localhost/"], Answering());

        // Assert
        Assert.Equal(BodyKind.None, Assert.Single(await CallsAsync()).Entry.Request.BodyKind);
    }

    [Theory]
    [InlineData("--json", "application/json; charset=utf-8")]
    [InlineData("--text", "text/plain; charset=utf-8")]
    public async Task RunAsync_WhenSendingABody_ThenSendsItAsItsKind(string option, string contentType)
    {
        // Act
        await RunAsync(["send", "POST", $"{server.Http}", option, "{\"city\":\"Ærø\"}"]);

        // Assert
        Assert.Equal(("{\"city\":\"Ærø\"}", contentType), (Echo.Body, Echo.Headers["Content-Type"]));
    }

    [Fact]
    public async Task RunAsync_WhenTheBodyStartsWithTwoAts_ThenSendsOne()
    {
        // Act
        await RunAsync(["send", "POST", $"{server.Http}", "--text", "@@literal"]);

        // Assert
        Assert.Equal("@literal", Echo.Body);
    }

    [Fact]
    public async Task RunAsync_WhenTheBodyIsAFile_ThenSendsItsTextAsItIs()
    {
        // Arrange
        Directory.CreateDirectory(Folder.Root);
        var path = Path.Combine(Folder.Root, "body.txt");
        await File.WriteAllTextAsync(path, "@Ærø 🚀", new UTF8Encoding(true), Cancellation);

        // Act
        await RunAsync(["send", "POST", $"{server.Http}", "--text", $"@{path}"]);

        // Assert
        Assert.Equal("@Ærø 🚀", Echo.Body);
    }

    [Fact]
    public async Task RunAsync_WhenAHeaderValueHoldsAColon_ThenKeepsIt()
    {
        // Act
        await RunAsync(["send", "GET", $"{server.Http}", "-H", " X : a:b "]);

        // Assert
        Assert.Equal("a:b", Echo.Headers["X"]);
    }

    [Fact]
    public async Task RunAsync_WhenAHeaderIsRepeated_ThenSendsBoth()
    {
        // Act
        await RunAsync(["send", "GET", $"{server.Http}", "-H", "X: one", "-H", "X: two"]);

        // Assert
        Assert.Equal("one, two", Echo.Headers["X"]);
    }

    [Fact]
    public async Task RunAsync_WhenSendingDirectly_ThenSendsNoAuth()
    {
        // Act
        await RunAsync(["send", "GET", $"{server.Http}"]);

        // Assert
        Assert.False(Echo.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task RunAsync_WhenSettingsHoldAnEnvironmentId_ThenUsesThatEnvironment()
    {
        // Arrange
        var chosen = new ApiEnvironment("Dev", []) { Id = Guid.NewGuid() };
        await Environments.SaveAsync([new("Dev", []) { Id = Guid.NewGuid() }, chosen], Cancellation);
        await Settings.UpdateAsync(_ => new(EnvironmentId: chosen.Id), Cancellation);
        var sender = Answering();

        // Act
        await RunAsync(["send", "GET", "https://localhost/"], sender);

        // Assert
        Assert.Equal(chosen.Id, sender.Environment?.Id);
    }

    [Fact]
    public async Task RunAsync_WhenSettingsHoldTheIdOfARemovedEnvironment_ThenFailsBeforeSending()
    {
        // Arrange
        await Environments.SaveAsync([new("Dev", []) { Id = Guid.NewGuid() }], Cancellation);
        await Settings.UpdateAsync(_ => new(EnvironmentId: Guid.NewGuid()), Cancellation);
        var sender = Answering();

        // Act
        var exitCode = await RunAsync(["send", "GET", "https://localhost/"], sender);

        // Assert
        Assert.Equal((2, "Selected environment was not found.", true), (exitCode, Problem, sender.Request is null));
    }

    [Fact]
    public async Task RunAsync_WhenAnEnvironmentIsGiven_ThenUsesIt()
    {
        // Arrange
        var selected = new ApiEnvironment("Selected", []) { Id = Guid.NewGuid() };
        await Settings.UpdateAsync(_ => new(EnvironmentId: selected.Id), Cancellation);
        await Environments.SaveAsync([selected, new("Other", []) { Id = Guid.NewGuid() }], Cancellation);
        var sender = Answering();

        // Act
        await RunAsync(["send", "GET", "https://localhost/", "--env", "Other"], sender);

        // Assert
        Assert.Equal("Other", sender.Environment?.Name);
    }

    [Fact]
    public async Task RunAsync_WhenNoEnvironmentIsSelected_ThenUsesNoneWithoutReadingTheEnvironments()
    {
        // Arrange
        Directory.CreateDirectory(Folder.Root);
        await File.WriteAllTextAsync(Folder.Environments, "{", Cancellation);
        var sender = Answering();

        // Act
        await RunAsync(["send", "GET", "https://localhost/"], sender);

        // Assert
        Assert.Same(ApiEnvironment.None, sender.Environment);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_WhenTheEnvironmentIsUnknown_ThenFailsBeforeSending(bool selected)
    {
        // Arrange
        await Environments.SaveAsync([new("Dev", [])], Cancellation);
        await Settings.UpdateAsync(_ => new(EnvironmentId: selected ? Guid.NewGuid() : null), Cancellation);
        string[] options = selected ? [] : ["--env", "dev"];
        var sender = Answering();

        // Act
        var exitCode = await RunAsync(["send", "GET", "https://localhost/", .. options], sender);

        // Assert
        Assert.Equal((2, "Selected environment was not found.", true), (exitCode, Problem, sender.Request is null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_WhenVariablesComeFromEverySource_ThenOptionsWinOverTheFileOverTheEnvironment(bool fileFirst)
    {
        // Arrange
        await Environments.SaveAsync([new("Dev", [new("a", "saved"), new("b", "saved"), new("c", "saved")])], Cancellation);
        Directory.CreateDirectory(Folder.Root);
        var path = Path.Combine(Folder.Root, "variables.json");
        await File.WriteAllTextAsync(path, """{"a":"file","b":"file"}""", Cancellation);
        string[] options = fileFirst ? ["--vars", path, "--var", "a=option"] : ["--var", "a=option", "--vars", path];

        // Act
        await RunAsync(["send", "GET", $"{server.Http}", "--env", "Dev", "-H", "X: {{a}}/{{b}}/{{c}}", .. options]);

        // Assert
        Assert.Equal("option/file/saved", Echo.Headers["X"]);
    }

    [Fact]
    public async Task RunAsync_WhenAValueFromAResponseIsGivenToTheNextCall_ThenSendsIt()
    {
        // Arrange
        await Library.SaveAtAsync("Create", Request with { Method = "POST", BodyKind = BodyKind.Json, Body = """{"id":42}""" }, Cancellation);
        await Library.SaveAtAsync("Next", Request with { Url = $"{server.Http}items/{{{{id}}}}" }, Cancellation);
        await RunAsync(["send", "Create"]);
        using var input = new StringReader(Echo.Body);
        _output.SetLength(0);

        // Act
        await RunAsync(["send", "Next", "--vars", "-"], input: input);

        // Assert
        Assert.Equal("/items/42", Echo.Target);
    }

    [Theory]
    [InlineData(new[] { "--var", "missing-equals" }, null)]
    [InlineData(new[] { "--vars", "-" }, null)]
    [InlineData(new[] { "--vars", "-" }, "{")]
    [InlineData(new[] { "--vars", "-" }, "[]")]
    public async Task RunAsync_WhenTheVariablesAreInvalid_ThenFailsBeforeSending(string[] options, string? json)
    {
        // Arrange
        using var input = json is null ? null : new StringReader(json);

        // Act
        var exitCode = await RunAsync(["send", "GET", "https://localhost/", .. options], Answering(), input: input);

        // Assert
        Assert.Equal((2, "Invalid variable input.", 0), (exitCode, Problem, (await CallsAsync()).Count));
    }

    [Theory]
    [InlineData(new[] { "send", "missing" }, "Saved request could not be loaded.")]
    [InlineData(new[] { "send", "5b8a7e1c-3f2d-4c9b-8a6e-1d2c3b4a5f60" }, "Saved request could not be loaded.")]
    [InlineData(new[] { "send", "POST", "https://localhost/", "--text", "@missing" }, "Input could not be read.")]
    [InlineData(new[] { "send", "GET", "https://localhost/", "--vars", "missing" }, "Input could not be read.")]
    [InlineData(new[] { "send", "GET", "https://localhost/", "-H", "missing-colon" }, "Invalid request input.")]
    [InlineData(new[] { "send", "GET", "https://localhost/", "-H", ": value" }, "Invalid request input.")]
    [InlineData(new[] { "send", "GET", "https://localhost/", "-H", "X: a\r\nY: b" }, "Invalid request input.")]
    [InlineData(new[] { "send", "GET", "https://localhost/", "--unknown", "value" }, "Invalid command arguments. Use --help for usage.")]
    public async Task RunAsync_WhenTheInputIsInvalid_ThenFailsBeforeSending(string[] arguments, string problem)
    {
        // Act
        var exitCode = await RunAsync(arguments, Answering());

        // Assert
        Assert.Equal((2, problem, 0), (exitCode, Problem, (await CallsAsync()).Count));
    }

    [Theory]
    [InlineData(false, "Saved request file is not valid.")]
    [InlineData(true, "Environment settings could not be read.")]
    public async Task RunAsync_WhenAFileIsCorrupt_ThenFailsBeforeSending(bool environments, string problem)
    {
        // Arrange
        var saved = await Library.SaveAtAsync("Send", Request, Cancellation);
        await File.WriteAllTextAsync(environments ? Folder.Environments : Path.Combine(Folder.Requests, $"{saved.Id}.json"), "{", Cancellation);

        // Act
        var exitCode = await RunAsync(["send", $"{saved.Id}", "--env", "Dev"]);

        // Assert
        Assert.Equal((2, problem, 0), (exitCode, Problem, (await CallsAsync()).Count));
    }

    [Theory]
    [InlineData("-url")]
    [InlineData("not-a-url")]
    public async Task RunAsync_WhenTheUrlIsInvalid_ThenSaysSoAndRemembersTheAttempt(string url)
    {
        // Act
        await RunAsync(["send", "--", "GET", url]);

        // Assert
        Assert.Equal(("Invalid request URL.", 1), (Problem, (await CallsAsync()).Count));
    }

    [Theory]
    [InlineData(true, "Network request failed.")]
    [InlineData(false, "Request failed.")]
    public async Task RunAsync_WhenSendingFails_ThenSaysSoWithoutTheDetails(bool network, string problem)
    {
        // Arrange
        var sender = Failing(network ? new HttpRequestException("secret") : new InvalidOperationException("secret"));

        // Act
        var exitCode = await RunAsync(["send", "GET", "https://localhost/"], sender);

        // Assert
        Assert.Equal((2, problem), (exitCode, Problem));
    }

    [Fact]
    public async Task RunAsync_WhenTheCallTimesOut_ThenSaysSo()
    {
        // Act
        await RunAsync(["send", "GET", "https://localhost/"], Failing(new TaskCanceledException()));

        // Assert
        Assert.Equal("Request timed out.", Problem);
    }

    [Fact]
    public async Task RunAsync_WhenTheCallerCancels_ThenSaysSoAndRemembersNothing()
    {
        // Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new FakeSender(() =>
        {
            sending.SetResult();
            return new TaskCompletionSource<ApiResponse>().Task;
        });
        var running = RunAsync(["send", "GET", "https://localhost/"], sender, cancellationToken: cancellation.Token);
        await sending.Task.WaitAsync(Cancellation);

        // Act
        await cancellation.CancelAsync();
        await running;

        // Assert
        Assert.Equal(("Request was cancelled.", 0), (Problem, (await CallsAsync()).Count));
    }

    [Fact]
    public async Task RunAsync_WhenCancelledWhileReadingVariables_ThenSaysSo()
    {
        // Arrange
        var input = new PendingReader();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var running = RunAsync(["send", "GET", "https://localhost/", "--vars", "-"], Answering(), input: input, cancellationToken: cancellation.Token);
        await input.Started.Task.WaitAsync(Cancellation);

        // Act
        await cancellation.CancelAsync();
        await running;

        // Assert
        Assert.Equal("Request was cancelled.", Problem);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_WhenTheOAuthTokenIsMissingOrExpired_ThenFetchesOneAndSends(bool expired)
    {
        // Arrange
        var request = Request with { Auth = new(AuthKind.OAuth2) };
        await Library.SaveAtAsync("Send", request, Cancellation);
        if (expired)
        {
            await Secrets.SaveAsync(request.Id, SecretKind.OAuthToken, new OAuthToken("expired", "Bearer", DateTimeOffset.UtcNow.AddMinutes(-1), null).ToJson(), Cancellation);
        }

        // Act
        await RunAsync(["send", "Send"], oauth: new(_ => Task.FromResult(_fetched)));

        // Assert
        Assert.Equal("Bearer fetched", Echo.Headers["Authorization"]);
    }

    [Fact]
    public async Task RunAsync_WhenATokenIsFetched_ThenFetchesItWithTheSavedEnvironment()
    {
        // Arrange
        var oauth = new FakeOAuthClient(_ => Task.FromResult(_fetched));
        await Library.SaveAtAsync("Send", Request with { Auth = new(AuthKind.OAuth2) }, Cancellation);
        await Environments.SaveAsync([new("Dev", [new("clientId", "saved")])], Cancellation);

        // Act
        await RunAsync(["send", "Send", "--env", "Dev", "--var", "clientId=temporary"], oauth: oauth);

        // Assert
        Assert.Equal("saved", oauth.Asked?.Environment?.Resolve("{{clientId}}"));
    }

    [Fact]
    public async Task RunAsync_WhenNoTokenCanBeFetched_ThenAsksForItToBeFetchedInHoboman()
    {
        // Arrange
        await Library.SaveAtAsync("Send", Request with { Auth = new(AuthKind.OAuth2) }, Cancellation);

        // Act
        var exitCode = await RunAsync(["send", "Send"]);

        // Assert
        Assert.Equal((2, "Fetch a new OAuth token in Hoboman before sending this request."), (exitCode, Problem));
    }

    [Fact]
    public async Task RunAsync_WhenEveryStepSucceeds_ThenWritesEventsAndExitsZero()
    {
        // Arrange
        await SaveWorkflowAsync(Call);

        // Act
        var exitCode = await RunAsync(["run", "Flow"]);

        // Assert
        Assert.Equal((0, "run.started step.started step.finished run.finished"), (exitCode, string.Join(" ", Events.Select(element => element.GetProperty("type").GetString()))));
    }

    [Fact]
    public async Task RunAsync_WhenAStepFails_ThenEndsWithRunFinishedAndExitsOne()
    {
        // Arrange
        await SaveWorkflowAsync(Call, Call);

        // Act
        var exitCode = await RunAsync(["run", "Flow"], Answering(500));

        // Assert
        Assert.Equal((1, "run.finished", "Failed"), (exitCode, Events.Last().GetProperty("type").GetString(), Event("run.finished").GetProperty("outcome").GetString()));
    }

    [Fact]
    public async Task RunAsync_WhenRunning_ThenWritesTheSameLinesToTheRunFile()
    {
        // Arrange
        await SaveWorkflowAsync(Call);

        // Act
        await RunAsync(["run", "Flow"]);

        // Assert
        Assert.Equal(Output, await File.ReadAllTextAsync(Event("run.started").GetProperty("runFile").GetString()!, Cancellation));
    }

    [Fact]
    public async Task RunAsync_WhenRunning_ThenKeepsTheCallsOutOfTheHistory()
    {
        // Arrange
        await SaveWorkflowAsync(Call, Call);

        // Act
        await RunAsync(["run", "Flow"], Answering());

        // Assert
        Assert.Empty(await CallsAsync());
    }

    [Fact]
    public async Task RunAsync_WhenTheCheckFails_ThenWritesNothingToStdoutAndExitsTwo()
    {
        // Arrange
        await SaveWorkflowAsync(Call with { Url = $"{server.Http}{{{{token}}}}" });

        // Act
        var exitCode = await RunAsync(["run", "Flow"]);

        // Assert
        Assert.Equal((2, "", false), (exitCode, Output, Directory.Exists(Folder.Runs)));
        Assert.Equal("""{"error":"Workflow cannot run.","problems":[{"kind":"UnknownName","step":0,"detail":"token"}]}""", Error.TrimEnd());
    }

    [Fact]
    public async Task RunAsync_WhenAParameterIsUnknown_ThenExitsTwo()
    {
        // Arrange
        await SaveWorkflowAsync(Call);

        // Act
        var exitCode = await RunAsync(["run", "Flow", "--param", "unknown=1"], Answering());

        // Assert
        Assert.Equal((2, "Workflow cannot run.", 0), (exitCode, Problem, (await CallsAsync()).Count));
    }

    [Fact]
    public async Task RunAsync_WhenParametersComeFromTheFileAndTheOptions_ThenTheOptionsWinAndTheFileKeepsItsTypes()
    {
        // Arrange
        await SaveWorkflowAsync(new Workflow { Id = Guid.NewGuid(), Parameters = [new("a"), new("b")] });
        using var input = new StringReader("""{"a":"file","b":42}""");

        // Act
        await RunAsync(["run", "Flow", "--param", "a=option", "--params", "-"], input: input);

        // Assert
        Assert.Equal("""{"a":"option","b":42}""", Event("run.started").GetProperty("parameters").GetRawText());
    }

    [Theory]
    [InlineData(new[] { "--param", "missing-equals" }, null)]
    [InlineData(new[] { "--params", "-" }, "[]")]
    public async Task RunAsync_WhenTheParametersAreInvalid_ThenFailsBeforeStarting(string[] options, string? json)
    {
        // Arrange
        await SaveWorkflowAsync(Call);
        using var input = json is null ? null : new StringReader(json);

        // Act
        var exitCode = await RunAsync(["run", "Flow", .. options], Answering(), input: input);

        // Assert
        Assert.Equal((2, "Invalid parameter input.", ""), (exitCode, Problem, Output));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("5b8a7e1c-3f2d-4c9b-8a6e-1d2c3b4a5f60")]
    public async Task RunAsync_WhenTheWorkflowCannotBeLoaded_ThenFailsBeforeStarting(string name)
    {
        // Act
        var exitCode = await RunAsync(["run", name]);

        // Assert
        Assert.Equal((2, "Workflow could not be loaded."), (exitCode, Problem));
    }

    [Fact]
    public async Task RunAsync_WhenTheSavedRequestFileIsInvalid_ThenTellsTheFileThePathAndTheLineWithoutTheValue()
    {
        // Arrange
        var id = Guid.NewGuid();
        var path = Path.Combine(Folder.Requests, $"{id}.json");
        Directory.CreateDirectory(Folder.Requests);
        await File.WriteAllTextAsync(path, "{\n  \"headers\": \"secret-value\"\n}", Cancellation);

        // Act
        var exitCode = await RunAsync(["send", $"{id}"]);

        // Assert
        Assert.Equal((2, JsonSerializer.Serialize(new { error = "Saved request file is not valid.", file = path, path = "$.headers", line = 2 }, CompactJson.Options)), (exitCode, Error.TrimEnd()));
    }

    [Theory]
    [InlineData("{\n  \"id\": \"secret-value\"\n}", "$.id", 2)]
    [InlineData("{\n  \"steps\": [\n    {\n      \"request\": \"secret-value\"\n    }\n  ]\n}", "$.steps[0].request", 4)]
    public async Task RunAsync_WhenTheWorkflowFileIsInvalid_ThenTellsTheFileThePathAndTheLineWithoutTheValue(string json, string jsonPath, int line)
    {
        // Arrange
        var id = Guid.NewGuid();
        var path = Path.Combine(Folder.Workflows, $"{id}", "workflow.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, json, Cancellation);

        // Act
        var exitCode = await RunAsync(["run", $"{id}"]);

        // Assert
        Assert.Equal((2, JsonSerializer.Serialize(new { error = "Workflow file is not valid.", file = path, path = jsonPath, line }, CompactJson.Options)), (exitCode, Error.TrimEnd()));
    }

    [Fact]
    public async Task RunAsync_WhenTheEnvironmentIsUnknownForAWorkflow_ThenFailsBeforeStarting()
    {
        // Arrange
        await SaveWorkflowAsync(Call);

        // Act
        var exitCode = await RunAsync(["run", "Flow", "--env", "missing"], Answering());

        // Assert
        Assert.Equal((2, "Selected environment was not found.", 0), (exitCode, Problem, (await CallsAsync()).Count));
    }

    [Fact]
    public async Task RunAsync_WhenATokenIsFetchedInARun_ThenFetchesItWithTheSavedEnvironment()
    {
        // Arrange
        var oauth = new FakeOAuthClient(_ => Task.FromResult(_fetched));
        await SaveWorkflowAsync(new Workflow { Id = Guid.NewGuid(), Parameters = [new("clientId")], Steps = [new() { Request = Call with { Id = Guid.NewGuid(), Auth = new(AuthKind.OAuth2) } }] });
        await Environments.SaveAsync([new("Dev", [new("clientId", "saved")])], Cancellation);

        // Act
        await RunAsync(["run", "Flow", "--env", "Dev", "--param", "clientId=temporary"], oauth: oauth);

        // Assert
        Assert.Equal("saved", oauth.Asked?.Environment?.Resolve("{{clientId}}"));
    }

    [Fact]
    public async Task RunAsync_WhenTheCallerCancelsARun_ThenEndsWithACancelledRunAndExitsOne()
    {
        // Arrange
        await SaveWorkflowAsync(Call);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new FakeSender(() =>
        {
            sending.SetResult();
            return new TaskCompletionSource<ApiResponse>().Task;
        });
        var running = RunAsync(["run", "Flow"], sender, cancellationToken: cancellation.Token);
        await sending.Task.WaitAsync(Cancellation);

        // Act
        await cancellation.CancelAsync();
        var exitCode = await running;

        // Assert
        Assert.Equal((1, "Cancelled"), (exitCode, Event("run.finished").GetProperty("outcome").GetString()));
    }
}
