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

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, "Request")]
    public async Task UpdateAsync_WhenSavingASession_ThenCanReadItAndCreateHttpClients(bool hasRequests, string? selected)
    {
        var session = new TabSession(hasRequests ? ["Request"] : [], selected);
        await Store().UpdateAsync(settings => settings with { Session = session }, Cancellation);

        var restored = (await Store().LoadAsync(Cancellation)).Session!;
        Assert.Equal(session.Requests, restored.Requests);
        Assert.Equal(selected, restored.Selected);
        using var clients = new HttpClients(Store());
        Assert.NotNull(await clients.CurrentAsync(Cancellation));
        Assert.NotNull(await clients.TokenClientAsync(Cancellation));
        await Store().UpdateAsync(settings => settings with { LanguageName = "English" }, Cancellation);
        Assert.Equal("English", (await Store().LoadAsync(Cancellation)).LanguageName);
    }
}
