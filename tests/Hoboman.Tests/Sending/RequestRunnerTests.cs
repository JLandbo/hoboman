using System.Net.Http;

namespace Hoboman.Tests.Sending;

public sealed class RequestRunnerTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    HistoryStore History() => new(new AppFolder(_temporary.Path), NullLogger<HistoryStore>.Instance);

    RequestRunner Runner(Func<Task<ApiResponse>> send) => new(new FakeSender(send), History(), NullLogger<RequestRunner>.Instance);

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
        Assert.Equal("{}", Assert.Single(await History().LatestAsync(10, null, Cancellation)).Entry.Response?.Body);
    }

    [Fact]
    public async Task RunAsync_WhenTheCallSucceeds_ThenRemembersTheName()
    {
        // Arrange
        var runner = Runner(() => Task.FromResult(new ApiResponse(200, "OK", 0, 2, [], "{}")));

        // Act
        await runner.RunAsync(Request(), "Brugere/Hent", null, HistorySource.App, Cancellation);

        // Assert
        Assert.Equal("Brugere/Hent", Assert.Single(await History().LatestAsync(10, null, Cancellation)).Entry.Name);
    }

    [Fact]
    public async Task RunAsync_WhenTheCallSucceeds_ThenShowsTheAddressWithoutTheQuery()
    {
        // Arrange
        var runner = Runner(() => Task.FromResult(new ApiResponse(200, "OK", 0, 2, [], "{}")));

        // Act
        await runner.RunAsync(Request(), "Brugere/Hent", null, HistorySource.App, Cancellation);

        // Assert
        Assert.Equal("dev.local:5001/users", Assert.Single(await History().LatestAsync(10, null, Cancellation)).Entry.Address);
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
        Assert.Equal("Ingen forbindelse", Assert.Single(await History().LatestAsync(10, null, Cancellation)).Entry.Error);
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
        Assert.Empty(await History().LatestAsync(10, null, Cancellation));
    }
}
