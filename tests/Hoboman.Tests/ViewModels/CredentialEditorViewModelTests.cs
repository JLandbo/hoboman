namespace Hoboman.Tests.ViewModels;

public sealed class CredentialEditorViewModelTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static async Task<CredentialEditorViewModel> EditorAsync(Harness harness)
    {
        await harness.CredentialEditor.LoadAsync(TestContext.Current.CancellationToken);
        return harness.CredentialEditor;
    }

    static IEnumerable<string> Names(CredentialEditorViewModel editor) => editor.Shown.Select(draft => draft.Name);

    [Fact]
    public async Task LoadAsync_WhenAnEnvironmentIsChosen_ThenOpensOnItsCredentials()
    {
        // Arrange
        using var harness = new Harness();
        var (_, test) = await harness.SaveCredentialsAsync();
        await harness.Environments.ChooseAsync(test);

        // Act
        var editor = await EditorAsync(harness);

        // Assert
        Assert.Equal(["Batch"], Names(editor));
    }

    [Fact]
    public async Task Search_WhenTyped_ThenShowsTheCredentialsWithItInTheName()
    {
        // Arrange
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        var editor = await EditorAsync(harness);

        // Act
        editor.Search = "ADM";

        // Assert
        Assert.Equal(["Admin"], Names(editor));
    }

    [Fact]
    public async Task Add_WhenClicked_ThenANewOAuthClientIsChosen()
    {
        // Arrange
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        var editor = await EditorAsync(harness);

        // Act
        editor.Add();

        // Assert
        Assert.Equal(("New credential", AuthKind.OAuth2), (editor.Selected?.Name, editor.Selected?.Auth.Kind));
    }

    [Fact]
    public async Task SaveAsync_WhenTheCredentialsChangedOnDisk_ThenRefusesAndKeepsThem()
    {
        // Arrange
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        var editor = await EditorAsync(harness);
        await harness.CredentialStore.SaveAsync([], TestContext.Current.CancellationToken);

        // Act
        var saved = await editor.SaveAsync();

        // Assert
        Assert.Equal((false, harness.Translator.Of("Credentials.ChangedOnDisk"), 0), (saved, editor.Problem, (await harness.CredentialStore.AllAsync(TestContext.Current.CancellationToken)).Count));
    }

    [Fact]
    public async Task SaveAsync_WhenSavedTwiceInTheOpenWindow_ThenSavesBoth()
    {
        // Arrange
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        var editor = await EditorAsync(harness);
        editor.Add();
        editor.Selected!.Name = "Første";
        await editor.SaveAsync();
        editor.Add();
        editor.Selected!.Name = "Anden";

        // Act
        var saved = await editor.SaveAsync();

        // Assert
        Assert.Equal((true, (string?)null), (saved, editor.Problem));
    }

    [Fact]
    public async Task SaveAsync_WhenTwoInAnEnvironmentShareAName_ThenRefuses()
    {
        // Arrange
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        var editor = await EditorAsync(harness);
        editor.Add();
        editor.Selected!.Name = "admin";

        // Act
        var saved = await editor.SaveAsync();

        // Assert
        Assert.Equal((false, "Every credential needs a name, and the names must differ within each environment."), (saved, editor.Problem));
    }

    [Fact]
    public async Task SaveAsync_WhenTheNameIsUsedInAnotherEnvironment_ThenSaves()
    {
        // Arrange
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        var editor = await EditorAsync(harness);
        editor.Add();
        editor.Selected!.Name = "Batch";

        // Act
        var saved = await editor.SaveAsync();

        // Assert
        Assert.True(saved);
    }

    [Fact]
    public async Task SaveAsync_WhenASecretIsChanged_ThenItIsSavedUnderTheCredential()
    {
        // Arrange
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        var editor = await EditorAsync(harness);
        var admin = editor.Shown.Single(draft => draft.Name == "Admin");
        admin.Auth.Password = "new-password";

        // Act
        await editor.SaveAsync();

        // Assert
        Assert.Equal("new-password", await harness.Secrets.OfAsync(admin.Id, SecretKind.Password, Cancellation));
    }

    [Fact]
    public async Task SaveAsync_WhenAKindIsChanged_ThenTheWholeAuthIsSaved()
    {
        // Arrange
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        var editor = await EditorAsync(harness);
        var admin = editor.Shown.Single(draft => draft.Name == "Admin");
        admin.Auth.Kind = AuthKind.Bearer;

        // Act
        await editor.SaveAsync();

        // Assert
        Assert.Equal(AuthKind.Bearer, (await harness.CredentialStore.AllAsync(Cancellation)).Single(credential => credential.Id == admin.Id).Auth.Kind);
    }

    [Fact]
    public async Task SaveAsync_WhenSaved_ThenTheNewOneCanBePicked()
    {
        // Arrange
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        var editor = await EditorAsync(harness);
        editor.Add();
        editor.Selected!.Name = "Print";

        // Act
        await editor.SaveAsync();

        // Assert
        Assert.Contains("Print", harness.Credentials.OfChosenEnvironment.Select(choice => choice.Credential.Name));
    }

    [Fact]
    public async Task Remove_WhenSaved_ThenItIsGone()
    {
        // Arrange
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        var editor = await EditorAsync(harness);
        editor.Selected = editor.Shown.Single(draft => draft.Name == "Admin");

        // Act
        editor.Remove();
        await editor.SaveAsync();

        // Assert
        Assert.Equal(["Acies Docs", "Batch"], (await harness.CredentialStore.AllAsync(Cancellation)).Select(credential => credential.Name));
    }

    [Fact]
    public async Task FetchToken_WhenTried_ThenUsesTheCredentialsOwnEnvironment()
    {
        // Arrange
        using var harness = new Harness();
        var (_, test) = await harness.SaveCredentialsAsync();
        var editor = await EditorAsync(harness);
        var docs = editor.Shown.Single(draft => draft.Name == "Acies Docs");
        await harness.Environments.ChooseAsync(test);

        // Act
        await docs.Auth.FetchTokenAsync();

        // Assert
        Assert.Equal("dev", harness.OAuth.Asked?.Environment?.Name);
    }

    [Fact]
    public async Task SaveAsync_WhenATokenWasTried_ThenItIsNotSaved()
    {
        // Arrange
        using var harness = new Harness();
        var (dev, _) = await harness.SaveCredentialsAsync();
        var editor = await EditorAsync(harness);
        var docs = editor.Shown.Single(draft => draft.Name == "Acies Docs");
        await docs.Auth.FetchTokenAsync();

        // Act
        await editor.SaveAsync();

        // Assert
        Assert.Null(await harness.Secrets.OfAsync(docs.Id, SecretKind.OAuthToken, dev.Id, Cancellation));
    }

    [Fact]
    public async Task EditAsync_WhenNoWindowIsOpen_ThenShowsTheEditor()
    {
        // Arrange
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();

        // Act
        await harness.Credentials.EditAsync();

        // Assert
        Assert.Same(harness.CredentialEditor, harness.Dialogs.Shown);
    }
}
