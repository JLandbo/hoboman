namespace Hoboman.Tests.Environments;

public sealed class EnvironmentStoreTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    EnvironmentStore Store() => new(new AppFolder(_temporary.Path), NullLogger<EnvironmentStore>.Instance);

    public void Dispose() => _temporary.Dispose();

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

    [Fact]
    public async Task AllAsync_WhenTheListHoldsNull_ThenThrows()
    {
        // Arrange
        Directory.CreateDirectory(_temporary.Path);
        File.WriteAllText(Path.Combine(_temporary.Path, "environments.json"), "[null]");

        // Act
        var loading = Store().AllAsync(Cancellation);

        // Assert
        await Assert.ThrowsAsync<InvalidFileException>(() => loading);
    }
}
