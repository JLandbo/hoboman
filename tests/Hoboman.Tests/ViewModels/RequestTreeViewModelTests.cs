namespace Hoboman.Tests.ViewModels;

public sealed class RequestTreeViewModelTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static RequestTreeViewModel Tree(Harness harness) => new(harness.Library, NullLogger<RequestTreeViewModel>.Instance);

    [Fact]
    public async Task LoadAsync_WhenARequestIsInAFolder_ThenShowsItInTheFolder()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAsync("Users/Get user", ApiRequest.New(), Cancellation);
        var tree = Tree(harness);

        // Act
        await tree.LoadAsync(Cancellation);

        // Assert
        Assert.Equal("Users/Get user", Assert.Single(Assert.Single(tree.Nodes).Children).Path);
    }

    [Fact]
    public async Task LoadAsync_WhenThereAreFoldersAndRequests_ThenShowsTheFoldersFirst()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAsync("Ping", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAsync("Users/Get user", ApiRequest.New(), Cancellation);
        var tree = Tree(harness);

        // Act
        await tree.LoadAsync(Cancellation);

        // Assert
        Assert.Equal(["Users", "Ping"], tree.Nodes.Select(node => node.Path));
    }

    [Fact]
    public async Task LoadAsync_WhenLoadedAgain_ThenKeepsOpenFoldersOpen()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAsync("Users/Get user", ApiRequest.New(), Cancellation);
        var tree = Tree(harness);
        await tree.LoadAsync(Cancellation);
        tree.Nodes.Single().IsExpanded = true;

        // Act
        await tree.LoadAsync(Cancellation);

        // Assert
        Assert.True(tree.Nodes.Single().IsExpanded);
    }

    [Fact]
    public async Task NameOf_WhenTheIdIsSaved_ThenGivesTheName()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New();
        await harness.Library.SaveAsync("Users/Get user", request, Cancellation);
        var tree = Tree(harness);
        await tree.LoadAsync(Cancellation);

        // Act
        var name = tree.NameOf(request.Id);

        // Assert
        Assert.Equal("Users/Get user", name);
    }

    [Fact]
    public async Task NameOf_WhenTwoRequestsShareTheId_ThenGivesNoName()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New();
        await harness.Library.SaveAsync("Ping", request, Cancellation);
        await harness.Library.SaveAsync("Copy", request, Cancellation);
        var tree = Tree(harness);
        await tree.LoadAsync(Cancellation);

        // Act
        var name = tree.NameOf(request.Id);

        // Assert
        Assert.Null(name);
    }
}
