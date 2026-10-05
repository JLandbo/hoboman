namespace Hoboman.Tests.Environments;

public sealed class EnvironmentStoreTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    AppFolder Folder => new(_temporary.Path);

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    EnvironmentStore Store() => new(Folder, NullLogger<EnvironmentStore>.Instance);

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

    [Fact]
    public async Task AllAsync_WhenAnEnvironmentHasNoId_ThenGivesItOneAndKeepsItOnTheNextLoad()
    {
        // Arrange
        await Store().SaveAsync([new("Dev", [])], Cancellation);

        // Act
        var first = (await Store().AllAsync(Cancellation)).Single();
        var next = (await Store().AllAsync(Cancellation)).Single();

        // Assert
        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.Equal(first.Id, next.Id);
    }

    [Fact]
    public async Task AllAsync_WhenEveryEnvironmentHasAnId_ThenWritesNothing()
    {
        // Arrange
        await Store().SaveAsync([new("Dev", []) { Id = Guid.NewGuid() }], Cancellation);
        var written = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Folder.Environments, written);

        // Act
        await Store().AllAsync(Cancellation);

        // Assert
        Assert.Equal(written, File.GetLastWriteTimeUtc(Folder.Environments));
    }

    [Fact]
    public async Task AllAsync_WhenTwoEnvironmentsShareAnId_ThenGivesTheLaterOneANewId()
    {
        // Arrange
        var id = Guid.NewGuid();
        await Store().SaveAsync([new("Dev", []) { Id = id }, new("Copy", []) { Id = id }], Cancellation);

        // Act
        var environments = await Store().AllAsync(Cancellation);

        // Assert
        Assert.Equal(id, environments[0].Id);
        Assert.DoesNotContain(environments[1].Id, new[] { id, Guid.Empty });
    }
}
