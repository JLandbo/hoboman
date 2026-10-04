using Hoboman.Tests.Auth;

namespace Hoboman.Tests.ViewModels;

public sealed class WorkflowAuthRefreshTests
{
    static readonly AuthSettings _clientCredentials = new(AuthKind.OAuth2, OAuth: new() { Grant = OAuthGrant.ClientCredentials, TokenUrl = "https://dev.local/token", ClientId = "client" });

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static async Task<WorkflowViewModel> OpenAsync(Harness harness, AuthSettings? auth, AuthSettings stepAuth)
    {
        await harness.WorkflowLibrary.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Auth = auth, Steps = [new() { Request = new() { Url = "https://dev.local/ping", Auth = stepAuth } }] }, TestContext.Current.CancellationToken);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenWorkflowAsync("Flow");
        return main.Workflow!;
    }

    [Fact]
    public async Task HasOAuth_WhenTheWorkflowsAuthIsOAuth_ThenTheWorkflowAndTheStepThatInheritsCanRefresh()
    {
        // Arrange
        using var harness = new Harness();

        // Act
        var workflow = await OpenAsync(harness, _clientCredentials, new(AuthKind.Inherit));

        // Assert
        Assert.Equal((true, true), (workflow.CanRefreshAuth, workflow.Steps.Single().CanRefreshAuth));
    }

    [Fact]
    public async Task HasOAuth_WhenTheWorkflowsAuthBecomesOAuth_ThenTheStepThatInheritsCanRefresh()
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await OpenAsync(harness, null, new(AuthKind.Inherit));
        var step = workflow.Steps.Single();
        var told = false;
        step.PropertyChanged += (_, e) => told |= e.PropertyName == nameof(WorkflowStepViewModel.HasOAuth);

        // Act
        workflow.Auth.Kind = AuthKind.OAuth2;

        // Assert
        Assert.Equal((true, true), (told, step.HasOAuth));
    }

    [Fact]
    public async Task ReloadAsync_WhenALoginIsOpen_ThenWaitsForItSoItsTokenIsKept()
    {
        // Arrange
        var login = new TaskCompletionSource<OAuthToken>();
        using var harness = new Harness(oauth: new(_ => login.Task));
        var (flowId, stepId) = (Guid.NewGuid(), Guid.NewGuid());
        Workflow Flow(string url) => new() { Id = flowId, Steps = [new() { Request = new() { Id = stepId, Url = url, Auth = _clientCredentials } }] };
        await harness.WorkflowLibrary.SaveAsync("Flow", Flow("https://dev.local/ping"), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenWorkflowAsync("Flow");
        var workflow = main.Workflow!;
        var refreshing = workflow.RefreshAuthAsync(workflow.Steps.Single());
        await harness.WorkflowLibrary.SaveAsync("Flow", Flow("https://dev.local/changed"), Cancellation);
        await workflow.ReloadAsync();

        // Act
        login.SetResult(FakeOAuthClient.Token);

        // Assert
        Assert.Equal((true, "https://dev.local/changed"), (await refreshing, workflow.Steps.Single().Request!.Url));
    }

    [Fact]
    public async Task HasOAuth_WhenAStepHasBasicAuth_ThenItCannotRefresh()
    {
        // Arrange
        using var harness = new Harness();

        // Act
        var workflow = await OpenAsync(harness, _clientCredentials, new(AuthKind.Basic, "admin"));

        // Assert
        Assert.False(workflow.Steps.Single().HasOAuth);
    }

    [Fact]
    public async Task RefreshAuthAsync_WhenTheWorkflowHasOAuth_ThenFetchesItsTokenAndSavesIt()
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await OpenAsync(harness, _clientCredentials, new(AuthKind.Inherit));
        var id = (await harness.WorkflowLibrary.LoadAsync("Flow", Cancellation))!.Id;

        // Act
        var succeeded = await workflow.RefreshAuthAsync();

        // Assert
        Assert.Equal((true, true), (succeeded, await harness.Secrets.OfAsync(id, SecretKind.OAuthToken, Cancellation) is not null));
    }

    [Fact]
    public async Task RefreshAuthAsync_WhenTheStepInherits_ThenFetchesTheWorkflowsToken()
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await OpenAsync(harness, _clientCredentials, new(AuthKind.Inherit));

        // Act
        await workflow.RefreshAuthAsync(workflow.Steps.Single());

        // Assert
        Assert.NotNull(workflow.Auth.AccessToken);
    }

    [Fact]
    public async Task RefreshAuthAsync_WhenTheStepHasItsOwnOAuth_ThenFetchesItsOwnToken()
    {
        // Arrange
        using var harness = new Harness();
        var workflow = await OpenAsync(harness, _clientCredentials, _clientCredentials);
        var step = workflow.Steps.Single();

        // Act
        await workflow.RefreshAuthAsync(step);

        // Assert
        Assert.Equal((true, false), (step.Auth!.AccessToken is not null, workflow.Auth.AccessToken is not null));
    }
}
