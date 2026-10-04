using System.Text.Json;

namespace Hoboman.Tests.Workflows;

public sealed class WorkflowCheckTests
{
    static WorkflowRequest Login => new() { Method = "POST", Url = "https://dev.local/login" };

    static WorkflowRequest Order => new() { Url = "https://dev.local/orders/{{orderId}}", Headers = [new("Authorization", "Bearer {{token}}")] };

    static WorkflowStep LoginStep => new() { Request = Login, Saves = [new("token", "$.access_token")] };

    static WorkflowStep OrderStep => new() { Name = "Hent ordre", Request = Order };

    static Workflow OrderSync(params WorkflowStep[] steps) => new() { Id = Guid.NewGuid(), Parameters = [new("orderId")], Variables = [new("token")], Steps = steps };

    static Dictionary<string, JsonElement> Parameters(params string[] names) => names.ToDictionary(name => name, name => JsonSerializer.SerializeToElement("o-17"));

    static CheckedWorkflow Check(Workflow workflow, Dictionary<string, JsonElement>? parameters = null, ApiEnvironment? environment = null, Dictionary<string, string?>? scripts = null) =>
        WorkflowCheck.Check("Ordre-sync", workflow, environment ?? ApiEnvironment.None, parameters ?? Parameters("orderId"), scripts);

