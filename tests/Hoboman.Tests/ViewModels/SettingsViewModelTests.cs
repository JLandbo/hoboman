namespace Hoboman.Tests.ViewModels;

public sealed class SettingsViewModelTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    JsonFile<AppSettings> Store() => new(Path.Combine(_directory, "settings.json"), AppSettings.Default);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Language_WhenSet_ThenTranslatesWithItAndSavesIt()
    {
        // Arrange
        var translator = new Translator(Translation.Danish);
        var settings = new SettingsViewModel(Store(), translator);

        // Act
        settings.Language = Translation.English;

        // Assert
        Assert.Same(Translation.English, translator.Current);
        Assert.Equal("English", Store().Load().LanguageName);
    }

    [Fact]
    public void IgnoreCertificateErrors_WhenSet_ThenSavesItAndKeepsTheOtherSettings()
    {
        // Arrange
        Store().Save(new AppSettings(LanguageName: "English"));
        var settings = new SettingsViewModel(Store(), new Translator(Translation.English));

        // Act
        settings.IgnoreCertificateErrors = true;

        // Assert
        Assert.Equal(new AppSettings(IgnoreCertificateErrors: true, LanguageName: "English"), Store().Load());
    }
}
