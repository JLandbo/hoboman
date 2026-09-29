namespace Hoboman.Tests.Auth;

public sealed class SecretStoreTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    AppFolder Folder => new(_temporary.Path);

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    SecretStore Store() => new(Folder, NullLogger<SecretStore>.Instance);

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public async Task OfAsync_WhenTheSecretWasSaved_ThenGivesItBack()
    {
        // Arrange
        var id = Guid.NewGuid();
        await Store().SaveAsync(id, SecretKind.Password, "hemmelig", Cancellation);

        // Act
        var secret = await Store().OfAsync(id, SecretKind.Password, Cancellation);

        // Assert
        Assert.Equal("hemmelig", secret);
    }

    [Fact]
    public async Task OfAsync_WhenOnlyAnotherKindWasSaved_ThenGivesNull()
    {
        // Arrange
        var id = Guid.NewGuid();
        await Store().SaveAsync(id, SecretKind.ClientSecret, "hemmelig", Cancellation);

        // Act
        var secret = await Store().OfAsync(id, SecretKind.Token, Cancellation);

        // Assert
        Assert.Null(secret);
    }

    [Fact]
    public async Task OfAsync_WhenNothingWasSaved_ThenGivesNull()
    {
        // Act
        var secret = await Store().OfAsync(Guid.NewGuid(), SecretKind.Token, Cancellation);

        // Assert
        Assert.Null(secret);
    }

    [Fact]
    public async Task SaveAsync_WhenCalled_ThenDoesNotWriteTheSecretInPlainText()
    {
        // Act
        await Store().SaveAsync(Guid.NewGuid(), SecretKind.Password, "hemmelig", Cancellation);

        // Assert
        Assert.DoesNotContain("hemmelig", File.ReadAllText(Folder.Secrets));
    }

    [Fact]
    public async Task SaveAsync_WhenTheIdIsEmpty_ThenThrows()
    {
        // Act
        var saving = Store().SaveAsync(Guid.Empty, SecretKind.Password, "hemmelig", Cancellation);

        // Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => saving);
    }
}
