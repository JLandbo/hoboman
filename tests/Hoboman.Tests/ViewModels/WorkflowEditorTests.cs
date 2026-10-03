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

    static async Task<Guid> SaveRequestAsync(Harness harness, string name, string url = "https://dev.local/ping")
    {
        var request = ApiRequest.New() with { Url = url, Auth = AuthSettings.None };
        await harness.Library.SaveAsync(name, request, TestContext.Current.CancellationToken);
        return request.Id;
    }

    static async Task<WorkflowViewModel> OpenAsync(MainViewModel main, string name, Workflow workflow, Harness harness)
    {
        await harness.WorkflowLibrary.SaveAsync(name, workflow, TestContext.Current.CancellationToken);
        await main.LoadAsync();
        await main.OpenWorkflowAsync(name);
        return main.Workflow!;
    }

    [Fact]
    public async Task NewWorkflowAsync_WhenNamed_ThenCreatesItWithAnIdAndShowsItInsteadOfTheTab()
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
        Assert.Null(main.SelectedTab);
        Assert.Same(main.Workflow, main.Content);
        Assert.Equal("Ordre-sync", main.Workflow!.Name);
    }

    [Fact]
    public async Task Content_WhenATabIsChosenAndTheWorkflowOpenedAgain_ThenSwitchesBetweenThem()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid() }, harness);
        var tab = main.Tabs.Single();

        // Act
        main.SelectedTab = tab;
        var shownTab = main.Content;
        await main.OpenWorkflowAsync("Flow");

        // Assert
        Assert.Same(tab, shownTab);
        Assert.Same(workflow, main.Content);
        Assert.Null(main.SelectedTab);
    }

    [Fact]
    public async Task RenameWorkflowAsync_WhenTheWorkflowIsOpen_ThenItsNextSaveGoesToTheNewName()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Renamed"));
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid() }, harness);
        workflow.Variables.Rows[0].Name = "token";

        // Act
        await main.RenameWorkflowAsync("Flow");
        await workflow.SaveAsync();

        // Assert
        Assert.Equal(["Renamed"], main.Workflows.Names);
        Assert.Equal("token", (await harness.WorkflowLibrary.LoadAsync("Renamed", Cancellation))!.Variables.Single().Name);
        Assert.False(Directory.Exists(Path.Combine(harness.Folder.Workflows, "Flow")));
    }

    [Fact]
    public async Task DeleteWorkflowAsync_WhenConfirmed_ThenRemovesItAndShowsATab()
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
        Assert.Same(main.Tabs.Single(), main.Content);
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
            workflow.Variables.Rows[0].Name = "mine";
        }
        await harness.WorkflowLibrary.SaveAsync("Flow", new() { Id = id, Variables = [new("theirs")] }, Cancellation);

        // Act
        await main.WorkflowsChangedAsync();

        // Assert
        Assert.Equal(edited ? "mine" : "theirs", workflow.Variables.Rows[0].Name);
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
            workflow.Variables.Rows[0].Name = "mine";
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
        workflow.Variables.Rows[0].Name = "mine";

        // Act
        await main.OpenWorkflowAsync("Other");

        // Assert
        Assert.Equal(harness.Translator.Format("Workflow.CloseMessage", "Flow"), harness.Dialogs.ConfirmQuestion!.Value.Message);
        Assert.Same(workflow, main.Workflow);
        Assert.True(workflow.IsDirty);
    }

    [Fact]
    public async Task AddStepAsync_WhenTheRequestHasNoId_ThenTellsToSaveItFirst()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAsync("Ping", ApiRequest.New() with { Id = Guid.Empty }, Cancellation);
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid() }, harness);

        // Act
        await workflow.AddStepAsync("Ping");

        // Assert
        Assert.Empty(workflow.Steps);
        Assert.Equal(harness.Translator.Of("Workflow.NoId"), harness.Dialogs.Notification!.Value.Message);
    }

    [Fact]
    public async Task AddStepAsync_WhenTheRequestSharesItsId_ThenTellsToRemoveTheCopy()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New();
        await harness.Library.SaveAsync("Ping", request, Cancellation);
        await harness.Library.SaveAsync("Ping copy", request, Cancellation);
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid() }, harness);

        // Act
        await workflow.AddStepAsync("Ping");

        // Assert
        Assert.Empty(workflow.Steps);
        Assert.Equal(harness.Translator.Of("Workflow.SharedId"), harness.Dialogs.Notification!.Value.Message);
    }

    [Fact]
    public async Task SaveAsync_WhenStepsAreAddedAndMoved_ThenWritesTheRequestIdsInTheirOrder()
    {
        // Arrange
        using var harness = new Harness();
        var login = await SaveRequestAsync(harness, "Shop/Login");
        var orders = await SaveRequestAsync(harness, "Shop/Orders");
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid() }, harness);
        await workflow.AddStepAsync("Shop/Orders");
        await workflow.AddStepAsync("Shop/Login");

        // Act
        workflow.MoveStep(workflow.Steps[1], -1);
        await workflow.SaveAsync();

        // Assert
        Assert.Equal([login, orders], (await harness.WorkflowLibrary.LoadAsync("Flow", Cancellation))!.Steps.Select(step => step.Request));
        Assert.Equal(("Login", "Shop /"), (workflow.Steps[0].Title, workflow.Steps[0].Folder));
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
        workflow.Variables.Rows[0].Name = "token";
        Directory.Delete(Path.Combine(harness.Folder.Workflows, "Flow"), recursive: true);

        // Act
        await workflow.SaveAsync();

        // Assert
        Assert.Equal(harness.Translator.Of("Workflow.SaveFailed"), harness.Dialogs.Notification!.Value.Title);
        Assert.False(Directory.Exists(Path.Combine(harness.Folder.Workflows, "Flow")));
    }

    [Fact]
    public async Task Uses_WhenNamesComeFromDifferentPlaces_ThenTellsWhereEachComesFromWithoutValues()
    {
        // Arrange
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([new("Dev", [new("host", "dev.local")])], Cancellation);
        var login = await SaveRequestAsync(harness, "Login");
        var orders = await SaveRequestAsync(harness, "Orders", "https://{{host}}/{{orderId}}/{{token}}/{{page}}/{{size}}/{{gone}}");
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new()
        {
            Id = Guid.NewGuid(),
            Parameters = [new("orderId")],
            Variables = [new("token"), new("size") { Default = JsonSerializer.SerializeToElement(50) }, new("gone")],
            Steps = [new() { Request = login, Saves = [new("token", "$.token")] }, new() { Request = orders, With = [new("page", "{{external}}")] }],
        }, harness);
        await main.Environments.ChooseAsync(main.Environments.Items.Single());
        main.EnvironmentChosen();

        // Act
        var uses = workflow.Steps[1].Uses.Select(use => $"{use.Name} {use.Source}");

        // Assert
        Assert.Equal(["{{external}} missing", "{{gone}} missing", "{{host}} environment Dev", "{{orderId}} parameter", "{{page}} this step", "{{size}} default", "{{token}} step 1"], uses);
        Assert.Equal("uses gone, orderId, size, token", workflow.Steps[1].Summary);
        Assert.Equal("saves token", workflow.Steps[0].Summary);
    }

    [Fact]
    public async Task RunAsync_WhenEveryStepSucceeds_ThenShowsEachStepAndRemembersTheCallsAsFromTheApp()
    {
        // Arrange
        using var harness = new Harness(send: Answering(Ok("""{"token":"abc"}"""), Ok("""{"id":7}""")));
        var login = await SaveRequestAsync(harness, "Login");
        var orders = await SaveRequestAsync(harness, "Orders", "https://dev.local/{{token}}");
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
        Assert.Contains("\"id\": 7", workflow.Steps[1].Response!.Body);
        Assert.Equal("abc", harness.Sender.Environment!.Resolve("{{token}}"));
        var history = await harness.History().ReadAsync(await harness.History().LatestAsync(10, Cancellation), Cancellation);
        Assert.Equal([HistorySource.App, HistorySource.App], history.Select(file => file.Entry.Source));
        Assert.Single(Directory.GetFiles(Path.Combine(harness.Folder.Runs, $"{id}")));
        Assert.False(workflow.IsRunning);
    }

    [Fact]
    public async Task RunAsync_WhenAStepFails_ThenSkipsTheRestAndTellsWhy()
    {
        // Arrange
        using var harness = new Harness(send: Answering(new ApiResponse(500, "Internal Server Error", 1, 2, [], "{}")));
        var first = await SaveRequestAsync(harness, "First");
        var second = await SaveRequestAsync(harness, "Second");
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = first }, new() { Request = second }] }, harness);

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal([("500", false, null), (null, false, harness.Translator.Of("Workflow.Skipped"))], workflow.Steps.Select(step => (step.Status, step.IsSuccess, step.State)));
    }

    [Fact]
    public async Task RunAsync_WhenAParameterHasNoDefault_ThenAsksForItAndSendsItsValue()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs { Values = new Dictionary<string, string> { ["orderId"] = "o-17" } }, Answering(Ok("{}")));
        var orders = await SaveRequestAsync(harness, "Orders", "https://dev.local/{{orderId}}/{{pageSize}}");
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
        var orders = await SaveRequestAsync(harness, "Orders", "https://dev.local/{{token}}");
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Variables = [new("token")], Steps = [new() { Request = orders }] }, harness);

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal([harness.Translator.Format("Workflow.StepProblem", 1, harness.Translator.Format("WorkflowProblem.UsedBeforeSaved", "token"))], workflow.Problems);
        Assert.Null(harness.Sender.Request);
        Assert.False(Directory.Exists(harness.Folder.Runs));
    }

    [Fact]
    public async Task RunAsync_WhenAClientCredentialsTokenIsMissing_ThenFetchesAndSavesItForTheChosenEnvironment()
    {
        // Arrange
        var calls = 0;
        using var harness = new Harness(send: () => ++calls == 1 ? throw new MissingSecretException(SecretKind.OAuthToken) : Task.FromResult(Ok("{}")));
        var dev = new ApiEnvironment("Dev", []) { Id = Guid.NewGuid() };
        await harness.EnvironmentStore.SaveAsync([dev], Cancellation);
        var request = ApiRequest.New() with { Url = "https://dev.local", Auth = new(AuthKind.OAuth2, OAuth: new() { Grant = OAuthGrant.ClientCredentials, TokenUrl = "https://dev.local/token" }) };
        await harness.Library.SaveAsync("Orders", request, Cancellation);
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = request.Id }] }, harness);
        await main.Environments.ChooseAsync(main.Environments.Items.Single());

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal("200", workflow.Steps.Single().Status);
        Assert.Equal("Dev", harness.OAuth.Asked!.Value.Environment!.Name);
        Assert.NotNull(await harness.Secrets.OfAsync(request.Id, SecretKind.OAuthToken, dev.Id, Cancellation));
    }

    [Fact]
    public async Task OpenWorkflowAsync_WhenOpenedAgainAfterARun_ThenShowsNothingOfThatRun()
    {
        // Arrange
        using var harness = new Harness(send: Answering(Ok("{}")));
        var ping = await SaveRequestAsync(harness, "Ping");
        await harness.WorkflowLibrary.SaveAsync("Other", new() { Id = Guid.NewGuid() }, Cancellation);
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping }] }, harness);
        await workflow.RunAsync();

        // Act
        await main.OpenWorkflowAsync("Other");
        await main.OpenWorkflowAsync("Flow");

        // Assert
        var step = main.Workflow!.Steps.Single();
        Assert.Equal((null, null, null), (step.Status, step.Response, step.State));
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
        var ping = await SaveRequestAsync(harness, "Ping");
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
    public async Task RunAsync_WhenTheRequestHasUnsavedEditsInATab_ThenSendsTheSavedRequest()
    {
        // Arrange
        using var harness = new Harness(send: Answering(Ok("{}")));
        var ping = await SaveRequestAsync(harness, "Ping");
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping }] }, harness);
        await main.OpenAsync(RequestTreeViewModel.Flatten(main.Tree.Nodes).Single(node => node.Path == "Ping"));
        main.SelectedTab!.Url = "https://dev.local/unsaved";

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal("https://dev.local/ping", harness.Sender.Request!.Url);
    }

    [Fact]
    public async Task With_WhenAStepsValueIsEdited_ThenMarksTheWorkflowUnsavedShowsWhereTheNameComesFromAndSavesIt()
    {
        // Arrange
        using var harness = new Harness();
        var orders = await SaveRequestAsync(harness, "Orders", "https://dev.local/{{page}}");
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = orders }] }, harness);
        var row = workflow.Steps[0].With.Rows[0];

        // Act
        row.Name = "page";
        row.Value = "2";
        var dirty = workflow.IsDirty;
        await workflow.SaveAsync();

        // Assert
        Assert.True(dirty);
        Assert.Equal(harness.Translator.Of("Workflow.FromThisStep"), workflow.Steps[0].Uses.Single().Source);
        var with = (await harness.WorkflowLibrary.LoadAsync("Flow", Cancellation))!.Steps.Single().With.Single();
        Assert.Equal(("page", "2"), (with.Name, with.Value));
    }

    [Fact]
    public async Task WorkflowsChangedAsync_WhenTheEditorsOwnSaveIsSeen_ThenKeepsTheRunShown()
    {
        // Arrange
        using var harness = new Harness(send: Answering(Ok("{}")));
        var ping = await SaveRequestAsync(harness, "Ping");
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping }] }, harness);
        await workflow.RunAsync();
        workflow.Variables.Rows[0].Name = "token";
        await workflow.SaveAsync();

        // Act
        await main.WorkflowsChangedAsync();

        // Assert
        Assert.Equal("200", workflow.Steps.Single().Status);
    }

    [Fact]
    public async Task Uses_WhenAWithValueUsesANameTheStepAlsoGives_ThenTellsWhereItComesFromOutsideTheStep()
    {
        // Arrange
        using var harness = new Harness();
        var orders = await SaveRequestAsync(harness, "Orders", "https://dev.local/{{token}}/{{auth}}");
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = orders, With = [new("token", "abc"), new("auth", "Bearer {{token}}")] }] }, harness);

        // Act
        var uses = workflow.Steps[0].Uses.Select(use => $"{use.Name} {use.Source}");

        // Assert
        Assert.Equal(["{{auth}} this step", "{{token}} missing"], uses);
    }

    [Fact]
    public async Task RunAsync_WhenTheWorkflowChangesOnDiskWhileValuesAreAsked_ThenRunsTheNewSteps()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs { Values = new Dictionary<string, string> { ["orderId"] = "o-17" } }, Answering(Ok("{}"), Ok("{}")));
        var ping = await SaveRequestAsync(harness, "Ping");
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
        var ping = await SaveRequestAsync(harness, "Ping");
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Parameters = [new("orderId")], Steps = [new() { Request = ping }] }, harness);
        harness.Dialogs.Asking = workflow.Close;

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
        var ping = await SaveRequestAsync(harness, "Ping");
        var main = harness.Main();
        var id = Guid.NewGuid();
        var workflow = await OpenAsync(main, "Flow", new() { Id = id, Steps = [new() { Request = ping }] }, harness);
        var running = workflow.RunAsync();
        await sending.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
        await harness.WorkflowLibrary.SaveAsync("Flow", new() { Id = id, Variables = [new("theirs")], Steps = [new() { Request = ping }] }, Cancellation);

        // Act
        await main.WorkflowsChangedAsync();
        var duringRun = workflow.Variables.Rows[0].Name;
        answer.SetResult(Ok("{}"));
        await running.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Assert
        Assert.Equal(("", "theirs"), (duringRun, workflow.Variables.Rows[0].Name));
    }

    [Fact]
    public async Task RunAsync_WhenRunAgain_ThenClearsTheEarlierRun()
    {
        // Arrange
        using var harness = new Harness(send: Answering(Ok("{}")));
        var ping = await SaveRequestAsync(harness, "Ping");
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping }] }, harness);
        await workflow.RunAsync();
        workflow.Parameters.Rows[0].Name = "pageSize";
        workflow.Parameters.Rows[0].Value = "fifty";

        // Act
        await workflow.RunAsync();

        // Assert
        var step = workflow.Steps.Single();
        Assert.Equal((null, null, null), (step.Status, step.Response, step.State));
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
        var ping = await SaveRequestAsync(harness, "Ping");
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
        var ping = await SaveRequestAsync(harness, "Ping");
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
        var ping = await SaveRequestAsync(harness, "Ping");
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Variables = [new("token")], Steps = [new() { Request = ping, Saves = [new("token", "$.token")] }] }, harness);

        // Act
        await workflow.RunAsync();

        // Assert
        Assert.Equal(harness.Translator.Format("Workflow.MissingSave", "$.token"), workflow.Steps.Single().Error);
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenAStepsRequestIsRenamed_ThenShowsTheNewPath()
    {
        // Arrange
        using var harness = new Harness();
        var login = await SaveRequestAsync(harness, "Shop/Login");
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = login }] }, harness);
        await harness.Library.RenameAsync("Shop/Login", "Auth/Login", Cancellation);

        // Act
        await main.RequestsChangedAsync();

        // Assert
        Assert.Equal(("Login", "Auth /"), (workflow.Steps[0].Title, workflow.Steps[0].Folder));
    }

    [Fact]
    public async Task RunAsync_WhenTheFoldersClientCredentialsTokenIsMissing_ThenFetchesAndSavesItWithTheFolder()
    {
        // Arrange
        var calls = 0;
        using var harness = new Harness(send: () => ++calls == 1 ? throw new MissingSecretException(SecretKind.OAuthToken) : Task.FromResult(Ok("{}")));
        var dev = new ApiEnvironment("Dev", []) { Id = Guid.NewGuid() };
        await harness.EnvironmentStore.SaveAsync([dev], Cancellation);
        await harness.Library.SaveFolderAsync("Shop", new() { Auth = new(AuthKind.OAuth2, OAuth: new() { Grant = OAuthGrant.ClientCredentials, TokenUrl = "https://dev.local/token" }) }, Cancellation);
        var request = ApiRequest.New() with { Url = "https://dev.local", Auth = AuthSettings.Inherit };
        await harness.Library.SaveAsync("Shop/Orders", request, Cancellation);
        var main = harness.Main();
        var workflow = await OpenAsync(main, "Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = request.Id }] }, harness);
        await main.Environments.ChooseAsync(main.Environments.Items.Single());

        // Act
        await workflow.RunAsync();

        // Assert
        var folder = await harness.Library.LoadFolderAsync("Shop", Cancellation);
        Assert.Equal("200", workflow.Steps.Single().Status);
        Assert.NotNull(await harness.Secrets.OfAsync(folder!.Id, SecretKind.OAuthToken, dev.Id, Cancellation));
    }
}
