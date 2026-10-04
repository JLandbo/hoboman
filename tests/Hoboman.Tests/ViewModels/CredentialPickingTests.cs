using Hoboman.Tests.Auth;

namespace Hoboman.Tests.ViewModels;

public sealed class CredentialPickingTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static CredentialChoice Choice(Harness harness, string name) => harness.Credentials.All.Single(choice => choice.Credential.Name == name);

    [Fact]
    public async Task FindCredentials_WhenTyped_ThenFindsTheChosenEnvironmentsByName()
    {
        // Arrange
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        var auth = harness.Tab().Auth;

        // Act
        auth.FindCredentials("a");

        // Assert
        Assert.Equal(["Acies Docs", "Admin"], auth.FoundCredentials.Select(choice => choice.Credential.Name));
    }

    [Fact]
    public async Task UseAsync_WhenAnOAuthClientIsPicked_ThenFillsInItsKindFieldsAndSecret()
    {
        // Arrange
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        var auth = harness.Tab().Auth;

        // Act
        await auth.UseAsync(Choice(harness, "Acies Docs"));

        // Assert
        Assert.Equal((AuthKind.OAuth2, "https://auth.{{env}}.example/token", "docs-client", "apis", "docs-secret"), (auth.Kind, auth.TokenUrl, auth.ClientId, auth.Scope, auth.ClientSecret));
    }

    [Fact]
    public async Task UseAsync_WhenAnOAuthClientIsPicked_ThenFetchesATokenWithItInTheChosenEnvironment()
    {
        // Arrange
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        var auth = harness.Tab().Auth;

        // Act
        await auth.UseAsync(Choice(harness, "Acies Docs"));

        // Assert
        Assert.Equal(("docs-client", "docs-secret", "dev"), (harness.OAuth.Asked?.Settings.ClientId, harness.OAuth.Asked?.ClientSecret, harness.OAuth.Asked?.Environment?.Name));
    }

    [Fact]
    public async Task UseAsync_WhenAnOAuthClientIsPicked_ThenTheTokenIsNotSavedBeforeTheRequestIs()
    {
        // Arrange
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        var auth = harness.Tab().Auth;
        var id = Guid.NewGuid();
        await auth.LoadSecretsAsync(id, Cancellation);

        // Act
        await auth.UseAsync(Choice(harness, "Acies Docs"));

        // Assert
        Assert.Equal((true, null), (auth.HasUnsavedSecrets, await harness.Secrets.OfAsync(id, SecretKind.ClientSecret, Cancellation)));
    }

    [Fact]
    public async Task UseAsync_WhenABasicLoginIsPicked_ThenFetchesNothing()
    {
        // Arrange
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        var auth = harness.Tab().Auth;

        // Act
        await auth.UseAsync(Choice(harness, "Admin"));

        // Assert
        Assert.Equal((AuthKind.Basic, "admin", "admin-password", false), (auth.Kind, auth.UserName, auth.Password, harness.OAuth.Asked is not null));
    }

    [Fact]
    public async Task UseAsync_WhenPicked_ThenTheTokensOfTheClientBeforeAreDeletedWhenSaved()
    {
        // Arrange
        using var harness = new Harness();
        var (_, test) = await harness.SaveCredentialsAsync();
        var auth = harness.Tab().Auth;
        var id = Guid.NewGuid();
        await harness.Secrets.SaveAsync(id, SecretKind.OAuthToken, test.Id, FakeOAuthClient.Token.ToJson(), Cancellation);
        await auth.LoadSecretsAsync(id, Cancellation);

        // Act
        await auth.UseAsync(Choice(harness, "Admin"));
        await auth.SaveSecretsAsync(id, Cancellation);

        // Assert
        Assert.Null(await harness.Secrets.OfAsync(id, SecretKind.OAuthToken, test.Id, Cancellation));
    }

    [Fact]
    public async Task CredentialName_WhenPicked_ThenIsTheCredentials()
    {
        // Arrange
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        var auth = harness.Tab().Auth;

        // Act
        await auth.UseAsync(Choice(harness, "Admin"));

        // Assert
        Assert.Equal(("Admin", false), (auth.CredentialName, auth.IsCredentialOfOtherEnvironment));
    }

    [Fact]
    public async Task CredentialName_WhenAFieldIsChangedAfter_ThenIsNone()
    {
        // Arrange
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        var auth = harness.Tab().Auth;
        await auth.UseAsync(Choice(harness, "Admin"));

        // Act
        auth.Password = "another";

        // Assert
        Assert.Null(auth.CredentialName);
    }

    [Fact]
    public async Task CredentialName_WhenAnotherEnvironmentIsChosen_ThenSaysWhichAndIsMarked()
    {
        // Arrange
        using var harness = new Harness();
        var (_, test) = await harness.SaveCredentialsAsync();
        var auth = harness.Tab().Auth;
        await auth.UseAsync(Choice(harness, "Admin"));

        // Act
        await harness.Environments.ChooseAsync(test);

        // Assert
        Assert.Equal(("Admin · dev", true), (auth.CredentialName, auth.IsCredentialOfOtherEnvironment));
    }
}
