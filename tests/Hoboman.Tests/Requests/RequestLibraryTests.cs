namespace Hoboman.Tests.Requests;

public sealed class RequestLibraryTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    string RequestsFolder => Path.Combine(_temporary.Path, "requests");

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    RequestLibrary Library() => new(new AppFolder(_temporary.Path), NullLogger<RequestLibrary>.Instance);

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public async Task LoadAsync_WhenTheRequestWasSaved_ThenGivesItBack()
    {
        // Arrange
        var request = ApiRequest.New() with { Method = "POST", Url = "https://dev.local", Headers = [new("Accept", "application/json")], BodyKind = BodyKind.Json, Body = "{}" };
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
        await Assert.ThrowsAsync<InvalidDataException>(() => loading);
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

        // Act
        var auth = await Library().AuthOfAsync("Users/Admin/Get user", ApiRequest.New() with { Auth = new(AuthKind.Basic) }, Cancellation);

        // Assert
        Assert.Equal(AuthKind.Basic, auth.Settings.Kind);

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
        Assert.Equal(users.Id, auth.Id);

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
        Assert.Equal(admin.Id, auth.Id);

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
        Assert.Equal(users.Id, auth.Id);

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
        // Arrange

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
}
