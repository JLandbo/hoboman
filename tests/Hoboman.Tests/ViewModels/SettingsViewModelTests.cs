namespace Hoboman.Tests.ViewModels;

public sealed class SettingsViewModelTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    string FilePath => Path.Combine(_directory, "settings.json");

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    JsonFile<AppSettings> Store() => new(FilePath, AppSettings.Default, NullLogger.Instance);

    SettingsViewModel Settings(Translator translator) => new(Store(), translator, NullLogger<SettingsViewModel>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_WhenTheSettingsChooseEnglish_ThenTranslatesWithEnglish()
    {
        // Arrange
        await Store().SaveAsync(new AppSettings(LanguageName: "English"), Cancellation);
        var translator = new Translator(Translation.Danish);

        // Act
        await Settings(translator).LoadAsync(Cancellation);

        // Assert
        Assert.Same(Translation.English, translator.Current);
    }

    [Fact]
    public async Task Language_WhenSet_ThenTranslatesWithItAndSavesIt()
    {
        // Arrange
        var translator = new Translator(Translation.Danish);
        var settings = Settings(translator);

        // Act
        settings.Language = Translation.English;
        await settings.Saving;

        // Assert
        Assert.Same(Translation.English, translator.Current);
        Assert.Equal("English", (await Store().LoadAsync(Cancellation)).LanguageName);
    }

    [Fact]
    public async Task IgnoreCertificateErrors_WhenSet_ThenSavesItAndKeepsTheOtherSettings()
    {
        // Arrange
        await Store().SaveAsync(new AppSettings(LanguageName: "English"), Cancellation);
        var settings = Settings(new Translator(Translation.English));

        // Act
        settings.IgnoreCertificateErrors = true;
        await settings.Saving;

        // Assert
        Assert.Equal(new AppSettings(IgnoreCertificateErrors: true, LanguageName: "English"), await Store().LoadAsync(Cancellation));
    }

    [Fact]
    public async Task IgnoreCertificateErrors_WhenSavingFails_ThenShowsTheProblemAndUndoesTheChange()
    {
        // Arrange
        Directory.CreateDirectory(_directory);
        using var locked = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        var settings = Settings(new Translator(Translation.English));

        // Act
        settings.IgnoreCertificateErrors = true;
        await settings.Saving;

        // Assert
        Assert.StartsWith("The settings could not be saved", settings.Problem);
        Assert.False(settings.IgnoreCertificateErrors);
    }

    [Fact]
    public async Task LoadAsync_WhenTheFileIsLocked_ThenKeepsTheCurrentLanguage()
    {
        // Arrange
        Directory.CreateDirectory(_directory);
        using var locked = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        var translator = new Translator(Translation.English);

        // Act
        await Settings(translator).LoadAsync(Cancellation);

        // Assert
        Assert.Same(Translation.English, translator.Current);
    }

    [Fact]
    public async Task LoadAsync_WhenAProblemIsShown_ThenClearsIt()
    {
        // Arrange
        Directory.CreateDirectory(_directory);
        var settings = Settings(new Translator(Translation.English));
        using (new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            settings.IgnoreCertificateErrors = true;
            await settings.Saving;
        }

        // Act
        await settings.LoadAsync(Cancellation);

        // Assert
        Assert.Null(settings.Problem);
    }
}
