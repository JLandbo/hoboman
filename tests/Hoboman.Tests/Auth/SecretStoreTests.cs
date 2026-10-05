using System.Text.Json;

namespace Hoboman.Tests.Auth;

public sealed class SecretStoreTests : IDisposable
{
    static readonly Guid _dev = Guid.NewGuid();
    static readonly Guid _prod = Guid.NewGuid();
    static readonly Guid _other = Guid.NewGuid();

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
    public async Task OfAsync_WhenTheSecretIsForAnotherEnvironment_ThenGivesNull()
    {
        // Arrange
        var id = Guid.NewGuid();
        await Store().SaveAsync(id, SecretKind.OAuthToken, _dev, "dev token", Cancellation);

        // Act
        var secret = await Store().OfAsync(id, SecretKind.OAuthToken, _prod, Cancellation);

        // Assert
        Assert.Null(secret);
    }

    [Fact]
    public async Task OfEachEnvironmentAsync_WhenSavedForTwoEnvironmentsAndNone_ThenGivesAllThree()
    {
        // Arrange
        var id = Guid.NewGuid();
        await Store().SaveAsync(id, SecretKind.OAuthToken, _dev, "dev token", Cancellation);
        await Store().SaveAsync(id, SecretKind.OAuthToken, _prod, "prod token", Cancellation);
        await Store().SaveAsync(id, SecretKind.OAuthToken, "token", Cancellation);

        // Act
        var secrets = await Store().OfEachEnvironmentAsync(id, SecretKind.OAuthToken, Cancellation);

        // Assert
        Assert.Equal(new Dictionary<Guid, string> { [Guid.Empty] = "token", [_dev] = "dev token", [_prod] = "prod token" }, secrets);
    }

    [Fact]
    public async Task ForgetEnvironmentsAsync_WhenAnEnvironmentIsRemoved_ThenItsSecretsAreDeleted()
    {
        // Arrange
        var id = Guid.NewGuid();
        await Store().SaveAsync(id, SecretKind.OAuthToken, _dev, "dev token", Cancellation);
        await Store().SaveAsync(id, SecretKind.OAuthToken, _prod, "prod token", Cancellation);

        // Act
        await Store().ForgetEnvironmentsAsync(new HashSet<Guid> { _dev }, Cancellation);

        // Assert
        Assert.Equal(new Dictionary<Guid, string> { [_prod] = "prod token" }, await Store().OfEachEnvironmentAsync(id, SecretKind.OAuthToken, Cancellation));
    }

    [Fact]
    public async Task ForgetEnvironmentsAsync_WhenAnEnvironmentIsRemoved_ThenKeepsTheSecretsWithoutOne()
    {
        // Arrange
        var id = Guid.NewGuid();
        await Store().SaveAsync(id, SecretKind.Password, "hemmelig", Cancellation);

        // Act
        await Store().ForgetEnvironmentsAsync(new HashSet<Guid> { _dev }, Cancellation);

        // Assert
        Assert.Equal("hemmelig", await Store().OfAsync(id, SecretKind.Password, Cancellation));
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

    [Fact]
    public async Task CopyAsync_WhenSecretsHaveKindsAndEnvironments_ThenCopiesThemWithoutChangingTheSource()
    {
        var store = Store();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        await store.SaveAsync(sourceId, SecretKind.ClientSecret, "client secret", Cancellation);
        await store.SaveAsync(sourceId, SecretKind.Password, "password", Cancellation);
        await store.SaveAsync(sourceId, SecretKind.Token, "bearer", Cancellation);
        await store.SaveAsync(sourceId, SecretKind.OAuthToken, _dev, "dev token", Cancellation);
        await store.SaveAsync(sourceId, SecretKind.OAuthToken, _prod, "prod token", Cancellation);
        await store.SaveAsync(targetId, SecretKind.OAuthToken, _other, "other token", Cancellation);

        await store.CopyAsync(sourceId, targetId, Cancellation);

        Assert.Equal("client secret", await store.OfAsync(targetId, SecretKind.ClientSecret, Cancellation));
        Assert.Equal("password", await store.OfAsync(targetId, SecretKind.Password, Cancellation));
        Assert.Equal("bearer", await store.OfAsync(targetId, SecretKind.Token, Cancellation));
        Assert.Equal("dev token", await store.OfAsync(targetId, SecretKind.OAuthToken, _dev, Cancellation));
        Assert.Equal("prod token", await store.OfAsync(targetId, SecretKind.OAuthToken, _prod, Cancellation));
        Assert.Equal("other token", await store.OfAsync(targetId, SecretKind.OAuthToken, _other, Cancellation));
        Assert.Equal("client secret", await store.OfAsync(sourceId, SecretKind.ClientSecret, Cancellation));
        Assert.Equal("dev token", await store.OfAsync(sourceId, SecretKind.OAuthToken, _dev, Cancellation));
    }

    [Fact]
    public async Task CopyAsync_WhenTheSourceIdIsEmpty_ThenKeepsTheTargetUntouched()
    {
        var store = Store();
        var targetId = Guid.NewGuid();
        await store.SaveAsync(targetId, SecretKind.ClientSecret, "secret", Cancellation);

        await store.CopyAsync(Guid.Empty, targetId, Cancellation);

        Assert.Equal("secret", await store.OfAsync(targetId, SecretKind.ClientSecret, Cancellation));
    }

    [Fact]
    public async Task CopyAsync_WhenTheTargetIdIsEmpty_ThenThrows()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Store().CopyAsync(Guid.NewGuid(), Guid.Empty, Cancellation));
    }

    [Fact]
    public async Task DeleteAsync_WhenOneEnvironmentsTokenIsDeleted_ThenTheOtherStays()
    {
        // Arrange
        var id = Guid.NewGuid();
        await Store().SaveAsync(id, SecretKind.OAuthToken, _dev, "dev token", Cancellation);
        await Store().SaveAsync(id, SecretKind.OAuthToken, _prod, "prod token", Cancellation);

        // Act
        await Store().DeleteAsync(id, SecretKind.OAuthToken, _dev, Cancellation);

        // Assert
        Assert.Equal((null, "prod token"), (await Store().OfAsync(id, SecretKind.OAuthToken, _dev, Cancellation), await Store().OfAsync(id, SecretKind.OAuthToken, _prod, Cancellation)));
    }
}
