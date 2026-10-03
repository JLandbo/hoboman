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

    RequestLibrary Library => new(Folder, NullLogger<RequestLibrary>.Instance);

    static FakeSender Answering(params ApiResponse[] responses)
    {
        var queue = new Queue<ApiResponse>(responses);
        return new(() => Task.FromResult(queue.Dequeue()));
    }

    static ApiResponse Ok(string body, params ResponseHeader[] headers) => new(200, "OK", 1, body.Length, headers, body);

    static ApiRequest Request(string url) => ApiRequest.New() with { Url = url, Auth = AuthSettings.None };

    async Task<Guid> SaveAsync(string name, ApiRequest request)
    {
        await Library.SaveAsync(name, request, Cancellation);
        return request.Id;
    }

    async Task<RunOutcome> RunAsync(Workflow workflow, FakeSender sender, Dictionary<string, JsonElement>? parameters = null, CancellationToken? cancellationToken = null, Action<WorkflowEvent>? told = null, ApiEnvironment? environment = null)
    {
        environment ??= ApiEnvironment.None;
        var check = new WorkflowCheck(Library, new SecretStore(Folder, NullLogger<SecretStore>.Instance), NullLogger<WorkflowCheck>.Instance);
        var checkedWorkflow = await check.CheckAsync("Ordre-sync", workflow, environment, parameters ?? [], Cancellation);
        var runner = new WorkflowRunner(new RequestRunner(sender, Library, new HistoryStore(Folder, NullLogger<HistoryStore>.Instance), NullLogger<RequestRunner>.Instance), Folder, NullLogger<WorkflowRunner>.Instance);
        return await runner.RunAsync(checkedWorkflow, environment, HistorySource.Cli, _ => Task.FromResult(false), workflowEvent =>
        {
            _events.Add(workflowEvent);
            told?.Invoke(workflowEvent);
            return Task.CompletedTask;
        }, cancellationToken ?? Cancellation);
    }

    T Single<T>() where T : WorkflowEvent => Assert.Single(_events.OfType<T>());

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public async Task RunAsync_WhenEveryStepSucceeds_ThenTellsTheEventsInOrder()
    {
        // Arrange
        var ping = await SaveAsync("Ping", Request("https://dev.local/ping"));

        // Act
        var outcome = await RunAsync(new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping }] }, Answering(Ok("{}")));

        // Assert
        Assert.Equal((RunOutcome.Succeeded, "RunStarted StepStarted StepFinished RunFinished"), (outcome, string.Join(" ", _events.Select(workflowEvent => workflowEvent.GetType().Name))));
    }

    [Fact]
    public async Task RunAsync_WhenTheRequestIsMoved_ThenStillRunsIt()
    {
        // Arrange
        var login = await SaveAsync("Shop/Login", Request("https://dev.local/login"));
        await Library.RenameAsync("Shop/Login", "Auth/Login", Cancellation);

        // Act
        var outcome = await RunAsync(new() { Id = Guid.NewGuid(), Steps = [new() { Request = login }] }, Answering(Ok("{}")));

        // Assert
        Assert.Equal((RunOutcome.Succeeded, "Auth/Login"), (outcome, Single<StepStarted>().Request));
    }

    [Fact]
    public async Task RunAsync_WhenTheSameRequestRunsTwiceWithDifferentWith_ThenEachSendsItsOwnValue()
    {
        // Arrange
        var order = await SaveAsync("Hent ordre", Request("https://dev.local/orders/{{orderId}}"));
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Parameters = [new("first"), new("second")],
            Steps = [new() { Request = order, With = [new("orderId", "{{first}}")] }, new() { Request = order, With = [new("orderId", "{{second}}-b")] }],
        };
        var parameters = new Dictionary<string, JsonElement> { ["first"] = JsonSerializer.SerializeToElement("o-17"), ["second"] = JsonSerializer.SerializeToElement("o-18") };

        // Act
        await RunAsync(workflow, Answering(Ok("{}"), Ok("{}")), parameters);

        // Assert
        Assert.Equal(["dev.local/orders/o-17", "dev.local/orders/o-18-b"], _events.OfType<StepStarted>().Select(started => started.Address));
    }

    [Fact]
    public async Task RunAsync_WhenAWithValueUsesAnotherWithName_ThenUsesTheRunValue()
    {
        // Arrange
        var page = await SaveAsync("Side", Request("https://dev.local/{{a}}/{{b}}"));
        var workflow = new Workflow { Id = Guid.NewGuid(), Parameters = [new("b")], Steps = [new() { Request = page, With = [new("a", "{{b}}"), new("b", "x")] }] };

        // Act
        await RunAsync(workflow, Answering(Ok("{}")), new() { ["b"] = JsonSerializer.SerializeToElement("y") });

        // Assert
        Assert.Equal("dev.local/y/x", Single<StepStarted>().Address);
    }

    [Fact]
    public async Task RunAsync_WhenOnlyTheFirstStepHasWith_ThenTheNextStepDoesNotSeeIt()
    {
        // Arrange
        var ping = await SaveAsync("Ping", Request("https://{{host}}/ping"));
        var workflow = new Workflow { Id = Guid.NewGuid(), Parameters = [new("host")], Steps = [new() { Request = ping, With = [new("host", "step.local")] }, new() { Request = ping }] };

        // Act
        await RunAsync(workflow, Answering(Ok("{}"), Ok("{}")), new() { ["host"] = JsonSerializer.SerializeToElement("dev.local") });

        // Assert
        Assert.Equal(["step.local/ping", "dev.local/ping"], _events.OfType<StepStarted>().Select(started => started.Address));
    }

    [Fact]
    public async Task RunAsync_WhenTheEnvironmentHasTheSavedName_ThenTheSavedValueWinsAndTheRestComesFromTheEnvironment()
    {
        // Arrange
        var login = await SaveAsync("Login", Request("https://{{host}}/login"));
        var order = await SaveAsync("Hent ordre", Request("https://{{host}}/{{token}}"));
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
        var login = await SaveAsync("Login", Request("https://dev.local/login"));
        var order = await SaveAsync("Hent ordre", Request("https://dev.local/orders") with { Headers = [new("Authorization", "Bearer {{token}}")] });
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
    public async Task RunAsync_WhenAnObjectIsSaved_ThenTheNextBodyIsByteIdentical()
    {
        // Arrange
        const string data = """{"id":"o-17","name":"Ordre æøå \"x\" \\ {{name}}","price":0.10,"lines":[{"sku":"a","qty":2}]}""";
        var order = await SaveAsync("Hent ordre", Request("https://dev.local/orders/o-17"));
        var import = await SaveAsync("Importér", Request("https://dev.local/import") with { Method = "POST", Body = """{"order": {{order}}}""", UseEnvironmentVariablesInBody = true });
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
        var order = await SaveAsync("Hent ordre", Request("https://dev.local/orders"));
        var item = await SaveAsync("Hent vare", Request("https://dev.local/items/{{itemId}}"));
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
        var import = await SaveAsync("Importér", Request("https://dev.local/import"));
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
        var import = await SaveAsync("Importér", Request("https://dev.local/import"));
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
        var import = await SaveAsync("Importér", Request("https://dev.local/import"));
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
        var ping = await SaveAsync("Ping", Request("https://dev.local/ping"));
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
        var ping = await SaveAsync("Ping", Request("https://dev.local/ping"));

        // Act
        await RunAsync(new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping }] }, new(() => throw new HttpRequestException("https://dev.local/?key=secret")));

        // Assert
        Assert.Equal("Network request failed.", Single<StepFinished>().Error);
    }

    [Fact]
    public async Task RunAsync_WhenTheCallTimesOut_ThenFailsTheStepWithoutCancellingTheRun()
    {
        // Arrange
        var ping = await SaveAsync("Ping", Request("https://dev.local/ping"));

        // Act
        var outcome = await RunAsync(new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping }] }, new(() => throw new TaskCanceledException()));

        // Assert
        Assert.Equal((RunOutcome.Failed, "Request timed out."), (outcome, Single<StepFinished>().Error));
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_ThenCancelsTheStepAndSkipsTheRest()
    {
        // Arrange
        var ping = await SaveAsync("Ping", Request("https://dev.local/ping"));
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
        var ping = await SaveAsync("Ping", Request("https://dev.local/ping"));

        // Act
        await RunAsync(new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping }] }, Answering(Ok("{}")));

        // Assert
        Assert.Equal(string.Concat(_events.Select(workflowEvent => Encoding.UTF8.GetString(RunLog.LineOf(workflowEvent).Span))), File.ReadAllText(Single<RunStarted>().RunFile));
    }

    [Fact]
    public async Task RunAsync_WhenTheRunLogCannotBeWritten_ThenStillRuns()
    {
        // Arrange
        var ping = await SaveAsync("Ping", Request("https://dev.local/ping"));
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
    public async Task RunAsync_WhenARequestFileChangesDuringTheRun_ThenSendsTheRequestAsItWasBeforeTheFirstCall()
    {
        // Arrange
        var ping = await SaveAsync("Ping", Request("https://dev.local/ping"));
        var order = await SaveAsync("Ordre", Request("https://dev.local/orders"));
        var workflow = new Workflow { Id = Guid.NewGuid(), Steps = [new() { Request = ping }, new() { Request = order }] };

        // Act
        await RunAsync(workflow, Answering(Ok("{}"), Ok("{}")), told: workflowEvent =>
        {
            if (workflowEvent is StepStarted { Index: 0 })
            {
                Library.SaveAsync("Ordre", Request("https://dev.local/changed") with { Id = order }, Cancellation).GetAwaiter().GetResult();
            }
        });

        // Assert
        Assert.Equal("dev.local/orders", _events.OfType<StepStarted>().Last().Address);
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
        var order = await SaveAsync("Hent ordre", Request("https://dev.local/{{token}}"));
        var workflow = new Workflow { Id = Guid.NewGuid(), Variables = [new("token") { Default = JsonSerializer.SerializeToElement("abc") }], Steps = [new() { Request = order }] };
        var environment = new ApiEnvironment("Dev", [new("token", "old")]);

        // Act
        await RunAsync(workflow, Answering(Ok("{}")), environment: environment);

        // Assert
        Assert.Equal("dev.local/abc", Single<StepStarted>().Address);
    }
}
