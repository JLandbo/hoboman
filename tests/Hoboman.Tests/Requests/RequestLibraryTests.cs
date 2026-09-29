namespace Hoboman.Tests.Requests;

public sealed class RequestLibraryTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

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
    public async Task LoadAsync_WhenTheFileOnlyHasAUrl_ThenFillsInTheRest()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_directory, "requests"));
        File.WriteAllText(Path.Combine(_directory, "requests", "Ping.json"), """{"url": "https://dev.local"}""");

        // Act
        var loaded = await Library().LoadAsync("Ping", Cancellation);

        // Assert
        Assert.NotNull(loaded);
        Assert.NotEqual(Guid.Empty, loaded.Id);
        Assert.Equivalent(ApiRequest.New() with { Id = loaded.Id, Url = "https://dev.local" }, loaded, strict: true);
    }

    [Fact]
    public async Task LoadAsync_WhenTheFileHasNoUrl_ThenGivesNull()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_directory, "requests"));
        File.WriteAllText(Path.Combine(_directory, "requests", "Ping.json"), """{"method": "GET"}""");

        // Act
        var loaded = await Library().LoadAsync("Ping", Cancellation);

        // Assert
        Assert.Null(loaded);
    }

    [Fact]
    public async Task LoadAsync_WhenTheRequestIsMissing_ThenGivesNull()
    {
        // Act
        var loaded = await Library().LoadAsync("Missing", Cancellation);

        // Assert
        Assert.Null(loaded);
    }

    [Fact]
    public async Task Names_WhenRequestsAreInSubfolders_ThenGivesTheirPaths()
    {
        // Arrange
        await Library().SaveAsync("Ping", ApiRequest.New(), Cancellation);
        await Library().SaveAsync("Users/Get user", ApiRequest.New(), Cancellation);

        // Act
        var names = Library().Names();

        // Assert
        Assert.Equal(["Ping", "Users/Get user"], names.Order());
    }

    [Fact]
    public void Names_WhenTheFolderIsMissing_ThenGivesNothing()
    {
        // Act
        var names = Library().Names();

        // Assert
        Assert.Empty(names);
    }
}
