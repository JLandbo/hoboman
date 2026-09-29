namespace Hoboman.Tests.ViewModels;

public sealed class KeyValueListViewModelTests
{
    [Fact]
    public void Rows_WhenTheLastRowGetsText_ThenAddsANewBlankRow()
    {
        // Arrange
        var list = new KeyValueListViewModel();

        // Act
        list.Rows[^1].Name = "Accept";

        // Assert
        Assert.Equal([false, true], list.Rows.Select(row => row.IsBlank));
    }

    [Fact]
    public void ToList_WhenARowIsBlank_ThenLeavesItOut()
    {
        // Arrange
        var list = new KeyValueListViewModel();
        list.Load([new("Accept", "application/json"), new("Off", "1", Enabled: false)]);

        // Act
        var values = list.ToList();

        // Assert
        Assert.Equal([new("Accept", "application/json"), new KeyValue("Off", "1", Enabled: false)], values);
    }

    [Fact]
    public void EnabledCount_WhenARowIsDisabled_ThenLeavesItOut()
    {
        // Arrange
        var list = new KeyValueListViewModel();
        list.Load([new("Accept", "application/json"), new("Off", "1", Enabled: false)]);

        // Act
        var count = list.EnabledCount;

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public void Remove_WhenCalled_ThenRemovesTheRow()
    {
        // Arrange
        var list = new KeyValueListViewModel();
        list.Load([new("Accept", "application/json")]);

        // Act
        list.Remove(list.Rows[0]);

        // Assert
        Assert.Empty(list.ToList());
    }

    [Fact]
    public void Remove_WhenCalled_ThenTellsListeners()
    {
        // Arrange
        var list = new KeyValueListViewModel();
        list.Load([new("Accept", "application/json")]);
        var changed = false;
        list.Changed += () => changed = true;

        // Act
        list.Remove(list.Rows[0]);

        // Assert
        Assert.True(changed);
    }
}
