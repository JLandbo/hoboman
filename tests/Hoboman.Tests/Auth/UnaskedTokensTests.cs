namespace Hoboman.Tests.Auth;

public sealed class UnaskedTokensTests : IDisposable
{
    static readonly OAuthToken _fetched = new("fetched", "Bearer", null, null);

    readonly TemporaryFolder _temporary = new();

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    SecretStore Secrets => field ??= new(new(_temporary.Path), NullLogger<SecretStore>.Instance);

    UnaskedTokens Tokens(FakeOAuthClient oauth) => new(oauth, Secrets, NullLogger<UnaskedTokens>.Instance);

    static AuthSource Source(Guid id) => new(id, new(AuthKind.OAuth2));

    public void Dispose() => _temporary.Dispose();

    [Theory]
    [InlineData(AuthKind.OAuth2, OAuthGrant.ClientCredentials, true)]
    [InlineData(AuthKind.OAuth2, OAuthGrant.AuthorizationCode, false)]
    [InlineData(AuthKind.Bearer, OAuthGrant.ClientCredentials, false)]
    public void CanFetch_WhenGivenAnAuth_ThenOnlyAllowsClientCredentials(AuthKind kind, OAuthGrant grant, bool expected)
    {
        // Act
        var canFetch = UnaskedTokens.CanFetch(new(kind, OAuth: new() { Grant = grant }));

        // Assert
        Assert.Equal(expected, canFetch);
    }

    [Fact]
    public void CanFetch_WhenTheOAuthSettingsWereNeverChanged_ThenAllowsTheDefaultClientCredentials()
    {
        // Act
        var canFetch = UnaskedTokens.CanFetch(new(AuthKind.OAuth2));

        // Assert
        Assert.True(canFetch);
    }

    [Fact]
    public async Task FetchAsync_WhenATokenIsFetched_ThenSavesItForTheEnvironment()
    {
        // Arrange
        var id = Guid.NewGuid();
        var environment = new ApiEnvironment("Dev", []) { Id = Guid.NewGuid() };
        var tokens = Tokens(new(_ => Task.FromResult(_fetched)));

        // Act
        var fetched = await tokens.FetchAsync(Source(id), environment, Cancellation);

        // Assert
        Assert.Equal((true, _fetched.ToJson()), (fetched, await Secrets.OfAsync(id, SecretKind.OAuthToken, environment.Id, Cancellation)));
    }

    [Fact]
    public async Task FetchAsync_WhenAClientSecretIsSaved_ThenFetchesWithIt()
    {
        // Arrange
        var id = Guid.NewGuid();
        await Secrets.SaveAsync(id, SecretKind.ClientSecret, "secret", Cancellation);
        var oauth = new FakeOAuthClient(_ => Task.FromResult(_fetched));

        // Act
        await Tokens(oauth).FetchAsync(Source(id), new("Dev", []), Cancellation);

        // Assert
        Assert.Equal("secret", oauth.Asked?.ClientSecret);
    }

    [Fact]
    public async Task FetchAsync_WhenTheOwnerHasNoId_ThenFetchesNone()
    {
        // Arrange
        var oauth = new FakeOAuthClient(_ => Task.FromResult(_fetched));

        // Act
        var fetched = await Tokens(oauth).FetchAsync(Source(Guid.Empty), new("Dev", []), Cancellation);

        // Assert
        Assert.Equal((false, null), (fetched, oauth.Asked));
    }

    [Fact]
    public async Task FetchAsync_WhenTheServerRefuses_ThenSaysNoneWasFetched()
    {
        // Arrange
        var tokens = Tokens(new(_ => throw new OAuthException(OAuthProblem.Denied, "access_denied")));

        // Act
        var fetched = await tokens.FetchAsync(Source(Guid.NewGuid()), new("Dev", []), Cancellation);

        // Assert
        Assert.False(fetched);
    }

    [Fact]
    public async Task FetchAsync_WhenCancelled_ThenStops()
    {
        // Arrange
        var tokens = Tokens(new(cancellationToken => Task.FromCanceled<OAuthToken>(cancellationToken)));

        // Act
        var fetching = tokens.FetchAsync(Source(Guid.NewGuid()), new("Dev", []), new CancellationToken(canceled: true));

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetching);
    }
}
