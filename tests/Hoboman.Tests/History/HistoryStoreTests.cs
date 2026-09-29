namespace Hoboman.Tests.History;

public sealed class HistoryStoreTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    HistoryStore Store() => new(new AppFolder(_temporary.Path), NullLogger<HistoryStore>.Instance);

    static HistoryEntry EntryAt(int minute) =>
        new(new DateTimeOffset(2026, 9, 29, 12, minute, 0, TimeSpan.Zero), HistorySource.App, "dev.local/users", ApiRequest.New(), Response: new(200, "OK", 5, 2, [], $"{minute}"));

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public async Task LatestAsync_WhenCallsWereAdded_ThenGivesTheNewestFirst()
    {
        // Arrange
        await Store().AddAsync(EntryAt(1), Cancellation);
        await Store().AddAsync(EntryAt(3), Cancellation);
        await Store().AddAsync(EntryAt(2), Cancellation);

        // Act
        var entries = await Store().LatestAsync(2, null, Cancellation);

        // Assert
        Assert.Equal(["3", "2"], entries.Select(file => file.Entry.Response?.Body));
    }

    [Fact]
    public async Task LatestAsync_WhenAFileIsInvalid_ThenSkipsIt()
    {
        // Arrange
        await Store().AddAsync(EntryAt(1), Cancellation);
        File.WriteAllText(Path.Combine(_temporary.Path, "history", "99999999-999999-999-broken.json"), "{");

        // Act
        var entries = await Store().LatestAsync(10, null, Cancellation);

        // Assert
        Assert.Equal(["1"], entries.Select(file => file.Entry.Response?.Body));
    }

    [Fact]
    public async Task LatestAsync_WhenGivenTheNewestName_ThenGivesOnlyNewerCalls()
    {
        // Arrange
        await Store().AddAsync(EntryAt(1), Cancellation);
        var newest = (await Store().LatestAsync(1, null, Cancellation))[0].Name;
        await Store().AddAsync(EntryAt(2), Cancellation);

        // Act
        var entries = await Store().LatestAsync(10, newest, Cancellation);

        // Assert
        Assert.Equal(["2"], entries.Select(file => file.Entry.Response?.Body));
    }

    [Fact]
    public async Task LatestAsync_WhenAnEntryHasNoError_ThenLeavesTheErrorOutOfTheFile()
    {
        // Arrange
        await Store().AddAsync(EntryAt(1), Cancellation);

        // Act
        var text = File.ReadAllText(Directory.EnumerateFiles(Path.Combine(_temporary.Path, "history")).Single());

        // Assert
        Assert.DoesNotContain("error", text);
    }

    [Fact]
    public async Task LatestAsync_WhenNothingWasAdded_ThenGivesNothing()
    {
        // Act
        var entries = await Store().LatestAsync(10, null, Cancellation);

        // Assert
        Assert.Empty(entries);
    }
}
