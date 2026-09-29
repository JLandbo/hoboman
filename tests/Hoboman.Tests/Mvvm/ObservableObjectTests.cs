namespace Hoboman.Tests.Mvvm;

public sealed class ObservableObjectTests
{
    sealed class Sample : ObservableObject
    {
        public string Name { get; set => Set(ref field, value); } = "";

        public void Refresh() => OnPropertyChanged("Summary");
    }

    [Fact]
    public void Set_WhenTheValueChanges_ThenTellsWhichPropertyChanged()
    {
        // Arrange
        var sample = new Sample();
        string? changed = null;
        sample.PropertyChanged += (_, e) => changed = e.PropertyName;

        // Act
        sample.Name = "Hobo";

        // Assert
        Assert.Equal(nameof(Sample.Name), changed);
    }

    [Fact]
    public void Set_WhenTheValueIsTheSame_ThenDoesNotNotify()
    {
        // Arrange
        var sample = new Sample();
        var notified = false;
        sample.PropertyChanged += (_, _) => notified = true;

        // Act
        sample.Name = "";

        // Assert
        Assert.False(notified);
    }

    [Fact]
    public void OnPropertyChanged_WhenCalled_ThenTellsWhichPropertyChanged()
    {
        // Arrange
        var sample = new Sample();
        string? changed = null;
        sample.PropertyChanged += (_, e) => changed = e.PropertyName;

        // Act
        sample.Refresh();

        // Assert
        Assert.Equal("Summary", changed);
    }
}
