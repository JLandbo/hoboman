using System.Net.Http;
using Hoboman.Services;

namespace Hoboman.Tests.Auth;

public sealed class AuthRefreshServiceTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    RequestLibrary Library => field ??= new(new(_temporary.Path), NullLogger<RequestLibrary>.Instance);

    SecretStore Secrets => field ??= new(new(_temporary.Path), NullLogger<SecretStore>.Instance);

    AuthRefreshService Service(FakeOAuthClient? oauth = null) => new(oauth ?? new(), Library, Secrets);

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public async Task FetchAsync_WhenTheSameOwnerAndEnvironmentAreFetching_ThenFetchesOnlyOnce()
    {
        var login = new TaskCompletionSource<OAuthToken>();
        var requestsMade = 0;
        var service = Service(new(cancellationToken => { requestsMade++; return login.Task.WaitAsync(cancellationToken); }));
        var source = new AuthSource(Guid.NewGuid(), new(AuthKind.OAuth2));
        var fetching = service.FetchAsync(source, "secret", new("Dev", []), (_, _, _) => Task.FromResult(true), Cancellation);

        var duplicate = await service.FetchAsync(source, "secret", new("Dev", []), (_, _, _) => Task.FromResult(true), Cancellation);
        login.SetResult(FakeOAuthClient.Token);
        var fetched = await fetching;

        Assert.False(duplicate);
        Assert.True(fetched);
        Assert.Equal(1, requestsMade);
        Assert.False(service.IsRefreshing(AuthRefreshService.OwnerOf(source), "Dev"));
    }

    [Fact]
    public async Task FetchAsync_WhenCancelled_ThenReleasesTheOwner()
    {
        var login = new TaskCompletionSource<OAuthToken>();
        var service = Service(new(cancellationToken => login.Task.WaitAsync(cancellationToken)));
        var source = new AuthSource(Guid.NewGuid(), new(AuthKind.OAuth2));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var fetching = service.FetchAsync(source, "secret", ApiEnvironment.None, (_, _, _) => Task.FromResult(true), cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetching);
        Assert.False(service.IsRefreshing(AuthRefreshService.OwnerOf(source), ""));
    }

    [Fact]
    public async Task FetchAsync_WhenOAuthFails_ThenReleasesTheOwner()
    {
        var service = Service(new(_ => Task.FromException<OAuthToken>(new HttpRequestException("Rejected"))));
        var source = new AuthSource(Guid.NewGuid(), new(AuthKind.OAuth2));

        await Assert.ThrowsAsync<HttpRequestException>(() => service.FetchAsync(source, "secret", ApiEnvironment.None, (_, _, _) => Task.FromResult(true), Cancellation));

        Assert.False(service.IsRefreshing(AuthRefreshService.OwnerOf(source), ""));
    }

    [Fact]
    public async Task RefreshFolderAsync_WhenTheFolderHasItsOwnId_ThenSavesItsTokenWithoutChangingTheId()
    {
        var settings = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.OAuth2) };
        await Library.SaveFolderAsync("Users", settings, Cancellation);
        await Secrets.SaveAsync(settings.Id, SecretKind.ClientSecret, "secret", Cancellation);
        var oauth = new FakeOAuthClient();

        var refreshed = await Service(oauth).RefreshFolderAsync(new(settings.Id, settings.Auth, "Users"), new("Dev", []), Cancellation);

        Assert.True(refreshed);
        Assert.Equal(settings, await Library.LoadFolderAsync("Users", Cancellation));
        Assert.Equal(FakeOAuthClient.Token.ToJson(), await Secrets.OfAsync(settings.Id, SecretKind.OAuthToken, "Dev", Cancellation));
        Assert.Equal("secret", oauth.Asked!.Value.ClientSecret);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshFolderAsync_WhenTheIdChangesDuringParallelRefreshes_ThenAllEnvironmentsUseTheNewId(bool sharedId)
    {
        var settings = new FolderSettings { Id = sharedId ? Guid.NewGuid() : Guid.Empty, Auth = new(AuthKind.OAuth2) };
        await Library.SaveFolderAsync("Users", settings, Cancellation);
        if (sharedId)
        {
            await Library.SaveFolderAsync("Copy", settings, Cancellation);
            await Secrets.SaveAsync(settings.Id, SecretKind.ClientSecret, "secret", Cancellation);
        }
        var developmentLogin = new TaskCompletionSource<OAuthToken>();
        var productionLogin = new TaskCompletionSource<OAuthToken>();
        using var loginStarted = new SemaphoreSlim(0);
        var requestsMade = 0;
        var service = Service(new(cancellationToken =>
        {
            var login = ++requestsMade switch
            {
                1 => developmentLogin.Task.WaitAsync(cancellationToken),
                2 => productionLogin.Task.WaitAsync(cancellationToken),
                _ => Task.FromResult(FakeOAuthClient.Token),
            };
            loginStarted.Release();
            return login;
        }));
        var source = new AuthSource(settings.Id, settings.Auth, "Users");
        var development = service.RefreshFolderAsync(source, new("Dev", []), Cancellation);
        await loginStarted.WaitAsync(Cancellation);
        var production = service.RefreshFolderAsync(source, new("Prod", []), Cancellation);
        await loginStarted.WaitAsync(Cancellation);

        developmentLogin.SetResult(FakeOAuthClient.Token);
        Assert.True(await development);
        var saved = (await Library.LoadFolderAsync("Users", Cancellation))!;
        Assert.NotEqual(settings.Id, saved.Id);
        Assert.True(service.IsRefreshing(AuthRefreshService.OwnerOf(source), "Prod"));
        Assert.True(service.IsRefreshing(AuthRefreshService.OwnerOf(source with { SecretsId = saved.Id }), "Prod"));
        Assert.False(await service.RefreshFolderAsync(source with { SecretsId = saved.Id }, new("Prod", []), Cancellation));
        Assert.Equal(2, requestsMade);
        Assert.True(await service.RefreshFolderAsync(source, new("Staging", []), Cancellation));
        productionLogin.SetResult(FakeOAuthClient.Token);

        Assert.True(await production);
        Assert.Equal(["Dev", "Prod", "Staging"], (await Secrets.OfEachEnvironmentAsync(saved.Id, SecretKind.OAuthToken, Cancellation)).Keys.Order());
        if (sharedId)
        {
            Assert.Equal("secret", await Secrets.OfAsync(saved.Id, SecretKind.ClientSecret, Cancellation));
            Assert.Equal(settings, await Library.LoadFolderAsync("Copy", Cancellation));
        }
    }

    [Fact]
    public async Task RefreshFolderAsync_WhenTheFolderMovesDuringLogin_ThenDoesNotRecreateIt()
    {
        var settings = new FolderSettings { Auth = new(AuthKind.OAuth2) };
        await Library.SaveFolderAsync("Users", settings, Cancellation);
        var login = new TaskCompletionSource<OAuthToken>();
        var service = Service(new(cancellationToken => login.Task.WaitAsync(cancellationToken)));
        var fetching = service.RefreshFolderAsync(new(settings.Id, settings.Auth, "Users"), ApiEnvironment.None, Cancellation);

        await Library.RenameFolderAsync("Users", "Moved", Cancellation);
        login.SetResult(FakeOAuthClient.Token);

        Assert.False(await fetching);
        Assert.False(Library.FolderExists("Users"));
        Assert.Equal(settings, await Library.LoadFolderAsync("Moved", Cancellation));
    }

    [Fact]
    public async Task RefreshFolderAsync_WhenTheAuthChangesDuringLogin_ThenDiscardsTheToken()
    {
        var settings = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.OAuth2) };
        await Library.SaveFolderAsync("Users", settings, Cancellation);
        var login = new TaskCompletionSource<OAuthToken>();
        var service = Service(new(cancellationToken => login.Task.WaitAsync(cancellationToken)));
        var fetching = service.RefreshFolderAsync(new(settings.Id, settings.Auth, "Users"), ApiEnvironment.None, Cancellation);

        await Library.SaveFolderAsync("Users", settings with { Auth = AuthSettings.None }, Cancellation);
        login.SetResult(FakeOAuthClient.Token);

        Assert.False(await fetching);
        Assert.Null(await Secrets.OfAsync(settings.Id, SecretKind.OAuthToken, Cancellation));
    }

    [Fact]
    public async Task FetchAsync_WhenTheEnvironmentIsRenamedDuringLogin_ThenAcceptsTheNewName()
    {
        var login = new TaskCompletionSource<OAuthToken>();
        var service = Service(new(cancellationToken => login.Task.WaitAsync(cancellationToken)));
        var source = new AuthSource(Guid.NewGuid(), new(AuthKind.OAuth2));
        string? acceptedEnvironment = null;
        var fetching = service.FetchAsync(source, "secret", new("Dev", []), (_, environment, _) => { acceptedEnvironment = environment; return Task.FromResult(true); }, Cancellation);

        await service.FollowEnvironmentsAsync(new Dictionary<string, string?> { ["Dev"] = "Development" }, Cancellation);
        login.SetResult(FakeOAuthClient.Token);

        Assert.True(await fetching);
        Assert.Equal("Development", acceptedEnvironment);
    }

    [Fact]
    public async Task FetchAsync_WhenTheEnvironmentIsRemovedDuringLogin_ThenDoesNotAcceptTheToken()
    {
        var login = new TaskCompletionSource<OAuthToken>();
        var service = Service(new(cancellationToken => login.Task.WaitAsync(cancellationToken)));
        var source = new AuthSource(Guid.NewGuid(), new(AuthKind.OAuth2));
        var accepted = false;
        var fetching = service.FetchAsync(source, "secret", new("Dev", []), (_, _, _) => { accepted = true; return Task.FromResult(true); }, Cancellation);

        await service.FollowEnvironmentsAsync(new Dictionary<string, string?> { ["Dev"] = null }, Cancellation);
        login.SetResult(FakeOAuthClient.Token);

        Assert.False(await fetching);
        Assert.False(accepted);
    }

    [Fact]
    public async Task SaveAsync_WhenAnotherSaveIsRunning_ThenWaitsForIt()
    {
        var release = new TaskCompletionSource();
        var service = Service();
        var first = service.SaveAsync(() => release.Task, Cancellation);
        var secondRan = false;
        var second = service.SaveAsync(() => { secondRan = true; return Task.CompletedTask; }, Cancellation);
        Assert.False(secondRan);

        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.True(secondRan);
    }
}
