namespace Hoboman.Tests.Environments;

public sealed class EnvironmentStoreTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    EnvironmentStore Store() => new(new AppFolder(_directory), NullLogger<EnvironmentStore>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task FindAsync_WhenTheNameIsSaved_ThenGivesTheEnvironment()
    {
        // Arrange
        await Store().SaveAsync([new("Dev", [new("base", "https://dev.local")]), new("Prod", [])], Cancellation);

        // Act
        var environment = await Store().FindAsync("Dev", Cancellation);

        // Assert
        Assert.Equal("https://dev.local", environment?.Resolve("{{base}}"));
    }

    [Fact]
    public async Task FindAsync_WhenNoNameIsGiven_ThenGivesNull()
    {
        // Arrange
        await Store().SaveAsync([new("Dev", [])], Cancellation);

        // Act
        var environment = await Store().FindAsync(null, Cancellation);

        // Assert
        Assert.Null(environment);
    }
}
