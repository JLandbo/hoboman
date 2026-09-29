using Microsoft.Extensions.Time.Testing;

namespace Hoboman.Tests.ViewModels;

public sealed class HistoryViewModelTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static readonly DateTimeOffset _noon = new(new DateTime(2026, 9, 29, 12, 0, 0));

    // Entries are placed in Windows' time zone, so the clock uses it too. The fake clock takes its start as UTC.
    static FakeTimeProvider ClockAt(DateTimeOffset now)
    {
        var clock = new FakeTimeProvider(now.ToUniversalTime());
        clock.SetLocalTimeZone(TimeZoneInfo.Local);
        return clock;
    }

    static HistoryEntry Entry(string address, DateTimeOffset? at = null) => new(at ?? _noon, HistorySource.App, address, ApiRequest.New());

    [Fact]
    public async Task RefreshAsync_WhenACallIsAdded_ThenShowsItFirst()
    {
        // Arrange
        using var harness = new Harness();
        var history = new HistoryViewModel(harness.History(), new Translator(Translation.English), ClockAt(_noon), NullLogger<HistoryViewModel>.Instance);
        await harness.History().AddAsync(Entry("first.local", _noon.AddHours(-1)), Cancellation);
        await history.RefreshAsync(Cancellation);
        await harness.History().AddAsync(Entry("second.local", _noon), Cancellation);

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
        var history = new HistoryViewModel(harness.History(), new Translator(Translation.English), ClockAt(_noon), NullLogger<HistoryViewModel>.Instance);
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
        var history = new HistoryViewModel(harness.History(), new Translator(Translation.English), ClockAt(_noon), NullLogger<HistoryViewModel>.Instance);
        await harness.History().AddAsync(Entry("dev.local", _noon.AddDays(-1)), Cancellation);

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
        var history = new HistoryViewModel(harness.History(), new Translator(Translation.Danish), ClockAt(_noon), NullLogger<HistoryViewModel>.Instance);
        await harness.History().AddAsync(Entry("dev.local", new DateTimeOffset(2025, 3, 5, 12, 0, 0, TimeSpan.Zero)), Cancellation);

        // Act
        await history.RefreshAsync(Cancellation);

        // Assert
        Assert.Equal("5. marts 2025", Assert.Single(history.Items).Day);
    }

    [Fact]
    public async Task RefreshAsync_WhenTheDayHasChanged_ThenRelabelsTheOlderCalls()
    {
        // Arrange
        using var harness = new Harness();
        var clock = ClockAt(_noon);
        var history = new HistoryViewModel(harness.History(), new Translator(Translation.English), clock, NullLogger<HistoryViewModel>.Instance);
        await harness.History().AddAsync(Entry("first.local"), Cancellation);
        await history.RefreshAsync(Cancellation);
        clock.Advance(TimeSpan.FromDays(1));
        await harness.History().AddAsync(Entry("second.local", _noon.AddDays(1)), Cancellation);

        // Act
        await history.RefreshAsync(Cancellation);

        // Assert
        Assert.Equal("Yesterday", history.Items.Single(item => item.Address == "first.local").Day);
    }

    [Fact]
    public async Task RefreshAsync_WhenMidnightPasses_ThenTellsThatTheDayChanged()
    {
        // Arrange
        using var harness = new Harness();
        var clock = ClockAt(new(new DateTime(2026, 9, 29, 23, 0, 0)));
        var history = new HistoryViewModel(harness.History(), new Translator(Translation.English), clock, NullLogger<HistoryViewModel>.Instance);
        var changed = false;
        history.DayChanged += () => changed = true;
        await history.RefreshAsync(Cancellation);

        // Act
        clock.Advance(TimeSpan.FromHours(2));

        // Assert
        Assert.True(changed);
    }
}
