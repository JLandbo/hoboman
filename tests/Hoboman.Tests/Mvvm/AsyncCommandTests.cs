namespace Hoboman.Tests.Mvvm;

public sealed class AsyncCommandTests
{
    [Fact]
    public void CanExecute_WhenTheCommandIsRunning_ThenIsFalseUntilItIsDone()
    {
        // Arrange
        var finish = new TaskCompletionSource();
        var command = new AsyncCommand(() => finish.Task);

        // Act
        command.Execute(null);
        var whileRunning = command.CanExecute(null);
        finish.SetResult();

        // Assert
        Assert.False(whileRunning);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void CanExecute_WhenTheConditionIsFalse_ThenIsFalse()
    {
        // Arrange
        var command = new AsyncCommand(() => Task.CompletedTask, () => false);

        // Act
        var canExecute = command.CanExecute(null);

        // Assert
        Assert.False(canExecute);
    }
}
