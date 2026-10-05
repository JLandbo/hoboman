namespace Hoboman.Tests.Requests;

public sealed class RequestLibraryTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    string RequestsFolder => Path.Combine(_temporary.Path, "requests");

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    RequestLibrary Library() => new(new AppFolder(_temporary.Path), NullLogger<RequestLibrary>.Instance);

    public void Dispose() => _temporary.Dispose();

    Guid WriteRequest(string json)
    {
        var id = Guid.NewGuid();
        Directory.CreateDirectory(RequestsFolder);
        File.WriteAllText(Path.Combine(RequestsFolder, $"{id}.json"), json);
        return id;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadAsync_WhenTheRequestWasSaved_ThenGivesItBack(bool useVariables)
    {
        // Arrange
        var request = ApiRequest.New() with { Name = "Create user", FolderId = Guid.NewGuid(), Method = "POST", Url = "https://dev.local", Headers = [new("Accept", "application/json")], BodyKind = BodyKind.Json, Body = "{}", UseEnvironmentVariablesInBody = useVariables, Base64 = new() { Encode = ["$.html"], Decode = ["$.token"] } };
        await Library().SaveAsync(request, Cancellation);

        // Act
        var loaded = await Library().LoadAsync(request.Id, Cancellation);

        // Assert
        Assert.Equivalent(request, loaded, strict: true);
    }

    [Fact]
    public async Task LoadAsync_WhenTheFileOnlyHasAUrl_ThenFillsInTheRestAndTakesTheIdFromTheFileName()
    {
        // Arrange
        var id = WriteRequest("""{"url": "https://dev.local"}""");

        // Act
        var loaded = await Library().LoadAsync(id, Cancellation);

        // Assert
        Assert.Equivalent(new ApiRequest { Id = id, Url = "https://dev.local" }, loaded, strict: true);
        Assert.Equal(BodyKind.Json, loaded!.BodyKind);
    }

    [Fact]
    public async Task LoadAsync_WhenTheIdInTheFileIsAnother_ThenTheFileNameWins()
    {
        // Arrange
        var id = WriteRequest($$"""{"id": "{{Guid.NewGuid()}}", "url": "https://dev.local"}""");

        // Act
        var loaded = await Library().LoadAsync(id, Cancellation);

        // Assert
        Assert.Equal(id, loaded!.Id);
    }

    [Fact]
    public async Task LoadAsync_WhenOAuthOnlyHasTheFieldsItsGrantUses_ThenLoadsThem()
    {
        // Arrange
        var id = WriteRequest("""
            {"url": "https://dev.local", "auth": {"kind": "OAuth2", "oauth": {"grant": "ClientCredentials", "tokenUrl": "https://login.local/token", "clientId": "hoboman"}}}
            """);

        // Act
        var loaded = await Library().LoadAsync(id, Cancellation);

        // Assert
        Assert.Equal("https://login.local/token", loaded?.Auth.OAuth?.TokenUrl);
    }

    [Fact]
    public async Task LoadAsync_WhenAHeaderHasNoValue_ThenUsesAnEmptyValue()
    {
        // Arrange
        var id = WriteRequest("""{"url": "https://dev.local", "headers": [{"name": "X-Flag"}]}""");

        // Act
        var loaded = await Library().LoadAsync(id, Cancellation);

        // Assert
        Assert.Equal(new KeyValue("X-Flag", ""), Assert.Single(loaded!.Headers));
    }

    [Fact]
    public async Task LoadAsync_WhenTheFileHasNoUrl_ThenThrows()
    {
        // Arrange
        var id = WriteRequest("""{"method": "GET"}""");

        // Act
        var loading = Library().LoadAsync(id, Cancellation);

        // Assert
        await Assert.ThrowsAsync<InvalidFileException>(() => loading);
    }

    [Fact]
    public async Task LoadAsync_WhenAHeaderIsNull_ThenThrows()
    {
        // Arrange
        var id = WriteRequest("""{"url": "https://dev.local", "headers": [null]}""");

        // Act
        var loading = Library().LoadAsync(id, Cancellation);

        // Assert
        await Assert.ThrowsAsync<InvalidFileException>(() => loading);
    }

    [Fact]
    public async Task LoadAsync_WhenTheRequestIsMissing_ThenGivesNull()
    {
        // Act
        var loaded = await Library().LoadAsync(Guid.NewGuid(), Cancellation);

        // Assert
        Assert.Null(loaded);
    }

    [Theory]
    [InlineData("/api/users")]
    [InlineData("../settings")]
    [InlineData("Auth: get token")]
    [InlineData("Users./Get user")]
    [InlineData("Api v1.2")]
    [InlineData("Ping ")]
    public async Task SaveAsync_WhenTheNameCouldNotBeAFileName_ThenKeepsItAsItIs(string name)
    {
        // Arrange
        var request = ApiRequest.New() with { Name = name };

        // Act
        await Library().SaveAsync(request, Cancellation);

        // Assert
        Assert.Equal([name], (await Library().LoadAllAsync(Cancellation)).Requests.Select(saved => saved.Name));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("Get\tuser", false)]
    [InlineData("Get\nuser", false)]
    [InlineData("Auth: get token / v2", true)]
    public void IsValidName_WhenGivenAName_ThenOnlyRefusesEmptyNamesAndControlCharacters(string name, bool expected)
    {
        // Act
        var valid = RequestLibrary.IsValidName(name);

        // Assert
        Assert.Equal(expected, valid);
    }

    [Fact]
    public async Task LoadAllAsync_WhenRequestsAreInFolders_ThenGivesTheirPaths()
    {
        // Arrange
        await Library().SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        await Library().SaveAtAsync("Users/Get user", ApiRequest.New(), Cancellation);

        // Act
        var paths = await Library().PathsAsync(Cancellation);

        // Assert
        Assert.Equal(["Ping", "Users/Get user"], paths);
    }

    [Theory]
    [InlineData("Ping.json")]
    [InlineData("{0} - Copy.json")]
    [InlineData("{0:B}.json")]
    [InlineData("{0:N}.json")]
    [InlineData(" {0}.json")]
    [InlineData("00000000-0000-0000-0000-000000000000.json")]
    public async Task LoadAllAsync_WhenAFileNameIsNotAnId_ThenLeavesItOut(string name)
    {
        // Arrange
        Directory.CreateDirectory(RequestsFolder);
        File.WriteAllText(Path.Combine(RequestsFolder, string.Format(name, Guid.NewGuid())), """{"name": "Ping", "url": "https://dev.local"}""");

        // Act
        var collection = await Library().LoadAllAsync(Cancellation);

        // Assert
        Assert.Empty(collection.Requests);
    }

    [Fact]
    public async Task LoadAllAsync_WhenAFileCannotBeRead_ThenTellsItsIdApart()
    {
        // Arrange
        await Library().SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        var broken = WriteRequest("{");

        // Act
        var collection = await Library().LoadAllAsync(Cancellation);

        // Assert
        Assert.Equal(["Ping"], collection.Requests.Select(request => request.Name));
        Assert.Equal([broken], collection.Unreadable);
    }

    [Fact]
    public async Task LoadAllAsync_WhenTheFoldersAreMissing_ThenGivesNothing()
    {
        // Act
        var collection = await Library().LoadAllAsync(Cancellation);

        // Assert
        Assert.Equal((0, 0), (collection.Requests.Count, collection.Folders.Count));
    }

    [Fact]
    public async Task LoadAllAsync_WhenAFolderIsEmpty_ThenGivesItToo()
    {
        // Arrange
        await Library().FolderAtAsync("Users/Admin", Cancellation);

        // Act
        var collection = await Library().LoadAllAsync(Cancellation);

        // Assert
        Assert.Equal(["Users", "Users/Admin"], collection.Folders.Select(folder => collection.FolderPathOf(folder.Id)).Order());
    }

    [Fact]
    public async Task LoadAllAsync_WhenARequestsFolderIsGone_ThenShowsItAtTheTop()
    {
        // Arrange
        await Library().SaveAtAsync("Users/Get", ApiRequest.New(), Cancellation);
        await Library().DeleteFolderAsync((await Library().FolderAtAsync("Users", Cancellation))!.Value, Cancellation);

        // Act
        var paths = await Library().PathsAsync(Cancellation);

        // Assert
        Assert.Equal(["Get"], paths);
    }

    [Fact]
    public async Task RenameAsync_WhenCalled_ThenChangesOnlyTheName()
    {
        // Arrange
        var request = await Library().SaveAtAsync("Health/Ping", ApiRequest.New() with { Url = "https://dev.local" }, Cancellation);

        // Act
        await Library().RenameAsync(request.Id, "Pong", Cancellation);

        // Assert
        Assert.Equivalent(request with { Name = "Pong" }, await Library().LoadAsync(request.Id, Cancellation), strict: true);
    }

    [Fact]
    public async Task MoveAsync_WhenCalled_ThenChangesOnlyTheFolder()
    {
        // Arrange
        var request = await Library().SaveAtAsync("Ping", ApiRequest.New() with { Url = "https://dev.local" }, Cancellation);
        var health = await Library().FolderAtAsync("Health", Cancellation);

        // Act
        await Library().MoveAsync(request.Id, health, Cancellation);

        // Assert
        Assert.Equivalent(request with { FolderId = health }, await Library().LoadAsync(request.Id, Cancellation), strict: true);
    }

    [Fact]
    public async Task RenameFolderAsync_WhenCalled_ThenEverythingInItHasTheNewPath()
    {
        // Arrange
        await Library().SaveAtAsync("Users/Get", ApiRequest.New(), Cancellation);
        await Library().SaveAtAsync("Users/Admin/List", ApiRequest.New(), Cancellation);

        // Act
        await Library().RenameFolderAsync((await Library().FolderAtAsync("Users", Cancellation))!.Value, "People", Cancellation);

        // Assert
        Assert.Equal(["People/Admin/List", "People/Get"], await Library().PathsAsync(Cancellation));
    }

    [Fact]
    public async Task RenameFolderAsync_WhenTheFolderHasAuth_ThenKeepsItAndItsId()
    {
        // Arrange
        var folder = new RequestFolder { Id = Guid.NewGuid(), Name = "Users", Auth = new(AuthKind.Bearer) };
        await Library().SaveFolderAsync(folder, Cancellation);

        // Act
        await Library().RenameFolderAsync(folder.Id, "People", Cancellation);

        // Assert
        Assert.Equal(folder with { Name = "People" }, await Library().LoadFolderAsync(folder.Id, Cancellation));
    }

    [Fact]
    public async Task RenameFolderAsync_WhenOnlyTheCaseChanges_ThenRenamesTheFolder()
    {
        // Arrange
        var users = (await Library().FolderAtAsync("Users", Cancellation))!.Value;

        // Act
        await Library().RenameFolderAsync(users, "users", Cancellation);

        // Assert
        Assert.Equal("users", (await Library().LoadFolderAsync(users, Cancellation))?.Name);
    }

    [Fact]
    public async Task MoveFolderAsync_WhenCalled_ThenMovesEverythingInItWithoutWritingTheirFiles()
    {
        // Arrange
        var request = await Library().SaveAtAsync("Users/Admin/List", ApiRequest.New(), Cancellation);
        var file = Path.Combine(RequestsFolder, $"{request.Id}.json");
        var written = File.GetLastWriteTimeUtc(file);
        var old = (await Library().FolderAtAsync("Old", Cancellation))!.Value;

        // Act
        await Library().MoveFolderAsync((await Library().FolderAtAsync("Users", Cancellation))!.Value, old, Cancellation);

        // Assert
        Assert.Equal(["Old/Users/Admin/List"], await Library().PathsAsync(Cancellation));
        Assert.Equal(written, File.GetLastWriteTimeUtc(file));
    }

    [Fact]
    public async Task DeleteAsync_WhenCalled_ThenRemovesTheRequest()
    {
        // Arrange
        var request = await Library().SaveAtAsync("Ping", ApiRequest.New(), Cancellation);

        // Act
        await Library().DeleteAsync(request.Id, Cancellation);

        // Assert
        Assert.Empty(await Library().PathsAsync(Cancellation));
    }

    [Fact]
    public async Task DeleteAsync_WhenTheFileIsHeldForAMoment_ThenDeletesItOnceItIsLetGo()
    {
        // Arrange
        var request = await Library().SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        var held = new FileStream(Path.Combine(RequestsFolder, $"{request.Id}.json"), FileMode.Open, FileAccess.Read, FileShare.Read);

        // Act
        var deleting = Library().DeleteAsync(request.Id, Cancellation);
        await Task.Delay(100, Cancellation);
        held.Dispose();
        await deleting;

        // Assert
        Assert.False(Library().Exists(request.Id));
    }

    [Fact]
    public async Task AuthOfAsync_WhenTheRequestHasItsOwnAuth_ThenUsesIt()
    {
        // Arrange
        var users = new RequestFolder { Id = Guid.NewGuid(), Name = "Users", Auth = new(AuthKind.Bearer) };
        await Library().SaveFolderAsync(users, Cancellation);
        var request = ApiRequest.New() with { FolderId = users.Id, Auth = new(AuthKind.Basic) };

        // Act
        var auth = await Library().AuthOfAsync(request, Cancellation);

        // Assert
        Assert.Equal(new AuthSource(request.Id, request.Auth), auth);
    }

    [Fact]
    public async Task AuthOfAsync_WhenAFolderAboveHasAuth_ThenUsesTheFoldersAndTellsItsPath()
    {
        // Arrange
        var users = new RequestFolder { Id = Guid.NewGuid(), Name = "Users", Auth = new(AuthKind.Bearer) };
        var admin = new RequestFolder { Id = Guid.NewGuid(), Name = "Admin", ParentId = users.Id };
        await Library().SaveFolderAsync(users, Cancellation);
        await Library().SaveFolderAsync(admin, Cancellation);

        // Act
        var auth = await Library().AuthOfAsync(ApiRequest.New() with { FolderId = admin.Id }, Cancellation);

        // Assert
        Assert.Equal(new AuthSource(users.Id, users.Auth, "Users"), auth);
    }

    [Fact]
    public async Task AuthOfAsync_WhenTheNearestFolderHasAuth_ThenItWins()
    {
        // Arrange
        var users = new RequestFolder { Id = Guid.NewGuid(), Name = "Users", Auth = new(AuthKind.Bearer) };
        var admin = new RequestFolder { Id = Guid.NewGuid(), Name = "Admin", ParentId = users.Id, Auth = new(AuthKind.Basic) };
        await Library().SaveFolderAsync(users, Cancellation);
        await Library().SaveFolderAsync(admin, Cancellation);

        // Act
        var auth = await Library().AuthOfAsync(ApiRequest.New() with { FolderId = admin.Id }, Cancellation);

        // Assert
        Assert.Equal(new AuthSource(admin.Id, admin.Auth, "Users / Admin"), auth);
    }

    [Fact]
    public async Task AuthOfAsync_WhenTheNearestFolderSaysNone_ThenSendsNoAuth()
    {
        // Arrange
        var users = new RequestFolder { Id = Guid.NewGuid(), Name = "Users", Auth = new(AuthKind.Bearer) };
        var admin = new RequestFolder { Id = Guid.NewGuid(), Name = "Admin", ParentId = users.Id, Auth = AuthSettings.None };
        await Library().SaveFolderAsync(users, Cancellation);
        await Library().SaveFolderAsync(admin, Cancellation);

        // Act
        var auth = await Library().AuthOfAsync(ApiRequest.New() with { FolderId = admin.Id }, Cancellation);

        // Assert
        Assert.Equal(AuthKind.None, auth.Settings.Kind);
    }

    [Fact]
    public async Task AuthOfAsync_WhenNoFolderHasAuth_ThenSendsNoAuth()
    {
        // Arrange
        var users = (await Library().FolderAtAsync("Users/Admin", Cancellation))!.Value;

        // Act
        var auth = await Library().AuthOfAsync(ApiRequest.New() with { FolderId = users }, Cancellation);

        // Assert
        Assert.Equal(AuthKind.None, auth.Settings.Kind);
    }

    [Fact]
    public async Task AuthOfAsync_WhenTheRequestIsInNoFolder_ThenSendsNoAuth()
    {
        // Act
        var auth = await Library().AuthOfAsync(ApiRequest.New(), Cancellation);

        // Assert
        Assert.Equal(AuthKind.None, auth.Settings.Kind);
    }

    [Fact]
    public async Task AuthOfAsync_WhenTheFoldersLeadBackToThemselves_ThenStopsAtTheFolderTheTreeShowsAtTheTop()
    {
        // Arrange
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());
        await Library().SaveFolderAsync(new() { Id = first, Name = "First", ParentId = second }, Cancellation);
        await Library().SaveFolderAsync(new() { Id = second, Name = "Second", ParentId = first, Auth = new(AuthKind.Bearer) }, Cancellation);

        // Act
        var auth = await Library().AuthOfAsync(ApiRequest.New() with { FolderId = first }, Cancellation);

        // Assert
        Assert.Equal(AuthKind.None, auth.Settings.Kind);
    }
}
