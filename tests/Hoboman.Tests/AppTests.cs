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
        var registrations = App.Services(new AppFolder(_directory));
        using var services = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        // Act
        var failures = registrations.Select(registration => registration.ServiceType).Where(type => type != typeof(MainWindow) && !type.IsGenericTypeDefinition).Where(type => Record.Exception(() => services.GetRequiredService(type)) is not null);

        // Assert
        Assert.Empty(failures);
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
