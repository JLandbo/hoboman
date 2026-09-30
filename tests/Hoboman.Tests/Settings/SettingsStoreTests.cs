namespace Hoboman.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    SettingsStore Store() => new(new AppFolder(_temporary.Path), NullLogger<SettingsStore>.Instance);

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public async Task UpdateAsync_WhenTheLayoutIsSaved_ThenItIsThereAfterARestart()
    {
        // Arrange
        var layout = new WindowLayout(1200, 800, IsMaximized: true, SidebarWidth: 340);

        // Act
        await Store().UpdateAsync(saved => saved with { Layout = layout }, Cancellation);

        // Assert
        Assert.Equal(layout, (await Store().LoadAsync(Cancellation)).Layout);
    }

    [Fact]
    public async Task UpdateAsync_WhenTheLayoutIsSaved_ThenKeepsTheOtherSettings()
    {
        // Arrange
        await Store().UpdateAsync(saved => saved with { LanguageName = "English" }, Cancellation);

        // Act
        await Store().UpdateAsync(saved => saved with { Layout = new(1200, 800, false, 340) }, Cancellation);

        // Assert
        Assert.Equal("English", (await Store().LoadAsync(Cancellation)).LanguageName);
    }
}
