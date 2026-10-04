using System.Globalization;

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
    public async Task AddAsync_WhenTheResponseHasItsBytes_ThenKeepsOnlyItsText()
    {
        // Arrange
        var store = new HistoryStore(new(_temporary.Path), NullLogger<HistoryStore>.Instance);
        var entry = EntryAt(1);

        // Act
        await store.AddAsync(entry with { Response = entry.Response! with { Bytes = [1, 2] } }, TestContext.Current.CancellationToken);

        // Assert
        var read = Assert.Single(await store.ReadAsync(await store.LatestAsync(10, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken));
        Assert.Equal(("1", null), (read.Entry.Response!.Body, read.Entry.Response.Bytes));
    }

    async Task<IReadOnlyList<HistoryFile>> ReadLatestAsync(int count) => await Store().ReadAsync(await Store().LatestAsync(count, Cancellation), Cancellation);

    [Fact]
    public async Task DeleteAsync_WhenCalled_ThenTheCallIsGone()
    {
        // Arrange
        await Store().AddAsync(EntryAt(1), Cancellation);
        await Store().AddAsync(EntryAt(2), Cancellation);
        var first = (await ReadLatestAsync(2))[1];

        // Act
        await Store().DeleteAsync(first.Name, Cancellation);

        // Assert
        Assert.Equal(["2"], (await ReadLatestAsync(2)).Select(file => file.Entry.Response?.Body));
    }

    [Fact]
    public async Task LatestAsync_WhenCallsWereAdded_ThenGivesTheNewestFirst()
    {
        // Arrange
        await Store().AddAsync(EntryAt(1), Cancellation);
        await Store().AddAsync(EntryAt(3), Cancellation);
        await Store().AddAsync(EntryAt(2), Cancellation);

        // Act
        var entries = await ReadLatestAsync(2);

        // Assert
        Assert.Equal(["3", "2"], entries.Select(file => file.Entry.Response?.Body));
    }

    [Fact]
    public async Task ReadAsync_WhenAFileIsInvalid_ThenSkipsIt()
    {
        // Arrange
        await Store().AddAsync(EntryAt(1), Cancellation);
        File.WriteAllText(Path.Combine(_temporary.Path, "history", "99999999-999999-999-broken.json"), "{");

        // Act
        var entries = await ReadLatestAsync(10);

        // Assert
        Assert.Equal(["1"], entries.Select(file => file.Entry.Response?.Body));
    }

    [Fact]
    public async Task AddAsync_WhenTheEntryHasNoError_ThenLeavesTheErrorOutOfTheFile()
    {
        // Act
        await Store().AddAsync(EntryAt(1), Cancellation);

        // Assert
        Assert.DoesNotContain("error", File.ReadAllText(Directory.EnumerateFiles(Path.Combine(_temporary.Path, "history")).Single()));
    }

    [Fact]
    public async Task LatestAsync_WhenNothingWasAdded_ThenGivesNothing()
    {
        // Act
        var entries = await ReadLatestAsync(10);

        // Assert
        Assert.Empty(entries);
    }

    [Fact]
    public async Task AddAsync_WhenTheCultureUsesAnotherCalendar_ThenNamesTheFileByTheGregorianDate()
    {
        // Arrange
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("th-TH");

        // Act
        try
        {
            await Store().AddAsync(EntryAt(1), Cancellation);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        // Assert
        Assert.StartsWith("20260929-", Path.GetFileName(Directory.EnumerateFiles(Path.Combine(_temporary.Path, "history")).Single()));
    }

    [Fact]
    public async Task LatestAsync_WhenAFileIsNotACall_ThenLeavesItOut()
    {
        // Arrange
        await Store().AddAsync(EntryAt(1), Cancellation);
        File.WriteAllText(Path.Combine(_temporary.Path, "history", "backup.json"), "{}");

        // Act
        var entries = await ReadLatestAsync(10);

        // Assert
        Assert.Equal(["1"], entries.Select(file => file.Entry.Response?.Body));
    }
}
