namespace Hoboman.Tests.Storage;

public sealed class AppFolderWatcherTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    public AppFolderWatcherTests() => Directory.CreateDirectory(_temporary.Path);

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public async Task RequestsChanged_WhenARequestFileIsWritten_ThenFires()
    {
        // Arrange
        var folder = new AppFolder(_temporary.Path);
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
    public async Task RequestsChanged_WhenTheOrderIsSaved_ThenFiresWithoutChangingRequests()
    {
        var folder = new AppFolder(_temporary.Path);
        using var watcher = new AppFolderWatcher(folder, NullLogger<AppFolderWatcher>.Instance);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.RequestsChanged += () => changed.TrySetResult();
        watcher.Start();
        var library = new RequestLibrary(folder, NullLogger<RequestLibrary>.Instance);
        await library.SaveOrderAsync(["B", "Folder/", "A"], Cancellation);
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
        Assert.Equal(["B", "Folder/", "A"], await library.LoadOrderAsync(Cancellation));
        Assert.Empty(await library.NamesAsync(Cancellation));
    }

    [Fact]
    public async Task HistoryChanged_WhenACallIsAdded_ThenFires()
    {
        // Arrange
        var folder = new AppFolder(_temporary.Path);
        using var watcher = new AppFolderWatcher(folder, NullLogger<AppFolderWatcher>.Instance);
        var changed = new TaskCompletionSource();
        watcher.HistoryChanged += changed.SetResult;
        watcher.Start();

        // Act
        await new HistoryStore(folder, NullLogger<HistoryStore>.Instance).AddAsync(new(DateTimeOffset.Now, HistorySource.Cli, "dev.local", ApiRequest.New()), Cancellation);

        // Assert
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
    }

    [Fact]
    public async Task RequestsChanged_WhenAFileIsWrittenSeveralTimes_ThenFiresOnce()
    {
        // Arrange
        var folder = new AppFolder(_temporary.Path);
        Directory.CreateDirectory(folder.Requests);
        using var watcher = new AppFolderWatcher(folder, NullLogger<AppFolderWatcher>.Instance);
        var changes = 0;
        watcher.RequestsChanged += () => Interlocked.Increment(ref changes);
        watcher.Start();

        // Act
        for (var write = 0; write < 3; write++)
        {
            await File.WriteAllTextAsync(Path.Combine(folder.Requests, "Ping.json"), $$"""{"url": "https://dev.local/{{write}}"}""", Cancellation);
        }
        await Task.Delay(TimeSpan.FromSeconds(1), Cancellation);

        // Assert
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Changed_WhenALogIsWritten_ThenTellsNobody()
    {
        // Arrange
        var folder = new AppFolder(_temporary.Path);
        Directory.CreateDirectory(folder.Logs);
        using var watcher = new AppFolderWatcher(folder, NullLogger<AppFolderWatcher>.Instance);
        var changes = 0;
        watcher.RequestsChanged += () => Interlocked.Increment(ref changes);
        watcher.HistoryChanged += () => Interlocked.Increment(ref changes);
        watcher.EnvironmentsChanged += () => Interlocked.Increment(ref changes);
        watcher.Start();

        // Act
        await File.WriteAllTextAsync(Path.Combine(folder.Logs, "hoboman.log"), "started", Cancellation);
        await Task.Delay(TimeSpan.FromSeconds(1), Cancellation);

        // Assert
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task EnvironmentsChanged_WhenTheEnvironmentsAreSaved_ThenFires()
    {
        // Arrange
        var folder = new AppFolder(_temporary.Path);
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
