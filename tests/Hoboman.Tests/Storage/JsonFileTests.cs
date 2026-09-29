namespace Hoboman.Tests.Storage;

public sealed class JsonFileTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    string FilePath => Path.Combine(_temporary.Path, "settings.json");

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    JsonFile<AppSettings> Store(string? path = null) => new(path ?? FilePath, AppSettings.Default, NullLogger.Instance);

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public async Task LoadAsync_WhenTheFileIsMissing_ThenReturnsTheEmptyValue()
    {
        // Act
        var settings = await Store().LoadAsync(Cancellation);

        // Assert
        Assert.Same(AppSettings.Default, settings);
    }

    [Fact]
    public async Task LoadAsync_WhenTheFileIsInvalid_ThenThrowsWithThePath()
    {
        // Arrange
        Directory.CreateDirectory(_temporary.Path);
        File.WriteAllText(FilePath, "{");

        // Act
        var loading = Store().LoadAsync(Cancellation);

        // Assert
        Assert.Contains(FilePath, (await Assert.ThrowsAsync<InvalidDataException>(() => loading)).Message);
    }

    [Fact]
    public async Task UpdateAsync_WhenTheFileIsInvalid_ThenThrows()
    {
        // Arrange
        Directory.CreateDirectory(_temporary.Path);
        File.WriteAllText(FilePath, "{");

        // Act
        var updating = Store().UpdateAsync(settings => settings with { EnvironmentName = "Test" }, Cancellation);

        // Assert
        await Assert.ThrowsAsync<InvalidDataException>(() => updating);
    }

    [Fact]
    public async Task UpdateAsync_WhenTheFileIsInvalid_ThenLeavesItUntouched()
    {
        // Arrange
        Directory.CreateDirectory(_temporary.Path);
        File.WriteAllText(FilePath, "{");

        // Act
        await Record.ExceptionAsync(() => Store().UpdateAsync(settings => settings with { EnvironmentName = "Test" }, Cancellation));

        // Assert
        Assert.Equal("{", File.ReadAllText(FilePath));
    }

    [Fact]
    public async Task SaveAsync_WhenCalled_ThenWritesCamelCaseNames()
    {
        // Act
        await Store().SaveAsync(new AppSettings("Test"), Cancellation);

        // Assert
        Assert.Contains("\"environmentName\": \"Test\"", File.ReadAllText(FilePath));
    }

    [Fact]
    public async Task SaveAsync_WhenTextHasQuotesAndLetters_ThenWritesThemAsTheyAre()
    {
        // Act
        await Store().SaveAsync(new AppSettings("Søren \"&\" Co"), Cancellation);

        // Assert
        Assert.Contains("\"environmentName\": \"Søren \\\"&\\\" Co\"", File.ReadAllText(FilePath));
    }

    [Fact]
    public async Task SaveAsync_WhenAValueIsNull_ThenLeavesItOut()
    {
        // Act
        await Store().SaveAsync(AppSettings.Default, Cancellation);

        // Assert
        Assert.DoesNotContain("environmentName", File.ReadAllText(FilePath));
    }

    [Fact]
    public async Task LoadAsync_WhenTheFileHasCommentsAndATrailingComma_ThenReadsIt()
    {
        // Arrange
        Directory.CreateDirectory(_temporary.Path);
        File.WriteAllText(FilePath, """
            {
              // chosen by an agent
              "environmentName": "Dev",
            }
            """);

        // Act
        var settings = await Store().LoadAsync(Cancellation);

        // Assert
        Assert.Equal("Dev", settings.EnvironmentName);
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
        Directory.CreateDirectory(_temporary.Path);
        File.WriteAllText(Path.Combine(_temporary.Path, "blocked"), "");

        // Act
        var saving = Store(Path.Combine(_temporary.Path, "blocked", "settings.json")).SaveAsync(AppSettings.Default, Cancellation);

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

    [Fact]
    public async Task LoadAsync_WhenANameIsMisspelled_ThenThrows()
    {
        // Arrange
        Directory.CreateDirectory(_temporary.Path);
        File.WriteAllText(FilePath, """{"environmentNam": "Dev"}""");

        // Act
        var loading = Store().LoadAsync(Cancellation);

        // Assert
        await Assert.ThrowsAsync<InvalidDataException>(() => loading);
    }

    [Fact]
    public async Task UpdateAsync_WhenCalledOneAfterTheOther_ThenTheLastOneWins()
    {
        // Arrange
        var store = Store();

        // Act
        var first = store.UpdateAsync(settings => settings with { EnvironmentName = "First" }, Cancellation);
        var last = store.UpdateAsync(settings => settings with { EnvironmentName = "Last" }, Cancellation);
        await first;
        await last;

        // Assert
        Assert.Equal("Last", (await store.LoadAsync(Cancellation)).EnvironmentName);
    }
}
