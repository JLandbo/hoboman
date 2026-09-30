namespace Hoboman.Tests.Mvvm;

public sealed class AsyncCommandTests
{
    [Fact]
    public void CanExecute_WhenItsConditionIsNotMet_ThenIsFalse()
    {
        // Arrange
        var command = new AsyncCommand(() => Task.CompletedTask, () => false);

        // Act
        var canExecute = command.CanExecute(null);

        // Assert
        Assert.False(canExecute);
    }

    [Fact]
    public void CanExecute_WhenTheCommandIsRunning_ThenIsFalse()
    {
        // Arrange
        var command = new AsyncCommand(() => new TaskCompletionSource().Task);

        // Act
        command.Execute(null);

        // Assert
        Assert.False(command.CanExecute(null));
    }

    [Fact]
    public void CanExecute_WhenTheCommandIsDone_ThenIsTrueAgain()
    {
        // Arrange
        var finish = new TaskCompletionSource();
        var command = new AsyncCommand(() => finish.Task);
        command.Execute(null);

        // Act
        finish.SetResult();

        // Assert
        Assert.True(command.CanExecute(null));
    }
}
