namespace Hoboman.Tests.Requests;

public sealed class RequestLibraryTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    RequestLibrary Library() => new(new AppFolder(_directory));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Load_WhenTheRequestWasSaved_ThenGivesItBack()
    {
        // Arrange
        var request = ApiRequest.New() with { Method = "POST", Url = "https://dev.local", Headers = [new("Accept", "application/json")], BodyKind = BodyKind.Json, Body = "{}" };
        Library().Save("Users/Create user", request);

        // Act
        var loaded = Library().Load("Users/Create user");

        // Assert
        Assert.Equivalent(request, loaded, strict: true);
    }

    [Fact]
    public void Load_WhenTheFileOnlyHasAUrl_ThenFillsInTheRest()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_directory, "requests"));
        File.WriteAllText(Path.Combine(_directory, "requests", "Ping.json"), """{"url": "https://dev.local"}""");

        // Act
        var loaded = Library().Load("Ping");

        // Assert
        Assert.NotNull(loaded);
        Assert.NotEqual(Guid.Empty, loaded.Id);
        Assert.Equivalent(ApiRequest.New() with { Id = loaded.Id, Url = "https://dev.local" }, loaded, strict: true);
    }

    [Fact]
    public void Load_WhenTheFileHasNoUrl_ThenGivesNull()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_directory, "requests"));
        File.WriteAllText(Path.Combine(_directory, "requests", "Ping.json"), """{"method": "GET"}""");

        // Act
        var loaded = Library().Load("Ping");

        // Assert
        Assert.Null(loaded);
    }

    [Fact]
    public void Load_WhenTheRequestIsMissing_ThenGivesNull()
    {
        // Act
        var loaded = Library().Load("Missing");

        // Assert
        Assert.Null(loaded);
    }

    [Fact]
    public void Names_WhenRequestsAreInSubfolders_ThenGivesTheirPaths()
    {
        // Arrange
        Library().Save("Ping", ApiRequest.New());
        Library().Save("Users/Get user", ApiRequest.New());

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
