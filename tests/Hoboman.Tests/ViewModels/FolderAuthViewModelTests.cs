namespace Hoboman.Tests.ViewModels;

public sealed class FolderAuthViewModelTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SaveAsync_WhenBearerIsChosen_ThenRequestsInTheFolderUseIt()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.CreateFolderAsync("Users", Cancellation);
        var folderAuth = harness.FolderAuth();
        await folderAuth.LoadAsync("Users", Cancellation);
        folderAuth.AuthKind = AuthKind.Bearer;
        folderAuth.Token = "token";

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
        folderAuth.AuthKind = AuthKind.Bearer;
        folderAuth.Token = "token";

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
        Assert.Equal("token", folderAuth.Token);
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
        Assert.Equal(AuthKind.Inherit, folderAuth.AuthKind);
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
        folderAuth.AuthKind = AuthKind.Bearer;

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
        folderAuth.Token = "token";

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
        Directory.Move(Path.Combine(harness.Folder.Requests, "Users"), Path.Combine(harness.Folder.Requests, "Moved"));

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
        folderAuth.Token = "copy";

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
        folderAuth.Token = "copy";

        // Act
        await folderAuth.SaveAsync();

        // Assert
        Assert.Equal("users", await harness.Secrets.OfAsync(users.Id, SecretKind.Token, Cancellation));
    }
}
