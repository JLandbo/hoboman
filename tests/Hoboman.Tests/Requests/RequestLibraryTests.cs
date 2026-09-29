namespace Hoboman.Tests.Requests;

public sealed class RequestLibraryTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    string RequestsFolder => Path.Combine(_directory, "requests");

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    RequestLibrary Library() => new(new AppFolder(_directory), NullLogger<RequestLibrary>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

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
    public async Task SaveAsync_WhenTheNameIsInvalid_ThenThrowsWithoutWriting(string name)
    {
        // Act
        var saving = Library().SaveAsync(name, ApiRequest.New(), Cancellation);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(() => saving);
        Assert.False(Directory.Exists(_directory));
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
}
