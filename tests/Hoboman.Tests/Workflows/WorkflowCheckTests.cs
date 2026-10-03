using System.Text.Json;

namespace Hoboman.Tests.Workflows;

public sealed class WorkflowCheckTests : IDisposable
{
    static readonly Guid _login = Guid.NewGuid();
    static readonly Guid _order = Guid.NewGuid();

    readonly TemporaryFolder _temporary = new();

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static ApiRequest Login => new() { Id = _login, Method = "POST", Url = "https://dev.local/login" };

    static ApiRequest Order => new() { Id = _order, Url = "https://dev.local/orders/{{orderId}}", Headers = [new("Authorization", "Bearer {{token}}")] };

    static List<(string Name, ApiRequest? Request)> Requests => [("Shop/Login", Login), ("Shop/Hent ordre", Order)];

    static WorkflowStep LoginStep => new() { Request = _login, Saves = [new("token", "$.access_token")] };

    static WorkflowStep OrderStep => new() { Request = _order };

    static Workflow OrderSync(params WorkflowStep[] steps) => new() { Id = Guid.NewGuid(), Parameters = [new("orderId")], Variables = [new("token")], Steps = steps };

    static Dictionary<string, JsonElement> Parameters(params string[] names) => names.ToDictionary(name => name, name => JsonSerializer.SerializeToElement("o-17"));

