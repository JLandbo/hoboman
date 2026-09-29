namespace Hoboman.Tests.ViewModels;

public sealed class EnvironmentEditorViewModelTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SaveAsync_WhenAnEnvironmentWasAdded_ThenSavesItWithItsVariables()
    {
        // Arrange
        using var harness = new Harness();
        var editor = harness.EnvironmentEditor();
        await editor.LoadAsync(Cancellation);
        editor.Add();
        editor.Selected!.Name = "Dev";
        editor.Selected.Variables.Rows[^1].Name = "baseUrl";
        editor.Selected.Variables.Rows[0].Value = "https://dev.local";

        // Act
        var saved = await editor.SaveAsync();

        // Assert
        Assert.True(saved);
        Assert.Equal("https://dev.local", (await harness.EnvironmentStore.FindAsync("Dev", Cancellation))?.Resolve("{{baseUrl}}"));
    }

    [Fact]
    public async Task SaveAsync_WhenTwoEnvironmentsHaveTheSameName_ThenShowsTheProblem()
    {
        // Arrange
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([new("Dev", [])], Cancellation);
        var editor = harness.EnvironmentEditor();
        await editor.LoadAsync(Cancellation);
        editor.Add();
        editor.Selected!.Name = "dev";

        // Act
        var saved = await editor.SaveAsync();

        // Assert
        Assert.False(saved);
        Assert.Equal("Every environment needs a name, and the names must be different.", editor.Problem);
    }

    [Fact]
    public async Task SaveAsync_WhenTheFileCouldNotBeRead_ThenLeavesItUntouched()
    {
        // Arrange
        using var harness = new Harness();
        Directory.CreateDirectory(harness.Folder.Root);
        File.WriteAllText(harness.Folder.Environments, "[{");
        var editor = harness.EnvironmentEditor();
        await editor.LoadAsync(Cancellation);
        editor.Add();

        // Act
        var saved = await editor.SaveAsync();

        // Assert
        Assert.False(saved);
        Assert.Equal("[{", File.ReadAllText(harness.Folder.Environments));
    }

    [Fact]
    public async Task SaveAsync_WhenTheFileChangedWhileEditing_ThenLeavesTheChangeAlone()
    {
        // Arrange
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([new("Dev", [])], Cancellation);
        var editor = harness.EnvironmentEditor();
        await editor.LoadAsync(Cancellation);
        await harness.EnvironmentStore.SaveAsync([new("Dev", []), new("Agent", [])], Cancellation);
        editor.Add();

        // Act
        var saved = await editor.SaveAsync();

        // Assert
        Assert.False(saved);
        Assert.NotNull(await harness.EnvironmentStore.FindAsync("Agent", Cancellation));
    }

    [Fact]
    public async Task SaveAsync_WhenTheChosenEnvironmentIsRenamed_ThenTheChoiceFollows()
    {
        // Arrange
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([new("Dev", [])], Cancellation);
        await harness.Environments.LoadAsync(Cancellation);
        harness.Environments.Choose(harness.Environments.All.Single());
        var editor = harness.EnvironmentEditor();
        await editor.LoadAsync(Cancellation);
        editor.Selected!.Name = "Development";

        // Act
        await editor.SaveAsync();
        await harness.Settings.Saving;

        // Assert
        Assert.Equal("Development", harness.Environments.Selected?.Name);
    }

    [Fact]
    public async Task Remove_WhenCalled_ThenSelectsTheNextEnvironment()
    {
        // Arrange
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([new("Dev", []), new("Prod", [])], Cancellation);
        var editor = harness.EnvironmentEditor();
        await editor.LoadAsync(Cancellation);

        // Act
        editor.Remove();

        // Assert
        Assert.Equal("Prod", Assert.Single(editor.Environments).Name);
        Assert.Same(editor.Environments[0], editor.Selected);
    }
}
