namespace Hoboman.Tests.ViewModels;

public sealed class HistoryViewModelTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // Noon keeps the day right on days with a daylight saving change.
    static HistoryEntry Entry(string address, DateTimeOffset? at = null) => new(at ?? new DateTimeOffset(DateTime.Today.AddHours(12)), HistorySource.App, address, ApiRequest.New());

    [Fact]
    public async Task RefreshAsync_WhenACallIsAdded_ThenShowsItFirst()
    {
        // Arrange
        using var harness = new Harness();
        var history = new HistoryViewModel(harness.History(), new Translator(Translation.English), NullLogger<HistoryViewModel>.Instance);
        await harness.History().AddAsync(Entry("first.local", new DateTimeOffset(DateTime.Today.AddHours(11))), Cancellation);
        await history.RefreshAsync(Cancellation);
        await harness.History().AddAsync(Entry("second.local", new DateTimeOffset(DateTime.Today.AddHours(12))), Cancellation);

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

    [Fact]
    public async Task RefreshAsync_WhenTheCallIsFromYesterday_ThenSaysYesterday()
    {
        // Arrange
        using var harness = new Harness();
        var history = new HistoryViewModel(harness.History(), new Translator(Translation.English), NullLogger<HistoryViewModel>.Instance);
        await harness.History().AddAsync(Entry("dev.local", new DateTimeOffset(DateTime.Today.AddDays(-1).AddHours(12))), Cancellation);

        // Act
        await history.RefreshAsync(Cancellation);

        // Assert
        Assert.Equal("Yesterday", Assert.Single(history.Items).Day);
    }

    [Fact]
    public async Task RefreshAsync_WhenTheCallIsFromAnEarlierYear_ThenShowsTheDateInTheChosenLanguage()
    {
        // Arrange
        using var harness = new Harness();
        var history = new HistoryViewModel(harness.History(), new Translator(Translation.Danish), NullLogger<HistoryViewModel>.Instance);
        await harness.History().AddAsync(Entry("dev.local", new DateTimeOffset(2025, 3, 5, 12, 0, 0, TimeSpan.Zero)), Cancellation);

        // Act
        await history.RefreshAsync(Cancellation);

        // Assert
        Assert.Equal("5. marts 2025", Assert.Single(history.Items).Day);
    }
}
