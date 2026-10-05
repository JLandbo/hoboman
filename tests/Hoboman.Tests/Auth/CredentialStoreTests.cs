namespace Hoboman.Tests.Auth;

public sealed class CredentialStoreTests : IDisposable
{
    static readonly Guid _dev = Guid.NewGuid();
    static readonly Guid _test = Guid.NewGuid();

    readonly TemporaryFolder _temporary = new();

    AppFolder Folder => new(_temporary.Path);

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    SecretStore Secrets() => new(Folder, NullLogger<SecretStore>.Instance);

    CredentialStore Store() => new(Folder, Secrets(), NullLogger<CredentialStore>.Instance);

    static Credential OAuthClient(Guid environment, string name) => new(Guid.NewGuid(), environment, name, new(AuthKind.OAuth2, OAuth: new() { TokenUrl = "https://auth.{{env}}.example/token", ClientId = $"{name}-client" }));

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public async Task AllAsync_WhenSaved_ThenGivesTheWholeAuthBack()
    {
        // Arrange
        var docs = OAuthClient(_dev, "docs");

        // Act
        await Store().SaveAsync([docs], Cancellation);

        // Assert
        Assert.Equal([docs], await Store().AllAsync(Cancellation));
    }

    [Fact]
    public async Task SaveAsync_WhenACredentialIsLeftOut_ThenItsSecretsAreDeleted()
    {
        // Arrange
        var docs = OAuthClient(_dev, "docs");
        await Store().SaveAsync([docs], Cancellation);
        await Secrets().SaveAsync(docs.Id, SecretKind.ClientSecret, "docs-secret", Cancellation);

        // Act
        await Store().SaveAsync([], Cancellation);

        // Assert
        Assert.Null(await Secrets().OfAsync(docs.Id, SecretKind.ClientSecret, Cancellation));
    }

    [Fact]
    public async Task ForgetEnvironmentsAsync_WhenAnEnvironmentIsRemoved_ThenOnlyItsCredentialsAndSecretsAreDeleted()
    {
        // Arrange
        var docs = OAuthClient(_dev, "docs");
        var batch = OAuthClient(_test, "batch");
        await Store().SaveAsync([docs, batch], Cancellation);
        await Secrets().SaveAsync(docs.Id, SecretKind.ClientSecret, "docs-secret", Cancellation);
        await Secrets().SaveAsync(batch.Id, SecretKind.ClientSecret, "batch-secret", Cancellation);

        // Act
        await Store().ForgetEnvironmentsAsync(new HashSet<Guid> { _dev }, Cancellation);

        // Assert
        Assert.Equal([batch], await Store().AllAsync(Cancellation));
        Assert.Equal((null, "batch-secret"), (await Secrets().OfAsync(docs.Id, SecretKind.ClientSecret, Cancellation), await Secrets().OfAsync(batch.Id, SecretKind.ClientSecret, Cancellation)));
    }
}
