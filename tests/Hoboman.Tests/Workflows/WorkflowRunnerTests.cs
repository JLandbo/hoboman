using System.Net.Http;
using System.Text;
using System.Text.Json;
using Hoboman.Core.Scripts;

namespace Hoboman.Tests.Workflows;

public sealed class WorkflowRunnerTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();
    readonly List<WorkflowEvent> _events = [];
    readonly Dictionary<string, string> _scripts = [];
    readonly WatchedClock _clock = new();

    AppFolder Folder => new(_temporary.Path);

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static FakeSender Answering(params ApiResponse[] responses)
    {
        var queue = new Queue<ApiResponse>(responses);
        return new(() => Task.FromResult(queue.Dequeue()));
    }

    static ApiResponse Ok(string body, params ResponseHeader[] headers) => new(200, "OK", 1, body.Length, headers, body);

    static WorkflowRequest Request(string url) => new() { Url = url };

    static ApiResponse Status(int status) => new(status, "", 1, 0, [], "");

    static Workflow Retrying(WorkflowRetry retry, params WorkflowSave[] saves) =>
        new() { Id = Guid.NewGuid(), Variables = [.. saves.Select(save => new WorkflowValue(save.Variable))], Steps = [new() { Request = Request("https://dev.local/status"), Retry = retry, Saves = saves }] };

    async Task<RunOutcome> RunAsync(Workflow workflow, FakeSender sender, Dictionary<string, JsonElement>? parameters = null, CancellationToken? cancellationToken = null, Action<WorkflowEvent>? told = null, ApiEnvironment? environment = null,
        Func<AuthSource, Task<bool>>? fetchToken = null)
    {
        environment ??= ApiEnvironment.None;
        var library = new WorkflowLibrary(Folder, NullLogger<WorkflowLibrary>.Instance);
        foreach (var (name, code) in _scripts)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(library.ScriptPathOf(workflow.Id, name))!);
            await library.CreateScriptAsync(workflow.Id, name, code, Cancellation);
        }
        var check = new WorkflowCheck(library, new SecretStore(Folder, NullLogger<SecretStore>.Instance), NullLogger<WorkflowCheck>.Instance);
        var checkedWorkflow = await check.CheckAsync(workflow, environment, parameters ?? [], Cancellation);
        var runner = new WorkflowRunner(sender, Folder, _clock, NullLogger<WorkflowRunner>.Instance);
        return await runner.RunAsync(checkedWorkflow, environment, fetchToken ?? (_ => Task.FromResult(false)), workflowEvent =>
        {
            _events.Add(workflowEvent);
            told?.Invoke(workflowEvent);
            return Task.CompletedTask;
        }, cancellationToken ?? Cancellation);
    }

    T Single<T>() where T : WorkflowEvent => Assert.Single(_events.OfType<T>());

    // A script lies in the folder of the workflow it belongs to, which is known only when the workflow runs.
    void Script(string name, string code) => _scripts[name] = code;

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public async Task RunAsync_WhenAStepWaits_ThenGoesOnOnlyWhenTheTimeHasPassed()
    {
        // Arrange
        var workflow = new Workflow { Id = Guid.NewGuid(), Steps = [new() { DelaySeconds = 30 }, new() { Request = Request("https://dev.local/after") }] };
        var running = RunAsync(workflow, Answering(Ok("{}")));
        await _clock.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
        var startedBefore = _events.OfType<StepStarted>().Count();

        // Act
        _clock.Advance(TimeSpan.FromSeconds(30));
        var outcome = await running.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Assert
        Assert.Equal((1, RunOutcome.Succeeded, "WAIT", 30000L), (startedBefore, outcome, _events.OfType<StepStarted>().First().Method, _events.OfType<StepFinished>().First().ElapsedMs));
    }

    [Fact]
    public async Task RunAsync_WhenAWaitHasNoName_ThenItIsNamedInWholeWords()
    {
        // Arrange
        var running = RunAsync(new Workflow { Id = Guid.NewGuid(), Steps = [new() { DelaySeconds = 30 }] }, Answering());
        await _clock.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Act
        _clock.Advance(TimeSpan.FromSeconds(30));
        await running.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Assert
        Assert.Equal("Wait 30 seconds", Single<StepStarted>().Name);
    }

    [Fact]
    public async Task RunAsync_WhenABodyHasATemplatesOwnNames_ThenSendsThemAsWrittenWithTheWorkflowsValues()
    {
        // Arrange
        var sender = Answering(Ok("{}"));
        var request = new WorkflowRequest { Method = "POST", Url = "https://dev.local/docs/templates", BodyKind = BodyKind.Text, Body = "{{#each linjer}}{{tekst}}{{/each}} {{dokument}}" };
        var workflow = new Workflow { Id = Guid.NewGuid(), Parameters = [new("dokument") { Default = JsonSerializer.SerializeToElement("hoboman-test") }], Steps = [new() { Request = request }] };

        // Act
        var outcome = await RunAsync(workflow, sender);

        // Assert
        Assert.Equal((RunOutcome.Succeeded, "{{#each linjer}}{{tekst}}{{/each}} hoboman-test"), (outcome, sender.Environment!.Resolve(sender.Request!.Body)));
    }

    [Fact]
    public async Task RunAsync_WhenCancelledWhileAStepWaits_ThenStopsAtOnce()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var running = RunAsync(new Workflow { Id = Guid.NewGuid(), Steps = [new() { DelaySeconds = 300 }] }, Answering(), cancellationToken: cancellation.Token);
        await _clock.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Act
        cancellation.Cancel();
        var outcome = await running.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Assert
        Assert.Equal(RunOutcome.Cancelled, outcome);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_WhenAStepInheritsTheWorkflowsAuth_ThenSendsWithItUnderTheWorkflowsId(bool workflowHasAuth)
    {
        // Arrange
        var sender = Answering(Ok("{}"));
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Auth = workflowHasAuth ? new(AuthKind.Bearer) : null,
            Steps = [new() { Request = Request("https://dev.local/ping") with { Id = Guid.NewGuid(), Auth = new(AuthKind.Inherit) } }],
        };

        // Act
        await RunAsync(workflow, sender);

        // Assert
        Assert.Equal((workflow.Id, workflowHasAuth ? AuthKind.Bearer : AuthKind.None), (sender.Auth!.SecretsId, sender.Auth.Settings.Kind));
    }

    [Fact]
    public async Task RunAsync_WhenTheValueIsNotReadyYet_ThenTriesAgainAndSavesFromTheReadyAnswer()
    {
        // Arrange
        var workflow = Retrying(new() { Until = "$.status", Value = "Succeeded", Times = 5, WaitMilliseconds = 0 }, new WorkflowSave("token", "$.token"));

        // Act
        var outcome = await RunAsync(workflow, Answering(Ok("""{"status":"processing","token":"old"}"""), Ok("""{"status":"succeeded","token":"new"}""")));

        // Assert
        Assert.Equal((RunOutcome.Succeeded, 2, "processing", "new"),
            (outcome, Single<StepFinished>().Attempts, Single<StepRetrying>().Value, Single<RunFinished>().Variables["token"].GetString()));
    }

    [Fact]
    public async Task RunAsync_WhenARetryWaitsOnTheWholeTextBody_ThenComparesTheTextAsItIs()
    {
        // Arrange
        var answer = Ok("\"done\"", new ResponseHeader("Content-Type", "text/plain"));

        // Act
        var outcome = await RunAsync(Retrying(new() { Until = "$", Value = "\"done\"", Times = 2, WaitMilliseconds = 0 }), Answering(answer, answer));

        // Assert
        Assert.Equal((RunOutcome.Succeeded, 1), (outcome, Single<StepFinished>().Attempts));
    }

    [Fact]
    public async Task RunAsync_WhenTheAnswerIsNotASuccessYet_ThenTriesAgainUntilItIs()
    {
        // Act
        var outcome = await RunAsync(Retrying(new() { Times = 3, WaitMilliseconds = 0 }), Answering(Status(403), Status(403), Ok("{}")));

        // Assert
        Assert.Equal((RunOutcome.Succeeded, 3, "403 403"), (outcome, Single<StepFinished>().Attempts, string.Join(" ", _events.OfType<StepRetrying>().Select(retrying => retrying.Status))));
    }

    [Fact]
    public async Task RunAsync_WhenTheAnswerIsNeverReady_ThenFailsAfterTheLastAttempt()
    {
        // Act
        var outcome = await RunAsync(Retrying(new() { Until = "$.status", Value = "succeeded", Times = 3, WaitMilliseconds = 0 }),
            Answering(Ok("""{"status":"processing"}"""), Ok("""{"status":"processing"}"""), Ok("""{"status":"processing"}""")));

        // Assert
        Assert.Equal((RunOutcome.Failed, "The answer was not ready after 3 attempts.", 2), (outcome, Single<StepFinished>().Error, _events.OfType<StepRetrying>().Count()));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(500)]
    public async Task RunAsync_WhenTheAnswerTellsItFailed_ThenStopsAtOnce(int status)
    {
        // Arrange
        var retry = new WorkflowRetry { Until = "$.status", Value = "succeeded", StopIf = "$.status", StopEquals = "failed", Times = 5, WaitMilliseconds = 0 };
        var failed = new ApiResponse(status, "", 1, 0, [], """{"status":"Failed"}""");

        // Act
        var outcome = await RunAsync(Retrying(retry), Answering(Ok("""{"status":"processing"}"""), failed, Ok("""{"status":"succeeded"}""")));

        // Assert
        Assert.Equal((RunOutcome.Failed, 2, "Stopped as $.status was Failed."), (outcome, Single<StepFinished>().Attempts, Single<StepFinished>().Error));
    }

    [Fact]
    public async Task RunAsync_WhenASaveIsNotThereYet_ThenTriesAgainUntilItIs()
    {
        // Act
        var outcome = await RunAsync(Retrying(new() { Times = 3, WaitMilliseconds = 0 }, new WorkflowSave("url", "$.url")), Answering(Ok("{}"), Ok("""{"url":"https://s3.local/a.pdf"}""")));

        // Assert
        Assert.Equal((RunOutcome.Succeeded, "https://s3.local/a.pdf"), (outcome, Single<RunFinished>().Variables["url"].GetString()));
    }

    [Theory]
    [InlineData(true, RunOutcome.Succeeded, 1)]
    [InlineData(false, RunOutcome.Failed, 0)]
    public async Task RunAsync_WhenSendingFails_ThenTriesAgainOnlyWhenTheNetworkFailed(bool network, RunOutcome expected, int retries)
    {
        // Arrange
        var calls = 0;
        Exception failure = network ? new HttpRequestException("down") : new UriFormatException("bad");
        var sender = new FakeSender(() => calls++ == 0 ? Task.FromException<ApiResponse>(failure) : Task.FromResult(Ok("{}")));

        // Act
        var outcome = await RunAsync(Retrying(new() { Times = 3, WaitMilliseconds = 0 }), sender);

        // Assert
        Assert.Equal((expected, retries), (outcome, _events.OfType<StepRetrying>().Count()));
    }

    [Fact]
    public async Task RunAsync_WhenTheLastAttemptFails_ThenTellsHowManyAttemptsWereMade()
    {
        // Act
        await RunAsync(Retrying(new() { Times = 2, WaitMilliseconds = 0 }), new FakeSender(() => Task.FromException<ApiResponse>(new HttpRequestException("down"))));

        // Assert
        Assert.Equal((2, RequestProblem.TextOf(RequestProblemKind.NetworkFailed)), (Single<StepFinished>().Attempts, Single<StepFinished>().Error));
    }

    [Fact]
    public async Task RunAsync_WhenARetryWaitsAHundredMilliseconds_ThenTriesAgainOnlyWhenTheyHavePassed()
    {
        // Arrange
        var calls = 0;
        var running = RunAsync(Retrying(new() { Times = 2, WaitMilliseconds = 100 }), new FakeSender(() => Task.FromResult(calls++ == 0 ? Status(403) : Ok("{}"))));
        await _clock.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
        _clock.Advance(TimeSpan.FromMilliseconds(99));
        var callsBefore = calls;

        // Act
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        var outcome = await running.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Assert
        Assert.Equal((1, RunOutcome.Succeeded, 2), (callsBefore, outcome, calls));
    }

    [Fact]
    public async Task RunAsync_WhenCancelledWhileARetryWaits_ThenStopsAtOnce()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var running = RunAsync(Retrying(new() { Times = 5, WaitMilliseconds = 300000 }), new FakeSender(() => Task.FromResult(Status(403))), cancellationToken: cancellation.Token);
        await _clock.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Act
        cancellation.Cancel();
        var outcome = await running.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Assert
        Assert.Equal(RunOutcome.Cancelled, outcome);
    }

    [Fact]
    public async Task RunAsync_WhenAScriptMapsAnEarlierResponse_ThenALaterStepSendsItsOutput()
    {
        // Arrange
        var order = Request("https://dev.local/orders/1");
        var import = Request("https://dev.local/import/{{reference}}");
        Script("map.js", "return { reference: `${vars.order.id}-${vars.order.lines.length}` };");
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
        Script("map.js", "return { result: `${vars.host}-${vars.tenant}` };");
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
        Script("check.js", "if (!vars.order) { throw new Error('No order'); }");
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
    public async Task RunAsync_WhenAScriptAnswers_ThenItsOutputCanBeSavedAsBytes()
    {
        // Arrange
        Script("map.js", "return { navn: 'Dør' };");
        var workflow = new Workflow { Id = Guid.NewGuid(), Steps = [new() { Script = "map.js" }] };

        // Act
        await RunAsync(workflow, Answering());

        // Assert
        var finished = Single<StepFinished>();
        Assert.Equal(Encoding.UTF8.GetBytes(finished.Body!), finished.Bytes);
    }

    [Theory]
    [InlineData(null, "application/json", "\"<p>Dør</p>\"")]
    [InlineData(ScriptOutput.Json, "application/json", "\"<p>Dør</p>\"")]
    [InlineData(ScriptOutput.Html, "text/html; charset=utf-8", "<p>Dør</p>")]
    [InlineData(ScriptOutput.Xml, "application/xml; charset=utf-8", "<p>Dør</p>")]
    [InlineData(ScriptOutput.Text, "text/plain; charset=utf-8", "<p>Dør</p>")]
    public async Task RunAsync_WhenAScriptHasAnOutput_ThenAnswersWithItsType(ScriptOutput? output, string type, string body)
    {
        // Arrange
        Script("html.js", "return '<p>Dør</p>';");
        var workflow = new Workflow { Id = Guid.NewGuid(), Steps = [new() { Script = "html.js", Output = output }] };

        // Act
        await RunAsync(workflow, Answering());

        // Assert
        var finished = Single<StepFinished>();
        Assert.Equal((type, body), (Assert.Single(finished.Headers!).Value, finished.Body));
    }

    [Fact]
    public async Task RunAsync_WhenAScriptOfHtmlReturnsNoText_ThenFails()
    {
        // Arrange
        Script("html.js", "return { navn: 'Dør' };");
        var workflow = new Workflow { Id = Guid.NewGuid(), Steps = [new() { Script = "html.js", Output = ScriptOutput.Html }] };

        // Act
        var outcome = await RunAsync(workflow, Answering());

        // Assert
        Assert.Equal((RunOutcome.Failed, "html.js must return text when its output is Html."), (outcome, Single<StepFinished>().Error));
    }

    [Fact]
    public async Task RunAsync_WhenAScriptFails_ThenTellsItsLineAndSkipsTheRest()
    {
        // Arrange
        var ping = Request("https://dev.local/ping");
        Script("map.js", "const order = vars;\nthrow new Error('No order');");
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

    [Theory]
    [InlineData("text/plain; charset=utf-8", JsonValueKind.String)]
    [InlineData("application/json", JsonValueKind.Object)]
    [InlineData(null, JsonValueKind.Object)]
    public async Task RunAsync_WhenTheWholeBodyIsSaved_ThenItIsTextUnlessTheAnswerMayBeJson(string? type, JsonValueKind saved)
    {
        // Arrange
        var import = Request("https://dev.local/import");
        var workflow = new Workflow { Id = Guid.NewGuid(), Variables = [new("body")], Steps = [new() { Request = import, Saves = [new("body", "$")] }] };

        // Act
        await RunAsync(workflow, Answering(type is null ? Ok("""{"id": 7}""") : Ok("""{"id": 7}""", new ResponseHeader("Content-Type", type))));

        // Assert
        Assert.Equal(saved, Single<RunFinished>().Variables["body"].ValueKind);
    }

    [Fact]
    public async Task RunAsync_WhenAPathIsSavedFromJsonSentAsText_ThenReadsIt()
    {
        // Arrange
        var import = Request("https://dev.local/import");
        var workflow = new Workflow { Id = Guid.NewGuid(), Variables = [new("id")], Steps = [new() { Request = import, Saves = [new("id", "$.id")] }] };

        // Act
        await RunAsync(workflow, Answering(Ok("""{"id": 7}""", new ResponseHeader("Content-Type", "text/html"))));

        // Assert
        Assert.Equal("7", Single<RunFinished>().Variables["id"].GetRawText());
    }

    [Fact]
    public async Task RunAsync_WhenASaveIsAJsonValue_ThenSavesItAsIs()
    {
        // Arrange
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Variables = [new("text"), new("number")],
            Steps = [new() { Request = Request("https://dev.local/import"), Saves = [new("text", "\"1\""), new("number", "2")] }],
        };

        // Act
        await RunAsync(workflow, Answering(Ok("{}")));

        // Assert
        Assert.Equal("""{"text":"1","number":2}""", JsonSerializer.Serialize(Single<StepFinished>().Saved));
    }

    [Fact]
    public async Task RunAsync_WhenAStepIsAnswered_ThenItsBytesGoWithTheStepButNotIntoItsEvent()
    {
        // Arrange
        var workflow = new Workflow { Id = Guid.NewGuid(), Steps = [new() { Request = Request("https://dev.local/file.pdf") }] };

        // Act
        await RunAsync(workflow, Answering(Ok("%PDF") with { Bytes = [0x25, 0x50, 0x44, 0x46] }));

        // Assert
        Assert.Equal([0x25, 0x50, 0x44, 0x46], Single<StepFinished>().Bytes);
        Assert.DoesNotContain("bytes", JsonSerializer.Serialize<WorkflowEvent>(Single<StepFinished>(), JsonSerializerOptions.Web), StringComparison.OrdinalIgnoreCase);
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
