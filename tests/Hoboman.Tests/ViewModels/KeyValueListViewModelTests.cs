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
        Assert.Equal(2, list.Rows.Count);
        Assert.True(list.Rows[^1].IsBlank);
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
    public void Remove_WhenCalled_ThenRemovesTheRowAndTellsListeners()
    {
        // Arrange
        var list = new KeyValueListViewModel();
        list.Load([new("Accept", "application/json")]);
        var changed = false;
        list.Changed += () => changed = true;

        // Act
        list.Remove(list.Rows[0]);

        // Assert
        Assert.Empty(list.ToList());
        Assert.True(changed);
    }
}
