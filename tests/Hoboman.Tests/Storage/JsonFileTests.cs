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
        Assert.Contains(FilePath, (await Assert.ThrowsAsync<InvalidFileException>(() => loading)).Message);
    }

    [Fact]
    public async Task UpdateAsync_WhenTheFileIsInvalid_ThenThrows()
    {
        // Arrange
        Directory.CreateDirectory(_temporary.Path);
        File.WriteAllText(FilePath, "{");

        // Act
        var updating = Store().UpdateAsync(settings => settings with { LanguageName = "Test" }, Cancellation);

        // Assert
        await Assert.ThrowsAsync<InvalidFileException>(() => updating);
    }

    [Fact]
    public async Task UpdateAsync_WhenTheFileIsInvalid_ThenLeavesItUntouched()
    {
        // Arrange
        Directory.CreateDirectory(_temporary.Path);
        File.WriteAllText(FilePath, "{");

        // Act
        await Record.ExceptionAsync(() => Store().UpdateAsync(settings => settings with { LanguageName = "Test" }, Cancellation));

        // Assert
        Assert.Equal("{", File.ReadAllText(FilePath));
    }

    [Fact]
    public async Task SaveAsync_WhenCalled_ThenWritesCamelCaseNames()
    {
        // Act
        await Store().SaveAsync(new AppSettings(LanguageName: "Test"), Cancellation);

        // Assert
        Assert.Contains("\"languageName\": \"Test\"", File.ReadAllText(FilePath));
    }

    [Fact]
    public async Task SaveAsync_WhenTextHasQuotesAndLetters_ThenWritesThemAsTheyAre()
    {
        // Act
        await Store().SaveAsync(new AppSettings(LanguageName: "Søren \"&\" Co"), Cancellation);

        // Assert
        Assert.Contains("\"languageName\": \"Søren \\\"&\\\" Co\"", File.ReadAllText(FilePath));
    }

    [Fact]
    public async Task SaveAsync_WhenAValueIsNull_ThenLeavesItOut()
    {
        // Act
        await Store().SaveAsync(AppSettings.Default, Cancellation);

        // Assert
        Assert.DoesNotContain("languageName", File.ReadAllText(FilePath));
    }

    [Fact]
    public async Task LoadAsync_WhenTheFileHasCommentsAndATrailingComma_ThenReadsIt()
    {
        // Arrange
        Directory.CreateDirectory(_temporary.Path);
        File.WriteAllText(FilePath, """
            {
              // chosen by an agent
              "languageName": "Dev",
            }
            """);

        // Act
        var settings = await Store().LoadAsync(Cancellation);

        // Assert
        Assert.Equal("Dev", settings.LanguageName);
    }

    [Fact]
    public async Task LoadAsync_WhenSaved_ThenReturnsTheSameValue()
    {
        // Arrange
        await Store().SaveAsync(new AppSettings(LanguageName: "Test", IgnoreCertificateErrors: true), Cancellation);

        // Act
        var settings = await Store().LoadAsync(Cancellation);

        // Assert
        Assert.Equal(new AppSettings(LanguageName: "Test", IgnoreCertificateErrors: true), settings);
    }

    [Fact]
    public async Task LoadAsync_WhenTheFileIsLockedForAMoment_ThenReadsItOnceItIsReleased()
    {
        // Arrange
        ReleaseSoon(Locked("""{"languageName": "Test"}"""));

        // Act
        var settings = await Store().LoadAsync(Cancellation);

        // Assert
        Assert.Equal("Test", settings.LanguageName);
    }

    [Fact]
    public async Task LoadAsync_WhenTheFileStaysLocked_ThenThrows()
    {
        // Arrange
        using var locked = Locked("{}");

        // Act
        var loading = Store().LoadAsync(Cancellation);

        // Assert
        await Assert.ThrowsAsync<IOException>(() => loading);
    }

    [Fact]
    public async Task SaveAsync_WhenTheFileIsLockedForAMoment_ThenSavesOnceItIsReleased()
    {
        // Arrange
        ReleaseSoon(Locked("{}"));

        // Act
        await Store().SaveAsync(new AppSettings(LanguageName: "Test"), Cancellation);

        // Assert
        Assert.Equal("Test", (await Store().LoadAsync(Cancellation)).LanguageName);
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
        Func<AppSettings, AppSettings>[] changes = [settings => settings with { LanguageName = "Test" }, settings => settings with { IgnoreCertificateErrors = true }];

        // Act
        await Parallel.ForEachAsync(changes, Cancellation, async (change, cancellationToken) => await store.UpdateAsync(change, cancellationToken));

        // Assert
        Assert.Equal(new AppSettings(LanguageName: "Test", IgnoreCertificateErrors: true), await store.LoadAsync(Cancellation));
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

    // Writing and locking through one handle leaves no moment in between for another program, such as a virus scanner, to open the file.
    FileStream Locked(string json)
    {
        Directory.CreateDirectory(_temporary.Path);
        var locked = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        locked.Write(System.Text.Encoding.UTF8.GetBytes(json));
        locked.Flush();
        return locked;
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
        File.WriteAllText(FilePath, """{"languageNam": "Dev"}""");

        // Act
        var loading = Store().LoadAsync(Cancellation);

        // Assert
        await Assert.ThrowsAsync<InvalidFileException>(() => loading);
    }

    [Fact]
    public async Task UpdateAsync_WhenCalledOneAfterTheOther_ThenTheLastOneWins()
    {
        // Arrange
        var store = Store();

        // Act
        var first = store.UpdateAsync(settings => settings with { LanguageName = "First" }, Cancellation);
        var last = store.UpdateAsync(settings => settings with { LanguageName = "Last" }, Cancellation);
        await first;
        await last;

        // Assert
        Assert.Equal("Last", (await store.LoadAsync(Cancellation)).LanguageName);
    }

    [Fact]
    public async Task SaveAsync_WhenDirectoryCreationIsDisabled_ThenDoesNotRecreateAMissingFolder()
    {
        var path = Path.Combine(_temporary.Path, "Missing", "settings.json");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => Store(path).SaveAsync(AppSettings.Default, Cancellation, createDirectory: false));

        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
    }

    [Fact]
    public async Task SaveAsync_WhenDirectoryCreationIsDisabledAndTheFolderExists_ThenSavesNormally()
    {
        Directory.CreateDirectory(_temporary.Path);
        var store = Store();

        await store.SaveAsync(new AppSettings(LanguageName: "Dev"), Cancellation, createDirectory: false);

        Assert.Equal("Dev", (await store.LoadAsync(Cancellation)).LanguageName);
    }
}
