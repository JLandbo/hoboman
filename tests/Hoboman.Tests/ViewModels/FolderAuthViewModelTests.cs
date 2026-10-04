namespace Hoboman.Tests.ViewModels;

public sealed class FolderAuthViewModelTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SaveAsync_WhenAnOAuthTokenWasFetched_ThenSavesItForTheFolder()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.CreateFolderAsync("Users", Cancellation);
        var folderAuth = harness.FolderAuth();
        await folderAuth.LoadAsync("Users", Cancellation);
        folderAuth.Auth.Kind = AuthKind.OAuth2;
        await folderAuth.Auth.FetchTokenAsync();

        // Act
        await folderAuth.SaveAsync();

        // Assert
        Assert.NotNull(await harness.Secrets.OfAsync((await harness.Library.LoadFolderAsync("Users", Cancellation))!.Id, SecretKind.OAuthToken, Cancellation));
    }

    [Fact]
    public async Task SaveAsync_WhenBearerIsChosen_ThenRequestsInTheFolderUseIt()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.CreateFolderAsync("Users", Cancellation);
        var folderAuth = harness.FolderAuth();
        await folderAuth.LoadAsync("Users", Cancellation);
        folderAuth.Auth.Kind = AuthKind.Bearer;
        folderAuth.Auth.Token = "token";

        // Act
        await folderAuth.SaveAsync();

        // Assert
        Assert.Equal(AuthKind.Bearer, (await harness.Library.AuthOfAsync("Users/Get user", ApiRequest.New(), Cancellation)).Settings.Kind);
    }

    [Fact]
    public async Task SaveAsync_WhenATokenIsGiven_ThenSavesItForTheFolder()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.CreateFolderAsync("Users", Cancellation);
        var folderAuth = harness.FolderAuth();
        await folderAuth.LoadAsync("Users", Cancellation);
        folderAuth.Auth.Kind = AuthKind.Bearer;
        folderAuth.Auth.Token = "token";

        // Act
        await folderAuth.SaveAsync();

        // Assert
        Assert.Equal("token", await harness.Secrets.OfAsync((await harness.Library.LoadFolderAsync("Users", Cancellation))!.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task LoadAsync_WhenTheFolderHasAToken_ThenShowsIt()
    {
        // Arrange
        using var harness = new Harness();
        var folder = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveFolderAsync("Users", folder, Cancellation);
        await harness.Secrets.SaveAsync(folder.Id, SecretKind.Token, "token", Cancellation);
        var folderAuth = harness.FolderAuth();

        // Act
        await folderAuth.LoadAsync("Users", Cancellation);

        // Assert
        Assert.Equal("token", folderAuth.Auth.Token);
    }

    [Fact]
    public async Task LoadAsync_WhenTheFolderHasNoSettings_ThenInherits()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.CreateFolderAsync("Users", Cancellation);
        var folderAuth = harness.FolderAuth();

        // Act
        await folderAuth.LoadAsync("Users", Cancellation);

        // Assert
        Assert.Equal(AuthKind.Inherit, folderAuth.Auth.Kind);
    }

    [Fact]
    public async Task SaveAsync_WhenTheSettingsCouldNotBeRead_ThenLeavesThemUntouched()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.CreateFolderAsync("Users", Cancellation);
        var file = Path.Combine(harness.Folder.Requests, "Users", ".folder.json");
        File.WriteAllText(file, "{");
        var folderAuth = harness.FolderAuth();
        await folderAuth.LoadAsync("Users", Cancellation);
        folderAuth.Auth.Kind = AuthKind.Bearer;

        // Act
        await folderAuth.SaveAsync();

        // Assert
        Assert.Equal("{", File.ReadAllText(file));
    }

    [Fact]
    public async Task SaveAsync_WhenTheFileChangedWhileOpen_ThenLeavesTheChangeAlone()
    {
        // Arrange
        using var harness = new Harness();
        var users = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveFolderAsync("Users", users, Cancellation);
        var folderAuth = harness.FolderAuth();
        await folderAuth.LoadAsync("Users", Cancellation);
        await harness.Library.SaveFolderAsync("Users", users with { Auth = new(AuthKind.Basic, "agent") }, Cancellation);
        folderAuth.Auth.Token = "token";

        // Act
        await folderAuth.SaveAsync();

        // Assert
        Assert.Equal("agent", (await harness.Library.LoadFolderAsync("Users", Cancellation))?.Auth.UserName);
    }

    [Fact]
    public async Task SaveAsync_WhenTheFolderWasMovedWhileOpen_ThenDoesNotMakeItAgain()
    {
        // Arrange
        using var harness = new Harness();
        var users = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveFolderAsync("Users", users, Cancellation);
        var folderAuth = harness.FolderAuth();
        await folderAuth.LoadAsync("Users", Cancellation);
        await harness.Library.RenameFolderAsync("Users", "Moved", Cancellation);

        // Act
        await folderAuth.SaveAsync();

        // Assert
        Assert.False(harness.Library.FolderExists("Users"));
    }

    [Fact]
    public async Task SaveAsync_WhenACopySharesTheId_ThenGetsItsOwnId()
    {
        // Arrange
        using var harness = new Harness();
        var users = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveFolderAsync("Users", users, Cancellation);
        await harness.Library.SaveFolderAsync("Copy", users, Cancellation);
        await harness.Secrets.SaveAsync(users.Id, SecretKind.Token, "users", Cancellation);
        var folderAuth = harness.FolderAuth();
        await folderAuth.LoadAsync("Copy", Cancellation);
        folderAuth.Auth.Token = "copy";

        // Act
        await folderAuth.SaveAsync();

        // Assert
        Assert.NotEqual(users.Id, (await harness.Library.LoadFolderAsync("Copy", Cancellation))?.Id);
    }

    [Fact]
    public async Task SaveAsync_WhenACopySharesTheId_ThenLeavesTheOriginalsTokenAlone()
    {
        // Arrange
        using var harness = new Harness();
        var users = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveFolderAsync("Users", users, Cancellation);
        await harness.Library.SaveFolderAsync("Copy", users, Cancellation);
        await harness.Secrets.SaveAsync(users.Id, SecretKind.Token, "users", Cancellation);
        var folderAuth = harness.FolderAuth();
        await folderAuth.LoadAsync("Copy", Cancellation);
        folderAuth.Auth.Token = "copy";

        // Act
        await folderAuth.SaveAsync();

        // Assert
        Assert.Equal("users", await harness.Secrets.OfAsync(users.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task SaveAsync_WhenAPasswordIsGiven_ThenSavesItForTheFolder()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.CreateFolderAsync("Users", Cancellation);
        var folderAuth = harness.FolderAuth();
        await folderAuth.LoadAsync("Users", Cancellation);
        folderAuth.Auth.Kind = AuthKind.Basic;
        folderAuth.Auth.Password = "hemmelig";

        // Act
        await folderAuth.SaveAsync();

        // Assert
        Assert.Equal("hemmelig", await harness.Secrets.OfAsync((await harness.Library.LoadFolderAsync("Users", Cancellation))!.Id, SecretKind.Password, Cancellation));
    }

    [Fact]
    public async Task SaveAsync_WhenBasicIsChosen_ThenSavesTheUserName()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.CreateFolderAsync("Users", Cancellation);
        var folderAuth = harness.FolderAuth();
        await folderAuth.LoadAsync("Users", Cancellation);
        folderAuth.Auth.Kind = AuthKind.Basic;
        folderAuth.Auth.UserName = "hobo";

        // Act
        await folderAuth.SaveAsync();

        // Assert
        Assert.Equal("hobo", (await harness.Library.LoadFolderAsync("Users", Cancellation))?.Auth.UserName);
    }

    [Fact]
    public async Task SaveAsync_WhenSavedAgain_ThenKeepsTheId()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.CreateFolderAsync("Users", Cancellation);
        var folderAuth = harness.FolderAuth();
        await folderAuth.LoadAsync("Users", Cancellation);
        folderAuth.Auth.Kind = AuthKind.Bearer;
        await folderAuth.SaveAsync();
        var id = (await harness.Library.LoadFolderAsync("Users", Cancellation))!.Id;
        folderAuth.Auth.UserName = "again";

        // Act
        await folderAuth.SaveAsync();

        // Assert
        Assert.Equal(id, (await harness.Library.LoadFolderAsync("Users", Cancellation))?.Id);
    }

    [Fact]
    public async Task SaveAsync_WhenTheSecretsCannotBeSaved_ThenWritesNoFolderFile()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.CreateFolderAsync("Users", Cancellation);
        var folderAuth = harness.FolderAuth();
        await folderAuth.LoadAsync("Users", Cancellation);
        File.WriteAllText(harness.Folder.Secrets, "{");
        folderAuth.Auth.Kind = AuthKind.Bearer;
        folderAuth.Auth.Token = "token";

        // Act
        await folderAuth.SaveAsync();

        // Assert
        Assert.Null(await harness.Library.LoadFolderAsync("Users", Cancellation));
    }

    [Fact]
    public async Task LoadAsync_WhenTheFolderHasAPassword_ThenShowsIt()
    {
        // Arrange
        using var harness = new Harness();
        var folder = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Basic, "hobo") };
        await harness.Library.SaveFolderAsync("Users", folder, Cancellation);
        await harness.Secrets.SaveAsync(folder.Id, SecretKind.Password, "hemmelig", Cancellation);
        var folderAuth = harness.FolderAuth();

        // Act
        await folderAuth.LoadAsync("Users", Cancellation);

        // Assert
        Assert.Equal("hemmelig", folderAuth.Auth.Password);
    }

    [Fact]
    public async Task LoadAsync_WhenTheSettingsCannotBeRead_ThenShowsTheProblem()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.CreateFolderAsync("Users", Cancellation);
        File.WriteAllText(Path.Combine(harness.Folder.Requests, "Users", ".folder.json"), "{");
        var folderAuth = harness.FolderAuth();

        // Act
        await folderAuth.LoadAsync("Users", Cancellation);

        // Assert
        Assert.StartsWith("The folder's auth could not be loaded", folderAuth.Problem);
    }

    [Fact]
    public async Task SaveAsync_WhenAnotherFolderFileIsInvalid_ThenStillSaves()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveFolderAsync("A", new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) }, Cancellation);
        await harness.Library.CreateFolderAsync("B", Cancellation);
        File.WriteAllText(Path.Combine(harness.Folder.Requests, "B", ".folder.json"), "{");
        var folderAuth = harness.FolderAuth();
        await folderAuth.LoadAsync("A", Cancellation);
        folderAuth.Auth.Token = "token";

        // Act
        var saved = await folderAuth.SaveAsync();

        // Assert
        Assert.True(saved);
    }

    [Fact]
    public async Task LoadAsync_WhenTheFileIsInvalid_ThenSaysSoInTheChosenLanguage()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.CreateFolderAsync("Users", Cancellation);
        File.WriteAllText(Path.Combine(harness.Folder.Requests, "Users", ".folder.json"), "{");
        var folderAuth = new FolderAuthViewModel(harness.Library, harness.Secrets, harness.AuthRefresh, harness.Environments, harness.Credentials, new Translator(Translation.Danish), harness.Clock, NullLogger<FolderAuthViewModel>.Instance);

        // Act
        await folderAuth.LoadAsync("Users", Cancellation);

        // Assert
        Assert.DoesNotContain("is not valid", folderAuth.Problem);
    }
}
