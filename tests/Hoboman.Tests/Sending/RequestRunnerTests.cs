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

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public async Task RunAsync_WhenTheCallSucceeds_ThenRemembersTheResponse()
    {
        // Arrange
        var runner = Runner(() => Task.FromResult(new ApiResponse(200, "OK", 0, 2, [], "{}")));

        // Act
        await runner.RunAsync(Request(), "Brugere/Hent", null, HistorySource.App, Cancellation);

        // Assert
        Assert.Equal("{}", Assert.Single(await CallsAsync()).Entry.Response?.Body);
    }

    [Fact]
    public async Task RunAsync_WhenTheCallSucceeds_ThenRemembersTheName()
    {
        // Arrange
        var runner = Runner(() => Task.FromResult(new ApiResponse(200, "OK", 0, 2, [], "{}")));

        // Act
        await runner.RunAsync(Request(), "Brugere/Hent", null, HistorySource.App, Cancellation);

        // Assert
        Assert.Equal("Brugere/Hent", Assert.Single(await CallsAsync()).Entry.Name);
    }

    [Fact]
    public async Task RunAsync_WhenTheCallSucceeds_ThenShowsTheAddressWithoutTheQuery()
    {
        // Arrange
        var runner = Runner(() => Task.FromResult(new ApiResponse(200, "OK", 0, 2, [], "{}")));

        // Act
        await runner.RunAsync(Request(), "Brugere/Hent", null, HistorySource.App, Cancellation);

        // Assert
        Assert.Equal("dev.local:5001/users", Assert.Single(await CallsAsync()).Entry.Address);
    }

    [Fact]
    public async Task RunAsync_WhenTheCallFails_ThenThrows()
    {
        // Arrange
        var runner = Runner(() => throw new HttpRequestException("Ingen forbindelse"));

        // Act
        var failure = await Record.ExceptionAsync(() => runner.RunAsync(Request(), null, null, HistorySource.Cli, Cancellation));

        // Assert
        Assert.IsType<HttpRequestException>(failure);
    }

    [Fact]
    public async Task RunAsync_WhenTheCallFails_ThenRemembersTheError()
    {
        // Arrange
        var runner = Runner(() => throw new HttpRequestException("Ingen forbindelse"));

        // Act
        await Record.ExceptionAsync(() => runner.RunAsync(Request(), null, null, HistorySource.Cli, Cancellation));

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
        await Record.ExceptionAsync(() => runner.RunAsync(Request(), null, null, HistorySource.App, cancellation.Token));

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
        await Runner(sender).RunAsync(Request(), "Brugere/Hent", null, HistorySource.App, Cancellation);

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
        await Record.ExceptionAsync(() => Runner(() => Task.FromResult(new ApiResponse(200, "OK", 0, 2, [], "{}"))).RunAsync(Request(), "Brugere/Hent", null, HistorySource.App, Cancellation));

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
        var response = await runner.RunAsync(Request(), null, null, HistorySource.App, Cancellation);

        // Assert
        Assert.Equal(200, response.StatusCode);
    }
}
