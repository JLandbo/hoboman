namespace Hoboman.Tests.Storage;

public sealed class JsonFileTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    string FilePath => Path.Combine(_directory, "settings.json");

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    JsonFile<AppSettings> Store(string? path = null) => new(path ?? FilePath, AppSettings.Default, NullLogger.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_WhenTheFileIsMissing_ThenReturnsTheEmptyValue()
    {
        // Act
        var settings = await Store().LoadAsync(Cancellation);

        // Assert
        Assert.Same(AppSettings.Default, settings);
    }

    [Fact]
    public async Task LoadAsync_WhenTheFileIsCorrupt_ThenReturnsTheEmptyValue()
    {
        // Arrange
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{");

        // Act
        var settings = await Store().LoadAsync(Cancellation);

        // Assert
        Assert.Same(AppSettings.Default, settings);
    }

    [Fact]
    public async Task LoadAsync_WhenSaved_ThenReturnsTheSameValue()
    {
        // Arrange
        await Store().SaveAsync(new AppSettings("Test", IgnoreCertificateErrors: true), Cancellation);

        // Act
        var settings = await Store().LoadAsync(Cancellation);

        // Assert
        Assert.Equal(new AppSettings("Test", IgnoreCertificateErrors: true), settings);
    }

    [Fact]
    public async Task LoadAsync_WhenTheFileIsLockedForAMoment_ThenReadsItOnceItIsReleased()
    {
        // Arrange
        await Store().SaveAsync(new AppSettings("Test"), Cancellation);
        ReleaseSoon(new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None));

        // Act
        var settings = await Store().LoadAsync(Cancellation);

        // Assert
        Assert.Equal("Test", settings.EnvironmentName);
    }

    [Fact]
    public async Task LoadAsync_WhenTheFileStaysLocked_ThenThrows()
    {
        // Arrange
        await Store().SaveAsync(AppSettings.Default, Cancellation);
        using var locked = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);

        // Act
        var loading = Store().LoadAsync(Cancellation);

        // Assert
        await Assert.ThrowsAsync<IOException>(() => loading);
    }

    [Fact]
    public async Task SaveAsync_WhenTheFileIsLockedForAMoment_ThenSavesOnceItIsReleased()
    {
        // Arrange
        await Store().SaveAsync(AppSettings.Default, Cancellation);
        ReleaseSoon(new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None));

        // Act
        await Store().SaveAsync(new AppSettings("Test"), Cancellation);

        // Assert
        Assert.Equal("Test", (await Store().LoadAsync(Cancellation)).EnvironmentName);
    }

    [Fact]
    public async Task SaveAsync_WhenTheFolderCannotBeCreated_ThenThrows()
    {
        // Arrange
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "blocked"), "");

        // Act
        var saving = Store(Path.Combine(_directory, "blocked", "settings.json")).SaveAsync(AppSettings.Default, Cancellation);

        // Assert
        await Assert.ThrowsAsync<IOException>(() => saving);
    }

    [Fact]
    public async Task UpdateAsync_WhenCalledTogether_ThenKeepsEveryChange()
    {
        // Arrange
        var store = Store();
        Func<AppSettings, AppSettings>[] changes = [settings => settings with { EnvironmentName = "Test" }, settings => settings with { IgnoreCertificateErrors = true }];

        // Act
        await Parallel.ForEachAsync(changes, Cancellation, async (change, cancellationToken) => await store.UpdateAsync(change, cancellationToken));

        // Assert
        Assert.Equal(new AppSettings("Test", IgnoreCertificateErrors: true), await store.LoadAsync(Cancellation));
    }

    [Fact]
    public async Task SaveAsync_WhenCalled_ThenDoesTheWorkOffTheCallersThread()
    {
        // Arrange
        var logger = new RecordingLogger<AppSettings>();
        var store = new JsonFile<AppSettings>(FilePath, AppSettings.Default, logger);
        var cancellation = Cancellation;
        var saving = Task.CompletedTask;
        var caller = new Thread(() => saving = store.SaveAsync(AppSettings.Default, cancellation));

        // Act
        caller.Start();
        caller.Join();
        await saving;

        // Assert
        Assert.DoesNotContain(logger.Entries, entry => entry.Thread == caller.ManagedThreadId);
    }

    static void ReleaseSoon(FileStream locked) => new Thread(() =>
    {
        Thread.Sleep(20);
        locked.Dispose();
    }).Start();
}
