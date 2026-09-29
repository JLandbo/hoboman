namespace Hoboman.Tests.History;

public sealed class HistoryStoreTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    HistoryStore Store() => new(new AppFolder(_directory), NullLogger<HistoryStore>.Instance);

    static HistoryEntry EntryAt(int minute) =>
        new(new DateTimeOffset(2026, 9, 29, 12, minute, 0, TimeSpan.Zero), HistorySource.App, null, null, "dev.local/users", ApiRequest.New(), new(200, "OK", TimeSpan.FromMilliseconds(5), 2, [], $"{minute}"), null);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task LatestAsync_WhenCallsWereAdded_ThenGivesTheNewestFirst()
    {
        // Arrange
        await Store().AddAsync(EntryAt(1), Cancellation);
        await Store().AddAsync(EntryAt(3), Cancellation);
        await Store().AddAsync(EntryAt(2), Cancellation);

        // Act
        var entries = await Store().LatestAsync(2, Cancellation);

        // Assert
        Assert.Equal(["3", "2"], entries.Select(entry => entry.Response?.Body));
    }

    [Fact]
    public async Task LatestAsync_WhenAFileIsInvalid_ThenSkipsIt()
    {
        // Arrange
        await Store().AddAsync(EntryAt(1), Cancellation);
        File.WriteAllText(Path.Combine(_directory, "history", "99999999-999999-999-broken.json"), "{");

        // Act
        var entries = await Store().LatestAsync(10, Cancellation);

        // Assert
        Assert.Equal(["1"], entries.Select(entry => entry.Response?.Body));
    }

    [Fact]
    public async Task LatestAsync_WhenNothingWasAdded_ThenGivesNothing()
    {
        // Act
        var entries = await Store().LatestAsync(10, Cancellation);

        // Assert
        Assert.Empty(entries);
    }
}
