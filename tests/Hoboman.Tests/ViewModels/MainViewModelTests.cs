namespace Hoboman.Tests.ViewModels;

public sealed class MainViewModelTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OpenAsync_WhenTheRequestIsAlreadyOpen_ThenSelectsItsTab()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAsync("Ping", ApiRequest.New() with { Url = "https://dev.local" }, Cancellation);
        var main = harness.Main();
        await main.LoadAsync(Cancellation);
        var node = main.Tree.Nodes.Single();
        await main.OpenAsync(node);
        main.NewTab();

        // Act
        await main.OpenAsync(node);

        // Assert
        Assert.Equal((3, "Ping"), (main.Tabs.Count, main.SelectedTab?.Name));
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenAnOpenRequestChangedOnDisk_ThenReloadsIt()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New() with { Url = "https://dev.local" };
        await harness.Library.SaveAsync("Ping", request, Cancellation);
        var main = harness.Main();
        await main.LoadAsync(Cancellation);
        await main.OpenAsync(main.Tree.Nodes.Single());
        await harness.Library.SaveAsync("Ping", request with { Url = "https://agent.local" }, Cancellation);

        // Act
        await main.RequestsChangedAsync();

        // Assert
        Assert.Equal("https://agent.local", main.SelectedTab?.Url);
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenTheTabSavedItself_ThenKeepsNewerEdits()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAsync("Ping", ApiRequest.New() with { Url = "https://dev.local" }, Cancellation);
        var main = harness.Main();
        await main.LoadAsync(Cancellation);
        await main.OpenAsync(main.Tree.Nodes.Single());
        var tab = main.SelectedTab!;
        tab.Url = "https://saved.local";
        await tab.SaveAsync();
        tab.Url = "https://newer.local";

        // Act
        await main.RequestsChangedAsync();

        // Assert
        Assert.Equal("https://newer.local", tab.Url);
    }

    [Fact]
    public async Task CanClose_WhenATabIsUnsaved_ThenAsksFirst()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: false));
        var main = harness.Main();
        await main.LoadAsync(Cancellation);
        main.SelectedTab!.Url = "https://dev.local";

        // Act
        var canClose = main.CanClose();

        // Assert
        Assert.False(canClose);
        Assert.Equal(1, harness.Dialogs.Asked);
    }

    [Fact]
    public async Task Close_WhenTheLastTabIsClosed_ThenOpensANewOne()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        await main.LoadAsync(Cancellation);
        var first = main.SelectedTab!;

        // Act
        main.Close(first);

        // Assert
        Assert.NotSame(first, Assert.Single(main.Tabs));
    }
}
