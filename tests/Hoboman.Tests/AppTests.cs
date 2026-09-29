using Microsoft.Extensions.DependencyInjection;

namespace Hoboman.Tests;

public sealed class AppTests
{
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
}
