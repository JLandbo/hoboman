using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Hoboman.Tests.Workflows;

public sealed class WorkflowRunnerTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();
    readonly List<WorkflowEvent> _events = [];

    AppFolder Folder => new(_temporary.Path);

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static FakeSender Answering(params ApiResponse[] responses)
    {
        var queue = new Queue<ApiResponse>(responses);
        return new(() => Task.FromResult(queue.Dequeue()));
    }

    static ApiResponse Ok(string body, params ResponseHeader[] headers) => new(200, "OK", 1, body.Length, headers, body);

    static WorkflowRequest Request(string url) => new() { Url = url };

    async Task<RunOutcome> RunAsync(Workflow workflow, FakeSender sender, Dictionary<string, JsonElement>? parameters = null, CancellationToken? cancellationToken = null, Action<WorkflowEvent>? told = null, ApiEnvironment? environment = null,
        Func<AuthSource, Task<bool>>? fetchToken = null)
    {
        environment ??= ApiEnvironment.None;
        var check = new WorkflowCheck(new WorkflowLibrary(Folder, NullLogger<WorkflowLibrary>.Instance), new SecretStore(Folder, NullLogger<SecretStore>.Instance), NullLogger<WorkflowCheck>.Instance);
        var checkedWorkflow = await check.CheckAsync("Ordre-sync", workflow, environment, parameters ?? [], Cancellation);
        var runner = new WorkflowRunner(sender, Folder, NullLogger<WorkflowRunner>.Instance);
        return await runner.RunAsync(checkedWorkflow, environment, fetchToken ?? (_ => Task.FromResult(false)), workflowEvent =>
        {
            _events.Add(workflowEvent);
            told?.Invoke(workflowEvent);
            return Task.CompletedTask;
        }, cancellationToken ?? Cancellation);
    }

    T Single<T>() where T : WorkflowEvent => Assert.Single(_events.OfType<T>());

    async Task ScriptAsync(string name, string code)
    {
        Directory.CreateDirectory(Path.Combine(Folder.Workflows, "Ordre-sync"));
        await new WorkflowLibrary(Folder, NullLogger<WorkflowLibrary>.Instance).CreateScriptAsync("Ordre-sync", name, code, Cancellation);
    }

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public async Task RunAsync_WhenAScriptMapsAnEarlierResponse_ThenALaterStepSendsItsOutput()
    {
        // Arrange
        var order = Request("https://dev.local/orders/1");
        var import = Request("https://dev.local/import/{{reference}}");
        await ScriptAsync("map.js", "return { reference: `${vars.order.id}-${vars.order.lines.length}` };");
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Variables = [new("order"), new("reference")],
            Steps = [new() { Request = order, Saves = [new("order", "$.data")] }, new() { Script = "map.js", Saves = [new("reference", "$.reference")] }, new() { Request = import }],
        };

        // Act
        var outcome = await RunAsync(workflow, Answering(Ok("""{"data":{"id":"o-17","lines":[1,2]}}"""), Ok("{}")));

        // Assert
        Assert.Equal((RunOutcome.Succeeded, "JS", "dev.local/import/o-17-2"), (outcome, _events.OfType<StepStarted>().ElementAt(1).Method, _events.OfType<StepStarted>().Last().Address));
    }

    [Fact]
    public async Task RunAsync_WhenAScriptReadsVars_ThenGetsTheEnvironmentUnlessTheWorkflowHasTheName()
    {
        // Arrange
        var echo = Request("https://dev.local/{{result}}");
        await ScriptAsync("map.js", "return { result: `${vars.host}-${vars.tenant}` };");
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Parameters = [new("tenant") { Default = JsonSerializer.SerializeToElement("from-workflow") }],
            Variables = [new("result")],
            Steps = [new() { Script = "map.js", Saves = [new("result", "$.result")] }, new() { Request = echo }],
        };
        var environment = new ApiEnvironment("Dev", [new("host", "from-environment"), new("tenant", "ignored")]);

        // Act
        await RunAsync(workflow, Answering(Ok("{}")), environment: environment);

        // Assert
        Assert.Equal("dev.local/from-environment-from-workflow", _events.OfType<StepStarted>().Last().Address);
    }

    [Theory]
    [InlineData(false, RunOutcome.Succeeded)]
    [InlineData(true, RunOutcome.Failed)]
    public async Task RunAsync_WhenAScriptReturnsNothing_ThenFailsOnlyIfItHasSomethingToSave(bool saves, RunOutcome expected)
    {
        // Arrange
        await ScriptAsync("check.js", "if (!vars.order) { throw new Error('No order'); }");
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Variables = [new("order") { Default = JsonSerializer.SerializeToElement("o-17") }],
            Steps = [new() { Script = "check.js", Saves = saves ? [new("order", "$")] : [] }],
        };

        // Act
        var outcome = await RunAsync(workflow, Answering());

        // Assert
        Assert.Equal(expected, outcome);
    }

    [Fact]
    public async Task RunAsync_WhenAScriptFails_ThenTellsItsLineAndSkipsTheRest()
    {
        // Arrange
        var ping = Request("https://dev.local/ping");
        await ScriptAsync("map.js", "const order = vars;\nthrow new Error('No order');");
        var workflow = new Workflow { Id = Guid.NewGuid(), Steps = [new() { Script = "map.js" }, new() { Request = ping }] };

        // Act
        var outcome = await RunAsync(workflow, Answering());

        // Assert
        Assert.Equal((RunOutcome.Failed, "map.js:2: No order", 1), (outcome, Single<StepFinished>().Error, Single<StepSkipped>().Index));
    }

    [Fact]
    public async Task RunAsync_WhenEveryStepSucceeds_ThenTellsTheEventsInOrder()
    {
        // Arrange
        var ping = Request("https://dev.local/ping");

        // Act
        var outcome = await RunAsync(new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping }] }, Answering(Ok("{}")));

        // Assert
        Assert.Equal((RunOutcome.Succeeded, "RunStarted StepStarted StepFinished RunFinished"), (outcome, string.Join(" ", _events.Select(workflowEvent => workflowEvent.GetType().Name))));
    }

    [Fact]
    public async Task RunAsync_WhenTheEnvironmentHasTheSavedName_ThenTheSavedValueWinsAndTheRestComesFromTheEnvironment()
    {
        // Arrange
        var login = Request("https://{{host}}/login");
        var order = Request("https://{{host}}/{{token}}");
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Variables = [new("token")],
            Steps = [new() { Request = login, Saves = [new("token", "$.access_token")] }, new() { Request = order }],
        };
        var environment = new ApiEnvironment("Dev", [new("host", "dev.local"), new("token", "old")]);

        // Act
        await RunAsync(workflow, Answering(Ok("""{"access_token": "new"}"""), Ok("{}")), environment: environment);

        // Assert
        Assert.Equal("dev.local/new", _events.OfType<StepStarted>().Last().Address);
    }

    [Fact]
    public async Task RunAsync_WhenAStepSavesAToken_ThenTheNextStepSendsIt()
    {
        // Arrange
        var login = Request("https://dev.local/login");
        var order = Request("https://dev.local/orders") with { Headers = [new("Authorization", "Bearer {{token}}")] };
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Variables = [new("token")],
            Steps = [new() { Request = login, Saves = [new("token", "$.access_token")] }, new() { Request = order }],
        };
        var sender = Answering(Ok("""{"access_token": "eyJ.demo"}"""), Ok("{}"));

        // Act
        await RunAsync(workflow, sender);

        // Assert
        Assert.Equal("Bearer eyJ.demo", sender.Environment!.Resolve(sender.Request!.Headers[0].Value));
    }

    [Fact]
    public async Task RunAsync_WhenAStepIsSent_ThenSendsItWithItsOwnAuth()
    {
        // Arrange
        var id = Guid.NewGuid();
        var sender = Answering(Ok("{}"));

        // Act
        await RunAsync(new() { Id = Guid.NewGuid(), Steps = [new() { Request = Request("https://dev.local/ping") with { Id = id, Auth = new(AuthKind.Bearer) } }] }, sender);

        // Assert
        Assert.Equal(new AuthSource(id, new(AuthKind.Bearer)), sender.Auth);
    }

    [Fact]
    public async Task RunAsync_WhenAStepHasNoAuth_ThenSendsItWithout()
    {
        // Arrange
        var sender = Answering(Ok("{}"));

        // Act
        await RunAsync(new() { Id = Guid.NewGuid(), Steps = [new() { Request = Request("https://dev.local/ping") }] }, sender);

        // Assert
        Assert.Equal(AuthKind.None, sender.Auth!.Settings.Kind);
    }

    [Fact]
    public async Task RunAsync_WhenAClientCredentialsTokenIsMissing_ThenFetchesOneForTheStepAndSendsAgain()
    {
        // Arrange
        var id = Guid.NewGuid();
        var calls = 0;
        var sender = new FakeSender(() => ++calls == 1 ? throw new MissingSecretException(SecretKind.OAuthToken) : Task.FromResult(Ok("{}")));
        AuthSource? fetched = null;

        // Act
        var outcome = await RunAsync(new() { Id = Guid.NewGuid(), Steps = [new() { Request = Request("https://dev.local/ping") with { Id = id, Auth = new(AuthKind.OAuth2) } }] }, sender,
            fetchToken: auth =>
            {
                fetched = auth;
                return Task.FromResult(true);
            });

        // Assert
        Assert.Equal((RunOutcome.Succeeded, id, 2), (outcome, fetched?.SecretsId, calls));
    }

    [Fact]
    public async Task RunAsync_WhenAnObjectIsSaved_ThenTheNextBodyIsByteIdentical()
    {
        // Arrange
        const string data = """{"id":"o-17","name":"Ordre æøå \"x\" \\ {{name}}","price":0.10,"lines":[{"sku":"a","qty":2}]}""";
        var order = Request("https://dev.local/orders/o-17");
        var import = Request("https://dev.local/import") with { Method = "POST", Body = """{"order": {{order}}}""", BodyKind = BodyKind.Json };
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Variables = [new("order")],
            Steps = [new() { Request = order, Saves = [new("order", "$.data")] }, new() { Request = import }],
        };
        var sender = Answering(Ok($$"""{"data": {{data}}}"""), Ok("{}"));

        // Act
        await RunAsync(workflow, sender);

        // Assert
        Assert.Equal($$"""{"order": {{data}}}""", sender.Environment!.Resolve(sender.Request!.Body));
    }

    [Fact]
    public async Task RunAsync_WhenTheNumberIsBeyondDoublePrecision_ThenKeepsItExactly()
    {
        // Arrange
        var order = Request("https://dev.local/orders");
        var item = Request("https://dev.local/items/{{itemId}}");
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Variables = [new("itemId")],
            Steps = [new() { Request = order, Saves = [new("itemId", "$.items[0].id")] }, new() { Request = item }],
        };

        // Act
        await RunAsync(workflow, Answering(Ok("""{"items": [{"id": 9007199254740993}]}"""), Ok("{}")));

        // Assert
        Assert.Equal(("dev.local/items/9007199254740993", "9007199254740993"), (_events.OfType<StepStarted>().Last().Address, Single<RunFinished>().Variables["itemId"].GetRawText()));
    }

    [Fact]
    public async Task RunAsync_WhenASavePathIsMissing_ThenFailsTheStepAndSavesNothing()
    {
        // Arrange
        var import = Request("https://dev.local/import");
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Variables = [new("status"), new("importId")],
            Steps = [new() { Request = import, Saves = [new("status", "status"), new("importId", "$.importId")] }],
        };

        // Act
        var outcome = await RunAsync(workflow, Answering(Ok("""{"id": "imp-901"}""")));

        // Assert
        Assert.Equal((RunOutcome.Failed, StepOutcome.Failed, 0), (outcome, Single<StepFinished>().Outcome, Single<RunFinished>().Variables.Count));
    }

    [Fact]
    public async Task RunAsync_WhenTheStatusIsNotSuccess_ThenSavesNothing()
    {
        // Arrange
        var import = Request("https://dev.local/import");
        var workflow = new Workflow { Id = Guid.NewGuid(), Variables = [new("status")], Steps = [new() { Request = import, Saves = [new("status", "status")] }] };

        // Act
        await RunAsync(workflow, Answering(new ApiResponse(500, "Internal Server Error", 1, 0, [], "")));

        // Assert
        Assert.Equal((null, 0), (Single<StepFinished>().Saved, Single<RunFinished>().Variables.Count));
    }

    [Fact]
    public async Task RunAsync_WhenSavingFromTheHeadersStatusAndATextBody_ThenSavesEach()
    {
        // Arrange
        var import = Request("https://dev.local/import");
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Variables = [new("location"), new("status"), new("text")],
            Steps = [new() { Request = import, Saves = [new("location", "header:location"), new("status", "status"), new("text", "$")] }],
        };

        // Act
        await RunAsync(workflow, Answering(Ok("Oprettet", new("Location", "/imports/901"), new("Location", "/other"))));

        // Assert
        Assert.Equal("""{"location":"/imports/901","status":200,"text":"Oprettet"}""", JsonSerializer.Serialize(Single<StepFinished>().Saved));
    }

    [Fact]
    public async Task RunAsync_WhenAStepFails_ThenSkipsTheRest()
    {
        // Arrange
        var ping = Request("https://dev.local/ping");
        var workflow = new Workflow { Id = Guid.NewGuid(), Steps = [new() { Request = ping }, new() { Request = ping }, new() { Request = ping }] };

        // Act
        var outcome = await RunAsync(workflow, Answering(Ok("{}"), new(500, "Internal Server Error", 1, 0, [], "")));

        // Assert
        Assert.Equal((RunOutcome.Failed, "Succeeded Failed Skipped"), (outcome, string.Join(" ", Single<RunFinished>().Steps.Select(step => step.Outcome))));
    }

    [Fact]
    public async Task RunAsync_WhenTheCallFails_ThenTellsTheKindOfProblemWithoutItsDetails()
    {
        // Arrange
        var ping = Request("https://dev.local/ping");

        // Act
        await RunAsync(new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping }] }, new(() => throw new HttpRequestException("https://dev.local/?key=secret")));

        // Assert
        Assert.Equal("Network request failed.", Single<StepFinished>().Error);
    }

    [Fact]
    public async Task RunAsync_WhenTheCallTimesOut_ThenFailsTheStepWithoutCancellingTheRun()
    {
        // Arrange
        var ping = Request("https://dev.local/ping");

        // Act
        var outcome = await RunAsync(new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping }] }, new(() => throw new TaskCanceledException()));

        // Assert
        Assert.Equal((RunOutcome.Failed, "Request timed out."), (outcome, Single<StepFinished>().Error));
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_ThenCancelsTheStepAndSkipsTheRest()
    {
        // Arrange
        var ping = Request("https://dev.local/ping");
        var workflow = new Workflow { Id = Guid.NewGuid(), Steps = [new() { Request = ping }, new() { Request = ping }] };
        using var cancellation = new CancellationTokenSource();

        // Act
        var outcome = await RunAsync(workflow, new(() => Task.Delay(Timeout.Infinite).ContinueWith(_ => Ok("{}"))), cancellationToken: cancellation.Token, told: workflowEvent =>
        {
            if (workflowEvent is StepStarted)
            {
                cancellation.Cancel();
            }
        });

        // Assert
        Assert.Equal((RunOutcome.Cancelled, "Cancelled Skipped"), (outcome, string.Join(" ", Single<RunFinished>().Steps.Select(step => step.Outcome))));
    }

    [Fact]
    public async Task RunAsync_WhenTheRunEnds_ThenItsLogHoldsTheSameEvents()
    {
        // Arrange
        var ping = Request("https://dev.local/ping");

        // Act
        await RunAsync(new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping }] }, Answering(Ok("{}")));

        // Assert
        Assert.Equal(string.Concat(_events.Select(workflowEvent => Encoding.UTF8.GetString(RunLog.LineOf(workflowEvent).Span))), File.ReadAllText(Single<RunStarted>().RunFile));
    }

    [Fact]
    public async Task RunAsync_WhenTheRunLogCannotBeWritten_ThenStillRuns()
    {
        // Arrange
        var ping = Request("https://dev.local/ping");
        Directory.CreateDirectory(_temporary.Path);
        File.WriteAllText(Folder.Runs, "");

        // Act
        var outcome = await RunAsync(new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping }] }, Answering(Ok("{}")));

        // Assert
        Assert.Equal(RunOutcome.Succeeded, outcome);
    }

    [Fact]
    public async Task RunAsync_WhenTheRunStarts_ThenTellsTheParametersWithTheirDefaults()
    {
        // Arrange
        var workflow = new Workflow { Id = Guid.NewGuid(), Parameters = [new("orderId"), new("pageSize") { Default = JsonSerializer.SerializeToElement(50) }], Variables = [new("token")] };

        // Act
        await RunAsync(workflow, Answering(), new() { ["orderId"] = JsonSerializer.SerializeToElement("o-17") });

        // Assert
        Assert.Equal("""{"orderId":"o-17","pageSize":50}""", JsonSerializer.Serialize(Single<RunStarted>().Parameters));
    }

    [Fact]
    public async Task RunAsync_WhenTheRunFinishes_ThenTellsTheParametersAndTheVariablesWithAValue()
    {
        // Arrange
        var workflow = new Workflow { Id = Guid.NewGuid(), Parameters = [new("orderId"), new("pageSize") { Default = JsonSerializer.SerializeToElement(50) }], Variables = [new("token")] };

        // Act
        await RunAsync(workflow, Answering(), new() { ["orderId"] = JsonSerializer.SerializeToElement("o-17") });

        // Assert
        Assert.Equal("""{"orderId":"o-17","pageSize":50}""", JsonSerializer.Serialize(Single<RunFinished>().Variables));
    }

    [Fact]
    public async Task RunAsync_WhenAParameterWithADefaultIsGiven_ThenUsesTheGivenValue()
    {
        // Arrange
        var workflow = new Workflow { Id = Guid.NewGuid(), Parameters = [new("pageSize") { Default = JsonSerializer.SerializeToElement(50) }] };

        // Act
        await RunAsync(workflow, Answering(), new() { ["pageSize"] = JsonSerializer.SerializeToElement(10) });

        // Assert
        Assert.Equal("""{"pageSize":10}""", JsonSerializer.Serialize(Single<RunStarted>().Parameters));
    }

    [Fact]
    public async Task RunAsync_WhenAVariableHasADefault_ThenSendsTheDefaultInsteadOfTheEnvironmentValue()
    {
        // Arrange
        var order = Request("https://dev.local/{{token}}");
        var workflow = new Workflow { Id = Guid.NewGuid(), Variables = [new("token") { Default = JsonSerializer.SerializeToElement("abc") }], Steps = [new() { Request = order }] };
        var environment = new ApiEnvironment("Dev", [new("token", "old")]);

        // Act
        await RunAsync(workflow, Answering(Ok("{}")), environment: environment);

        // Assert
        Assert.Equal("dev.local/abc", Single<StepStarted>().Address);
    }
}
