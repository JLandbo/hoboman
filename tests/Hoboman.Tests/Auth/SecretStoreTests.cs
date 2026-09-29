namespace Hoboman.Tests.Auth;

public sealed class SecretStoreTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    AppFolder Folder => new(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Of_WhenTheSecretWasSaved_ThenGivesItBack()
    {
        // Arrange
        var id = Guid.NewGuid();
        new SecretStore(Folder).Save(id, "hemmelig");

        // Act
        var secret = new SecretStore(Folder).Of(id);

        // Assert
        Assert.Equal("hemmelig", secret);
    }

    [Fact]
    public void Of_WhenNothingWasSaved_ThenGivesEmpty()
    {
        // Act
        var secret = new SecretStore(Folder).Of(Guid.NewGuid());

        // Assert
        Assert.Empty(secret);
    }

    [Fact]
    public void Save_WhenCalled_ThenDoesNotWriteTheSecretInPlainText()
    {
        // Act
        new SecretStore(Folder).Save(Guid.NewGuid(), "hemmelig");

        // Assert
        Assert.DoesNotContain("hemmelig", File.ReadAllText(Folder.Secrets));
    }
}
