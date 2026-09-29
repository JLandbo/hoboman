namespace Hoboman.Tests.ViewModels;

public sealed class EnvironmentEditorViewModelTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    EnvironmentStore Store() => new(new AppFolder(_directory), NullLogger<EnvironmentStore>.Instance);

    EnvironmentEditorViewModel Editor() => new(Store(), new Translator(Translation.English), NullLogger<EnvironmentEditorViewModel>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_WhenAnEnvironmentWasAdded_ThenSavesItWithItsVariables()
    {
        // Arrange
        var editor = Editor();
        await editor.LoadAsync(Cancellation);
        editor.Add();
        editor.Selected!.Name = "Dev";
        editor.Selected.Variables.Rows[^1].Name = "baseUrl";
        editor.Selected.Variables.Rows[0].Value = "https://dev.local";

        // Act
        var saved = await editor.SaveAsync();

        // Assert
        Assert.True(saved);
        Assert.Equal("https://dev.local", (await Store().FindAsync("Dev", Cancellation))?.Resolve("{{baseUrl}}"));
    }

    [Fact]
    public async Task SaveAsync_WhenTwoEnvironmentsHaveTheSameName_ThenShowsTheProblem()
    {
        // Arrange
        await Store().SaveAsync([new("Dev", [])], Cancellation);
        var editor = Editor();
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
    public async Task Remove_WhenCalled_ThenSelectsTheNextEnvironment()
    {
        // Arrange
        await Store().SaveAsync([new("Dev", []), new("Prod", [])], Cancellation);
        var editor = Editor();
        await editor.LoadAsync(Cancellation);

        // Act
        editor.Remove();

        // Assert
        Assert.Equal("Prod", Assert.Single(editor.Environments).Name);
        Assert.Same(editor.Environments[0], editor.Selected);
    }
}
