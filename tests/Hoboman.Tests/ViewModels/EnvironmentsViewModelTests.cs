namespace Hoboman.Tests.ViewModels;

public sealed class EnvironmentsViewModelTests
{
    static readonly ApiEnvironment _dev = new("Dev", []) { Id = Guid.NewGuid() };
    static readonly ApiEnvironment _prod = new("Prod", []) { Id = Guid.NewGuid() };

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ChooseAsync_WhenAnEnvironmentIsChosen_ThenItIsStillChosenAfterARestart()
    {
        // Arrange
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([_dev, _prod], Cancellation);
        await harness.Environments.LoadAsync(Cancellation);
        var restarted = harness.Restarted();

        // Act
        await harness.Environments.ChooseAsync(harness.Environments.Items[1]);
        await restarted.LoadAsync(Cancellation);

        // Assert
        Assert.Equal("Prod", restarted.Selected?.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Dev")]
    [InlineData("Prod")]
    public async Task ChooseAsync_WhenTheSessionHasNoSelectedSavedTab_ThenRemembersTheLastEnvironment(string? name)
    {
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([_dev, _prod], Cancellation);
        await harness.SettingsStore.UpdateAsync(settings => settings with { Session = new(["Request"], null) }, Cancellation);
        await harness.Environments.LoadAsync(Cancellation);
        await harness.Environments.ChooseAsync(harness.Environments.Items[1]);

        var chosen = harness.Environments.Items.FirstOrDefault(environment => environment.Name == name);
        await harness.Environments.ChooseAsync(chosen);
        await harness.SettingsStore.UpdateAsync(settings => settings with { Layout = new(1200, 800, false, 340), Session = new(["Request"], null) }, Cancellation);
        var restarted = harness.Restarted();
        await restarted.LoadAsync(Cancellation);

        Assert.Equal(name, restarted.Selected?.Name);
        Assert.Equal(chosen?.Id, (await harness.SettingsStore.LoadAsync(Cancellation)).EnvironmentId);
        Assert.Equal(["Request"], (await harness.SettingsStore.LoadAsync(Cancellation)).Session!.Requests);
    }

    [Fact]
    public async Task LoadAsync_WhenTheChosenEnvironmentIsGone_ThenChoosesNone()
    {
        // Arrange
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([_dev], Cancellation);
        await harness.SettingsStore.UpdateAsync(settings => settings with { EnvironmentId = Guid.NewGuid() }, Cancellation);

        // Act
        await harness.Environments.LoadAsync(Cancellation);

        // Assert
        Assert.Null(harness.Environments.Selected);
    }

    [Fact]
    public async Task LoadAsync_WhenTheChosenEnvironmentIsRenamedOnDisk_ThenStaysChosen()
    {
        // Arrange
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([_dev, _prod], Cancellation);
        await harness.Environments.LoadAsync(Cancellation);
        await harness.Environments.ChooseAsync(harness.Environments.Items[0]);
        await harness.EnvironmentStore.SaveAsync([_dev with { Name = "Development" }, _prod], Cancellation);

        // Act
        await harness.Environments.LoadAsync(Cancellation);

        // Assert
        Assert.Equal("Development", harness.Environments.Selected?.Name);
    }

    [Fact]
    public async Task LoadAsync_WhenTheSettingsCannotBeRead_ThenStillShowsTheEnvironments()
    {
        // Arrange
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([_dev], Cancellation);
        File.WriteAllText(harness.Folder.Settings, "{");

        // Act
        await harness.Environments.LoadAsync(Cancellation);

        // Assert
        Assert.Equal("Dev", Assert.Single(harness.Environments.Items).Name);
    }

    [Fact]
    public async Task LoadAsync_WhenTheFileHoldsNull_ThenShowsNoEnvironments()
    {
        // Arrange
        using var harness = new Harness();
        Directory.CreateDirectory(harness.Folder.Root);
        File.WriteAllText(harness.Folder.Environments, "[null]");

        // Act
        await harness.Environments.LoadAsync(Cancellation);

        // Assert
        Assert.Empty(harness.Environments.Items);
    }

    [Fact]
    public async Task ChooseAsync_WhenTheChoiceCannotBeSaved_ThenStillChoosesIt()
    {
        // Arrange
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([_dev], Cancellation);
        await harness.Environments.LoadAsync(Cancellation);
        File.WriteAllText(harness.Folder.Settings, "{");

        // Act
        await harness.Environments.ChooseAsync(harness.Environments.Items.Single());

        // Assert
        Assert.Equal("Dev", harness.Environments.Selected?.Name);
    }
}
