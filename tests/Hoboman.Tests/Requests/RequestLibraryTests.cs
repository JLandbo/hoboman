namespace Hoboman.Tests.Requests;

public sealed class RequestLibraryTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    string RequestsFolder => Path.Combine(_temporary.Path, "requests");

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    RequestLibrary Library() => new(new AppFolder(_temporary.Path), NullLogger<RequestLibrary>.Instance);

    public void Dispose() => _temporary.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadAsync_WhenTheRequestWasSaved_ThenGivesItBack(bool useVariables)
    {
        // Arrange
        var request = ApiRequest.New() with { Method = "POST", Url = "https://dev.local", Headers = [new("Accept", "application/json")], BodyKind = BodyKind.Json, Body = "{}", UseEnvironmentVariablesInBody = useVariables, Base64 = new() { Encode = ["$.html"], Decode = ["$.token"] } };
        await Library().SaveAsync("Users/Create user", request, Cancellation);

        // Act
        var loaded = await Library().LoadAsync("Users/Create user", Cancellation);

        // Assert
        Assert.Equivalent(request, loaded, strict: true);
    }

    [Fact]
    public async Task LoadAsync_WhenTheFileOnlyHasAUrl_ThenFillsInTheRestWithoutAnId()
    {
        // Arrange
        Directory.CreateDirectory(RequestsFolder);
        File.WriteAllText(Path.Combine(RequestsFolder, "Ping.json"), """{"url": "https://dev.local"}""");

        // Act
        var loaded = await Library().LoadAsync("Ping", Cancellation);

        // Assert
        Assert.Equivalent(new ApiRequest { Url = "https://dev.local" }, loaded, strict: true);
    }

    [Fact]
    public async Task LoadAsync_WhenOAuthOnlyHasTheFieldsItsGrantUses_ThenLoadsThem()
    {
        // Arrange
        Directory.CreateDirectory(RequestsFolder);
        File.WriteAllText(Path.Combine(RequestsFolder, "Ping.json"), """
            {"url": "https://dev.local", "auth": {"kind": "OAuth2", "oauth": {"grant": "ClientCredentials", "tokenUrl": "https://login.local/token", "clientId": "hoboman"}}}
            """);

        // Act
        var loaded = await Library().LoadAsync("Ping", Cancellation);

        // Assert
        Assert.Equal("https://login.local/token", loaded?.Auth.OAuth?.TokenUrl);
    }

    [Fact]
    public async Task LoadAsync_WhenAHeaderHasNoValue_ThenUsesAnEmptyValue()
    {
        // Arrange
        Directory.CreateDirectory(RequestsFolder);
        File.WriteAllText(Path.Combine(RequestsFolder, "Ping.json"), """{"url": "https://dev.local", "headers": [{"name": "X-Flag"}]}""");

        // Act
        var loaded = await Library().LoadAsync("Ping", Cancellation);

        // Assert
        Assert.Equal(new KeyValue("X-Flag", ""), Assert.Single(loaded!.Headers));
    }

    [Fact]
    public async Task LoadAsync_WhenTheFileHasNoUrl_ThenThrows()
    {
        // Arrange
        Directory.CreateDirectory(RequestsFolder);
        File.WriteAllText(Path.Combine(RequestsFolder, "Ping.json"), """{"method": "GET"}""");

        // Act
        var loading = Library().LoadAsync("Ping", Cancellation);

        // Assert
        await Assert.ThrowsAsync<InvalidFileException>(() => loading);
    }

    [Fact]
    public async Task LoadAsync_WhenTheRequestIsMissing_ThenGivesNull()
    {
        // Act
        var loaded = await Library().LoadAsync("Missing", Cancellation);

        // Assert
        Assert.Null(loaded);
    }

    [Theory]
    [InlineData("/api/users")]
    [InlineData("../settings")]
    [InlineData("Auth: get token")]
    [InlineData("Users//Get user")]
    [InlineData("Users./Get user")]
    [InlineData("Users/.folder")]
    public async Task SaveAsync_WhenTheNameIsInvalid_ThenThrows(string name)
    {
        // Act
        var saving = Library().SaveAsync(name, ApiRequest.New(), Cancellation);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(() => saving);
    }

    [Theory]
    [InlineData("/api/users")]
    [InlineData("../settings")]
    [InlineData("Auth: get token")]
    [InlineData("Users//Get user")]
    [InlineData("Users./Get user")]
    [InlineData("Users/.folder")]
    public async Task SaveAsync_WhenTheNameIsInvalid_ThenWritesNothing(string name)
    {
        // Act
        await Record.ExceptionAsync(() => Library().SaveAsync(name, ApiRequest.New(), Cancellation));

        // Assert
        Assert.False(Directory.Exists(_temporary.Path));
    }

    [Fact]
    public async Task NamesAsync_WhenRequestsAreInSubfolders_ThenGivesTheirPaths()
    {
        // Arrange
        await Library().SaveAsync("Ping", ApiRequest.New(), Cancellation);
        await Library().SaveAsync("Users/Get user", ApiRequest.New(), Cancellation);

        // Act
        var names = await Library().NamesAsync(Cancellation);

        // Assert
        Assert.Equal(["Ping", "Users/Get user"], names.Order());
    }

    [Fact]
    public async Task NamesAsync_WhenAFileNameEndsWithASpace_ThenLeavesItOut()
    {
        // Arrange
        Directory.CreateDirectory(RequestsFolder);
        File.WriteAllText(Path.Combine(RequestsFolder, "Ping .json"), """{"url": "https://dev.local"}""");

        // Act
        var names = await Library().NamesAsync(Cancellation);

        // Assert
        Assert.Empty(names);
    }

    [Fact]
    public async Task NamesAsync_WhenTheFolderIsMissing_ThenGivesNothing()
    {
        // Act
        var names = await Library().NamesAsync(Cancellation);

        // Assert
        Assert.Empty(names);
    }

    [Fact]
    public async Task FoldersAsync_WhenAFolderIsEmpty_ThenGivesItToo()
    {
        // Arrange
        await Library().CreateFolderAsync("Users/Admin", Cancellation);

        // Act
        var folders = await Library().FoldersAsync(Cancellation);

        // Assert
        Assert.Equal(["Users", "Users/Admin"], folders.Order());
    }

    [Fact]
    public async Task RenameAsync_WhenCalled_ThenMovesTheRequest()
    {
        // Arrange
        await Library().SaveAsync("Ping", ApiRequest.New(), Cancellation);

        // Act
        await Library().RenameAsync("Ping", "Health/Ping", Cancellation);

        // Assert
        Assert.Equal(["Health/Ping"], await Library().NamesAsync(Cancellation));
    }

    [Fact]
    public async Task RenameFolderAsync_WhenCalled_ThenMovesEverythingInIt()
    {
        // Arrange
        await Library().SaveAsync("Users/Get", ApiRequest.New(), Cancellation);
        await Library().SaveAsync("Users/Admin/List", ApiRequest.New(), Cancellation);

        // Act
        await Library().RenameFolderAsync("Users", "People", Cancellation);

        // Assert
        Assert.Equal(["People/Admin/List", "People/Get"], (await Library().NamesAsync(Cancellation)).Order());
    }

    [Fact]
    public async Task RenameFolderAsync_WhenAFileInItIsHeldForAMoment_ThenMovesItOnceItIsLetGo()
    {
        // Arrange
        await Library().SaveAsync("Users/Get", ApiRequest.New(), Cancellation);
        var held = new FileStream(Path.Combine(RequestsFolder, "Users", "Get.json"), FileMode.Open, FileAccess.Read, FileShare.Read);

        // Act
        var renaming = Library().RenameFolderAsync("Users", "People", Cancellation);
        await Task.Delay(100, Cancellation);
        held.Dispose();
        await renaming;

        // Assert
        Assert.Equal(["People/Get"], await Library().NamesAsync(Cancellation));
    }

    [Fact]
    public async Task RenameFolderAsync_WhenTheFolderHasSettings_ThenTheyMoveWithIt()
    {
        // Arrange
        var settings = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) };
        await Library().SaveFolderAsync("Users", settings, Cancellation);

        // Act
        await Library().RenameFolderAsync("Users", "People", Cancellation);

        // Assert
        Assert.Equal(settings.Id, (await Library().LoadFolderAsync("People", Cancellation))?.Id);
    }

    [Fact]
    public async Task RenameFolderAsync_WhenOnlyTheCaseChanges_ThenRenamesTheFolder()
    {
        // Arrange
        await Library().CreateFolderAsync("Users", Cancellation);

        // Act
        await Library().RenameFolderAsync("Users", "users", Cancellation);

        // Assert
        Assert.Equal(["users"], await Library().FoldersAsync(Cancellation));
    }

    [Fact]
    public async Task RenameFolderAsync_WhenTheNewParentIsMissing_ThenMakesIt()
    {
        // Arrange
        await Library().CreateFolderAsync("Users", Cancellation);

        // Act
        await Library().RenameFolderAsync("Users", "Old/Users", Cancellation);

        // Assert
        Assert.Equal(["Old", "Old/Users"], (await Library().FoldersAsync(Cancellation)).Order());
    }

    [Fact]
    public async Task DeleteFolderAsync_WhenCalled_ThenRemovesEverythingInIt()
    {
        // Arrange
        await Library().SaveAsync("Users/Get", ApiRequest.New(), Cancellation);
        await Library().SaveAsync("Users/Admin/List", ApiRequest.New(), Cancellation);
        await Library().SaveAsync("Ping", ApiRequest.New(), Cancellation);

        // Act
        await Library().DeleteFolderAsync("Users", Cancellation);

        // Assert
        Assert.Equal(["Ping"], await Library().NamesAsync(Cancellation));
    }

    [Fact]
    public async Task DeleteAsync_WhenCalled_ThenRemovesTheRequest()
    {
        // Arrange
        await Library().SaveAsync("Ping", ApiRequest.New(), Cancellation);

        // Act
        await Library().DeleteAsync("Ping", Cancellation);

        // Assert
        Assert.Empty(await Library().NamesAsync(Cancellation));
    }

    [Fact]
    public async Task NamesAsync_WhenAFolderHasSettings_ThenLeavesThemOut()
    {
        // Arrange
        await Library().SaveAsync("Users/Get user", ApiRequest.New(), Cancellation);
        await Library().SaveFolderAsync("Users", new FolderSettings { Id = Guid.NewGuid() }, Cancellation);

        // Act
        var names = await Library().NamesAsync(Cancellation);

        // Assert
        Assert.Equal(["Users/Get user"], names);
    }

    [Fact]
    public async Task AuthOfAsync_WhenTheRequestHasItsOwnAuth_ThenUsesIt()
    {
        // Arrange
        var users = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) };
        await Library().SaveFolderAsync("Users", users, Cancellation);

        var request = ApiRequest.New() with { Auth = new(AuthKind.Basic) };

        // Act
        var auth = await Library().AuthOfAsync("Users/Admin/Get user", request, Cancellation);

        // Assert
        Assert.Equal(new AuthSource(request.Id, request.Auth), auth);
    }

    [Fact]
    public async Task AuthOfAsync_WhenAFolderAboveHasAuth_ThenUsesTheFolders()
    {
        // Arrange
        var users = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) };
        await Library().SaveFolderAsync("Users", users, Cancellation);

        // Act
        var auth = await Library().AuthOfAsync("Users/Admin/Get user", ApiRequest.New(), Cancellation);

        // Assert
        Assert.Equal(new AuthSource(users.Id, users.Auth, "Users"), auth);
    }

    [Fact]
    public async Task AuthOfAsync_WhenTheNearestFolderHasAuth_ThenItWins()
    {
        // Arrange
        var users = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) };
        await Library().SaveFolderAsync("Users", users, Cancellation);
        var admin = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Basic) };
        await Library().SaveFolderAsync("Users/Admin", admin, Cancellation);

        // Act
        var auth = await Library().AuthOfAsync("Users/Admin/Get user", ApiRequest.New(), Cancellation);

        // Assert
        Assert.Equal(admin.Id, auth.SecretsId);
    }

    [Fact]
    public async Task AuthOfAsync_WhenTheNearestFolderInherits_ThenLooksFurtherUp()
    {
        // Arrange
        var users = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) };
        await Library().SaveFolderAsync("Users", users, Cancellation);
        await Library().SaveFolderAsync("Users/Admin", new FolderSettings { Id = Guid.NewGuid() }, Cancellation);

        // Act
        var auth = await Library().AuthOfAsync("Users/Admin/Get user", ApiRequest.New(), Cancellation);

        // Assert
        Assert.Equal(users.Id, auth.SecretsId);
    }

    [Fact]
    public async Task AuthOfAsync_WhenTheNearestFolderSaysNone_ThenSendsNoAuth()
    {
        // Arrange
        var users = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) };
        await Library().SaveFolderAsync("Users", users, Cancellation);
        await Library().SaveFolderAsync("Users/Admin", new FolderSettings { Id = Guid.NewGuid(), Auth = AuthSettings.None }, Cancellation);

        // Act
        var auth = await Library().AuthOfAsync("Users/Admin/Get user", ApiRequest.New(), Cancellation);

        // Assert
        Assert.Equal(AuthKind.None, auth.Settings.Kind);
    }

    [Fact]
    public async Task AuthOfAsync_WhenNoFolderHasAuth_ThenSendsNoAuth()
    {
        // Act
        var auth = await Library().AuthOfAsync("Users/Admin/Get user", ApiRequest.New(), Cancellation);

        // Assert
        Assert.Equal(AuthKind.None, auth.Settings.Kind);
    }

    [Fact]
    public async Task AuthOfAsync_WhenTheRequestHasNoName_ThenSendsNoAuth()
    {
        // Arrange
        var users = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) };
        await Library().SaveFolderAsync("Users", users, Cancellation);

        // Act
        var auth = await Library().AuthOfAsync(null, ApiRequest.New(), Cancellation);

        // Assert
        Assert.Equal(AuthKind.None, auth.Settings.Kind);
    }

    [Fact]
    public async Task LoadAsync_WhenAHeaderIsNull_ThenThrows()
    {
        // Arrange
        Directory.CreateDirectory(RequestsFolder);
        File.WriteAllText(Path.Combine(RequestsFolder, "Ping.json"), """{"url": "https://dev.local", "headers": [null]}""");

        // Act
        var loading = Library().LoadAsync("Ping", Cancellation);

        // Assert
        await Assert.ThrowsAsync<InvalidFileException>(() => loading);
    }

    [Fact]
    public async Task SaveAsync_WhenTheNameHasADotInside_ThenSavesIt()
    {
        // Act
        await Library().SaveAsync("Api v1.2/Get user", ApiRequest.New(), Cancellation);

        // Assert
        Assert.Equal(["Api v1.2/Get user"], await Library().NamesAsync(Cancellation));
    }
}
