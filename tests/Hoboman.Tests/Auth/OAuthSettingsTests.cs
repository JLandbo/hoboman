namespace Hoboman.Tests.Auth;

public sealed class OAuthSettingsTests
{
    [Fact]
    public void Parse_WhenAnOldFileHasNoCallbackUrl_ThenGivesAnEmptyCallbackUrl()
    {
        // Act
        var auth = JsonFile<AuthSettings>.Parse("""{ "kind": "OAuth2", "oAuth": { "grant": "AuthorizationCode", "authorizeUrl": "https://login.example/authorize", "clientId": "hoboman" } }""");

        // Assert
        Assert.Equal((OAuthGrant.AuthorizationCode, ""), (auth.OAuth!.Grant, auth.OAuth.CallbackUrl));
    }

    [Fact]
    public void Parse_WhenTheGrantIsImplicitWithACallbackUrl_ThenReadsBoth()
    {
        // Act
        var auth = JsonFile<AuthSettings>.Parse("""{ "kind": "OAuth2", "oAuth": { "grant": "Implicit", "authorizeUrl": "https://auth.{{site-url}}/login", "callbackUrl": "https://{{site-url}}/", "clientId": "{{client-id}}", "scope": "aciescore/apis" } }""");

        // Assert
        Assert.Equal((OAuthGrant.Implicit, "https://{{site-url}}/"), (auth.OAuth!.Grant, auth.OAuth.CallbackUrl));
    }
}
