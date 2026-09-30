namespace Hoboman.Tests.ViewModels;

public sealed class EnvironmentEditorViewModelTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SaveAsync_WhenAnEnvironmentIsRenamed_ThenItsTokensFollow()
    {
        // Arrange
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([new("Dev", [])], Cancellation);
        var id = Guid.NewGuid();
        await harness.Secrets.SaveAsync(id, SecretKind.OAuthToken, "Dev", "token", Cancellation);
        var editor = harness.EnvironmentEditor();
        await editor.LoadAsync(Cancellation);
        editor.Selected!.Name = "Development";

        // Act
        await editor.SaveAsync();

        // Assert
        Assert.Equal("token", await harness.Secrets.OfAsync(id, SecretKind.OAuthToken, "Development", Cancellation));
    }

    [Fact]
    public async Task SaveAsync_WhenAnEnvironmentIsRemoved_ThenItsTokensAreDeleted()
    {
        // Arrange
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([new("Dev", [])], Cancellation);
        var id = Guid.NewGuid();
        await harness.Secrets.SaveAsync(id, SecretKind.OAuthToken, "Dev", "token", Cancellation);
        var editor = harness.EnvironmentEditor();
        await editor.LoadAsync(Cancellation);
        editor.Remove();

        // Act
        await editor.SaveAsync();

        // Assert
        Assert.Null(await harness.Secrets.OfAsync(id, SecretKind.OAuthToken, "Dev", Cancellation));
    }

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
        await editor.SaveAsync();

        // Assert
        Assert.Equal("https://dev.local", (await harness.EnvironmentStore.FindAsync("Dev", Cancellation))?.Resolve("{{baseUrl}}"));
    }

    [Fact]
    public async Task SaveAsync_WhenAnEnvironmentWasAdded_ThenSucceeds()
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
        await editor.SaveAsync();

        // Assert
        Assert.Equal("Every environment needs a name, and the names must be unique.", editor.Problem);
    }

    [Fact]
    public async Task SaveAsync_WhenTwoEnvironmentsHaveTheSameName_ThenFails()
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
    }

    [Fact]
    public async Task SaveAsync_WhenTheLastLoadFailed_ThenLeavesTheFileUntouched()
    {
        // Arrange
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([new("Dev", [])], Cancellation);
        var editor = harness.EnvironmentEditor();
        await editor.LoadAsync(Cancellation);
        using (new FileStream(harness.Folder.Environments, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await editor.LoadAsync(Cancellation);
        }
        editor.Add();

        // Act
        await editor.SaveAsync();

        // Assert
        Assert.NotNull(await harness.EnvironmentStore.FindAsync("Dev", Cancellation));
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
        await editor.SaveAsync();

        // Assert
        Assert.NotNull(await harness.EnvironmentStore.FindAsync("Agent", Cancellation));
    }

    [Fact]
    public async Task SaveAsync_WhenTheChosenEnvironmentIsRenamed_ThenTheChoiceFollows()
    {
        // Arrange
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([new("Dev", [])], Cancellation);
        await harness.Environments.LoadAsync(Cancellation);
        await harness.Environments.ChooseAsync(harness.Environments.Items.Single());
        var editor = harness.EnvironmentEditor();
        await editor.LoadAsync(Cancellation);
        editor.Selected!.Name = "Development";

        // Act
        await editor.SaveAsync();

        // Assert
        Assert.Equal("Development", harness.Environments.Selected?.Name);
    }

    [Fact]
    public async Task SaveAsync_WhenTheChosenEnvironmentIsRenamed_ThenTheChoiceIsRememberedAfterARestart()
    {
        // Arrange
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([new("Dev", [])], Cancellation);
        await harness.Environments.LoadAsync(Cancellation);
        await harness.Environments.ChooseAsync(harness.Environments.Items.Single());
        var editor = harness.EnvironmentEditor();
        await editor.LoadAsync(Cancellation);
        editor.Selected!.Name = "Development";
        var restarted = harness.Restarted();

        // Act
        await editor.SaveAsync();
        await restarted.LoadAsync(Cancellation);

        // Assert
        Assert.Equal("Development", restarted.Selected?.Name);
    }

    [Fact]
    public async Task Remove_WhenCalled_ThenRemovesTheSelectedEnvironment()
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
        Assert.Same(editor.Environments[0], editor.Selected);
    }

    [Fact]
    public async Task SaveAsync_WhenTheFileIsLocked_ThenShowsTheProblem()
    {
        // Arrange
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([new("Dev", [])], Cancellation);
        var editor = harness.EnvironmentEditor();
        await editor.LoadAsync(Cancellation);
        using var locked = new FileStream(harness.Folder.Environments, FileMode.Open, FileAccess.Read, FileShare.None);

        // Act
        await editor.SaveAsync();

        // Assert
        Assert.StartsWith("The environments could not be saved", editor.Problem);
    }
}