    static CheckedWorkflow Check(Workflow workflow, List<(string Name, ApiRequest? Request)>? requests = null, Dictionary<string, JsonElement>? parameters = null,
        ApiEnvironment? environment = null, Dictionary<string, IReadOnlyList<string>>? authTexts = null) =>
        WorkflowCheck.Check("Ordre-sync", workflow, requests ?? Requests, authTexts ?? [], environment ?? ApiEnvironment.None, parameters ?? Parameters("orderId"));

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public void Check_WhenEveryNameHasAValue_ThenFindsNoProblemsAndThePathsOfTheSteps()
    {
        // Act
        var checkedWorkflow = Check(OrderSync(LoginStep, OrderStep));

        // Assert
        Assert.Equal((0, "Shop/Login, Shop/Hent ordre"), (checkedWorkflow.Problems.Count, string.Join(", ", checkedWorkflow.Steps.Select(step => step.Path))));
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
        var ping = new ApiRequest { Id = Guid.NewGuid(), Url = "https://{{host}}/ping" };
        var workflow = new Workflow { Id = Guid.NewGuid(), Steps = [new() { Request = ping.Id }] };

        // Act
        var checkedWorkflow = Check(workflow, [("Ping", ping)], [], new ApiEnvironment("Demo", [new("host", "dev.local", enabled)]));

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

    [Fact]
    public void Check_WhenTwoRequestsShareTheId_ThenNamesBoth()
    {
        // Act
        var checkedWorkflow = Check(OrderSync(LoginStep, OrderStep), [.. Requests, ("Shop/Login - kopi", Login)]);

        // Assert
        Assert.Equal([new(WorkflowProblemKind.SharedRequestId, 0, "Shop/Login, Shop/Login - kopi")], checkedWorkflow.Problems);
    }

    [Fact]
    public void Check_WhenNoRequestHasTheId_ThenReportsItAndTheFilesThatCouldNotBeRead()
    {
        // Act
        var checkedWorkflow = Check(OrderSync(LoginStep), [("Shop/Broken", null)]);

        // Assert
        Assert.Equal([new(WorkflowProblemKind.RequestNotFound, 0, $"{_login}"), new(WorkflowProblemKind.UnreadableRequests, null, "Shop/Broken")], checkedWorkflow.Problems);
    }

    [Fact]
    public void Check_WhenAStepHasNoRequest_ThenReportsIt()
    {
        // Act
        var checkedWorkflow = Check(OrderSync(new WorkflowStep()));

        // Assert
        Assert.Equal([new(WorkflowProblemKind.MissingRequest, 0, "")], checkedWorkflow.Problems);
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
    public void Check_WhenAWithNameIsInvalid_ThenReportsIt()
    {
        // Act
        var checkedWorkflow = Check(OrderSync(LoginStep, OrderStep with { With = [new("{{orderId}}", "o-18")] }));

        // Assert
        Assert.Equal([new(WorkflowProblemKind.InvalidName, 1, "{{orderId}}")], checkedWorkflow.Problems);
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

    [Fact]
    public void Check_WhenAWithNameIsNotUsedByTheRequest_ThenReportsIt()
    {
        // Act
        var checkedWorkflow = Check(OrderSync(LoginStep, OrderStep with { With = [new("orderID", "o-18")] }));

        // Assert
        Assert.Equal([new(WorkflowProblemKind.UnusedWithName, 1, "orderID")], checkedWorkflow.Problems);
    }

    [Fact]
    public void Check_WhenAWithEntryIsDisabled_ThenIgnoresIt()
    {
        // Act
        var checkedWorkflow = Check(OrderSync(LoginStep, OrderStep with { With = [new("orderID", "{{missing}}", false)] }));

        // Assert
        Assert.Empty(checkedWorkflow.Problems);
    }

    [Fact]
    public void Check_WhenAWithEntryGivesANameThatIsNotSavedYet_ThenTheStepHasIt()
    {
        // Act
        var checkedWorkflow = Check(OrderSync(OrderStep with { With = [new("token", "fixed")] }));

        // Assert
        Assert.Empty(checkedWorkflow.Problems);
    }

    [Fact]
    public void Check_WhenAWithValueUsesAVariableBeforeItIsSaved_ThenReportsIt()
    {
        // Act
        var checkedWorkflow = Check(OrderSync(OrderStep with { With = [new("token", "{{token}}")] }, LoginStep));

        // Assert
        Assert.Equal([new(WorkflowProblemKind.UsedBeforeSaved, 0, "token")], checkedWorkflow.Problems);
    }

    [Fact]
    public void Check_WhenTheAuthUsesAName_ThenChecksIt()
    {
        // Act
        var checkedWorkflow = Check(OrderSync(OrderStep, LoginStep), [("Shop/Login", Login), ("Shop/Hent ordre", Order with { Headers = [] })], authTexts: new() { ["Shop/Hent ordre"] = ["{{token}}"] });

        // Assert
        Assert.Equal([new(WorkflowProblemKind.UsedBeforeSaved, 0, "token")], checkedWorkflow.Problems);
    }

    [Theory]
    [InlineData(false, BodyKind.Json, 0)]
    [InlineData(true, BodyKind.None, 0)]
    [InlineData(true, BodyKind.Json, 1)]
    public void Check_WhenTheBodyUsesAName_ThenChecksItOnlyWhenTheBodyIsFilledIn(bool useVariables, BodyKind kind, int expected)
    {
        // Arrange
        var login = Login with { Body = "{{missing}}", BodyKind = kind, UseEnvironmentVariablesInBody = useVariables };

        // Act
        var checkedWorkflow = Check(OrderSync(LoginStep), [("Shop/Login", login)]);

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
        var login = query ? Login with { Query = [entry] } : Login with { Headers = [entry] };

        // Act
        var checkedWorkflow = Check(OrderSync(LoginStep), [("Shop/Login", login)]);

        // Assert
        Assert.Equal(expected, checkedWorkflow.Problems.Count);
    }

    [Theory]
    [InlineData(AuthKind.Basic, 1)]
    [InlineData(AuthKind.Bearer, 0)]
    public async Task CheckAsync_WhenNoSecretIsSaved_ThenChecksOnlyTheUserNameOfBasic(AuthKind kind, int expected)
    {
        // Arrange
        var folder = new AppFolder(_temporary.Path);
        var library = new RequestLibrary(folder, NullLogger<RequestLibrary>.Instance);
        await library.SaveAsync("Shop/Login", Login with { Auth = new(kind, "{{user}}") }, Cancellation);
        var check = new WorkflowCheck(library, new SecretStore(folder, NullLogger<SecretStore>.Instance), NullLogger<WorkflowCheck>.Instance);

        // Act
        var checkedWorkflow = await check.CheckAsync("Ordre-sync", OrderSync(LoginStep), ApiEnvironment.None, Parameters("orderId"), Cancellation);

        // Assert
        Assert.Equal(expected, checkedWorkflow.Problems.Count(problem => problem == new WorkflowProblem(WorkflowProblemKind.UnknownName, 0, "user")));
    }

    [Theory]
    [InlineData(AuthKind.Basic, SecretKind.Password)]
    [InlineData(AuthKind.Bearer, SecretKind.Token)]
    public async Task CheckAsync_WhenTheSecretOfTheFolderUsesAName_ThenChecksIt(AuthKind kind, SecretKind secret)
    {
        // Arrange
        var folder = new AppFolder(_temporary.Path);
        var library = new RequestLibrary(folder, NullLogger<RequestLibrary>.Instance);
        var secrets = new SecretStore(folder, NullLogger<SecretStore>.Instance);
        var settings = new FolderSettings { Id = Guid.NewGuid(), Auth = new(kind) };
        await library.SaveFolderAsync("Shop", settings, Cancellation);
        await secrets.SaveAsync(settings.Id, secret, "{{token}}", Cancellation);
        await library.SaveAsync("Shop/Login", Login with { Auth = AuthSettings.None }, Cancellation);
        await library.SaveAsync("Shop/Hent ordre", Order with { Headers = [] }, Cancellation);
        var check = new WorkflowCheck(library, secrets, NullLogger<WorkflowCheck>.Instance);

        // Act
        var checkedWorkflow = await check.CheckAsync("Ordre-sync", OrderSync(OrderStep, LoginStep), ApiEnvironment.None, Parameters("orderId"), Cancellation);

        // Assert
        Assert.Equal([new(WorkflowProblemKind.UsedBeforeSaved, 0, "token")], checkedWorkflow.Problems);
    }

    [Fact]
    public async Task CheckAsync_WhenOAuth2UsesANameTheEnvironmentLacks_ThenFindsNoProblem()
    {
        // Arrange
        var folder = new AppFolder(_temporary.Path);
        var library = new RequestLibrary(folder, NullLogger<RequestLibrary>.Instance);
        await library.SaveAsync("Shop/Login", Login with { Auth = new(AuthKind.OAuth2, OAuth: new() { ClientId = "{{clientId}}", Scope = "{{scope}}" }) }, Cancellation);
        await library.SaveAsync("Shop/Hent ordre", Order, Cancellation);
        var check = new WorkflowCheck(library, new SecretStore(folder, NullLogger<SecretStore>.Instance), NullLogger<WorkflowCheck>.Instance);

        // Act
        var checkedWorkflow = await check.CheckAsync("Ordre-sync", OrderSync(LoginStep, OrderStep), ApiEnvironment.None, Parameters("orderId"), Cancellation);

        // Assert
        Assert.Empty(checkedWorkflow.Problems);
    }
}
