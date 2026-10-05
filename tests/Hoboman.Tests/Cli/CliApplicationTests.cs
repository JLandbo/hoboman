using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    // Below the temporary folder, so a test can keep files of its own outside Hoboman's.
    AppFolder Folder => new(Path.Combine(_temporary.Path, "data"));

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    RequestLibrary Library => new(Folder, NullLogger<RequestLibrary>.Instance);

    SettingsStore Settings => new(Folder, NullLogger<SettingsStore>.Instance);

    EnvironmentStore Environments => new(Folder, NullLogger<EnvironmentStore>.Instance);

    SecretStore Secrets => new(Folder, NullLogger<SecretStore>.Instance);

    HistoryStore History => new(Folder, NullLogger<HistoryStore>.Instance);

    WorkflowLibrary Workflows => new(Folder, NullLogger<WorkflowLibrary>.Instance);

    CredentialStore Credentials => new(Folder, Secrets, NullLogger<CredentialStore>.Instance);

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
    Task<int> RunAsync(string[] arguments, IRequestSender? sender = null, FakeOAuthClient? oauth = null, TextReader? input = null, CancellationToken? cancellationToken = null) =>
        new CliFactory(Folder, Secrets, sender ?? new HttpRequestSender(Secrets, _clients ??= new(Settings), TimeProvider.System, NullLogger<HttpRequestSender>.Instance),
                oauth ?? new(_ => throw new OAuthException(OAuthProblem.Denied, "access_denied")))
            .Create(_output, _error, input ?? TextReader.Null, input is not null)
            .RunAsync(arguments, cancellationToken ?? Cancellation);

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
    public async Task RunAsync_WhenTheOutFileExists_ThenSendsNothingAndKeepsTheFile()
    {
        // Arrange
        Directory.CreateDirectory(_temporary.Path);
        var file = Path.Combine(_temporary.Path, "svar.pdf");
        await File.WriteAllTextAsync(file, "keep", Cancellation);
        var sender = Answering();

        // Act
        var exitCode = await RunAsync(["send", "GET", "https://localhost/", "--out", file], sender);

        // Assert
        Assert.Equal((2, "Output file already exists.", true, "keep"), (exitCode, Problem, sender.Request is null, await File.ReadAllTextAsync(file, Cancellation)));
    }

    [Fact]
    public async Task RunAsync_WhenTheOutFileIsInHobomansFolder_ThenSendsNothing()
    {
        // Arrange
        var sender = Answering();

        // Act
        var exitCode = await RunAsync(["send", "GET", "https://localhost/", "--out", Path.Combine(Folder.Root, "requests", "x.json")], sender);

        // Assert
        Assert.Equal((2, "Hoboman's own files cannot be used.", true), (exitCode, Problem, sender.Request is null));
    }

    [Theory]
    [InlineData(@"\\?\{0}\x.txt")]
    [InlineData(@"\\.\{0}\x.txt")]
    [InlineData(@"\\localhost\{1}$\{2}\x.txt")]
    [InlineData(@"{0}::$INDEX_ALLOCATION\x.txt")]
    public async Task RunAsync_WhenTheOutFileIsNoPlainPathOnADrive_ThenSendsNothing(string form)
    {
        // Arrange
        Directory.CreateDirectory(Folder.Root);
        var root = Path.GetFullPath(Folder.Root);
        var sender = Answering();

        // Act
        var exitCode = await RunAsync(["send", "GET", "https://localhost/", "--out", string.Format(form, root, root[0], root[3..])], sender);

        // Assert
        Assert.Equal((2, @"Only a plain path on a drive, such as C:\folder\file, can be used.", true, false),
            (exitCode, Problem, sender.Request is null, File.Exists(Path.Combine(root, "x.txt"))));
    }

    [Theory]
    [InlineData("send", "POST", "https://localhost/", "--text", "@{0}")]
    [InlineData("send", "GET", "https://localhost/", "--vars", "{0}")]
    [InlineData("run", "Flow", "--params", "{0}")]
    [InlineData("new", "request", ".", "Ping", "--json", "@{0}")]
    [InlineData("update", "Dev", "--file", "{0}")]
    public async Task RunAsync_WhenAnInputFileIsInHobomansFolder_ThenRefusesToReadIt(params string[] arguments)
    {
        // Arrange
        await Environments.SaveAsync([new("Dev", [])], Cancellation);

        // Act
        var exitCode = await RunAsync([.. arguments.Select(argument => string.Format(argument, Folder.Environments))]);

        // Assert
        Assert.Equal((2, "Hoboman's own files cannot be used."), (exitCode, Problem));
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
        Directory.CreateDirectory(_temporary.Path);
        var path = Path.Combine(_temporary.Path, "body.txt");
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
        string[] options = selected ? [] : ["--env", "Prod"];
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
        Directory.CreateDirectory(_temporary.Path);
        var path = Path.Combine(_temporary.Path, "variables.json");
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

    JsonElement Result => JsonSerializer.Deserialize<JsonElement>(Output);

    [Fact]
    public async Task RunAsync_WhenANewRequestIsMade_ThenItIsInTheFolderAndInheritsItsAuth()
    {
        // Arrange
        await Library.FolderAtAsync("Shop", Cancellation);

        // Act
        var exitCode = await RunAsync(["new", "request", "Shop", "GET users/{id}", "--url", "https://dev.local/users", "--json", "{}"]);

        // Assert
        var request = await Library.LoadAtAsync("Shop/GET users/{id}", Cancellation);
        Assert.Equal((0, "https://dev.local/users", AuthKind.Inherit, BodyKind.Json), (exitCode, request!.Url, request.Auth.Kind, request.BodyKind));
        Assert.Equal(($"{request.Id}", "Shop/GET users/{id}"), (Result.GetProperty("id").GetString(), Result.GetProperty("path").GetString()));
    }

    [Fact]
    public async Task RunAsync_WhenANewFolderIsMadeAtTheTop_ThenListsIt()
    {
        // Act
        await RunAsync(["new", "folder", ".", "Shop"]);
        var id = Result.GetProperty("id").GetString();
        _output.SetLength(0);
        await RunAsync(["list", "folders"]);

        // Assert
        Assert.Equal($"{id}\tShop{Environment.NewLine}", Output);
    }

    [Fact]
    public async Task RunAsync_WhenANewWorkflowIsMade_ThenItHasTheName()
    {
        // Act
        await RunAsync(["new", "workflow", "Ordre: sync"]);

        // Assert
        Assert.Equal("Ordre: sync", (await Workflows.LoadAsync(Guid.Parse(Result.GetProperty("id").GetString()!), Cancellation))!.Name);
    }

    [Theory]
    [InlineData("new", "folder", ".", "  ")]
    [InlineData("new", "folder", "Missing", "Shop")]
    public async Task RunAsync_WhenTheNameOrFolderIsWrong_ThenMakesNothing(params string[] arguments)
    {
        // Act
        var exitCode = await RunAsync(arguments);

        // Assert
        Assert.Equal(2, exitCode);
        Assert.Empty((await Library.LoadAllAsync(Cancellation)).Folders);
    }

    [Fact]
    public async Task RunAsync_WhenARequestIsRenamed_ThenHasTheNewName()
    {
        // Arrange
        await Library.SaveAtAsync("Shop/Ping", Request, Cancellation);

        // Act
        var exitCode = await RunAsync(["rename", "shop/ping", "Pong"]);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal(["Shop/Pong"], await Library.PathsAsync(Cancellation));
    }

    [Fact]
    public async Task RunAsync_WhenAFolderIsRenamed_ThenHasTheNewName()
    {
        // Arrange
        await Library.FolderAtAsync("Shop/Ping", Cancellation);

        // Act
        var exitCode = await RunAsync(["rename", "shop/ping", "Pong"]);

        // Assert
        Assert.Equal((0, true), (exitCode, await Library.LoadFolderAtAsync("Shop/Pong", Cancellation) is not null));
    }

    [Fact]
    public async Task RunAsync_WhenAWorkflowIsRenamed_ThenHasTheNewName()
    {
        // Arrange
        await Workflows.SaveAsync("Ping", new() { Id = Guid.NewGuid() }, Cancellation);

        // Act
        var exitCode = await RunAsync(["rename", "ping", "Pong"]);

        // Assert
        Assert.Equal((0, "Pong"), (exitCode, (await Workflows.ListAsync(Cancellation)).Single().Name));
    }

    [Fact]
    public async Task RunAsync_WhenAnUnreadableRequestIsRenamed_ThenTellsWhereItsFileIsWrong()
    {
        // Arrange
        var id = Guid.NewGuid();
        Directory.CreateDirectory(Folder.Requests);
        await File.WriteAllTextAsync(Path.Combine(Folder.Requests, $"{id}.json"), "{", Cancellation);

        // Act
        var exitCode = await RunAsync(["rename", $"{id}", "Pong"]);

        // Assert
        Assert.Equal((2, "File is not valid."), (exitCode, Problem));
    }

    [Fact]
    public async Task RunAsync_WhenTwoHaveThePath_ThenAsksForTheIdAndChangesNothing()
    {
        // Arrange
        await Library.SaveAtAsync("Ping", Request, Cancellation);
        await Library.SaveAtAsync("Ping", ApiRequest.New(), Cancellation);

        // Act
        var exitCode = await RunAsync(["rename", "Ping", "Pong"]);

        // Assert
        Assert.Equal((2, "Target is ambiguous. Use its id."), (exitCode, Problem));
        Assert.Equal(["Ping", "Ping"], await Library.PathsAsync(Cancellation));
    }

    [Fact]
    public async Task RunAsync_WhenARequestIsMoved_ThenItIsInTheFolder()
    {
        // Arrange
        var request = await Library.SaveAtAsync("Ping", Request, Cancellation);
        await Library.FolderAtAsync("Shop/Orders", Cancellation);

        // Act
        var exitCode = await RunAsync(["move", $"{request.Id}", "shop/orders"]);

        // Assert
        Assert.Equal((0, "Shop/Orders/Ping"), (exitCode, Result.GetProperty("path").GetString()));
        Assert.Equal(["Shop/Orders/Ping"], await Library.PathsAsync(Cancellation));
    }

    [Fact]
    public async Task RunAsync_WhenAFolderIsMovedIntoItself_ThenFailsAndMovesNothing()
    {
        // Arrange
        await Library.FolderAtAsync("Shop/Orders", Cancellation);

        // Act
        var exitCode = await RunAsync(["move", "Shop", "Shop/Orders"]);

        // Assert
        Assert.Equal((2, "A folder cannot be moved into itself."), (exitCode, Problem));
        Assert.Null((await Library.LoadFolderAtAsync("Shop", Cancellation))!.ParentId);
    }

    [Fact]
    public async Task RunAsync_WhenDeletingWithoutYes_ThenTellsWhatWouldGoAndDeletesNothing()
    {
        // Arrange
        await Library.SaveAtAsync("Shop/Orders/Ping", Request, Cancellation);
        await Library.SaveAtAsync("Shop/Pong", ApiRequest.New(), Cancellation);

        // Act
        var exitCode = await RunAsync(["delete", "Shop"]);

        // Assert
        using var error = JsonDocument.Parse(Error);
        Assert.Equal((2, 2, 2), (exitCode, error.RootElement.GetProperty("folders").GetInt32(), error.RootElement.GetProperty("requests").GetInt32()));
        Assert.Equal(2, (await Library.PathsAsync(Cancellation)).Count);
    }

    [Fact]
    public async Task RunAsync_WhenDeletingARequestWithoutYes_ThenTellsThatOneRequestWouldGo()
    {
        // Arrange
        await Library.SaveAtAsync("Shop/Ping", Request, Cancellation);

        // Act
        await RunAsync(["delete", "Shop/Ping"]);

        // Assert
        using var error = JsonDocument.Parse(Error);
        Assert.Equal((0, 1), (error.RootElement.GetProperty("folders").GetInt32(), error.RootElement.GetProperty("requests").GetInt32()));
    }

    [Fact]
    public async Task RunAsync_WhenAFolderIsDeletedWithYes_ThenDeletesItsRequestsAndTheirSecrets()
    {
        // Arrange
        var request = await Library.SaveAtAsync("Shop/Orders/Ping", Request, Cancellation);
        await Secrets.SaveAsync(request.Id, SecretKind.Token, "token", Cancellation);

        // Act
        var exitCode = await RunAsync(["delete", "Shop", "--yes"]);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty((await Library.LoadAllAsync(Cancellation)).Folders);
        Assert.Null(await Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task RunAsync_WhenAWorkflowIsDeletedWithYes_ThenForgetsTheSecretsOfItsSteps()
    {
        // Arrange
        var step = Guid.NewGuid();
        var workflow = await Workflows.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Call with { Id = step } }] }, Cancellation);
        await Secrets.SaveAsync(step, SecretKind.Token, "token", Cancellation);

        // Act
        var exitCode = await RunAsync(["delete", $"{workflow.Id}", "--yes"]);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty(await Workflows.ListAsync(Cancellation));
        Assert.Null(await Secrets.OfAsync(step, SecretKind.Token, Cancellation));
    }
    JsonElement Shown => JsonSerializer.Deserialize<JsonElement>(_output.ToArray());

    // What show wrote, with the output emptied for the command that follows.
    async Task<JsonNode> ShownAsync(string target)
    {
        await RunAsync(["show", target]);
        var shown = JsonNode.Parse(_output.ToArray())!;
        _output.SetLength(0);
        return shown;
    }

    Task<int> UpdateAsync(string target, JsonNode input)
    {
        var reader = new StringReader(input.ToJsonString());
        return RunAsync(["update", target, "--file", "-"], input: reader);
    }

    [Fact]
    public async Task RunAsync_WhenARequestIsShown_ThenWritesItAsSaved()
    {
        // Arrange
        var request = Request with { Id = Guid.NewGuid(), Name = "Ping", Method = "POST" };
        await Library.CreateAsync(request, Cancellation);

        // Act
        var exitCode = await RunAsync(["show", "Ping"]);

        // Assert
        Assert.Equal((0, "POST", $"{request.Id}"), (exitCode, Shown.GetProperty("request").GetProperty("method").GetString(), Shown.GetProperty("request").GetProperty("id").GetString()));
    }

    [Fact]
    public async Task RunAsync_WhenAWorkflowIsShown_ThenWritesItWithItsScripts()
    {
        // Arrange
        var workflow = await Workflows.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Script = "map.js" }] }, Cancellation);
        await Workflows.CreateScriptAsync(workflow.Id, "map.js", "return 1;", Cancellation);

        // Act
        var exitCode = await RunAsync(["show", "Flow"]);

        // Assert
        Assert.Equal((0, "map.js", "return 1;"), (exitCode, Shown.GetProperty("workflow").GetProperty("steps")[0].GetProperty("script").GetString(), Shown.GetProperty("scripts").GetProperty("map.js").GetString()));
    }

    [Fact]
    public async Task RunAsync_WhenAnEnvironmentIsShown_ThenWritesItsValues()
    {
        // Arrange
        await Environments.SaveAsync([new("Dev", [new("baseUrl", "https://dev.local")]) { Id = Guid.NewGuid() }], Cancellation);

        // Act
        var exitCode = await RunAsync(["show", "Dev"]);

        // Assert
        Assert.Equal((0, "https://dev.local"), (exitCode, Shown.GetProperty("environment").GetProperty("variables")[0].GetProperty("value").GetString()));
    }

    [Fact]
    public async Task RunAsync_WhenAFolderIsShown_ThenWritesItsAuth()
    {
        // Arrange
        var folder = await Library.FolderAtAsync("Shop", Cancellation);
        await Library.SaveFolderAsync((await Library.LoadFolderAsync(folder!.Value, Cancellation))! with { Auth = new(AuthKind.Bearer) }, Cancellation);

        // Act
        var exitCode = await RunAsync(["show", "Shop"]);

        // Assert
        Assert.Equal((0, "Bearer"), (exitCode, Shown.GetProperty("folder").GetProperty("auth").GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task RunAsync_WhenTheEnvironmentsCannotBeRead_ThenARequestIsShownAllTheSame()
    {
        // Arrange
        await Library.CreateAsync(Request with { Id = Guid.NewGuid(), Name = "Ping" }, Cancellation);
        await File.WriteAllTextAsync(Folder.Environments, "[", Cancellation);

        // Act
        var exitCode = await RunAsync(["show", "Ping"]);

        // Assert
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task RunAsync_WhenTheSettingsCannotBeRead_ThenListingEnvironmentsTellsIt()
    {
        // Arrange
        Directory.CreateDirectory(Folder.Root);
        await File.WriteAllTextAsync(Folder.Settings, "{", Cancellation);

        // Act
        var exitCode = await RunAsync(["list", "environments"]);

        // Assert
        Assert.Equal((2, "Environment settings could not be read."), (exitCode, Problem));
    }

    [Fact]
    public async Task RunAsync_WhenAnUpdateIsReadFromAFile_ThenSavesIt()
    {
        // Arrange
        var request = Request with { Id = Guid.NewGuid(), Name = "Ping" };
        await Library.CreateAsync(request, Cancellation);
        var shown = await ShownAsync("Ping");
        shown["request"]!["url"] = "https://changed.local";
        var file = Path.Combine(_temporary.Path, "update.json");
        await File.WriteAllTextAsync(file, shown.ToJsonString(), Cancellation);

        // Act
        var exitCode = await RunAsync(["update", "Ping", "--file", file]);

        // Assert
        Assert.Equal((0, "https://changed.local"), (exitCode, (await Library.LoadAsync(request.Id, Cancellation))!.Url));
    }

    [Fact]
    public async Task RunAsync_WhenAWorkflowIsCheckedWithItsParameter_ThenItCanRun()
    {
        // Arrange
        await Workflows.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Parameters = [new("orderId")], Steps = [new() { Request = Call with { Url = $"{server.Http}orders/{{{{orderId}}}}" } }] }, Cancellation);

        // Act
        var exitCode = await RunAsync(["check", "Flow", "--param", "orderId=o-17"]);

        // Assert
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task RunAsync_WhenAnEnvironmentIsMade_ThenItIsThereWithoutVariables()
    {
        // Act
        var exitCode = await RunAsync(["new", "environment", "Dev"]);

        // Assert
        Assert.Equal((0, "Dev", 0), (exitCode, (await Environments.AllAsync(Cancellation)).Single().Name, (await Environments.AllAsync(Cancellation)).Single().Variables.Count));
    }

    [Theory]
    [InlineData("new", "environment", "dev")]
    [InlineData("rename", "Prod", "dev")]
    public async Task RunAsync_WhenAnEnvironmentWouldTakeANameThatIsTaken_ThenFailsAsEnvironmentsAreChosenByName(params string[] arguments)
    {
        // Arrange
        await Environments.SaveAsync([new("Dev", []) { Id = Guid.NewGuid() }, new("Prod", []) { Id = Guid.NewGuid() }], Cancellation);

        // Act
        var exitCode = await RunAsync(arguments);

        // Assert
        Assert.Equal((2, "Environment name is taken.", "Dev|Prod"), (exitCode, Problem, string.Join('|', (await Environments.AllAsync(Cancellation)).Select(environment => environment.Name))));
    }

    [Fact]
    public async Task RunAsync_WhenAShownEnvironmentIsUpdated_ThenGetsTheNewVariables()
    {
        // Arrange
        await Environments.SaveAsync([new("Dev", [new("baseUrl", "https://old.local")]) { Id = Guid.NewGuid() }], Cancellation);
        var shown = await ShownAsync("Dev");
        shown["environment"]!["variables"]![0]!["value"] = "https://new.local";

        // Act
        var exitCode = await UpdateAsync("Dev", shown);

        // Assert
        Assert.Equal((0, "https://new.local"), (exitCode, (await Environments.AllAsync(Cancellation)).Single().Variables.Single().Value));
    }

    [Fact]
    public async Task RunAsync_WhenAnUpdateRenamesAnEnvironment_ThenFails()
    {
        // Arrange
        await Environments.SaveAsync([new("Dev", []) { Id = Guid.NewGuid() }], Cancellation);
        var shown = await ShownAsync("Dev");
        shown["environment"]!["name"] = "Prod";

        // Act
        var exitCode = await UpdateAsync("Dev", shown);

        // Assert
        Assert.Equal((2, "Use rename to change the name."), (exitCode, Problem));
    }

    [Fact]
    public async Task RunAsync_WhenAnEnvironmentIsDeletedWithYes_ThenForgetsItsTokensAndCredentials()
    {
        // Arrange
        var environment = Guid.NewGuid();
        var owner = Guid.NewGuid();
        await Environments.SaveAsync([new("Dev", []) { Id = environment }], Cancellation);
        await Secrets.SaveAsync(owner, SecretKind.OAuthToken, environment, "token", Cancellation);
        await Credentials.SaveAsync([new(Guid.NewGuid(), environment, "Api", AuthSettings.None)], Cancellation);

        // Act
        var exitCode = await RunAsync(["delete", "Dev", "--yes"]);

        // Assert
        Assert.Equal((0, 0, (string?)null, 0), (exitCode, (await Environments.AllAsync(Cancellation)).Count, await Secrets.OfAsync(owner, SecretKind.OAuthToken, environment, Cancellation),
            (await Credentials.AllAsync(Cancellation)).Count));
    }

    [Fact]
    public async Task RunAsync_WhenTheSelectedEnvironmentIsDeleted_ThenSendUsesNoEnvironment()
    {
        // Arrange
        var environment = new ApiEnvironment("Dev", []) { Id = Guid.NewGuid() };
        await Environments.SaveAsync([environment], Cancellation);
        await Settings.UpdateAsync(_ => new(EnvironmentId: environment.Id), Cancellation);
        await RunAsync(["delete", "Dev", "--yes"]);
        var sender = Answering();

        // Act
        await RunAsync(["send", "GET", "https://localhost/"], sender);

        // Assert
        Assert.Same(ApiEnvironment.None, sender.Environment);
    }

    [Fact]
    public async Task RunAsync_WhenAnEnvironmentHasTheNameOfARequest_ThenTheNameIsAmbiguousAndNothingIsDeleted()
    {
        // Arrange
        await Library.CreateAsync(Request with { Id = Guid.NewGuid(), Name = "Dev" }, Cancellation);
        await Environments.SaveAsync([new("Dev", []) { Id = Guid.NewGuid() }], Cancellation);

        // Act
        var exitCode = await RunAsync(["delete", "Dev", "--yes"]);

        // Assert
        Assert.Equal((2, "Target is ambiguous. Use its id.", 1, 1), (exitCode, Problem, (await Library.LoadAllAsync(Cancellation)).Requests.Count, (await Environments.AllAsync(Cancellation)).Count));
    }

    [Fact]
    public async Task RunAsync_WhenTheEnvironmentsCannotBeReadAndNothingElseHasTheTarget_ThenTellsWhereTheyAreWrong()
    {
        // Arrange
        Directory.CreateDirectory(Folder.Root);
        await File.WriteAllTextAsync(Folder.Environments, "[", Cancellation);

        // Act
        var exitCode = await RunAsync(["delete", "Dev", "--yes"]);

        // Assert
        Assert.Equal((2, "File is not valid."), (exitCode, Problem));
    }

    [Fact]
    public async Task RunAsync_WhenAnEnvironmentIsMoved_ThenFails()
    {
        // Arrange
        await Environments.SaveAsync([new("Dev", []) { Id = Guid.NewGuid() }], Cancellation);

        // Act
        var exitCode = await RunAsync(["move", "Dev", "."]);

        // Assert
        Assert.Equal((2, "An environment cannot be moved."), (exitCode, Problem));
    }

    [Fact]
    public async Task RunAsync_WhenAnUpdateGivesAScriptAsNull_ThenDeletesIt()
    {
        // Arrange
        var workflow = await Workflows.SaveAsync("Flow", new() { Id = Guid.NewGuid() }, Cancellation);
        await Workflows.CreateScriptAsync(workflow.Id, "old.js", "return 1;", Cancellation);
        var shown = await ShownAsync("Flow");
        shown["scripts"]!["old.js"] = null;

        // Act
        var exitCode = await UpdateAsync("Flow", shown);

        // Assert
        Assert.Equal((0, 0), (exitCode, (await Workflows.ScriptsAsync(workflow.Id, Cancellation)).Count));
    }

    [Fact]
    public async Task RunAsync_WhenAnUpdateDeletesAScriptAStepUses_ThenFailsAndKeepsIt()
    {
        // Arrange
        var workflow = await Workflows.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Script = "map.js" }] }, Cancellation);
        await Workflows.CreateScriptAsync(workflow.Id, "map.js", "return 1;", Cancellation);
        var shown = await ShownAsync("Flow");
        shown["scripts"]!["map.js"] = null;

        // Act
        var exitCode = await UpdateAsync("Flow", shown);

        // Assert
        Assert.Equal((2, "A script a step uses cannot be deleted.", "return 1;"), (exitCode, Problem, await Workflows.LoadScriptAsync(workflow.Id, "map.js", Cancellation)));
    }

    [Fact]
    public async Task RunAsync_WhenTheHistoryIsListed_ThenTellsTheNewestCallsWithTheirStatus()
    {
        // Arrange
        await RunAsync(["send", "GET", $"{server.Http}"]);
        await RunAsync(["send", "POST", $"{server.Http}"]);
        _output.SetLength(0);

        // Act
        var exitCode = await RunAsync(["history", "--count", "1"]);

        // Assert
        var lines = Output.TrimEnd().Split(Environment.NewLine);
        var call = lines[0].Split('\t');
        Assert.Equal((0, 1, "Cli", "POST", "200"), (exitCode, lines.Length, call[2], call[3], call[4]));
    }

    [Fact]
    public async Task RunAsync_WhenACallInTheHistoryIsShown_ThenWritesItsRequestAndResponse()
    {
        // Arrange
        await RunAsync(["send", "GET", $"{server.Http}"]);
        _output.SetLength(0);
        await RunAsync(["history"]);
        var name = Output.Split('\t')[0];
        _output.SetLength(0);

        // Act
        var exitCode = await RunAsync(["history", name]);

        // Assert
        Assert.Equal((0, "GET", 200), (exitCode, Shown.GetProperty("call").GetProperty("request").GetProperty("method").GetString(), Shown.GetProperty("call").GetProperty("response").GetProperty("statusCode").GetInt32()));
    }

    [Fact]
    public async Task RunAsync_WhenACallIsNotInTheHistory_ThenFailsAlsoForACallOutsideIt()
    {
        // Arrange
        await RunAsync(["send", "GET", $"{server.Http}"]);
        var call = Directory.EnumerateFiles(Folder.History).Single();
        File.Copy(call, Path.Combine(Folder.Root, "outside.json"));

        // Act
        var exitCode = await RunAsync(["history", "../outside"]);

        // Assert
        Assert.Equal((2, "Call could not be found."), (exitCode, Problem));
    }

    [Fact]
    public async Task RunAsync_WhenAShownFileIsNotValid_ThenTellsWhere()
    {
        // Arrange
        var id = Guid.NewGuid();
        Directory.CreateDirectory(Folder.Requests);
        await File.WriteAllTextAsync(Path.Combine(Folder.Requests, $"{id}.json"), "{ \"url\": ", Cancellation);

        // Act
        var exitCode = await RunAsync(["show", $"{id}"]);

        // Assert
        Assert.Equal((2, "File is not valid."), (exitCode, Problem));
    }

    [Fact]
    public async Task RunAsync_WhenAShownRequestIsUpdated_ThenKeepsItsIdNameAndFolder()
    {
        // Arrange
        var folder = await Library.FolderAtAsync("Shop", Cancellation);
        var request = Request with { Id = Guid.NewGuid(), Name = "Ping", FolderId = folder };
        await Library.CreateAsync(request, Cancellation);
        var shown = await ShownAsync("Shop/Ping");
        shown["request"]!["method"] = "DELETE";

        // Act
        var exitCode = await UpdateAsync("Shop/Ping", shown);

        // Assert
        var saved = (await Library.LoadAsync(request.Id, Cancellation))!;
        Assert.Equal((0, "DELETE", "Ping", folder), (exitCode, saved.Method, saved.Name, saved.FolderId));
    }

    [Theory]
    [InlineData("name", "Pong", "Use rename to change the name.")]
    [InlineData("folderId", null, "Use move to change the folder.")]
    [InlineData("id", "6f0a7c1e-2b3d-4e5f-8a9b-0c1d2e3f4a5b", "Target is not the one in the input.")]
    public async Task RunAsync_WhenAnUpdateChangesTheIdNameOrFolder_ThenFailsAndChangesNothing(string property, string? value, string problem)
    {
        // Arrange
        var folder = await Library.FolderAtAsync("Shop", Cancellation);
        var request = Request with { Id = Guid.NewGuid(), Name = "Ping", FolderId = folder };
        await Library.CreateAsync(request, Cancellation);
        var shown = await ShownAsync("Shop/Ping");
        shown["request"]![property] = value;
        shown["request"]!["method"] = "DELETE";

        // Act
        var exitCode = await UpdateAsync("Shop/Ping", shown);

        // Assert
        Assert.Equal((2, problem, "GET"), (exitCode, Problem, (await Library.LoadAsync(request.Id, Cancellation))!.Method));
    }

    [Fact]
    public async Task RunAsync_WhenTheInputHasAnUnknownProperty_ThenTellsItsLine()
    {
        // Arrange
        await Library.CreateAsync(Request with { Id = Guid.NewGuid(), Name = "Ping" }, Cancellation);
        var input = new StringReader("{\n  \"request\": {\n    \"url\": \"https://dev.local\",\n    \"mystery\": 1\n  }\n}");

        // Act
        var exitCode = await RunAsync(["update", "Ping", "--file", "-"], input: input);

        // Assert
        using var error = JsonDocument.Parse(_error.ToArray());
        Assert.Equal((2, "Input is not valid.", 4), (exitCode, Problem, error.RootElement.GetProperty("line").GetInt32()));
    }

    [Fact]
    public async Task RunAsync_WhenAShownWorkflowIsUpdated_ThenKeepsTheIdsOfItsStepsAndGivesANewOneItsOwn()
    {
        // Arrange
        var step = Guid.NewGuid();
        var workflow = await Workflows.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Call with { Id = step } }] }, Cancellation);
        var shown = await ShownAsync("Flow");
        shown["workflow"]!["steps"]!.AsArray().Add(JsonNode.Parse("""{ "script": "map.js" }"""));
        shown["workflow"]!["steps"]!.AsArray().Add(JsonNode.Parse("""{ "request": { "url": "https://dev.local" } }"""));
        shown["scripts"] = JsonNode.Parse("""{ "map.js": "return 1;" }""");

        // Act
        var exitCode = await UpdateAsync("Flow", shown);

        // Assert
        var saved = (await Workflows.LoadAsync(workflow.Id, Cancellation))!;
        Assert.Equal((0, step, true, "return 1;"), (exitCode, saved.Steps[0].Request!.Id, saved.Steps[2].Request!.Id != Guid.Empty, await Workflows.LoadScriptAsync(workflow.Id, "map.js", Cancellation)));
    }

    [Fact]
    public async Task RunAsync_WhenWhatWasShownIsUpdatedUnchanged_ThenShowsTheSame()
    {
        // Arrange
        var step = Guid.NewGuid();
        await Workflows.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Variables = [new("token")], Steps = [new() { Request = Call with { Id = step }, Saves = [new("token", "$.token")] }] }, Cancellation);
        var shown = await ShownAsync("Flow");

        // Act
        var exitCode = await UpdateAsync("Flow", shown);

        // Assert
        _output.SetLength(0);
        Assert.Equal((0, shown.ToJsonString()), (exitCode, (await ShownAsync("Flow")).ToJsonString()));
    }

    [Fact]
    public async Task RunAsync_WhenTwoStepsHaveTheSameId_ThenFailsAsTheyWouldShareSecrets()
    {
        // Arrange
        var step = Guid.NewGuid();
        await Workflows.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Call with { Id = step } }] }, Cancellation);
        var shown = await ShownAsync("Flow");
        shown["workflow"]!["steps"]!.AsArray().Add(shown["workflow"]!["steps"]![0]!.DeepClone());

        // Act
        var exitCode = await UpdateAsync("Flow", shown);

        // Assert
        Assert.Equal((2, "Step id is not one of the workflow's."), (exitCode, Problem));
    }

    [Fact]
    public async Task RunAsync_WhenAnUpdateRemovesAStep_ThenForgetsItsSecrets()
    {
        // Arrange
        var step = Guid.NewGuid();
        await Workflows.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Call with { Id = step } }] }, Cancellation);
        await Secrets.SaveAsync(step, SecretKind.Token, "token", Cancellation);
        var shown = await ShownAsync("Flow");
        shown["workflow"]!["steps"] = new JsonArray();

        // Act
        var exitCode = await UpdateAsync("Flow", shown);

        // Assert
        Assert.Equal((0, (string?)null), (exitCode, await Secrets.OfAsync(step, SecretKind.Token, Cancellation)));
    }

    [Fact]
    public async Task RunAsync_WhenAStepTakesTheIdOfARequest_ThenFailsAsItWouldShareItsSecrets()
    {
        // Arrange
        var request = Request with { Id = Guid.NewGuid(), Name = "Ping" };
        await Library.CreateAsync(request, Cancellation);
        await Workflows.SaveAsync("Flow", new() { Id = Guid.NewGuid() }, Cancellation);
        var shown = await ShownAsync("Flow");
        shown["workflow"]!["steps"]!.AsArray().Add(JsonNode.Parse($$"""{ "request": { "id": "{{request.Id}}", "url": "https://dev.local" } }"""));

        // Act
        var exitCode = await UpdateAsync("Flow", shown);

        // Assert
        Assert.Equal((2, "Step id is not one of the workflow's."), (exitCode, Problem));
    }

    [Fact]
    public async Task RunAsync_WhenAnUpdateHasAScriptNameThatLeadsOut_ThenFails()
    {
        // Arrange
        await Workflows.SaveAsync("Flow", new() { Id = Guid.NewGuid() }, Cancellation);
        var shown = await ShownAsync("Flow");
        shown["scripts"] = JsonNode.Parse("""{ "../map.js": "return 1;" }""");

        // Act
        var exitCode = await UpdateAsync("Flow", shown);

        // Assert
        Assert.Equal((2, "Invalid script name."), (exitCode, Problem));
    }

    [Fact]
    public async Task RunAsync_WhenAFolderIsUpdated_ThenFails()
    {
        // Arrange
        await Library.FolderAtAsync("Shop", Cancellation);

        // Act
        var exitCode = await UpdateAsync("Shop", new JsonObject());

        // Assert
        Assert.Equal((2, "A folder cannot be updated."), (exitCode, Problem));
    }

    [Fact]
    public async Task RunAsync_WhenListingEnvironments_ThenMarksTheSelectedOne()
    {
        // Arrange
        var chosen = Guid.NewGuid();
        await Environments.SaveAsync([new("Prod", []) { Id = Guid.NewGuid() }, new("Dev", []) { Id = chosen }], Cancellation);
        await Settings.UpdateAsync(_ => new(EnvironmentId: chosen), Cancellation);

        // Act
        var exitCode = await RunAsync(["list", "environments"]);

        // Assert
        Assert.Equal((0, $"{chosen}\tDev\tselected"), (exitCode, Output.Split(Environment.NewLine)[0]));
    }

    [Fact]
    public async Task RunAsync_WhenTheLastRunIsLogged_ThenWritesItsEventsAsTheRunDid()
    {
        // Arrange
        await Workflows.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Call }] }, Cancellation);
        await RunAsync(["run", "Flow"]);
        var ran = Output;
        _output.SetLength(0);

        // Act
        var exitCode = await RunAsync(["log", "Flow", "--last"]);

        // Assert
        Assert.Equal((0, ran), (exitCode, Output));
    }

    [Fact]
    public async Task RunAsync_WhenRunsAreListed_ThenTellsEachWithItsOutcome()
    {
        // Arrange
        await Workflows.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Call }] }, Cancellation);
        await RunAsync(["run", "Flow"]);
        _output.SetLength(0);

        // Act
        var exitCode = await RunAsync(["log", "Flow"]);

        // Assert
        Assert.Equal((0, "Succeeded"), (exitCode, Output.TrimEnd().Split('\t')[2]));
    }

    [Fact]
    public async Task RunAsync_WhenARunIsNotOneOfTheWorkflows_ThenFails()
    {
        // Arrange
        await Workflows.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Call }] }, Cancellation);

        // Act
        var exitCode = await RunAsync(["log", "Flow", "../secrets"]);

        // Assert
        Assert.Equal((2, "Run could not be found."), (exitCode, Problem));
    }

    // A run's file, kept open for writing as by a run that is running.
    FileStream RunningRun(Guid workflow, string firstLine)
    {
        var path = RunLog.PathOf(Folder, workflow, "20261005-120000-000-abcd");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete);
        file.Write(Encoding.UTF8.GetBytes(firstLine));
        file.Flush();
        return file;
    }

    [Fact]
    public async Task RunAsync_WhenARunIsFollowed_ThenWritesItsEventsUntilItFinishes()
    {
        // Arrange
        var workflow = Guid.NewGuid();
        await using var run = RunningRun(workflow, "{\"type\":\"run.started\"}\n");

        // Act
        var following = RunAsync(["log", $"{workflow}", "--last", "--follow"]);
        await Task.Delay(300, Cancellation);
        run.Write("{\"type\":\"run.finished\",\"outcome\":\"Succeeded\"}\n"u8);
        run.Flush();
        var exitCode = await following.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

        // Assert
        Assert.Equal((0, 2), (exitCode, Events.Count()));
    }

    [Fact]
    public async Task RunAsync_WhenAFollowedRunIsWrittenNoMoreWithoutItsLastEvent_ThenStops()
    {
        // Arrange
        var workflow = Guid.NewGuid();
        RunningRun(workflow, "{\"type\":\"run.started\"}\n").Dispose();

        // Act
        var exitCode = await RunAsync(["log", $"{workflow}", "--last", "--follow"]).WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

        // Assert
        Assert.Equal((1, 1), (exitCode, Events.Count()));
    }

    [Fact]
    public async Task RunAsync_WhenAFollowedRunHasALargeLine_ThenWritesItWhole()
    {
        // Arrange
        var workflow = Guid.NewGuid();
        await using var run = RunningRun(workflow, $"{{\"type\":\"step.finished\",\"body\":\"{new string('x', 30_000_000)}\"}}\n");
        run.Write("{\"type\":\"run.finished\",\"outcome\":\"Succeeded\"}\n"u8);
        run.Flush();

        // Act
        var exitCode = await RunAsync(["log", $"{workflow}", "--last", "--follow"]).WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

        // Assert
        Assert.Equal((0, 2), (exitCode, Events.Count()));
    }

    [Fact]
    public async Task RunAsync_WhenTheLastEventOfARunIsLarge_ThenTheListStillTellsItsOutcome()
    {
        // Arrange
        var workflow = Guid.NewGuid();
        RunningRun(workflow, $"{{\"type\":\"run.started\"}}\n{{\"type\":\"run.finished\",\"outcome\":\"Failed\",\"variables\":{{\"pdf\":\"{new string('x', 3_000_000)}\"}}}}\n").Dispose();

        // Act
        var exitCode = await RunAsync(["log", $"{workflow}"]);

        // Assert
        Assert.Equal((0, "Failed"), (exitCode, Output.TrimEnd().Split('\t')[2]));
    }

    [Fact]
    public async Task RunAsync_WhenAWorkflowIsChecked_ThenSendsNothing()
    {
        // Arrange
        var sender = new FakeSender(() => throw new InvalidOperationException("Nothing is to be sent."));
        await Workflows.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Call }] }, Cancellation);

        // Act
        var exitCode = await RunAsync(["check", "Flow"], sender);

        // Assert
        Assert.Equal((0, (ApiRequest?)null), (exitCode, sender.Request));
    }

    [Fact]
    public async Task RunAsync_WhenACheckedWorkflowHasAProblem_ThenTellsItAsRunDoes()
    {
        // Arrange
        await Workflows.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Parameters = [new("orderId")], Steps = [new() { Request = Call }] }, Cancellation);

        // Act
        var exitCode = await RunAsync(["check", "Flow"]);

        // Assert
        Assert.Equal((2, "Workflow cannot run."), (exitCode, Problem));
    }
}
