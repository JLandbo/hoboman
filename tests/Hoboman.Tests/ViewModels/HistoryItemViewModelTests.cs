using System.Globalization;

namespace Hoboman.Tests.ViewModels;

public sealed class HistoryItemViewModelTests
{
    [Fact]
    public void Time_WhenTheCallWasMade_ThenShowsTheLocalTimeWithSeconds()
    {
        // Arrange
        var at = new DateTimeOffset(2026, 9, 30, 12, 5, 9, TimeSpan.Zero);

        // Act
        var time = new HistoryItemViewModel(new("call.json", new(at, HistorySource.App, "dev.local", ApiRequest.New())), "Today").Time;

        // Assert
        Assert.Equal(at.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture), time);
    }
}