    [Theory]
    [InlineData(AuthKind.Basic, SecretKind.Password)]
    [InlineData(AuthKind.Bearer, SecretKind.Token)]
    public async Task CheckAsync_WhenTheSecretOfAStepUsesAName_ThenChecksIt(AuthKind kind, SecretKind secret)
    {
        // Arrange
        using var temporary = new TemporaryFolder();
        var folder = new AppFolder(temporary.Path);
        var secrets = new SecretStore(folder, NullLogger<SecretStore>.Instance);
        var id = Guid.NewGuid();
        await secrets.SaveAsync(id, secret, "{{token}}", TestContext.Current.CancellationToken);
        var check = new WorkflowCheck(new WorkflowLibrary(folder, NullLogger<WorkflowLibrary>.Instance), secrets, NullLogger<WorkflowCheck>.Instance);
        var order = new WorkflowStep { Request = Login with { Id = id, Auth = new(kind) } };

        // Act
        var checkedWorkflow = await check.CheckAsync("Ordre-sync", OrderSync(order, LoginStep), ApiEnvironment.None, Parameters("orderId"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([new(WorkflowProblemKind.UsedBeforeSaved, 0, "token")], checkedWorkflow.Problems);
    }

    [Fact]
    public async Task CheckAsync_WhenTheWorkflowsSecretUsesAName_ThenChecksItForAStepThatInherits()
    {
        // Arrange
        using var temporary = new TemporaryFolder();
        var folder = new AppFolder(temporary.Path);
        var secrets = new SecretStore(folder, NullLogger<SecretStore>.Instance);
        var workflow = OrderSync(new WorkflowStep { Request = Login with { Auth = new(AuthKind.Inherit) } }, LoginStep) with { Auth = new(AuthKind.Bearer) };
        await secrets.SaveAsync(workflow.Id, SecretKind.Token, "{{token}}", TestContext.Current.CancellationToken);
        var check = new WorkflowCheck(new WorkflowLibrary(folder, NullLogger<WorkflowLibrary>.Instance), secrets, NullLogger<WorkflowCheck>.Instance);

        // Act
        var checkedWorkflow = await check.CheckAsync("Ordre-sync", workflow, ApiEnvironment.None, Parameters("orderId"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([new(WorkflowProblemKind.UsedBeforeSaved, 0, "token")], checkedWorkflow.Problems);
    }

    [Fact]
    public void Check_WhenAScriptSavesAVariable_ThenALaterStepCanUseIt()
    {
        // Act
        var checkedWorkflow = Check(OrderSync(new WorkflowStep { Script = "token.js", Saves = [new("token", "$")] }, OrderStep), scripts: new() { ["token.js"] = "return 'abc';" });

        // Assert
        Assert.Equal((0, "token.js"), (checkedWorkflow.Problems.Count, checkedWorkflow.Steps[0].Title));
    }

    [Theory]
    [InlineData(null, WorkflowProblemKind.ScriptNotFound, "map.js")]
    [InlineData("let a = 1;\nreturn a +;", WorkflowProblemKind.InvalidScript, "Unexpected token ';' (map.js:2:11)")]
    public void Check_WhenAScriptIsMissingOrInvalid_ThenReportsIt(string? code, WorkflowProblemKind kind, string detail)
    {
        // Act
        var checkedWorkflow = Check(OrderSync(new WorkflowStep { Script = "map.js" }), scripts: new() { ["map.js"] = code });

        // Assert
        Assert.Equal([new(kind, 0, detail)], checkedWorkflow.Problems);
    }

    [Fact]
    public void Check_WhenEveryNameHasAValue_ThenFindsNoProblemsAndTheTitlesOfTheSteps()
    {
        // Act
        var checkedWorkflow = Check(OrderSync(LoginStep, OrderStep));

        // Assert
        Assert.Equal((0, "POST dev.local/login, Hent ordre"), (checkedWorkflow.Problems.Count, string.Join(", ", checkedWorkflow.Steps.Select(step => step.Title))));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(301, false)]
    [InlineData(30, true)]
    public void Check_WhenAWaitIsOutOfRangeOrSaves_ThenReportsIt(int seconds, bool saves)
    {
        // Act
        var checkedWorkflow = Check(OrderSync(new WorkflowStep { DelaySeconds = seconds, Saves = saves ? [new("token", "$")] : [] }));

        // Assert
        Assert.Equal([new(WorkflowProblemKind.InvalidDelay, 0, $"{seconds}")], checkedWorkflow.Problems);
    }

    [Theory]
    [InlineData("$.status", null, 5, 0, false)]
    [InlineData("nope", "x", 5, 0, false)]
    [InlineData("\"done\"", "done", 5, 0, false)]
    [InlineData(null, null, 0, 0, false)]
    [InlineData(null, null, 101, 0, false)]
    [InlineData(null, null, 5, 301, false)]
    [InlineData(null, null, 5, 0, true)]
    public void Check_WhenARetryIsNotValid_ThenReportsIt(string? until, string? equals, int times, int waitSeconds, bool script)
    {
        // Arrange
        var step = script ? new WorkflowStep { Script = "token.js" } : new WorkflowStep { Request = Login };

        // Act
        var checkedWorkflow = Check(OrderSync(step with { Retry = new() { Until = until, Value = equals, Times = times, WaitSeconds = waitSeconds } }), scripts: new() { ["token.js"] = "return 1;" });

        // Assert
        Assert.Equal([new(WorkflowProblemKind.InvalidRetry, 0, until ?? "")], checkedWorkflow.Problems);
    }

    [Theory]
    [InlineData("$.status", null)]
    [InlineData(null, "failed")]
    [InlineData("nope", "failed")]
    public void Check_WhenAStopIsNotValid_ThenReportsIt(string? stopIf, string? stopEquals)
    {
        // Act
        var checkedWorkflow = Check(OrderSync(new WorkflowStep { Request = Login, Retry = new() { StopIf = stopIf, StopEquals = stopEquals } }));

        // Assert
        Assert.Equal([new(WorkflowProblemKind.InvalidRetry, 0, "")], checkedWorkflow.Problems);
    }

    [Fact]
    public void Check_WhenAStepHasBothARequestAndAScript_ThenReportsIt()
    {
        // Act
        var checkedWorkflow = Check(OrderSync(new WorkflowStep { Request = Login, Script = "token.js" }), scripts: new() { ["token.js"] = "return 'abc';" });

        // Assert
        Assert.Equal([new(WorkflowProblemKind.MixedStep, 0, "")], checkedWorkflow.Problems);
    }

    [Theory]
    [InlineData("https://dev.local/login?code=key")]
    [InlineData("dev.local/login?code=key")]
    public void Check_WhenAStepHasNoName_ThenItsTitleLeavesOutTheQuery(string url)
    {
        // Act
        var checkedWorkflow = Check(OrderSync(new WorkflowStep { Request = Login with { Url = url } }));

        // Assert
        Assert.Equal("POST dev.local/login", checkedWorkflow.Steps[0].Title);
    }

    [Fact]
    public void Check_WhenAStepUsesAVariableBeforeItIsSaved_ThenReportsIt()
    {
        // Act
        var checkedWorkflow = Check(OrderSync(OrderStep, LoginStep));

        // Assert
        Assert.Equal([new(WorkflowProblemKind.UsedBeforeSaved, 0, "token")], checkedWorkflow.Problems);
    }

    [Fact]
    public void Check_WhenAStepUsesWhatItSavesItself_ThenReportsUsedBeforeSaved()
    {
        // Act
        var checkedWorkflow = Check(OrderSync(OrderStep with { Saves = [new("token", "$.token")] }));

        // Assert
        Assert.Equal([new(WorkflowProblemKind.UsedBeforeSaved, 0, "token")], checkedWorkflow.Problems);
    }

    [Fact]
    public void Check_WhenAVariableHasADefault_ThenItIsAvailable()
    {
        // Act
        var checkedWorkflow = Check(OrderSync(OrderStep) with { Variables = [new("token") { Default = JsonSerializer.SerializeToElement("abc") }] });

        // Assert
        Assert.Empty(checkedWorkflow.Problems);
    }

    [Fact]
    public void Check_WhenADeclaredNameIsOnlyInTheEnvironment_ThenReportsIt()
    {
        // Arrange
        var environment = new ApiEnvironment("Demo", [new("token", "old")]);

        // Act
        var checkedWorkflow = Check(OrderSync(OrderStep), environment: environment);

        // Assert
        Assert.Equal([new(WorkflowProblemKind.UsedBeforeSaved, 0, "token")], checkedWorkflow.Problems);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void Check_WhenANameIsNotDeclared_ThenLooksForItInTheEnvironment(bool enabled, int expected)
    {
        // Arrange
        var workflow = new Workflow { Id = Guid.NewGuid(), Steps = [new() { Request = new() { Url = "https://{{host}}/ping" } }] };

        // Act
        var checkedWorkflow = Check(workflow, [], new ApiEnvironment("Demo", [new("host", "dev.local", enabled)]));

        // Assert
        Assert.Equal(expected, checkedWorkflow.Problems.Count(problem => problem == new WorkflowProblem(WorkflowProblemKind.UnknownName, 0, "host")));
    }

    [Fact]
    public void Check_WhenARequiredParameterIsMissing_ThenReportsIt()
    {
        // Act
        var checkedWorkflow = Check(OrderSync(LoginStep, OrderStep), parameters: []);

        // Assert
        Assert.Equal([new(WorkflowProblemKind.MissingParameter, null, "orderId")], checkedWorkflow.Problems);
    }

    [Theory]
    [InlineData("pageSize")]
    [InlineData("token")]
    public void Check_WhenAGivenParameterIsNotDeclaredAsOne_ThenReportsIt(string name)
    {
        // Act
        var checkedWorkflow = Check(OrderSync(LoginStep, OrderStep), parameters: Parameters("orderId", name));

        // Assert
        Assert.Equal([new(WorkflowProblemKind.UnknownParameter, null, name)], checkedWorkflow.Problems);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Check_WhenAStepHasNoUrl_ThenReportsIt(bool hasRequest)
    {
        // Act
        var checkedWorkflow = Check(OrderSync(hasRequest ? new WorkflowStep { Request = new() { Url = " " } } : new WorkflowStep()));

        // Assert
        Assert.Equal([new(WorkflowProblemKind.MissingUrl, 0, "")], checkedWorkflow.Problems);
    }

    [Fact]
    public void Check_WhenTheWorkflowHasNoId_ThenReportsIt()
    {
        // Act
        var checkedWorkflow = Check(OrderSync(LoginStep) with { Id = Guid.Empty });

        // Assert
        Assert.Equal([new(WorkflowProblemKind.MissingId, null, "Ordre-sync")], checkedWorkflow.Problems);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{{token}}")]
    public void Check_WhenANameIsInvalid_ThenReportsIt(string name)
    {
        // Act
        var checkedWorkflow = Check(new Workflow { Id = Guid.NewGuid(), Variables = [new(name)] }, parameters: []);

        // Assert
        Assert.Equal([new(WorkflowProblemKind.InvalidName, null, name)], checkedWorkflow.Problems);
    }

    [Fact]
    public void Check_WhenANameIsBothAParameterAndAVariable_ThenReportsIt()
    {
        // Act
        var checkedWorkflow = Check(new Workflow { Id = Guid.NewGuid(), Parameters = [new("orderId")], Variables = [new("orderId")] });

        // Assert
        Assert.Equal([new(WorkflowProblemKind.DuplicateName, null, "orderId")], checkedWorkflow.Problems);
    }

    [Theory]
    [InlineData("orderId")]
    [InlineData("unknown")]
    public void Check_WhenAStepSavesIntoSomethingElseThanAVariable_ThenReportsIt(string name)
    {
        // Act
        var checkedWorkflow = Check(OrderSync(LoginStep, LoginStep with { Saves = [new(name, "status")] }, OrderStep));

        // Assert
        Assert.Equal([new(WorkflowProblemKind.NotAVariable, 1, name)], checkedWorkflow.Problems);
    }

    [Theory]
    [InlineData("$.items[*].id")]
    [InlineData("header:")]
    [InlineData("body")]
    public void Check_WhenASaveHasAnInvalidSource_ThenReportsIt(string from)
    {
        // Act
        var checkedWorkflow = Check(OrderSync(LoginStep, LoginStep with { Saves = [new("token", from)] }, OrderStep));

        // Assert
        Assert.Equal([new(WorkflowProblemKind.InvalidSource, 1, from)], checkedWorkflow.Problems);
    }

    [Theory]
    [InlineData("\"1\"")]
    [InlineData("2")]
    [InlineData("null")]
    [InlineData("""{"a":[true]}""")]
    public void Check_WhenASaveIsAJsonValue_ThenAcceptsIt(string from)
    {
        // Act
        var checkedWorkflow = Check(OrderSync(LoginStep, LoginStep with { Saves = [new("token", from)] }, OrderStep));

        // Assert
        Assert.Empty(checkedWorkflow.Problems);
    }

    [Theory]
    [InlineData(BodyKind.None, true, 0)]
    [InlineData(BodyKind.Json, true, 1)]
    [InlineData(BodyKind.Json, false, 0)]
    public void Check_WhenTheBodyUsesAName_ThenChecksItOnlyWhenTheBodyIsFilledIn(BodyKind kind, bool useVariables, int expected)
    {
        // Arrange
        var login = LoginStep with { Request = Login with { Body = "{{missing}}", BodyKind = kind, UseEnvironmentVariablesInBody = useVariables } };

        // Act
        var checkedWorkflow = Check(OrderSync(login));

        // Assert
        Assert.Equal(expected, checkedWorkflow.Problems.Count);
    }

    [Theory]
    [InlineData(true, false, true, 1)]
    [InlineData(true, false, false, 0)]
    [InlineData(false, false, true, 1)]
    [InlineData(false, false, false, 0)]
    [InlineData(true, true, true, 1)]
    [InlineData(true, true, false, 0)]
    [InlineData(false, true, true, 1)]
    [InlineData(false, true, false, 0)]
    public void Check_WhenAQueryOrHeaderUsesAName_ThenChecksItOnlyWhenEnabled(bool query, bool inName, bool enabled, int expected)
    {
        // Arrange
        var entry = inName ? new KeyValue("{{missing}}", "x", enabled) : new KeyValue("x", "{{missing}}", enabled);
        var login = LoginStep with { Request = query ? Login with { Query = [entry] } : Login with { Headers = [entry] } };

        // Act
        var checkedWorkflow = Check(OrderSync(login));

        // Assert
        Assert.Equal(expected, checkedWorkflow.Problems.Count);
    }
}
