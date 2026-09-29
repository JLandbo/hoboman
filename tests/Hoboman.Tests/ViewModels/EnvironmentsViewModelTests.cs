namespace Hoboman.Tests.ViewModels;

public sealed class EnvironmentsViewModelTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ChooseAsync_WhenAnEnvironmentIsChosen_ThenItIsStillChosenAfterARestart()
    {
        // Arrange
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([new("Dev", []), new("Prod", [])], Cancellation);
        await harness.Environments.LoadAsync(Cancellation);
        var restarted = harness.Restarted();

        // Act
        await harness.Environments.ChooseAsync(harness.Environments.Items[1]);
        await restarted.LoadAsync(Cancellation);

        // Assert
        Assert.Equal("Prod", restarted.Selected?.Name);
    }

    [Fact]
    public async Task LoadAsync_WhenTheChosenEnvironmentIsGone_ThenChoosesNone()
    {
        // Arrange
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([new("Dev", [])], Cancellation);
        await harness.SettingsStore.UpdateAsync(settings => settings with { EnvironmentName = "Gone" }, Cancellation);

        // Act
        await harness.Environments.LoadAsync(Cancellation);

        // Assert
        Assert.Null(harness.Environments.Selected);
    }

    [Fact]
    public async Task LoadAsync_WhenTheSettingsCannotBeRead_ThenStillShowsTheEnvironments()
    {
        // Arrange
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([new("Dev", [])], Cancellation);
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
        await harness.EnvironmentStore.SaveAsync([new("Dev", [])], Cancellation);
        await harness.Environments.LoadAsync(Cancellation);
        File.WriteAllText(harness.Folder.Settings, "{");

        // Act
        await harness.Environments.ChooseAsync(harness.Environments.Items.Single());

        // Assert
        Assert.Equal("Dev", harness.Environments.Selected?.Name);
    }
}
