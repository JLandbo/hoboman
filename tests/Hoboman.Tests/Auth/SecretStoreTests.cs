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

    [Fact]
    public async Task OfAsync_WhenTheSavedValueIsNull_ThenGivesNull()
    {
        // Arrange
        var id = Guid.NewGuid();
        Directory.CreateDirectory(_temporary.Path);
        File.WriteAllText(Folder.Secrets, $$"""{"{{id}}/Token": null}""");

        // Act
        var secret = await Store().OfAsync(id, SecretKind.Token, Cancellation);

        // Assert
        Assert.Null(secret);
    }

    [Fact]
    public async Task DeleteAsync_WhenCalled_ThenRemovesEverySecretOfTheId()
    {
        // Arrange
        var id = Guid.NewGuid();
        await Store().SaveAsync(id, SecretKind.Password, "hemmelig", Cancellation);
        await Store().SaveAsync(id, SecretKind.Token, "token", Cancellation);

        // Act
        await Store().DeleteAsync(id, Cancellation);

        // Assert
        Assert.Null(await Store().OfAsync(id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task DeleteAsync_WhenCalled_ThenKeepsTheSecretsOfOthers()
    {
        // Arrange
        var other = Guid.NewGuid();
        await Store().SaveAsync(other, SecretKind.Token, "token", Cancellation);

        // Act
        await Store().DeleteAsync(Guid.NewGuid(), Cancellation);

        // Assert
        Assert.Equal("token", await Store().OfAsync(other, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task OfAsync_WhenTheSecretCannotBeDecrypted_ThenGivesNull()
    {
        // Arrange
        var id = Guid.NewGuid();
        Directory.CreateDirectory(_temporary.Path);
        File.WriteAllText(Folder.Secrets, $$"""{"{{id}}/Token": "{{Convert.ToBase64String("not encrypted"u8.ToArray())}}"}""");

        // Act
        var secret = await Store().OfAsync(id, SecretKind.Token, Cancellation);

        // Assert
        Assert.Null(secret);
    }

    [Fact]
    public async Task DeleteAsync_WhenCalled_ThenRemovesThePasswordToo()
    {
        // Arrange
        var id = Guid.NewGuid();
        await Store().SaveAsync(id, SecretKind.Password, "hemmelig", Cancellation);

        // Act
        await Store().DeleteAsync(id, Cancellation);

        // Assert
        Assert.Null(await Store().OfAsync(id, SecretKind.Password, Cancellation));
    }
}
