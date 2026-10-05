using System.Text.Json;
using System.Text.Json.Nodes;
using Hoboman.Cli;
using Hoboman.Tests.Auth;
using ModelContextProtocol.Protocol;

namespace Hoboman.Tests.Cli;

public sealed class McpToolsTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    // Below the temporary folder, so a test can keep files of its own outside Hoboman's.
    AppFolder Folder => new(Path.Combine(_temporary.Path, "data"));

    // Claude Code keeps only this much of the instructions and of each description.
    const int MaxLength = 2048;

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    RequestLibrary Library => new(Folder, NullLogger<RequestLibrary>.Instance);

    SecretStore Secrets => new(Folder, NullLogger<SecretStore>.Instance);

    public void Dispose() => _temporary.Dispose();

    McpTools Tools(IRequestSender? sender = null)
    {
        var factory = new CliFactory(Folder, Secrets, sender ?? Answering(), new FakeOAuthClient(_ => throw new OAuthException(OAuthProblem.Denied, "access_denied")));
        return new((output, error, input) => factory.Create(output, error, input, inputRedirected: true), Downloads);
    }

    string Downloads => Path.Combine(_temporary.Path, "downloads");

    static FakeSender Answering(int status = 200, string body = "") => new(() => Task.FromResult(new ApiResponse(status, "OK", 0, 0, [], body)));

    async Task SaveWorkflowAsync()
    {
        var workflows = new WorkflowLibrary(Folder, NullLogger<WorkflowLibrary>.Instance);
        var workflow = await workflows.CreateAsync("Flow", Cancellation);
        await workflows.SaveAsync(workflow with { Steps = [new() { Request = new() { Url = "https://localhost/" } }] }, Cancellation);
    }

    static List<JsonObject> Events(CallToolResult result) => [.. TextOf(result).Split('\n').Select(line => JsonNode.Parse(line)!.AsObject())];

    JsonElement PropertyOf(string tool, string parameter) =>
        Tools().All.Single(item => item.ProtocolTool.Name == tool).ProtocolTool.InputSchema.GetProperty("properties").GetProperty(parameter);

    static string TextOf(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    static string? ProblemOf(CallToolResult result)
    {
        using var error = JsonDocument.Parse(TextOf(result));
        return error.RootElement.GetProperty("error").GetString();
    }

    [Fact]
    public void All_WhenListed_ThenHasAToolForEveryCommand()
    {
        // Act
        var names = Tools().All.Select(tool => tool.ProtocolTool.Name);

        // Assert
        Assert.Equal(["check", "delete", "guide", "history", "list", "log", "move", "new_environment", "new_folder", "new_request", "new_workflow", "rename", "run", "send", "send_saved", "show", "update"],
            names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task NewFolderAsync_WhenTheNameLooksLikeAnOption_ThenItIsJustTheName()
    {
        // Act
        var result = await Tools().NewFolderAsync(".", "--yes", Cancellation);

        // Assert
        Assert.Equal((false, "--yes"), (result.IsError, (await Library.LoadAllAsync(Cancellation)).Folders.Single().Name));
    }

    [Fact]
    public async Task SendAsync_WhenTheBodyStartsWithAt_ThenSendsItAsTextAndReadsNoFile()
    {
        // Arrange
        var sender = Answering();

        // Act
        await Tools(sender).SendAsync("POST", "https://localhost/", text: @"@C:\Windows\win.ini", cancellationToken: Cancellation);

        // Assert
        Assert.Equal(@"@C:\Windows\win.ini", sender.Request?.Body);
    }

    [Fact]
    public async Task SendAsync_WhenTheStatusIsNot2xx_ThenTheResponseIsNoError()
    {
        // Act
        var result = await Tools(Answering(404)).SendAsync("GET", "https://localhost/", cancellationToken: Cancellation);

        // Assert
        Assert.Equal((false, 404), (result.IsError, JsonDocument.Parse(TextOf(result)).RootElement.GetProperty("status").GetInt32()));
    }

    [Theory]
    [InlineData(@"..\svar.pdf")]
    [InlineData(@"C:\Users\Public\svar.pdf")]
    [InlineData(@"Startup\svar.cmd")]
    [InlineData("svar.pdf:stream")]
    [InlineData("..")]
    [InlineData("svar.")]
    [InlineData("NUL")]
    [InlineData("com1.txt")]
    [InlineData("CONOUT$")]
    [InlineData("com¹.txt")]
    public async Task SendSavedAsync_WhenTheOutFileIsNotAPlainFileName_ThenSendsNothing(string name)
    {
        // Arrange
        await Library.CreateAsync(ApiRequest.New() with { Id = Guid.NewGuid(), Name = "Ping", Url = "https://localhost/" }, Cancellation);
        var sender = Answering();

        // Act
        var result = await Tools(sender).SendSavedAsync("Ping", @out: name, cancellationToken: Cancellation);

        // Assert
        Assert.Equal(("Output file must be a plain file name.", true), (ProblemOf(result), sender.Request is null));
    }

    [Fact]
    public async Task SendAsync_WhenAnOutFileNameIsGiven_ThenSavesTheBodyInDownloads()
    {
        // Act
        var result = await Tools(Answering(body: "svar")).SendAsync("GET", "https://localhost/", @out: "svar.txt", cancellationToken: Cancellation);

        // Assert
        var file = Path.Combine(Downloads, "svar.txt");
        Assert.Equal((file, true), (JsonDocument.Parse(TextOf(result)).RootElement.GetProperty("file").GetString(), File.Exists(file)));
    }

    [Fact]
    public async Task SendAsync_WhenVariablesAreGiven_ThenTheCallGetsThem()
    {
        // Arrange
        var sender = Answering();

        // Act
        await Tools(sender).SendAsync("GET", "https://localhost/{{id}}", variables: new JsonObject { ["id"] = 42 }, cancellationToken: Cancellation);

        // Assert
        Assert.Equal("42", sender.Environment?.Resolve("{{id}}"));
    }

    [Fact]
    public async Task NewRequestAsync_WhenOnlyTheUrlIsGiven_ThenMakesAGetWithTheBodyAsWritten()
    {
        // Act
        await Tools().NewRequestAsync(".", "Ping", "https://localhost/", json: "@body.json", cancellationToken: Cancellation);

        // Assert
        var request = (await Library.LoadAllAsync(Cancellation)).Requests.Single();
        Assert.Equal(("GET", "@body.json"), (request.Method, request.Body));
    }

    [Theory]
    [InlineData("Folders", "Shop")]
    [InlineData("Workflows", "Flow")]
    public async Task ListAsync_WhenAKindIsGiven_ThenListsThatKind(string kind, string expected)
    {
        // Arrange
        await Library.CreateFolderAsync(new() { Id = Guid.NewGuid(), Name = "Shop" }, Cancellation);
        await SaveWorkflowAsync();

        // Act
        var result = await Tools().ListAsync(Enum.Parse<ListKind>(kind), Cancellation);

        // Assert
        Assert.EndsWith($"\t{expected}", TextOf(result));
    }

    [Fact]
    public async Task ListAsync_WhenTheKindIsNotOneOfTheFour_ThenIsInvalid()
    {
        // Act
        var result = await Tools().ListAsync((ListKind)7, Cancellation);

        // Assert
        Assert.Equal("Invalid tool arguments: kind is requests, workflows, folders or environments.", ProblemOf(result));
    }

    [Fact]
    public async Task UpdateAsync_WhenNoContentIsGiven_ThenIsInvalid()
    {
        // Act
        var result = await Tools().UpdateAsync("Ping", null, Cancellation);

        // Assert
        Assert.Equal("Invalid tool arguments: content is the JSON object from show.", ProblemOf(result));
    }

    [Fact]
    public async Task LogAsync_WhenTheWorkflowIdIsUnknown_ThenSaysSo()
    {
        // Act
        var result = await Tools().LogAsync($"{Guid.NewGuid()}", cancellationToken: Cancellation);

        // Assert
        Assert.Equal("Workflow could not be found.", ProblemOf(result));
    }

    [Fact]
    public async Task ShowAsync_WhenTheTargetIsUnknown_ThenIsAnErrorWithTheCliError()
    {
        // Act
        var result = await Tools().ShowAsync("Ping", Cancellation);

        // Assert
        Assert.Equal((true, "Target could not be found."), (result.IsError, ProblemOf(result)));
    }

    [Fact]
    public async Task CheckAsync_WhenParametersAreGiven_ThenTheCheckGetsThem()
    {
        // Arrange
        var workflow = await new WorkflowLibrary(Folder, NullLogger<WorkflowLibrary>.Instance).CreateAsync("Flow", Cancellation);
        await new WorkflowLibrary(Folder, NullLogger<WorkflowLibrary>.Instance).SaveAsync(workflow with { Parameters = [new("id")] }, Cancellation);

        // Act
        var result = await Tools().CheckAsync("Flow", parameters: new JsonObject { ["id"] = 5 }, cancellationToken: Cancellation);

        // Assert
        Assert.False(result.IsError);
    }

    [Fact]
    public async Task UpdateAsync_WhenTheContentIsChanged_ThenSavesIt()
    {
        // Arrange
        var request = ApiRequest.New() with { Id = Guid.NewGuid(), Name = "Ping", Url = "https://localhost/" };
        await Library.CreateAsync(request, Cancellation);
        var shown = JsonNode.Parse(TextOf(await Tools().ShowAsync("Ping", Cancellation)))!.AsObject();
        shown["request"]!["url"] = "https://changed.local/";

        // Act
        var result = await Tools().UpdateAsync("Ping", shown, Cancellation);

        // Assert
        Assert.Equal((false, "https://changed.local/"), (result.IsError, (await Library.LoadAsync(request.Id, Cancellation))!.Url));
    }

    [Fact]
    public async Task DeleteAsync_WhenNotConfirmed_ThenDeletesNothing()
    {
        // Arrange
        await Library.CreateAsync(ApiRequest.New() with { Id = Guid.NewGuid(), Name = "Ping" }, Cancellation);

        // Act
        var result = await Tools().DeleteAsync("Ping", cancellationToken: Cancellation);

        // Assert
        Assert.Equal(("Deleting needs confirmation.", 1), (ProblemOf(result), (await Library.LoadAllAsync(Cancellation)).Requests.Count));
    }

    [Fact]
    public async Task HistoryAsync_WhenANameAndACountAreGiven_ThenIsInvalid()
    {
        // Act
        var result = await Tools().HistoryAsync("call", 5, Cancellation);

        // Assert
        Assert.Equal("Invalid tool arguments: give name or count, not both, and count is at least 1.", ProblemOf(result));
    }

    [Fact]
    public void All_WhenListed_ThenListOffersOnlyItsKinds()
    {
        // Act
        var kinds = PropertyOf("list", "kind").GetProperty("enum").EnumerateArray().Select(kind => kind.GetString());

        // Assert
        Assert.Equal(["requests", "workflows", "folders", "environments"], kinds);
    }

    [Theory]
    [InlineData("update", "content")]
    [InlineData("run", "parameters")]
    [InlineData("check", "parameters")]
    [InlineData("send", "variables")]
    [InlineData("send_saved", "variables")]
    public void All_WhenListed_ThenJsonObjectsAreTypedAsObjects(string tool, string parameter)
    {
        // Act
        var type = PropertyOf(tool, parameter).GetProperty("type").GetRawText();

        // Assert
        Assert.Contains("\"object\"", type);
    }

    [Theory]
    [InlineData("send")]
    [InlineData("new_request")]
    public void All_WhenListed_ThenAHeaderIsAlwaysText(string tool)
    {
        // Act
        var type = PropertyOf(tool, "headers").GetProperty("items").GetProperty("type").GetString();

        // Assert
        Assert.Equal("string", type);
    }

    [Fact]
    public async Task RunAsync_WhenAStepAnswers_ThenTheResultLeavesItsHeadersAndBodyOut()
    {
        // Arrange
        await SaveWorkflowAsync();

        // Act
        var events = Events(await Tools(Answering(body: "svar")).RunAsync("Flow", cancellationToken: Cancellation));

        // Assert
        var finished = events.Single(line => line["type"]!.GetValue<string>() == "step.finished");
        Assert.Equal((false, false, false, "run.finished"), (finished.ContainsKey("body"), finished.ContainsKey("headers"), events[0].ContainsKey("runFile"), events[^1]["type"]!.GetValue<string>()));
    }

    [Fact]
    public async Task LogAsync_WhenAStepIsAskedFor_ThenGivesItsWholeResponse()
    {
        // Arrange
        await SaveWorkflowAsync();
        var tools = Tools(Answering(body: "svar"));
        await tools.RunAsync("Flow", cancellationToken: Cancellation);

        // Act
        var step = Events(await tools.LogAsync("Flow", last: true, step: 0, cancellationToken: Cancellation)).Single();

        // Assert
        Assert.Equal("svar", step["body"]!.GetValue<string>());
    }

    [Fact]
    public async Task RunAsync_WhenAStepHasABodyButNoKind_ThenSendsItAsJson()
    {
        // Arrange
        var workflows = new WorkflowLibrary(Folder, NullLogger<WorkflowLibrary>.Instance);
        var workflow = await workflows.CreateAsync("Flow", Cancellation);
        await workflows.SaveAsync(workflow with { Steps = [new() { Request = new() { Method = "POST", Url = "https://localhost/", Body = "{}" } }] }, Cancellation);
        var sender = Answering();

        // Act
        await Tools(sender).RunAsync("Flow", cancellationToken: Cancellation);

        // Assert
        Assert.Equal((BodyKind.Json, "{}"), (sender.Request?.BodyKind, sender.Request?.Body));
    }

    [Fact]
    public async Task LogAsync_WhenALineOfTheRunIsNotJson_ThenSaysTheRunCannotBeRead()
    {
        // Arrange
        await SaveWorkflowAsync();
        var tools = Tools();
        await tools.RunAsync("Flow", cancellationToken: Cancellation);
        await File.AppendAllTextAsync(Directory.EnumerateFiles(Folder.Runs, "*.jsonl", SearchOption.AllDirectories).Single(), "not json\n", Cancellation);

        // Act
        var result = await tools.LogAsync("Flow", last: true, cancellationToken: Cancellation);

        // Assert
        Assert.Equal("Run could not be read.", ProblemOf(result));
    }

    [Fact]
    public async Task LogAsync_WhenTheStepIsNotInTheRun_ThenSaysSo()
    {
        // Arrange
        await SaveWorkflowAsync();
        var tools = Tools();
        await tools.RunAsync("Flow", cancellationToken: Cancellation);

        // Act
        var result = await tools.LogAsync("Flow", last: true, step: 5, cancellationToken: Cancellation);

        // Assert
        Assert.Equal("Step could not be found.", ProblemOf(result));
    }

    [Fact]
    public async Task LogAsync_WhenAStepIsGivenWithoutARun_ThenIsInvalid()
    {
        // Act
        var result = await Tools().LogAsync("Flow", step: 0, cancellationToken: Cancellation);

        // Assert
        Assert.Equal("Invalid tool arguments: step needs run or last.", ProblemOf(result));
    }

    [Fact]
    public async Task LogAsync_WhenACountIsGiven_ThenListsThatManyRuns()
    {
        // Arrange
        await SaveWorkflowAsync();
        var tools = Tools();
        await tools.RunAsync("Flow", cancellationToken: Cancellation);
        await tools.RunAsync("Flow", cancellationToken: Cancellation);

        // Act
        var result = await tools.LogAsync("Flow", count: 1, cancellationToken: Cancellation);

        // Assert
        Assert.Single(TextOf(result).Split('\n'));
    }

    [Fact]
    public void Instructions_WhenSent_ThenFitWhatClientsKeep()
    {
        // Act
        var length = McpTools.Instructions.Length;

        // Assert
        Assert.InRange(length, 1, MaxLength);
    }

    [Fact]
    public void All_WhenDescribed_ThenEveryDescriptionFitsWhatClientsKeep()
    {
        // Act
        var tooLong = Tools().All.Where(tool => tool.ProtocolTool.Description!.Length > MaxLength).Select(tool => tool.ProtocolTool.Name);

        // Assert
        Assert.Empty(tooLong);
    }

    [Fact]
    public void All_WhenListed_ThenOnlyTheToolsThatReadAreReadOnly()
    {
        // Act
        var readOnly = Tools().All.Where(tool => tool.ProtocolTool.Annotations?.ReadOnlyHint == true).Select(tool => tool.ProtocolTool.Name);

        // Assert
        Assert.Equal(["check", "guide", "history", "list", "log", "show"], readOnly.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void All_WhenListed_ThenTheToolsThatCanDestroyAreMarked()
    {
        // Act
        var destructive = Tools().All.Where(tool => tool.ProtocolTool.Annotations?.DestructiveHint == true).Select(tool => tool.ProtocolTool.Name);

        // Assert
        Assert.Equal(["delete", "run", "send", "send_saved", "update"], destructive.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Guide_WhenRead_ThenHoldsTheRules()
    {
        // Act
        var guide = TextOf(McpTools.Guide());

        // Assert
        Assert.Contains("UFRAVIGELIG REGEL", guide);
    }

    [Fact]
    public async Task SendAsync_WhenAHeaderIsNull_ThenSendsNothing()
    {
        // Arrange
        var sender = Answering();

        // Act
        var result = await Tools(sender).SendAsync("GET", "https://localhost/", headers: [null!], cancellationToken: Cancellation);

        // Assert
        Assert.Equal(("Invalid tool arguments: a header cannot be null.", true), (ProblemOf(result), sender.Request is null));
    }
}