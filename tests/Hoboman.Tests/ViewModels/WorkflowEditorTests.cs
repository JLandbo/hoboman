using System.Net.Http;
using System.Text.Json;
using Hoboman.Tests.Auth;

namespace Hoboman.Tests.ViewModels;

public sealed class WorkflowEditorTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static ApiResponse Ok(string body) => new(200, "OK", 1, body.Length, [new("Content-Type", "application/json")], body);

    static Func<Task<ApiResponse>> Answering(params ApiResponse[] responses)
    {
        var queue = new Queue<ApiResponse>(responses);
        return () => Task.FromResult(queue.Dequeue());
    }

    static WorkflowRequest Request(string url = "https://dev.local/ping") => new() { Url = url };

    static AuthSettings ClientCredentials => new(AuthKind.OAuth2, OAuth: new() { Grant = OAuthGrant.ClientCredentials, TokenUrl = "https://dev.local/token" });

    // A token is fetched for the step until the fetch is cancelled.
    static Harness FetchingHarness(TaskCompletionSource fetching) => new(send: () => throw new MissingSecretException(SecretKind.OAuthToken), oauth: new(async cancellationToken =>
    {
        fetching.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return FakeOAuthClient.Token;
    }));

    internal static async Task<WorkflowViewModel> AddBearerStepAndRunAsync(MainViewModel main, Harness harness)
    {
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid() }, harness);
        workflow.AddRequest();
        workflow.Steps[0].Request!.Url = "https://dev.local";
        workflow.Steps[0].Auth!.Kind = AuthKind.Bearer;
        workflow.Steps[0].Auth!.Token = "abc";
        await workflow.RunAsync();
        return workflow;
    }

    static async Task<WorkflowViewModel> OpenAsync(MainViewModel main, string name, Workflow workflow, Harness harness)
    {
        await harness.WorkflowLibrary.SaveAsync(name, workflow, TestContext.Current.CancellationToken);
        await main.LoadAsync();
        await main.OpenWorkflowAsync(name);
        return main.Workflow!;
    }

    [Fact]
    public async Task NewWorkflowAsync_WhenNamed_ThenCreatesItWithAnIdAndShowsItWithTheWorkflows()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Ordre-sync"));
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.NewWorkflowAsync();

        // Assert
        Assert.NotEqual(Guid.Empty, (await harness.WorkflowLibrary.LoadAsync("Ordre-sync", Cancellation))!.Id);
        Assert.Equal(["Ordre-sync"], main.Workflows.Names);
        Assert.Equal(SidebarSection.Workflows, main.Section);
        Assert.Same(main.Workflow, main.Content);
        Assert.Equal("Ordre-sync", main.Workflow!.Name);
    }

    [Fact]
    public async Task Content_WhenTheSidebarSwitchesBetweenCollectionsAndWorkflows_ThenShowsTheChosenTabOrTheWorkflow()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid() }, harness);
        var tab = main.Tabs.Single();

        // Act
        main.Section = SidebarSection.Collections;
        var shownTab = main.Content;
        main.Section = SidebarSection.Workflows;

        // Assert
        Assert.Same(tab, shownTab);
        Assert.Same(workflow, main.Content);
        Assert.Same(tab, main.SelectedTab);
    }

    [Fact]
    public async Task NewTab_WhenTheWorkflowsAreShown_ThenShowsTheNewTabWithTheCollections()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid() }, harness);

        // Act
        main.NewTab();

        // Assert
        Assert.Equal(SidebarSection.Collections, main.Section);
        Assert.Same(main.Tabs.Last(), main.Content);
    }

    [Fact]
    public async Task RenameWorkflowAsync_WhenTheWorkflowIsOpen_ThenItsNextSaveGoesToTheNewName()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Renamed"));
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid() }, harness);
        workflow.Parameters.Rows[0].Name = "token";

        // Act
        await main.RenameWorkflowAsync("Flow");
        await workflow.SaveAsync();

        // Assert
        Assert.Equal(["Renamed"], main.Workflows.Names);
        Assert.Equal("token", (await harness.WorkflowLibrary.LoadAsync("Renamed", Cancellation))!.Parameters.Single().Name);
        Assert.False(Directory.Exists(Path.Combine(harness.Folder.Workflows, "Flow")));
    }

    [Fact]
    public async Task DeleteWorkflowAsync_WhenConfirmed_ThenRemovesItAndShowsNoWorkflow()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        var main = harness.Main();
        await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid() }, harness);

        // Act
        await main.DeleteWorkflowAsync("Flow");

        // Assert
        Assert.Empty(main.Workflows.Names);
        Assert.Null(main.Workflow);
        Assert.Null(main.Content);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task DeleteWorkflowAsync_WhenConfirmed_ThenForgetsTheSecretsOfItsStepsUnlessACopyUsesThem(bool copied, bool kept)
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        var id = Guid.NewGuid();
        await harness.Secrets.SaveAsync(id, SecretKind.Token, "abc", Cancellation);
        var workflow = new Workflow { Id = Guid.NewGuid(), Steps = [new() { Request = Request() with { Id = id, Auth = new(AuthKind.Bearer) } }] };
        if (copied)
        {
            await harness.WorkflowLibrary.SaveAsync("Flow - kopi", workflow, Cancellation);
        }
        var main = harness.Main();
        await OpenAsync(main, "Flow", workflow, harness);

        // Act
        await main.DeleteWorkflowAsync("Flow");

        // Assert
        Assert.Equal(kept, await harness.Secrets.OfAsync(id, SecretKind.Token, Cancellation) is not null);
    }

    [Fact]
    public async Task RunAsync_WhenAStepHoldsARequestAndAWait_ThenTellsItAndKeepsTheRequestAndItsSecret()
    {
        // Arrange
        using var harness = new Harness();
        var id = Guid.NewGuid();
        await harness.Secrets.SaveAsync(id, SecretKind.Token, "abc", Cancellation);
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { DelaySeconds = 5, Request = Request() with { Id = id, Auth = new(AuthKind.Bearer) } }] }, harness);

        // Act
        await workflow.RunAsync().WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
        await workflow.SaveAsync();

        // Assert
        var mixed = harness.Translator.Format("Workflow.StepProblem", 1, harness.Translator.Of("WorkflowProblem.MixedStep"));
        Assert.Equal((true, id, "abc"), (workflow.Problems.Contains(mixed), (await harness.WorkflowLibrary.LoadAsync("Flow", Cancellation))!.Steps.Single().Request?.Id,
            await harness.Secrets.OfAsync(id, SecretKind.Token, Cancellation)));
    }

    [Fact]
    public async Task SaveAsync_WhenAStepHoldingARequestAndAWaitIsRemoved_ThenForgetsTheRequestsSecret()
    {
        // Arrange
        using var harness = new Harness();
        var id = Guid.NewGuid();
        await harness.Secrets.SaveAsync(id, SecretKind.Token, "abc", Cancellation);
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { DelaySeconds = 5, Request = Request() with { Id = id, Auth = new(AuthKind.Bearer) } }] }, harness);
        await workflow.SaveAsync();
        workflow.RemoveStep(workflow.Steps.Single());

        // Act
        await workflow.SaveAsync();

        // Assert
        Assert.Null(await harness.Secrets.OfAsync(id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task RunAsync_WhenAScriptStepHasARetry_ThenTellsItAndShowsNoRetry()
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await OpenScriptAsync(harness, "return { value: 1 };", new WorkflowStep { Script = "map.js", Retry = new() });

        // Act
        await workflow.RunAsync();

        // Assert
        var invalid = harness.Translator.Format("Workflow.StepProblem", 2, harness.Translator.Format("WorkflowProblem.InvalidRetry", WorkflowCheck.MaxRetryTimes, WorkflowCheck.MaxDelaySeconds));
        Assert.Equal((true, false), (workflow.Problems.Contains(invalid), workflow.Steps[1].HasRetry));
    }

    [Fact]
    public async Task OpenWorkflowAsync_WhenAWaitHasSaves_ThenShowsThemSoTheyCanBeRemoved()
    {
        // Arrange
        using var harness = new Harness();

        // Act
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Variables = [new("x")], Steps = [new() { DelaySeconds = 5, Saves = [new("x", "$.id")] }] }, harness);

        // Assert
        Assert.True(workflow.Steps.Single().HasSaves);
    }

    [Fact]
    public async Task Relabel_WhenTheLanguageChangesAfterARetriedRun_ThenTellsTheAttemptsInTheNewLanguage()
    {
        // Arrange
        var calls = 0;
        using var harness = new Harness(send: () => Task.FromResult(++calls == 1 ? new ApiResponse(403, "Forbidden", 1, 0, [], "") : Ok("{}")));
        harness.Translator.Use(Translation.Danish);
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Request(), Retry = new() { Times = 3, WaitSeconds = 0 } }] }, harness);
        await workflow.RunAsync();
        harness.Translator.Use(Translation.English);
        var changed = new List<string?>();
        workflow.Steps.Single().PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Act
        workflow.Relabel();

        // Assert
        Assert.Equal((true, true), (changed.Contains(nameof(WorkflowStepViewModel.Elapsed)), workflow.Steps.Single().Elapsed!.EndsWith(harness.Translator.Format("Workflow.Attempts", 2))));
    }

    [Fact]
    public async Task RunAsync_WhenAStepWaits_ThenItRunsWithoutSending()
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { DelaySeconds = 5 }] }, harness);
        var step = workflow.Steps.Single();

        // Act
        var running = workflow.RunAsync();
        for (var tries = 0; tries < 500 && !step.IsRunning; tries++)
        {
            await Task.Delay(10, Cancellation);
        }
        var (isRunning, isSending) = (step.IsRunning, step.IsSending);
        for (var tries = 0; tries < 500 && !running.IsCompleted; tries++)
        {
            harness.Clock.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(10, Cancellation);
        }
        await running.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Assert
        Assert.Equal((true, false), (isRunning, isSending));
    }

    [Fact]
    public async Task SaveAsync_WhenAStepRetries_ThenSavesHowItRetries()
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Request() }] }, harness);
        var step = workflow.Steps.Single();
        (step.Retries, step.RetryUntil, step.RetryEquals, step.RetryTimes, step.RetryWaitSeconds) = (true, " $.result.status ", "succeeded", 60, 5);
        (step.RetryStopIf, step.RetryStopEquals) = (" $.result.status ", "failed");

        // Act
        await workflow.SaveAsync();

        // Assert
        var expected = new WorkflowRetry { Until = "$.result.status", Value = "succeeded", StopIf = "$.result.status", StopEquals = "failed", Times = 60, WaitSeconds = 5 };
        Assert.Equal(expected, (await harness.WorkflowLibrary.LoadAsync("Flow", Cancellation))!.Steps.Single().Retry);
    }

    [Fact]
    public async Task RunAsync_WhenAStepIsTriedAgain_ThenShowsWhichAttemptIsRunning()
    {
        // Arrange
        var sending = new TaskCompletionSource();
        var answer = new TaskCompletionSource<ApiResponse>();
        var calls = 0;
        using var harness = new Harness(send: () =>
        {
            if (++calls == 1)
            {
                return Task.FromResult(new ApiResponse(403, "Forbidden", 1, 0, [], ""));
            }
            sending.TrySetResult();
            return answer.Task;
        });
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Request(), Retry = new() { Times = 3, WaitSeconds = 0 } }] }, harness);

        // Act
        var running = workflow.RunAsync();
        await sending.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
        var state = workflow.Steps.Single().State;
        answer.SetResult(Ok("{}"));
        await running.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Assert
        Assert.Equal(harness.Translator.Format("Workflow.Attempt", 2, 3), state);
    }

    [Fact]
    public async Task RunAsync_WhenTheAnswerIsNeverReady_ThenTheStepTellsAfterHowManyAttempts()
    {
        // Arrange
        using var harness = new Harness(send: () => Task.FromResult(Ok("""{"status":"processing"}""")));
        harness.Translator.Use(Translation.Danish);
        var retry = new WorkflowRetry { Until = "$.status", Value = "done", Times = 2, WaitSeconds = 0 };
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Request(), Retry = retry }] }, harness);

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal(harness.Translator.Format("Workflow.NotReady", 2), workflow.Steps.Single().Error);
    }

    [Fact]
    public async Task RunAsync_WhenAStepIsAnswered_ThenItsResponseCanBeSavedAsAFile()
    {
        // Arrange
        byte[] file = [0x25, 0x50, 0x44, 0x46, 0x00, 0xFF];
        using var harness = new Harness(send: () => Task.FromResult(new ApiResponse(200, "OK", 1, file.Length, [new("Content-Type", "application/pdf")], "") { Bytes = file }));
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Request() }] }, harness);
        harness.Dialogs.SavePath = Path.Combine(harness.Folder.Root, "udskrift.pdf");

        // Act
        await workflow.RunAsync();
        await workflow.Steps.Single().Result.SaveAsAsync();

        // Assert
        Assert.Equal(file, await File.ReadAllBytesAsync(harness.Dialogs.SavePath, Cancellation));
    }

    [Fact]
    public async Task RunAsync_WhenAStepStops_ThenTheStepTellsWhy()
    {
        // Arrange
        using var harness = new Harness(send: () => Task.FromResult(Ok("""{"status":"failed"}""")));
        harness.Translator.Use(Translation.Danish);
        var retry = new WorkflowRetry { StopIf = "$.status", StopEquals = "failed", Times = 2, WaitSeconds = 0 };
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Request(), Retry = retry }] }, harness);

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal(harness.Translator.Format("Workflow.Stopped", "$.status", "failed"), workflow.Steps.Single().Error);
    }

    [Fact]
    public async Task OpenWorkflowAsync_WhenAStepInheritsAuthThatUsesNames_ThenTheStepShowsThemAsUsed()
    {
        // Arrange
        using var harness = new Harness();
        var step = new WorkflowStep { Request = Request() with { Auth = new(AuthKind.Inherit) } };
        var flow = new Workflow { Id = Guid.NewGuid(), Auth = new(AuthKind.Basic, "{{user}}"), Parameters = [new("user"), new("other")], Steps = [step] };

        // Act
        var workflow = await OpenAsync(harness.Main(), "Flow", flow, harness);

        // Assert
        Assert.Equal(["user"], workflow.Steps.Single().UsedNames);
    }

    [Fact]
    public async Task AddDelay_WhenSaved_ThenTheStepWaitsItsSeconds()
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid() }, harness);
        workflow.AddDelay();
        workflow.Steps.Single().DelaySeconds = 30;

        // Act
        await workflow.SaveAsync();

        // Assert
        var saved = (await harness.WorkflowLibrary.LoadAsync("Flow", Cancellation))!.Steps.Single();
        Assert.Equal((StepKind.Delay, 30, harness.Translator.Format("Workflow.DelayTitle", 30)), (saved.Kind, saved.DelaySeconds, workflow.Steps.Single().Title));
    }

    [Fact]
    public async Task SaveAsync_WhenTheWorkflowHasABearerToken_ThenSavesItUnderTheWorkflowAndNotInTheFile()
    {
        // Arrange
        using var harness = new Harness();
        var id = Guid.NewGuid();
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = id }, harness);
        workflow.Auth.Kind = AuthKind.Bearer;
        workflow.Auth.Token = "abc";

        // Act
        await workflow.SaveAsync();

        // Assert
        Assert.Equal((AuthKind.Bearer, "abc"), ((await harness.WorkflowLibrary.LoadAsync("Flow", Cancellation))!.Auth!.Kind, await harness.Secrets.OfAsync(id, SecretKind.Token, Cancellation)));
        Assert.DoesNotContain("abc", await File.ReadAllTextAsync(Path.Combine(harness.Folder.Workflows, "Flow", "workflow.json"), Cancellation));
    }

    [Fact]
    public async Task SaveAsync_WhenTheWorkflowsAuthIsRemovedAndAddedAgain_ThenSavesItsSecretAgain()
    {
        // Arrange
        using var harness = new Harness();
        var id = Guid.NewGuid();
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = id }, harness);
        (workflow.Auth.Kind, workflow.Auth.Token) = (AuthKind.Bearer, "abc");
        await workflow.SaveAsync();
        workflow.Auth.Kind = AuthKind.None;
        await workflow.SaveAsync();
        workflow.Auth.Kind = AuthKind.Bearer;

        // Act
        await workflow.SaveAsync();

        // Assert
        Assert.Equal("abc", await harness.Secrets.OfAsync(id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task RunAsync_WhenAStepThatInheritsMissesTheWorkflowsSecret_ThenPointsAtTheWorkflowsAuth()
    {
        // Arrange
        using var harness = new Harness(send: () => throw new MissingSecretException(SecretKind.Token));
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer), Steps = [new() { Request = Request() with { Auth = new(AuthKind.Inherit) } }] }, harness);

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal(harness.Translator.Of("Workflow.MissingSecret"), workflow.Steps.Single().Error);
    }

    [Fact]
    public async Task AddRequest_WhenAdded_ThenTheStepInheritsTheWorkflowsAuth()
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid() }, harness);

        // Act
        workflow.AddRequest();
        workflow.Steps.Single().Request!.Url = "https://dev.local";
        await workflow.SaveAsync();

        // Assert
        Assert.Equal(AuthKind.Inherit, (await harness.WorkflowLibrary.LoadAsync("Flow", Cancellation))!.Steps.Single().Request!.Auth!.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FetchToken_WhenFetchedByHand_ThenSavesItAndIsNoEdit(bool onStep)
    {
        // Arrange
        using var harness = new Harness();
        var (id, stepId) = (Guid.NewGuid(), Guid.NewGuid());
        var step = new WorkflowStep { Request = Request() with { Id = stepId, Auth = onStep ? ClientCredentials : new(AuthKind.Inherit) } };
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = id, Auth = onStep ? null : ClientCredentials, Steps = [step] }, harness);
        var auth = onStep ? workflow.Steps.Single().Auth! : workflow.Auth;

        // Act
        await auth.OwnerFetch!();

        // Assert
        var saved = await harness.Secrets.OfEachEnvironmentAsync(onStep ? stepId : id, SecretKind.OAuthToken, Cancellation);
        Assert.Equal((false, 1), (workflow.IsDirty, saved.Count));
    }

    [Fact]
    public async Task FetchToken_WhenTheSecretsCannotBeSaved_ThenTellsWhy()
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Auth = ClientCredentials, Steps = [new() { Request = Request() with { Auth = new(AuthKind.Inherit) } }] }, harness);
        Directory.CreateDirectory(harness.Folder.Secrets);

        // Act
        await workflow.Auth.OwnerFetch!();

        // Assert
        Assert.Equal(harness.Translator.Of("Workflow.SecretsSaveFailed"), harness.Dialogs.Notification?.Title);
    }

    [Fact]
    public async Task RunAsync_WhenTheWorkflowsClientCredentialsTokenIsMissing_ThenFetchesAndSavesItForTheWorkflow()
    {
        // Arrange
        var calls = 0;
        using var harness = new Harness(send: () => ++calls == 1 ? throw new MissingSecretException(SecretKind.OAuthToken) : Task.FromResult(Ok("{}")));
        var dev = new ApiEnvironment("Dev", []) { Id = Guid.NewGuid() };
        await harness.EnvironmentStore.SaveAsync([dev], Cancellation);
        var id = Guid.NewGuid();
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = id, Auth = ClientCredentials, Steps = [new() { Request = Request() with { Auth = new(AuthKind.Inherit) } }] }, harness);
        await main.Environments.ChooseAsync(main.Environments.Items.Single());

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal(("200", false), (workflow.Steps.Single().Status, workflow.IsDirty));
        Assert.NotNull(await harness.Secrets.OfAsync(id, SecretKind.OAuthToken, dev.Id, Cancellation));
    }

    [Fact]
    public async Task DeleteWorkflowAsync_WhenTheWorkflowHasAuth_ThenForgetsItsSecret()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        var id = Guid.NewGuid();
        await harness.Secrets.SaveAsync(id, SecretKind.Token, "abc", Cancellation);
        var main = harness.Main();
        await OpenAsync(main, "Flow", new() { Id = id, Auth = new(AuthKind.Bearer) }, harness);

        // Act
        await main.DeleteWorkflowAsync("Flow");

        // Assert
        Assert.Null(await harness.Secrets.OfAsync(id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task SaveAsync_WhenAStepHasABearerToken_ThenSavesItAsASecretAndNotInTheWorkflow()
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid() }, harness);
        workflow.AddRequest();
        workflow.Steps[0].Request!.Url = "https://dev.local";
        workflow.Steps[0].Auth!.Kind = AuthKind.Bearer;
        workflow.Steps[0].Auth!.Token = "abc";

        // Act
        await workflow.SaveAsync();

        // Assert
        var saved = (await harness.WorkflowLibrary.LoadAsync("Flow", Cancellation))!.Steps.Single().Request!;
        Assert.Equal((AuthKind.Bearer, "abc"), (saved.Auth!.Kind, await harness.Secrets.OfAsync(saved.Id, SecretKind.Token, Cancellation)));
        Assert.DoesNotContain("abc", await File.ReadAllTextAsync(Path.Combine(harness.Folder.Workflows, "Flow", "workflow.json"), Cancellation));
    }

    [Fact]
    public async Task SaveAsync_WhenAStepWithSecretsIsRemoved_ThenForgetsThem()
    {
        // Arrange
        using var harness = new Harness();
        var id = Guid.NewGuid();
        await harness.Secrets.SaveAsync(id, SecretKind.Token, "abc", Cancellation);
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Request() with { Id = id, Auth = new(AuthKind.Bearer) } }] }, harness);
        workflow.RemoveStep(workflow.Steps.Single());

        // Act
        await workflow.SaveAsync();

        // Assert
        Assert.Null(await harness.Secrets.OfAsync(id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task OpenWorkflowAsync_WhenAStepHasSecrets_ThenShowsThem()
    {
        // Arrange
        using var harness = new Harness();
        var id = Guid.NewGuid();
        await harness.Secrets.SaveAsync(id, SecretKind.Password, "secret", Cancellation);

        // Act
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Request() with { Id = id, Auth = new(AuthKind.Basic, "demo") } }] }, harness);

        // Assert
        Assert.Equal(("demo", "secret"), (workflow.Steps.Single().Auth!.UserName, workflow.Steps.Single().Auth!.Password));
    }

    [Fact]
    public async Task RunAsync_WhenAClientCredentialsTokenIsMissing_ThenFetchesAndSavesItForTheStepAndTheChosenEnvironment()
    {
        // Arrange
        var calls = 0;
        using var harness = new Harness(send: () => ++calls == 1 ? throw new MissingSecretException(SecretKind.OAuthToken) : Task.FromResult(Ok("{}")));
        var dev = new ApiEnvironment("Dev", []) { Id = Guid.NewGuid() };
        await harness.EnvironmentStore.SaveAsync([dev], Cancellation);
        var id = Guid.NewGuid();
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Request() with { Id = id, Auth = ClientCredentials } }] }, harness);
        await main.Environments.ChooseAsync(main.Environments.Items.Single());

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal(("200", "Dev", false), (workflow.Steps.Single().Status, harness.OAuth.Asked!.Value.Environment!.Name, workflow.IsDirty));
        Assert.NotNull(await harness.Secrets.OfAsync(id, SecretKind.OAuthToken, dev.Id, Cancellation));
    }

    [Fact]
    public async Task Cancel_WhenATokenIsFetchedDuringARun_ThenCancelsTheStep()
    {
        // Arrange
        var fetching = new TaskCompletionSource();
        using var harness = new Harness(send: () => throw new MissingSecretException(SecretKind.OAuthToken), oauth: new(async cancellationToken =>
        {
            fetching.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return FakeOAuthClient.Token;
        }));
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Request() with { Id = Guid.NewGuid(), Auth = ClientCredentials } }] }, harness);
        var running = workflow.RunAsync();
        await fetching.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Act
        workflow.Cancel();
        await running.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Assert
        Assert.Equal(harness.Translator.Of("Workflow.Cancelled"), workflow.Steps.Single().State);
    }

    [Fact]
    public async Task RunAsync_WhenTheStepIsEditedWhileItsTokenIsFetched_ThenTheWorkflowStaysUnsaved()
    {
        // Arrange
        var fetching = new TaskCompletionSource();
        using var harness = FetchingHarness(fetching);
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Request() with { Id = Guid.NewGuid(), Auth = ClientCredentials } }] }, harness);
        var running = workflow.RunAsync();
        await fetching.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Act
        workflow.Steps.Single().Auth!.Scope = "orders";
        await running.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Assert
        Assert.True(workflow.IsDirty);
    }

    [Fact]
    public async Task RemoveStep_WhenItsTokenIsFetchedDuringARun_ThenStopsTheFetch()
    {
        // Arrange
        var fetching = new TaskCompletionSource();
        using var harness = FetchingHarness(fetching);
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Request() with { Id = Guid.NewGuid(), Auth = ClientCredentials } }] }, harness);
        var running = workflow.RunAsync();
        await fetching.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Act
        workflow.RemoveStep(workflow.Steps.Single());
        await running.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Assert
        Assert.False(workflow.IsRunning);
    }

    [Fact]
    public async Task RunAsync_WhenOnlyASecretIsEdited_ThenSavesItAndTheWorkflowIsSaved()
    {
        // Arrange
        using var harness = new Harness();
        var id = Guid.NewGuid();
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Request() with { Id = id, Auth = new(AuthKind.Bearer) } }] }, harness);
        workflow.Steps.Single().Auth!.Token = "abc";

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal(("abc", false), (await harness.Secrets.OfAsync(id, SecretKind.Token, Cancellation), workflow.IsDirty));
    }

    [Fact]
    public async Task SaveAsync_WhenARunSavedTheSecretOfAStepThatIsRemoved_ThenForgetsIt()
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await AddBearerStepAndRunAsync(harness.Main(), harness);
        var id = workflow.Steps.Single().SecretsId!.Value;
        var savedByRun = await harness.Secrets.OfAsync(id, SecretKind.Token, Cancellation);
        workflow.RemoveStep(workflow.Steps.Single());

        // Act
        await workflow.SaveAsync();

        // Assert
        Assert.Equal(("abc", null), (savedByRun, await harness.Secrets.OfAsync(id, SecretKind.Token, Cancellation)));
    }

    [Fact]
    public async Task OpenWorkflowAsync_WhenAWorkflowIsLeftWithoutSavingAStepARunSavedSecretsFor_ThenForgetsThem()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        await harness.WorkflowLibrary.SaveAsync("Other", new() { Id = Guid.NewGuid() }, Cancellation);
        var main = harness.Main();
        var workflow = await AddBearerStepAndRunAsync(main, harness);
        var id = workflow.Steps.Single().SecretsId!.Value;
        var savedByRun = await harness.Secrets.OfAsync(id, SecretKind.Token, Cancellation);

        // Act
        await main.OpenWorkflowAsync("Other");

        // Assert
        Assert.Equal(("abc", null), (savedByRun, await harness.Secrets.OfAsync(id, SecretKind.Token, Cancellation)));
    }

    [Fact]
    public async Task WorkflowsChangedAsync_WhenTheOpenWorkflowIsRemovedOnDisk_ThenKeepsTheSecretsOfItsSteps()
    {
        // Arrange
        using var harness = new Harness();
        var id = Guid.NewGuid();
        await harness.Secrets.SaveAsync(id, SecretKind.Token, "abc", Cancellation);
        var main = harness.Main();
        await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Request() with { Id = id, Auth = new(AuthKind.Bearer) } }] }, harness);
        Directory.Delete(Path.Combine(harness.Folder.Workflows, "Flow"), recursive: true);

        // Act
        await main.WorkflowsChangedAsync();

        // Assert
        Assert.Equal((null, "abc"), (main.Workflow, await harness.Secrets.OfAsync(id, SecretKind.Token, Cancellation)));
    }

    [Fact]
    public async Task RunAsync_WhenAStepIsRemovedBeforeItRuns_ThenFetchesNoTokenForIt()
    {
        // Arrange
        var sending = new TaskCompletionSource();
        var answer = new TaskCompletionSource<ApiResponse>();
        var calls = 0;
        using var harness = new Harness(send: () =>
        {
            if (++calls > 1)
            {
                throw new MissingSecretException(SecretKind.OAuthToken);
            }
            sending.TrySetResult();
            return answer.Task;
        });
        var workflow = await OpenAsync(harness.Main(), "Flow", new()
        {
            Id = Guid.NewGuid(),
            Steps = [new() { Request = Request() }, new() { Request = Request() with { Id = Guid.NewGuid(), Auth = ClientCredentials } }],
        }, harness);
        var running = workflow.RunAsync();
        await sending.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Act
        workflow.RemoveStep(workflow.Steps[1]);
        answer.SetResult(Ok("{}"));
        await running.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Assert
        Assert.Null(harness.OAuth.Asked);
    }

    [Fact]
    public async Task SaveAsync_WhenAVariableHasADefaultWrittenByHand_ThenKeepsIt()
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await OpenAsync(harness.Main(), "Flow", new()
        {
            Id = Guid.NewGuid(),
            Variables = [new("token") { Default = JsonSerializer.SerializeToElement("abc") }],
            Steps = [new() { Request = Request(), Saves = [new("token", "$.token")] }],
        }, harness);
        workflow.Steps.Single().Name = "Login";

        // Act
        await workflow.SaveAsync();

        // Assert
        Assert.Equal("abc", (await harness.WorkflowLibrary.LoadAsync("Flow", Cancellation))!.Variables.Single().Default.GetString());
    }

    [Fact]
    public async Task OpenWorkflowAsync_WhenAStepsSavedTokenUsesAName_ThenTheStepShowsItAsUsed()
    {
        // Arrange
        using var harness = new Harness();
        var id = Guid.NewGuid();
        await harness.Secrets.SaveAsync(id, SecretKind.Token, "{{token}}", Cancellation);

        // Act
        var workflow = await OpenAsync(harness.Main(), "Flow", new()
        {
            Id = Guid.NewGuid(),
            Steps = [new() { Request = Request(), Saves = [new("token", "$.token")] }, new() { Request = Request() with { Id = id, Auth = new(AuthKind.Bearer) } }],
        }, harness);

        // Assert
        Assert.Equal(["token"], workflow.Steps[1].UsedNames);
    }

    [Fact]
    public async Task DeleteWorkflowAsync_WhenNotConfirmed_ThenKeepsTheWorkflow()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid() }, harness);

        // Act
        await main.DeleteWorkflowAsync("Flow");

        // Assert
        Assert.NotNull(harness.Dialogs.ConfirmQuestion);
        Assert.Equal(["Flow"], main.Workflows.Names);
        Assert.Same(workflow, main.Workflow);
    }

    [Fact]
    public async Task CanClose_WhenTheWorkflowIsUnsaved_ThenAsksWithItsName()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid() }, harness);
        workflow.Parameters.Rows[0].Name = "orderId";

        // Act
        var close = main.CanClose();

        // Assert
        Assert.False(close);
        Assert.Equal(["Flow"], harness.Dialogs.ConfirmQuestion!.Value.Items);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkflowsChangedAsync_WhenTheFileChangesOnDisk_ThenReloadsOnlyAWorkflowWithoutEdits(bool edited)
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        var id = Guid.NewGuid();
        var workflow = await OpenAsync(main, "Flow", new() { Id = id }, harness);
        if (edited)
        {
            workflow.Parameters.Rows[0].Name = "mine";
        }
        await harness.WorkflowLibrary.SaveAsync("Flow", new() { Id = id, Parameters = [new("theirs")] }, Cancellation);

        // Act
        await main.WorkflowsChangedAsync();

        // Assert
        Assert.Equal(edited ? "mine" : "theirs", workflow.Parameters.Rows[0].Name);
        Assert.Equal(edited, workflow.IsDirty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkflowsChangedAsync_WhenTheFolderIsGoneOnDisk_ThenKeepsOnlyAWorkflowWithEditsOpen(bool edited)
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid() }, harness);
        if (edited)
        {
            workflow.Parameters.Rows[0].Name = "mine";
        }
        Directory.Delete(Path.Combine(harness.Folder.Workflows, "Flow"), recursive: true);

        // Act
        await main.WorkflowsChangedAsync();

        // Assert
        Assert.Equal(edited, main.Workflow == workflow);
    }

    [Fact]
    public async Task OpenWorkflowAsync_WhenTheOpenWorkflowHasEditsAndClosingIsDeclined_ThenKeepsItOpen()
    {
        // Arrange
        using var harness = new Harness();
        await harness.WorkflowLibrary.SaveAsync("Other", new() { Id = Guid.NewGuid() }, Cancellation);
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid() }, harness);
        workflow.Parameters.Rows[0].Name = "mine";

        // Act
        await main.OpenWorkflowAsync("Other");

        // Assert
        Assert.Equal(harness.Translator.Format("Workflow.CloseMessage", "Flow"), harness.Dialogs.ConfirmQuestion!.Value.Message);
        Assert.Same(workflow, main.Workflow);
        Assert.True(workflow.IsDirty);
    }

    [Fact]
    public async Task AddScriptAsync_WhenNamed_ThenCreatesTheFileAndSavesAScriptStep()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "map"));
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid() }, harness);

        // Act
        await workflow.AddScriptAsync();
        await workflow.SaveAsync();

        // Assert
        Assert.Equal(harness.Translator.Of("Workflow.ScriptTemplate"), await File.ReadAllTextAsync(Path.Combine(harness.Folder.Workflows, "Flow", "map.js"), Cancellation));
        Assert.Equal("map.js", Assert.Single((await harness.WorkflowLibrary.LoadAsync("Flow", Cancellation))!.Steps).Script);
    }

    static async Task<WorkflowViewModel> OpenScriptAsync(Harness harness, string code, params WorkflowStep[] more)
    {
        Directory.CreateDirectory(Path.Combine(harness.Folder.Workflows, "Flow"));
        await File.WriteAllTextAsync(Path.Combine(harness.Folder.Workflows, "Flow", "map.js"), code, TestContext.Current.CancellationToken);
        var workflow = new Workflow { Id = Guid.NewGuid(), Parameters = [new("orderId") { Default = JsonSerializer.SerializeToElement("o-17") }], Variables = [new("value")],
            Steps = [new() { Script = "map.js", Saves = [new("value", "$.value")] }, .. more] };
        return await OpenAsync(harness.Main(), "Flow", workflow, harness);
    }

    [Fact]
    public async Task SaveAsync_WhenAStepSavesIntoANewName_ThenDeclaresItAsAVariable()
    {
        // Arrange
        using var harness = new Harness();
        var ping = Request();
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping }] }, harness);
        workflow.Steps[0].Saves.Rows[0].Name = "token";
        workflow.Steps[0].Saves.Rows[0].Value = "$.token";

        // Act
        await workflow.SaveAsync();

        // Assert
        Assert.Equal(["token"], (await harness.WorkflowLibrary.LoadAsync("Flow", Cancellation))!.Variables.Select(variable => variable.Name));
    }

    [Fact]
    public async Task Code_WhenEditedAndSaved_ThenWritesTheScript()
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await OpenScriptAsync(harness, "return { value: 1 };");

        // Act
        workflow.Code = "return { value: 2 };";
        var unsaved = workflow.IsDirty;
        await workflow.SaveAsync();

        // Assert
        Assert.Equal((true, false), (unsaved, workflow.IsDirty));
        Assert.Equal("return { value: 2 };", await File.ReadAllTextAsync(Path.Combine(harness.Folder.Workflows, "Flow", "map.js"), Cancellation));
    }

    [Fact]
    public async Task RunAsync_WhenTheCodeIsNotSaved_ThenRunsTheCodeInTheEditor()
    {
        // Arrange
        using var harness = new Harness(send: Answering(Ok("{}")));
        var echo = Request("https://dev.local/{{value}}");
        var workflow = await OpenScriptAsync(harness, "return { value: 'saved' };", new WorkflowStep { Request = echo });
        workflow.Code = "return { value: 'edited' };";

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal("edited", harness.Sender.Environment!.Resolve("{{value}}"));
    }

    [Fact]
    public async Task ReloadAsync_WhenTheScriptChangesOnDisk_ThenShowsTheNewCode()
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await OpenScriptAsync(harness, "return { value: 1 };");
        await File.WriteAllTextAsync(Path.Combine(harness.Folder.Workflows, "Flow", "map.js"), "return { value: 3 };", Cancellation);

        // Act
        await workflow.ReloadAsync();

        // Assert
        Assert.Equal("return { value: 3 };", workflow.Code);
    }

    [Theory]
    [InlineData(false, "return { value: 3 };")]
    [InlineData(true, "return { value: 2 };")]
    public async Task ReloadAsync_WhenTheWorkflowIsEditedAndTheScriptChangesOnDisk_ThenShowsTheNewCodeUnlessTheScriptIsEdited(bool editCode, string expected)
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await OpenScriptAsync(harness, "return { value: 1 };");
        workflow.Parameters.Rows[1].Name = "page";
        if (editCode)
        {
            workflow.Code = "return { value: 2 };";
        }
        await File.WriteAllTextAsync(Path.Combine(harness.Folder.Workflows, "Flow", "map.js"), "return { value: 3 };", Cancellation);

        // Act
        await workflow.ReloadAsync();

        // Assert
        Assert.Equal(expected, workflow.Code);
    }

    [Fact]
    public async Task LoadAsync_WhenAScriptNameLeadsOutOfTheFolder_ThenOpensTheWorkflowWithoutAPathToIt()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();

        // Act
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Script = "../secret.js" }] }, harness);

        // Assert
        Assert.Null(workflow.ScriptPathOf(workflow.Steps.Single()));
    }

    [Fact]
    public async Task RunAsync_WhenTheScriptFileIsMissing_ThenTellsItInsteadOfRunningNothing()
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Script = "map.js" }] }, harness);

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal([harness.Translator.Format("Workflow.StepProblem", 1, harness.Translator.Format("WorkflowProblem.ScriptNotFound", "map.js"))], workflow.Problems);
    }

    [Fact]
    public async Task RunAsync_WhenAStepFails_ThenTheResponseViewTellsWhy()
    {
        // Arrange
        using var harness = new Harness(send: () => throw new HttpRequestException());
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Request() }] }, harness);

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal(new ProblemMessage(harness.Translator.Of("Workflow.StepFailed"), harness.Translator.Of("RequestProblem.NetworkFailed")), workflow.Steps.Single().Problem);
    }

    [Fact]
    public async Task OpenWorkflowAsync_WhenAScriptReadsValuesThroughVars_ThenTheStepShowsThemAsUsed()
    {
        // Arrange
        using var harness = new Harness();

        // Act
        var workflow = await OpenScriptAsync(harness, "return { value: vars.orderId + vars.unknown };");

        // Assert
        Assert.Equal(["orderId"], workflow.Steps[0].UsedNames);
    }

    [Fact]
    public async Task RunAsync_WhenAScriptStepSucceeds_ThenShowsOkWithoutAStatusCode()
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await OpenScriptAsync(harness, "return { value: 1 };");

        // Act
        await workflow.RunAsync();

        // Assert
        var step = workflow.Steps.Single();
        Assert.Equal(("OK", "OK"), (step.Status, step.Result.Response!.Status));
    }

    [Fact]
    public async Task Code_WhenTwoStepsRunTheSameFileWrittenWithOtherCase_ThenTheyShareIt()
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await OpenScriptAsync(harness, "return 1;", new WorkflowStep { Script = "MAP.js" });
        workflow.Code = "return 2;";

        // Act
        workflow.SelectedStep = workflow.Steps[1];

        // Assert
        Assert.Equal("return 2;", workflow.Code);
    }

    [Fact]
    public async Task SaveAsync_WhenTheEditedScriptsStepIsRemoved_ThenLeavesTheFile()
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await OpenScriptAsync(harness, "return 1;");
        workflow.Code = "return 2;";
        workflow.RemoveStep(workflow.Steps.Single());

        // Act
        await workflow.SaveAsync();

        // Assert
        Assert.Equal("return 1;", await File.ReadAllTextAsync(Path.Combine(harness.Folder.Workflows, "Flow", "map.js"), Cancellation));
    }

    [Fact]
    public async Task AddScriptAsync_WhenARemovedStepRanTheScript_ThenReadsItFromTheFileAgain()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "map"));
        var workflow = await OpenScriptAsync(harness, "return 1;");
        workflow.RemoveStep(workflow.Steps.Single());
        await File.WriteAllTextAsync(Path.Combine(harness.Folder.Workflows, "Flow", "map.js"), "return 2;", Cancellation);

        // Act
        await workflow.AddScriptAsync();

        // Assert
        Assert.Equal("return 2;", workflow.Code);
    }

    [Fact]
    public async Task AddScriptAsync_WhenTheFileIsThere_ThenKeepsItsCode()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "map.js"));
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid() }, harness);
        var path = Path.Combine(harness.Folder.Workflows, "Flow", "map.js");
        await File.WriteAllTextAsync(path, "return 1;", Cancellation);

        // Act
        await workflow.AddScriptAsync();

        // Assert
        Assert.Equal("return 1;", await File.ReadAllTextAsync(path, Cancellation));
    }

    [Fact]
    public async Task AddRequest_WhenAdded_ThenStartsWithAJsonBody()
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid() }, harness);

        // Act
        workflow.AddRequest();

        // Assert
        Assert.Equal(BodyKind.Json, workflow.Steps.Single().Request!.BodyKind);
    }

    [Fact]
    public async Task SaveAsync_WhenRequestsAreAddedAndMoved_ThenWritesThemInTheirOrder()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid() }, harness);
        workflow.AddRequest();
        workflow.Steps[0].Request!.Url = "https://dev.local/orders";
        workflow.AddRequest();
        workflow.Steps[1].Request!.Method = "POST";
        workflow.Steps[1].Request!.Url = "https://dev.local/login";

        // Act
        workflow.MoveStep(workflow.Steps[1], -1);
        await workflow.SaveAsync();

        // Assert
        Assert.Equal(["POST https://dev.local/login", "GET https://dev.local/orders"], (await harness.WorkflowLibrary.LoadAsync("Flow", Cancellation))!.Steps.Select(step => $"{step.Request!.Method} {step.Request.Url}"));
        Assert.Equal("https://dev.local/login", workflow.Steps[0].Title);
        Assert.Equal([false, true], workflow.Steps.Select(step => step.IsLast));
        Assert.False(workflow.IsDirty);
    }

    [Fact]
    public async Task SaveAsync_WhenADefaultIsNotJson_ThenTellsWhichAndSavesNothing()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid() }, harness);
        workflow.Parameters.Rows[0].Name = "pageSize";
        workflow.Parameters.Rows[0].Value = "fifty";

        // Act
        await workflow.SaveAsync();

        // Assert
        Assert.Equal(harness.Translator.Format("Workflow.InvalidDefault", "pageSize"), harness.Dialogs.Notification!.Value.Message);
        Assert.Empty((await harness.WorkflowLibrary.LoadAsync("Flow", Cancellation))!.Parameters);
        Assert.True(workflow.IsDirty);
    }

    [Fact]
    public async Task SaveAsync_WhenDefaultsAreGiven_ThenKeepsTheirJsonTypes()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid() }, harness);
        workflow.Parameters.Rows[0].Name = "pageSize";
        workflow.Parameters.Rows[0].Value = "50";
        workflow.Parameters.Rows[1].Name = "orderId";

        // Act
        await workflow.SaveAsync();

        // Assert
        var saved = (await harness.WorkflowLibrary.LoadAsync("Flow", Cancellation))!.Parameters;
        Assert.Equal((JsonValueKind.Number, false), (saved[0].Default.ValueKind, saved[1].HasDefault));
    }

    [Fact]
    public async Task SaveAsync_WhenTheFolderIsGone_ThenTellsItAndDoesNotCreateItAgain()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid() }, harness);
        workflow.Parameters.Rows[0].Name = "token";
        Directory.Delete(Path.Combine(harness.Folder.Workflows, "Flow"), recursive: true);

        // Act
        await workflow.SaveAsync();

        // Assert
        Assert.Equal(harness.Translator.Of("Workflow.SaveFailed"), harness.Dialogs.Notification!.Value.Title);
        Assert.False(Directory.Exists(Path.Combine(harness.Folder.Workflows, "Flow")));
    }

    [Fact]
    public async Task Steps_WhenTheySaveAndUseNames_ThenShowWhatEachSavesAndWhichOfTheWorkflowsOwnNamesItUses()
    {
        // Arrange
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([new("Dev", [new("host", "dev.local")])], Cancellation);
        var login = Request();
        var orders = Request("https://{{host}}/{{orderId}}/{{token}}/{{size}}/{{gone}}");
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new()
        {
            Id = Guid.NewGuid(),
            Parameters = [new("orderId")],
            Variables = [new("token"), new("size") { Default = JsonSerializer.SerializeToElement(50) }, new("gone")],
            Steps = [new() { Name = "Login", Request = login, Saves = [new("token", "$.token")] }, new() { Request = orders }],
        }, harness);
        await main.Environments.ChooseAsync(main.Environments.Items.Single());
        main.EnvironmentChosen();

        // Act
        var shown = workflow.Steps.Select(step => (string.Join(", ", step.UsedNames), string.Join(", ", step.SavedNames)));

        // Assert
        Assert.Equal([("", "token"), ("orderId, token", "")], shown);
    }

    [Theory]
    [InlineData("$.token")]
    [InlineData("$['token']")]
    public async Task RunAsync_WhenAStepSavesFromTheBody_ThenMarksWhereInTheResponseItWasSavedFrom(string from)
    {
        // Arrange
        using var harness = new Harness(send: Answering(Ok("""{"name":"Emily","token":"abc"}""")));
        var workflow = await OpenAsync(harness.Main(), "Flow", new()
        {
            Id = Guid.NewGuid(),
            Variables = [new("token")],
            Steps = [new() { Request = Request(), Saves = [new("token", from)] }],
        }, harness);

        // Act
        await workflow.RunAsync();

        // Assert
        var saved = workflow.Steps.Single().Result.ResponseMarks.Where(mark => mark.Saved is not null).Select(mark => (mark.Path, mark.Saved));
        Assert.Equal([("$.token", harness.Translator.Format("Workflow.SavedAs", "token"))], saved);
    }

    [Fact]
    public async Task RunAsync_WhenWhereToSaveFromIsEditedDuringTheRun_ThenMarksWhereTheRunSavedFrom()
    {
        // Arrange
        var sending = new TaskCompletionSource();
        var answer = new TaskCompletionSource<ApiResponse>();
        using var harness = new Harness(send: () =>
        {
            sending.TrySetResult();
            return answer.Task;
        });
        var workflow = await OpenAsync(harness.Main(), "Flow", new()
        {
            Id = Guid.NewGuid(),
            Variables = [new("token")],
            Steps = [new() { Request = Request(), Saves = [new("token", "$.token")] }],
        }, harness);
        var running = workflow.RunAsync();
        await sending.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Act
        workflow.Steps.Single().Saves.Rows[0].Value = "$.name";
        answer.SetResult(Ok("""{"name":"Emily","token":"abc"}"""));
        await running.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Assert
        Assert.Equal(["$.token"], workflow.Steps.Single().Result.ResponseMarks.Where(mark => mark.Saved is not null).Select(mark => mark.Path));
    }

    [Theory]
    [InlineData(200, "2/2 steps", true)]
    [InlineData(500, "0/2 steps", false)]
    public async Task RunAsync_WhenTheRunEnds_ThenSummarizesHowManyStepsSucceeded(int status, string expected, bool succeeded)
    {
        // Arrange
        using var harness = new Harness(send: () => Task.FromResult(new ApiResponse(status, "", 1, 0, [], "")));
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Request() }, new() { Request = Request() }] }, harness);

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal((expected, succeeded), (workflow.Summary?.Split(" · ")[0], workflow.HasSucceeded));
    }

    [Fact]
    public async Task RunAsync_WhenEveryStepSucceeds_ThenShowsEachStepAndKeepsTheCallsOutOfTheHistory()
    {
        // Arrange
        using var harness = new Harness(send: Answering(Ok("""{"token":"abc"}"""), Ok("""{"id":7}""")));
        var login = Request();
        var orders = Request("https://dev.local/{{token}}");
        var main = harness.Main();
        var id = Guid.NewGuid();
        var workflow = await OpenAsync(main, "Flow", new()
        {
            Id = id,
            Variables = [new("token")],
            Steps = [new() { Request = login, Saves = [new("token", "$.token")] }, new() { Request = orders }],
        }, harness);

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal([("200", true), ("200", true)], workflow.Steps.Select(step => (step.Status, step.IsSuccess)));
        Assert.Contains("\"id\": 7", workflow.Steps[1].Result.Response!.Body);
        Assert.Equal("abc", harness.Sender.Environment!.Resolve("{{token}}"));
        Assert.Empty(await harness.History().LatestAsync(10, Cancellation));
        Assert.Single(Directory.GetFiles(Path.Combine(harness.Folder.Runs, $"{id}")));
        Assert.False(workflow.IsRunning);
    }

    [Fact]
    public async Task RunAsync_WhenAStepFails_ThenSkipsTheRestAndTellsWhy()
    {
        // Arrange
        using var harness = new Harness(send: Answering(new ApiResponse(500, "Internal Server Error", 1, 2, [], "{}")));
        var first = Request();
        var second = Request();
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = first }, new() { Request = second }] }, harness);

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal([("500", false, null), (null, false, harness.Translator.Of("Workflow.Skipped"))], workflow.Steps.Select(step => (step.Status, step.IsSuccess, step.State)));
        Assert.Equal([true, false], workflow.Steps.Select(step => step.HasFailed));
    }

    [Fact]
    public async Task RunAsync_WhenAParameterHasNoDefault_ThenAsksForItAndSendsItsValue()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs { Values = new Dictionary<string, string> { ["orderId"] = "o-17" } }, Answering(Ok("{}")));
        var orders = Request("https://dev.local/{{orderId}}/{{pageSize}}");
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new()
        {
            Id = Guid.NewGuid(),
            Parameters = [new("orderId"), new("pageSize") { Default = JsonSerializer.SerializeToElement(50) }],
            Steps = [new() { Request = orders }],
        }, harness);

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal(["orderId"], harness.Dialogs.AskedValues);
        Assert.Equal("o-17/50", harness.Sender.Environment!.Resolve("{{orderId}}/{{pageSize}}"));
    }

    [Fact]
    public async Task RunAsync_WhenTheCheckFindsProblems_ThenListsThemAndSendsNothing()
    {
        // Arrange
        using var harness = new Harness();
        var orders = Request("https://dev.local/{{token}}");
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = orders }, new() { Request = Request(), Saves = [new("token", "$.token")] }] }, harness);

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal([harness.Translator.Format("Workflow.StepProblem", 1, harness.Translator.Format("WorkflowProblem.UsedBeforeSaved", "token"))], workflow.Problems);
        Assert.Null(harness.Sender.Request);
        Assert.False(Directory.Exists(harness.Folder.Runs));
    }

    [Fact]
    public async Task OpenWorkflowAsync_WhenOpenedAgainAfterARun_ThenShowsNothingOfThatRun()
    {
        // Arrange
        using var harness = new Harness(send: Answering(Ok("{}")));
        var ping = Request();
        await harness.WorkflowLibrary.SaveAsync("Other", new() { Id = Guid.NewGuid() }, Cancellation);
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping }] }, harness);
        await workflow.RunAsync();

        // Act
        await main.OpenWorkflowAsync("Other");
        await main.OpenWorkflowAsync("Flow");

        // Assert
        var step = main.Workflow!.Steps.Single();
        Assert.Equal((null, null, null), (step.Status, step.Result.Response, step.State));
    }

    [Fact]
    public async Task OpenWorkflowAsync_WhenTheOpenWorkflowIsRunning_ThenAsksBeforeCancellingIt()
    {
        // Arrange
        var sending = new TaskCompletionSource();
        var answer = new TaskCompletionSource<ApiResponse>();
        using var harness = new Harness(send: () =>
        {
            sending.TrySetResult();
            return answer.Task;
        });
        var ping = Request();
        await harness.WorkflowLibrary.SaveAsync("Other", new() { Id = Guid.NewGuid() }, Cancellation);
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping }] }, harness);
        var running = workflow.RunAsync();
        await sending.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Act
        await main.OpenWorkflowAsync("Other");
        var stillRunning = workflow.IsRunning;
        answer.SetResult(Ok("{}"));
        await running.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Assert
        Assert.Equal(harness.Translator.Format("Workflow.StopMessage", "Flow"), harness.Dialogs.ConfirmQuestion!.Value.Message);
        Assert.True(stillRunning);
        Assert.Same(workflow, main.Workflow);
    }

    [Fact]
    public async Task Headers_WhenAHeaderIsEdited_ThenMarksTheWorkflowUnsavedAndSavesIt()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Request() }] }, harness);
        var row = workflow.Steps[0].Request!.Headers.Rows[0];

        // Act
        row.Name = "Authorization";
        row.Value = "Bearer {{token}}";
        var dirty = workflow.IsDirty;
        await workflow.SaveAsync();

        // Assert
        Assert.True(dirty);
        var header = (await harness.WorkflowLibrary.LoadAsync("Flow", Cancellation))!.Steps.Single().Request!.Headers.Single();
        Assert.Equal(("Authorization", "Bearer {{token}}"), (header.Name, header.Value));
    }

    [Fact]
    public async Task SaveAsync_WhenTheRequestOfAStepIsEdited_ThenSavesItsQueryAndBody()
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await OpenAsync(harness.Main(), "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = Request() }] }, harness);
        var request = workflow.Steps[0].Request!;
        request.Query.Rows[0].Name = "page";
        request.Query.Rows[0].Value = "{{page}}";
        request.BodyKind = BodyKind.Json;
        request.Body = "{}";
        request.UseEnvironmentVariablesInBody = false;

        // Act
        await workflow.SaveAsync();

        // Assert
        var saved = (await harness.WorkflowLibrary.LoadAsync("Flow", Cancellation))!.Steps.Single().Request!;
        Assert.Equal(("page", "{{page}}", BodyKind.Json, "{}", false), (saved.Query.Single().Name, saved.Query.Single().Value, saved.BodyKind, saved.Body, saved.UseEnvironmentVariablesInBody));
    }

    [Fact]
    public async Task WorkflowsChangedAsync_WhenTheEditorsOwnSaveIsSeen_ThenKeepsTheRunShown()
    {
        // Arrange
        using var harness = new Harness(send: Answering(Ok("{}")));
        var ping = Request();
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping }] }, harness);
        await workflow.RunAsync();
        workflow.Parameters.Rows[0].Name = "token";
        await workflow.SaveAsync();

        // Act
        await main.WorkflowsChangedAsync();

        // Assert
        Assert.Equal("200", workflow.Steps.Single().Status);
    }

    [Fact]
    public async Task RunAsync_WhenTheWorkflowChangesOnDiskWhileValuesAreAsked_ThenRunsTheNewSteps()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs { Values = new Dictionary<string, string> { ["orderId"] = "o-17" } }, Answering(Ok("{}"), Ok("{}")));
        var ping = Request();
        var main = harness.Main();
        var id = Guid.NewGuid();
        var workflow = await OpenAsync(main, "Flow", new() { Id = id, Parameters = [new("orderId")], Steps = [new() { Request = ping }, new() { Request = ping }] }, harness);
        harness.Dialogs.Asking = () =>
        {
            harness.WorkflowLibrary.SaveAsync("Flow", new() { Id = id, Parameters = [new("orderId")], Steps = [new() { Request = ping }] }, Cancellation).GetAwaiter().GetResult();
            main.WorkflowsChangedAsync().GetAwaiter().GetResult();
        };

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal("200", workflow.Steps.Single().Status);
    }

    [Fact]
    public async Task RunAsync_WhenTheWorkflowIsClosedWhileValuesAreAsked_ThenSendsNothing()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs { Values = new Dictionary<string, string> { ["orderId"] = "o-17" } });
        var ping = Request();
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Parameters = [new("orderId")], Steps = [new() { Request = ping }] }, harness);
        harness.Dialogs.Asking = () => _ = workflow.CloseAsync();

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Null(harness.Sender.Request);
    }

    [Fact]
    public async Task WorkflowsChangedAsync_WhenTheFileChangesDuringARun_ThenReloadsItWhenTheRunEnds()
    {
        // Arrange
        var sending = new TaskCompletionSource();
        var answer = new TaskCompletionSource<ApiResponse>();
        using var harness = new Harness(send: () =>
        {
            sending.TrySetResult();
            return answer.Task;
        });
        var ping = Request();
        var main = harness.Main();
        var id = Guid.NewGuid();
        var workflow = await OpenAsync(main, "Flow", new() { Id = id, Steps = [new() { Request = ping }] }, harness);
        var running = workflow.RunAsync();
        await sending.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
        await harness.WorkflowLibrary.SaveAsync("Flow", new() { Id = id, Parameters = [new("theirs")], Steps = [new() { Request = ping }] }, Cancellation);

        // Act
        await main.WorkflowsChangedAsync();
        var duringRun = workflow.Parameters.Rows[0].Name;
        answer.SetResult(Ok("{}"));
        await running.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Assert
        Assert.Equal(("", "theirs"), (duringRun, workflow.Parameters.Rows[0].Name));
    }

    [Fact]
    public async Task RunAsync_WhenRunAgain_ThenClearsTheEarlierRun()
    {
        // Arrange
        using var harness = new Harness(send: Answering(Ok("{}")));
        var ping = Request();
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping }] }, harness);
        await workflow.RunAsync();
        workflow.Parameters.Rows[0].Name = "pageSize";
        workflow.Parameters.Rows[0].Value = "fifty";

        // Act
        await workflow.RunAsync();

        // Assert
        var step = workflow.Steps.Single();
        Assert.Equal((null, null, null), (step.Status, step.Result.Response, step.State));
    }

    [Fact]
    public async Task Cancel_WhenRunning_ThenCancelsTheStepAndSkipsTheRest()
    {
        // Arrange
        var sending = new TaskCompletionSource();
        using var harness = new Harness(send: () =>
        {
            sending.TrySetResult();
            return new TaskCompletionSource<ApiResponse>().Task;
        });
        var ping = Request();
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping }, new() { Request = ping }] }, harness);
        var running = workflow.RunAsync();
        await sending.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Act
        workflow.Cancel();
        await running.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Assert
        Assert.Equal([harness.Translator.Of("Workflow.Cancelled"), harness.Translator.Of("Workflow.Skipped")], workflow.Steps.Select(step => step.State));
        Assert.False(workflow.IsRunning);
    }

    [Fact]
    public async Task RunAsync_WhenTheCallFails_ThenTellsWhyInTheChosenLanguage()
    {
        // Arrange
        using var harness = new Harness(send: () => throw new HttpRequestException());
        var ping = Request();
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping }] }, harness);

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal(harness.Translator.Of("RequestProblem.NetworkFailed"), workflow.Steps.Single().Error);
    }

    [Fact]
    public async Task RunAsync_WhenASavePathIsMissing_ThenTellsWhichInTheChosenLanguage()
    {
        // Arrange
        using var harness = new Harness(send: Answering(Ok("{}")));
        var ping = Request();
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Variables = [new("token")], Steps = [new() { Request = ping, Saves = [new("token", "$.token")] }] }, harness);

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal(harness.Translator.Format("Workflow.MissingSave", "$.token"), workflow.Steps.Single().Error);
    }
}
