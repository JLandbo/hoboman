using System.Net.Http;

namespace Hoboman.Tests.Sending;

public sealed class RequestRunnerTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    HistoryStore History() => new(new AppFolder(_directory), NullLogger<HistoryStore>.Instance);

    RequestRunner Runner(Func<Task<ApiResponse>> send) => new(new FakeSender(send), History(), NullLogger<RequestRunner>.Instance);

    static ApiRequest Request() => ApiRequest.New() with { Url = "https://dev.local:5001/users?key=secret" };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WhenTheCallSucceeds_ThenRemembersTheResponseWithoutTheQuery()
    {
        // Arrange
        var runner = Runner(() => Task.FromResult(new ApiResponse(200, "OK", TimeSpan.Zero, 2, [], "{}")));

        // Act
        await runner.RunAsync(Request(), "Brugere/Hent", null, HistorySource.App, Cancellation);

        // Assert
        var entry = Assert.Single(await History().LatestAsync(10, Cancellation));
        Assert.Equal(("Brugere/Hent", "dev.local:5001/users", "{}"), (entry.Name, entry.Address, entry.Response?.Body));
    }

    [Fact]
    public async Task RunAsync_WhenTheCallFails_ThenRemembersTheErrorAndThrows()
    {
        // Arrange
        var runner = Runner(() => throw new HttpRequestException("Ingen forbindelse"));

        // Act
        var running = runner.RunAsync(Request(), null, null, HistorySource.Cli, Cancellation);

        // Assert
        await Assert.ThrowsAsync<HttpRequestException>(() => running);
        Assert.Equal("Ingen forbindelse", Assert.Single(await History().LatestAsync(10, Cancellation)).Error);
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_ThenRemembersNothing()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var runner = Runner(() => Task.FromCanceled<ApiResponse>(cancellation.Token));

        // Act
        var running = runner.RunAsync(Request(), null, null, HistorySource.App, cancellation.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Empty(await History().LatestAsync(10, Cancellation));
    }

    sealed class FakeSender(Func<Task<ApiResponse>> send) : IRequestSender
    {
        public Task<ApiResponse> SendAsync(ApiRequest request, ApiEnvironment? environment, CancellationToken cancellationToken) => send();
    }
}
