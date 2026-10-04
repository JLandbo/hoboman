using Hoboman.Themes;

namespace Hoboman.Tests.ViewModels;

public sealed class SettingsViewModelTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    string FilePath => Path.Combine(_temporary.Path, "settings.json");

    string ThemesFolder => Path.Combine(_temporary.Path, "themes");

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    SettingsStore Store() => new(new AppFolder(_temporary.Path), NullLogger<SettingsStore>.Instance);

    SettingsViewModel Settings(Translator translator, ThemeLibrary? themes = null) => new(Store(), translator, themes ?? new(ThemesFolder), NullLogger<SettingsViewModel>.Instance);

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public async Task LoadAsync_WhenTheSettingsChooseEnglish_ThenTranslatesWithEnglish()
    {
        // Arrange
        await Store().UpdateAsync(_ => new(LanguageName: "English"), Cancellation);
        var translator = new Translator(Translation.Danish);

        // Act
        await Settings(translator).LoadAsync(Cancellation);

        // Assert
        Assert.Same(Translation.English, translator.Current);
    }

    [Fact]
    public async Task Language_WhenSet_ThenTranslatesWithIt()
    {
        // Arrange
        var translator = new Translator(Translation.Danish);
        var settings = Settings(translator);

        // Act
        settings.Language = Translation.English;
        await settings.Saving;

        // Assert
        Assert.Same(Translation.English, translator.Current);
    }

    [Fact]
    public async Task Language_WhenSet_ThenSavesIt()
    {
        // Arrange
        var settings = Settings(new Translator(Translation.Danish));

        // Act
        settings.Language = Translation.English;
        await settings.Saving;

        // Assert
        Assert.Equal("English", (await Store().LoadAsync(Cancellation)).LanguageName);
    }

    [Fact]
    public async Task LoadAsync_WhenTheSettingsChooseATheme_ThenUsesIt()
    {
        // Arrange
        await Store().UpdateAsync(_ => new(ThemeName: "Lys"), Cancellation);
        Directory.CreateDirectory(ThemesFolder);
        File.WriteAllText(Path.Combine(ThemesFolder, "Lys.json"), "{}");
        var themes = new ThemeLibrary(ThemesFolder);

        // Act
        await Settings(new Translator(Translation.Danish), themes).LoadAsync(Cancellation);

        // Assert
        Assert.Equal("Lys", themes.Current.Name);
    }

    [Fact]
    public async Task ChooseTheme_WhenChosen_ThenSavesIt()
    {
        // Arrange
        var settings = Settings(new Translator(Translation.Danish));

        // Act
        settings.ChooseTheme(Theme.Parse("Lys", "{}"));
        await settings.Saving;

        // Assert
        Assert.Equal("Lys", (await Store().LoadAsync(Cancellation)).ThemeName);
    }

    [Fact]
    public async Task ChooseTheme_WhenChosen_ThenOnlyItIsSelected()
    {
        // Arrange
        Directory.CreateDirectory(ThemesFolder);
        File.WriteAllText(Path.Combine(ThemesFolder, "Lys.json"), "{}");
        var themes = new ThemeLibrary(ThemesFolder);
        var settings = Settings(new Translator(Translation.Danish), themes);

        // Act
        settings.ChooseTheme(themes.Find("Lys"));
        await settings.Saving;

        // Assert
        Assert.Equal(["Lys"], settings.Themes.Where(choice => choice.Selected).Select(choice => choice.Name));
    }

    [Fact]
    public async Task IgnoreCertificateErrors_WhenSet_ThenSavesItAndKeepsTheOtherSettings()
    {
        // Arrange
        await Store().UpdateAsync(_ => new(LanguageName: "English"), Cancellation);
        var settings = Settings(new Translator(Translation.English));

        // Act
        settings.IgnoreCertificateErrors = true;
        await settings.Saving;

        // Assert
        Assert.Equal(new AppSettings(IgnoreCertificateErrors: true, LanguageName: "English"), await Store().LoadAsync(Cancellation));
    }

    [Fact]
    public async Task IgnoreCertificateErrors_WhenSavingFails_ThenShowsTheProblem()
    {
        // Arrange
        Directory.CreateDirectory(_temporary.Path);
        using var locked = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        var settings = Settings(new Translator(Translation.English));

        // Act
        settings.IgnoreCertificateErrors = true;
        await settings.Saving;

        // Assert
        Assert.StartsWith("The settings could not be saved", settings.Problem);
    }

    [Fact]
    public async Task IgnoreCertificateErrors_WhenSavingFails_ThenUndoesTheChange()
    {
        // Arrange
        Directory.CreateDirectory(_temporary.Path);
        using var locked = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        var settings = Settings(new Translator(Translation.English));

        // Act
        settings.IgnoreCertificateErrors = true;
        await settings.Saving;

        // Assert
        Assert.False(settings.IgnoreCertificateErrors);
    }

    [Fact]
    public async Task IgnoreCertificateErrors_WhenTwoSavesFail_ThenShowsWhatTheFileHolds()
    {
        // Arrange
        await Store().UpdateAsync(_ => new(IgnoreCertificateErrors: true), Cancellation);
        var settings = Settings(new Translator(Translation.English));
        await settings.LoadAsync(Cancellation);
        using var locked = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);

        // Act
        settings.IgnoreCertificateErrors = false;
        settings.IgnoreCertificateErrors = true;
        await settings.Saving;

        // Assert
        Assert.True(settings.IgnoreCertificateErrors);
    }

    [Fact]
    public async Task LoadAsync_WhenTheFileIsLocked_ThenKeepsTheCurrentLanguage()
    {
        // Arrange
        Directory.CreateDirectory(_temporary.Path);
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
        Directory.CreateDirectory(_temporary.Path);
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
