namespace Hoboman.Tests.ViewModels;

public sealed class HistoryViewModelTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static HistoryEntry Entry(string address) => new(DateTimeOffset.Now, HistorySource.App, address, ApiRequest.New());

    [Fact]
    public async Task RefreshAsync_WhenACallIsAdded_ThenShowsItFirst()
    {
        // Arrange
        using var harness = new Harness();
        var history = new HistoryViewModel(harness.History(), new Translator(Translation.English), NullLogger<HistoryViewModel>.Instance);
        await harness.History().AddAsync(Entry("first.local"), Cancellation);
        await history.RefreshAsync(Cancellation);
        await harness.History().AddAsync(Entry("second.local"), Cancellation);

        // Act
        await history.RefreshAsync(Cancellation);

        // Assert
        Assert.Equal(["second.local", "first.local"], history.Items.Select(item => item.Address));
    }

    [Fact]
    public async Task RefreshAsync_WhenTheCallIsFromToday_ThenSaysToday()
    {
        // Arrange
        using var harness = new Harness();
        var history = new HistoryViewModel(harness.History(), new Translator(Translation.English), NullLogger<HistoryViewModel>.Instance);
        await harness.History().AddAsync(Entry("dev.local"), Cancellation);

        // Act
        await history.RefreshAsync(Cancellation);

        // Assert
        Assert.Equal("Today", Assert.Single(history.Items).Day);
    }
}
