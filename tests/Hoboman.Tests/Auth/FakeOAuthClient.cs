namespace Hoboman.Tests.Auth;

public sealed class FakeOAuthClient(Func<CancellationToken, Task<OAuthToken>>? get = null) : IOAuthClient
{
    public static OAuthToken Token { get; } = new("access", "Bearer", new DateTimeOffset(2026, 9, 30, 13, 0, 0, TimeSpan.Zero), null);

    public (OAuthSettings Settings, string ClientSecret, ApiEnvironment? Environment)? Asked { get; private set; }

    public Task<OAuthToken> GetTokenAsync(OAuthSettings settings, string clientSecret, ApiEnvironment? environment, CancellationToken cancellationToken)
    {
        Asked = (settings, clientSecret, environment);
        return get?.Invoke(cancellationToken) ?? Task.FromResult(Token);
    }
}
