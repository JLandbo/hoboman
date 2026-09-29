namespace Hoboman.Tests.Storage;

public sealed class AppFolderWatcherTests : IDisposable
{
    readonly string _directory = Directory.CreateTempSubdirectory().FullName;

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task RequestsChanged_WhenARequestFileIsWritten_ThenFires()
    {
        // Arrange
        var folder = new AppFolder(_directory);
        Directory.CreateDirectory(folder.Requests);
        using var watcher = new AppFolderWatcher(folder, NullLogger<AppFolderWatcher>.Instance);
        var changed = new TaskCompletionSource();
        watcher.RequestsChanged += changed.SetResult;
        watcher.Start();

        // Act
        await File.WriteAllTextAsync(Path.Combine(folder.Requests, "Ping.json"), """{"url": "https://dev.local"}""", Cancellation);

        // Assert
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
    }

    [Fact]
    public async Task EnvironmentsChanged_WhenTheEnvironmentsAreSaved_ThenFires()
    {
        // Arrange
        var folder = new AppFolder(_directory);
        using var watcher = new AppFolderWatcher(folder, NullLogger<AppFolderWatcher>.Instance);
        var changed = new TaskCompletionSource();
        watcher.EnvironmentsChanged += changed.SetResult;
        watcher.Start();

        // Act
        await new EnvironmentStore(folder, NullLogger<EnvironmentStore>.Instance).SaveAsync([new("Dev", [])], Cancellation);

        // Assert
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
    }
}
