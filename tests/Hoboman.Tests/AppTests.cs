using Microsoft.Extensions.DependencyInjection;

namespace Hoboman.Tests;

public sealed class AppTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public void Services_WhenBuilt_ThenEveryServiceTheWindowNeedsCanBeCreated()
    {
        // Arrange
        var registrations = App.Services(new AppFolder(_temporary.Path));
        using var services = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        // Act
        var failures = registrations.Select(registration => registration.ServiceType).Where(type => type != typeof(MainWindow) && !type.IsGenericTypeDefinition).Where(type => Record.Exception(() => services.GetRequiredService(type)) is not null).ToList();

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
