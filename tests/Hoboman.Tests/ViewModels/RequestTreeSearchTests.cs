using Hoboman.Tests.Requests;

namespace Hoboman.Tests.ViewModels;

public sealed class RequestTreeSearchTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static IReadOnlyList<string> Shown(RequestTreeViewModel tree) => [.. RequestTreeViewModel.Flatten(tree.Nodes).Where(node => node.IsShown).Select(tree.PathOf)];

    static async Task<RequestTreeViewModel> TreeAsync(Harness harness)
    {
        await harness.Library.SaveAtAsync("aciescore/docs/GET documents", ApiRequest.New(), CancellationToken.None);
        await harness.Library.SaveAtAsync("aciescore/docs/GET templates", ApiRequest.New(), CancellationToken.None);
        await harness.Library.SaveAtAsync("aciescore/bom/GET engines", ApiRequest.New(), CancellationToken.None);
        await harness.Tree.LoadAsync(CancellationToken.None);
        return harness.Tree;
    }

    [Fact]
    public async Task Search_WhenARequestNameMatches_ThenShowsItAndOpensTheFoldersAboveIt()
    {
        // Arrange
        using var harness = new Harness();
        var tree = await TreeAsync(harness);

        // Act
        tree.Search = "engines";

        // Assert
        Assert.Equal(["aciescore", "aciescore/bom", "aciescore/bom/GET engines"], Shown(tree));
        Assert.True(tree.NodeAt("aciescore").IsExpanded && tree.NodeAt("aciescore/bom").IsExpanded);
    }

    [Theory]
    [InlineData(true, new[] { "aciescore", "aciescore/docs", "aciescore/docs/GET documents", "aciescore/docs/GET templates" })]
    [InlineData(false, new string[0])]
    public async Task Search_WhenAFolderNameMatches_ThenShowsAllInItOnlyWhenWholeFoldersAreShown(bool wholeFolders, string[] expected)
    {
        // Arrange
        using var harness = new Harness();
        var tree = await TreeAsync(harness);
        tree.ShowWholeFolders = wholeFolders;

        // Act
        tree.Search = "DOCS";

        // Assert
        Assert.Equal(expected, Shown(tree));
        Assert.Equal(!wholeFolders, tree.NothingFound);
    }

    [Fact]
    public async Task Search_WhenCleared_ThenShowsAllAndOpensOnlyTheFoldersOpenBefore()
    {
        // Arrange
        using var harness = new Harness();
        var tree = await TreeAsync(harness);
        tree.NodeAt("aciescore").IsExpanded = true;
        tree.Search = "engines";

        // Act
        tree.Search = "";

        // Assert
        Assert.Equal(6, Shown(tree).Count);
        Assert.Equal((true, false), (tree.NodeAt("aciescore").IsExpanded, tree.NodeAt("aciescore/bom").IsExpanded));
    }

    [Fact]
    public async Task LoadAsync_WhenSearching_ThenFiltersTheNewRows()
    {
        // Arrange
        using var harness = new Harness();
        var tree = await TreeAsync(harness);
        tree.Search = "engines";
        await harness.Library.SaveAtAsync("aciescore/bom/POST engines", ApiRequest.New(), Cancellation);

        // Act
        await tree.LoadAsync(Cancellation);

        // Assert
        Assert.Equal(["aciescore", "aciescore/bom", "aciescore/bom/GET engines", "aciescore/bom/POST engines"], Shown(tree));
    }

    [Fact]
    public async Task SaveOrderAsync_WhenSearching_ThenKeepsTheHiddenRowsInTheOrder()
    {
        // Arrange
        using var harness = new Harness();
        var tree = await TreeAsync(harness);
        tree.Search = "engines";

        // Act
        await tree.SaveOrderAsync();

        // Assert
        Assert.Equal(6, (await harness.Library.LoadOrderAsync(Cancellation)).Count);
    }

    [Fact]
    public async Task Search_WhenADraftsTitleMatches_ThenShowsTheDraft()
    {
        // Arrange
        using var harness = new Harness();
        await TreeAsync(harness);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(main.Tree.NodeAt("aciescore/bom"));

        // Act
        main.Tree.Search = main.SelectedTab!.Title;

        // Assert
        Assert.Equal(["aciescore", "aciescore/bom", $"aciescore/bom/{main.SelectedTab.Title}"], Shown(main.Tree));
    }
}
