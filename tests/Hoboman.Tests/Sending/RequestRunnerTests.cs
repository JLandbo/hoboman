using System.Net.Http;

namespace Hoboman.Tests.Sending;

public sealed class RequestRunnerTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    HistoryStore History() => new(new AppFolder(_temporary.Path), NullLogger<HistoryStore>.Instance);

    async Task<IReadOnlyList<HistoryFile>> CallsAsync() => await History().ReadAsync(await History().LatestAsync(10, Cancellation), Cancellation);

    RequestLibrary Library() => new(new AppFolder(_temporary.Path), NullLogger<RequestLibrary>.Instance);

    RequestRunner Runner(Func<Task<ApiResponse>> send) => Runner(new FakeSender(send));

    RequestRunner Runner(FakeSender sender) => new(sender, Library(), History(), NullLogger<RequestRunner>.Instance);

    static ApiRequest Request() => ApiRequest.New() with { Url = "https://dev.local:5001/users?key=secret" };

    static ApiRequest OAuthRequest() => Request() with { Auth = new(AuthKind.OAuth2) };

    static Task<bool> NoToken(AuthSource auth) => Task.FromResult(false);

    static Task<bool> NewToken(AuthSource auth) => Task.FromResult(true);

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public async Task RunAsync_WhenTheCallSucceeds_ThenRemembersTheResponse()
    {
        // Arrange
        var runner = Runner(() => Task.FromResult(new ApiResponse(200, "OK", 0, 2, [], "{}")));

        // Act
        await runner.RunAsync(Request(), "Brugere/Hent", null, HistorySource.App, NoToken, Cancellation);

        // Assert
        Assert.Equal("{}", Assert.Single(await CallsAsync()).Entry.Response?.Body);
    }

    [Fact]
    public async Task RunAsync_WhenTheCallSucceeds_ThenRemembersTheName()
    {
        // Arrange
        var runner = Runner(() => Task.FromResult(new ApiResponse(200, "OK", 0, 2, [], "{}")));

        // Act
        await runner.RunAsync(Request(), "Brugere/Hent", null, HistorySource.App, NoToken, Cancellation);

        // Assert
        Assert.Equal("Brugere/Hent", Assert.Single(await CallsAsync()).Entry.Name);
    }

    [Fact]
    public async Task RunAsync_WhenTheCallSucceeds_ThenShowsTheAddressWithoutTheQuery()
    {
        // Arrange
        var runner = Runner(() => Task.FromResult(new ApiResponse(200, "OK", 0, 2, [], "{}")));

        // Act
        await runner.RunAsync(Request(), "Brugere/Hent", null, HistorySource.App, NoToken, Cancellation);

        // Assert
        Assert.Equal("dev.local:5001/users", Assert.Single(await CallsAsync()).Entry.Address);
    }

    [Fact]
    public async Task RunAsync_WhenTheCallFails_ThenThrows()
    {
        // Arrange
        var runner = Runner(() => throw new HttpRequestException("Ingen forbindelse"));

        // Act
        var failure = await Record.ExceptionAsync(() => runner.RunAsync(Request(), null, null, HistorySource.Cli, NoToken, Cancellation));

        // Assert
        Assert.IsType<HttpRequestException>(failure);
    }

    [Fact]
    public async Task RunAsync_WhenTheCallFails_ThenRemembersTheError()
    {
        // Arrange
        var runner = Runner(() => throw new HttpRequestException("Ingen forbindelse"));

        // Act
        await Record.ExceptionAsync(() => runner.RunAsync(Request(), null, null, HistorySource.Cli, NoToken, Cancellation));

        // Assert
        Assert.Equal("Ingen forbindelse", Assert.Single(await CallsAsync()).Entry.Error);
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_ThenRemembersNothing()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var runner = Runner(() => Task.FromCanceled<ApiResponse>(cancellation.Token));

        // Act
        await Record.ExceptionAsync(() => runner.RunAsync(Request(), null, null, HistorySource.App, NoToken, cancellation.Token));

        // Assert
        Assert.Empty(await CallsAsync());
    }

    [Fact]
    public async Task RunAsync_WhenTheRequestInherits_ThenSendsWithTheFoldersAuth()
    {
        // Arrange
        var folder = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) };
        await Library().SaveFolderAsync("Brugere", folder, Cancellation);
        var sender = new FakeSender(() => Task.FromResult(new ApiResponse(200, "OK", 0, 2, [], "{}")));

        // Act
        await Runner(sender).RunAsync(Request(), "Brugere/Hent", null, HistorySource.App, NoToken, Cancellation);

        // Assert
        Assert.Equal(folder.Id, sender.Auth?.SecretsId);
    }

    [Fact]
    public async Task RunAsync_WhenTheFolderSettingsAreInvalid_ThenRemembersTheError()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_temporary.Path, "requests", "Brugere"));
        File.WriteAllText(Path.Combine(_temporary.Path, "requests", "Brugere", ".folder.json"), "{");

        // Act
        await Record.ExceptionAsync(() => Runner(() => Task.FromResult(new ApiResponse(200, "OK", 0, 2, [], "{}"))).RunAsync(Request(), "Brugere/Hent", null, HistorySource.App, NoToken, Cancellation));

        // Assert
        Assert.NotNull(Assert.Single(await CallsAsync()).Entry.Error);
    }

    [Fact]
    public async Task RunAsync_WhenTheHistoryCannotBeWritten_ThenStillGivesTheResponse()
    {
        // Arrange
        Directory.CreateDirectory(_temporary.Path);
        File.WriteAllText(Path.Combine(_temporary.Path, "history"), "");
        var runner = Runner(() => Task.FromResult(new ApiResponse(200, "OK", 0, 2, [], "{}")));

        // Act
        var response = await runner.RunAsync(Request(), null, null, HistorySource.App, NoToken, Cancellation);

        // Assert
        Assert.Equal(200, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_WhenTheTokenIsMissingOrExpiredAndCanBeFetchedUnasked_ThenSendsAgainWithANewOne(bool expired)
    {
        // Arrange
        var calls = 0;
        var runner = Runner(() => ++calls == 1
            ? throw (expired ? new ExpiredTokenException(DateTimeOffset.MinValue) : new MissingSecretException(SecretKind.OAuthToken))
            : Task.FromResult(new ApiResponse(200, "OK", 0, 2, [], "{}")));

        // Act
        var response = await runner.RunAsync(OAuthRequest(), null, null, HistorySource.App, NewToken, Cancellation);

        // Assert
        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    public async Task RunAsync_WhenTheServerRejectsTheTokenAndOneCanBeFetchedUnasked_ThenSendsAgainWithANewOne()
    {
        // Arrange
        var calls = 0;
        var runner = Runner(() => Task.FromResult(++calls == 1 ? new ApiResponse(401, "Unauthorized", 0, 0, [], "") : new ApiResponse(200, "OK", 0, 2, [], "{}")));

        // Act
        var response = await runner.RunAsync(OAuthRequest(), null, null, HistorySource.App, NewToken, Cancellation);

        // Assert
        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    public async Task RunAsync_WhenTheNewTokenIsAlsoRejected_ThenFetchesOnlyOnce()
    {
        // Arrange
        var fetches = 0;
        var runner = Runner(() => Task.FromResult(new ApiResponse(401, "Unauthorized", 0, 0, [], "")));

        // Act
        var response = await runner.RunAsync(OAuthRequest(), null, null, HistorySource.App, _ => Task.FromResult(++fetches > 0), Cancellation);

        // Assert
        Assert.Equal((401, 1), (response.StatusCode, fetches));
    }

    [Fact]
    public async Task RunAsync_WhenNoTokenCanBeFetched_ThenThrowsTheFirstProblem()
    {
        // Arrange
        var runner = Runner(() => throw new MissingSecretException(SecretKind.OAuthToken));

        // Act
        var failure = await Record.ExceptionAsync(() => runner.RunAsync(OAuthRequest(), null, null, HistorySource.App, NoToken, Cancellation));

        // Assert
        Assert.IsType<MissingSecretException>(failure);
    }

    [Fact]
    public async Task RunAsync_WhenTheTokenNeedsALogin_ThenFetchesNone()
    {
        // Arrange
        var fetched = false;
        var runner = Runner(() => throw new MissingSecretException(SecretKind.OAuthToken));
        var request = Request() with { Auth = new(AuthKind.OAuth2, OAuth: new() { Grant = OAuthGrant.AuthorizationCode }) };

        // Act
        await Record.ExceptionAsync(() => runner.RunAsync(request, null, null, HistorySource.App, _ => Task.FromResult(fetched = true), Cancellation));

        // Assert
        Assert.False(fetched);
    }

    [Fact]
    public async Task RunAsync_WhenTheRequestInherits_ThenFetchesTheFoldersToken()
    {
        // Arrange
        var folder = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.OAuth2) };
        await Library().SaveFolderAsync("Brugere", folder, Cancellation);
        AuthSource? fetchedFor = null;
        var runner = Runner(() => Task.FromResult(new ApiResponse(401, "Unauthorized", 0, 0, [], "")));

        // Act
        await runner.RunAsync(Request(), "Brugere/Hent", null, HistorySource.App, auth => Task.FromResult((fetchedFor = auth) is not null), Cancellation);

        // Assert
        Assert.Equal(folder.Id, fetchedFor?.SecretsId);
    }
}
