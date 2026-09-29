namespace Hoboman.Tests.Mvvm;

public sealed class ObservableObjectTests
{
    [Fact]
    public void Set_WhenTheValueChanges_ThenTellsWhichPropertyChanged()
    {
        // Arrange
        var editor = new RequestEditorViewModel();
        string? changed = null;
        editor.PropertyChanged += (_, e) => changed = e.PropertyName;

        // Act
        editor.Url = "https://localhost";

        // Assert
        Assert.Equal(nameof(RequestEditorViewModel.Url), changed);
    }

    [Fact]
    public void Set_WhenTheValueIsTheSame_ThenDoesNotNotify()
    {
        // Arrange
        var editor = new RequestEditorViewModel();
        var notified = false;
        editor.PropertyChanged += (_, _) => notified = true;

        // Act
        editor.Method = "GET";

        // Assert
        Assert.False(notified);
    }
}
