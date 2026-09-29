using Microsoft.Extensions.DependencyInjection;

namespace Hoboman.Tests;

public sealed class AppTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Services_WhenBuilt_ThenEveryServiceTheWindowNeedsCanBeCreated()
    {
        // Arrange
        var registrations = App.Services(new AppFolder(Path.GetTempPath()));
        using var services = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        // Act
        var failures = registrations.Select(registration => registration.ServiceType).Where(type => type != typeof(MainWindow)).Where(type => Record.Exception(() => services.GetRequiredService(type)) is not null);

        // Assert
        Assert.Empty(failures);
    }

    [Fact]
    public void Services_WhenTheSettingsChooseEnglish_ThenTheTranslatorUsesEnglish()
    {
        // Arrange
        var folder = new AppFolder(_directory);
        new JsonFile<AppSettings>(folder.Settings, AppSettings.Default).Save(new AppSettings(LanguageName: "English"));
        using var services = App.Services(folder).BuildServiceProvider();

        // Act
        var translator = services.GetRequiredService<Translator>();

        // Assert
        Assert.Same(Translation.English, translator.Current);
    }

    [Fact]
    public void ResourcesOf_WhenEnglish_ThenGivesTheEnglishTexts()
    {
        // Act
        var resources = App.ResourcesOf(Translation.English);

        // Assert
        Assert.Equal("Response", resources["Response.Title"]);
    }
}
