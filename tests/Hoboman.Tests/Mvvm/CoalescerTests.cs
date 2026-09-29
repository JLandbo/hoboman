namespace Hoboman.Tests.Mvvm;

public sealed class CoalescerTests
{
    [Fact]
    public async Task RunAsync_WhenAskedAgainWhileRunning_ThenRunsOnceMoreAfterwards()
    {
        // Arrange
        var finish = new TaskCompletionSource();
        var runs = 0;
        var coalescer = new Coalescer(async () =>
        {
            runs++;
            await finish.Task;
        });
        var first = coalescer.RunAsync();

        // Act
        await coalescer.RunAsync();
        await coalescer.RunAsync();
        finish.SetResult();
        await first;

        // Assert
        Assert.Equal(2, runs);
    }
}
