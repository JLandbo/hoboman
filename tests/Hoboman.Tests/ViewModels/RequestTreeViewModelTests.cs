using Microsoft.Extensions.Logging;

namespace Hoboman.Tests.ViewModels;

public sealed class RequestTreeViewModelTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static RequestTreeViewModel Tree(Harness harness) => new(harness.Library, harness.Dialogs, harness.Translator, NullLogger<RequestTreeViewModel>.Instance);

    [Fact]
    public async Task LoadAsync_WhenAnOlderReadFinishesAfterAFolderMove_ThenKeepsTheNewDestination()
    {
        using var harness = new Harness();
        using var logger = new BlockedReadLogger(Cancellation);
        await harness.Library.CreateFolderAsync("Users", Cancellation);
        var tree = new RequestTreeViewModel(harness.Library, harness.Dialogs, harness.Translator, logger);
        await tree.LoadAsync(Cancellation);
        var tab = new RequestTabViewModel(harness.Services, ApiRequest.New(), destination: "Users");
        tree.Follow([tab]);
        File.WriteAllText(Path.Combine(harness.Folder.Requests, "Broken.json"), "{");
        var earlierLoad = tree.LoadAsync(Cancellation);
        try
        {
            await logger.Reading.Task.WaitAsync(Cancellation);
            await harness.Library.RenameFolderAsync("Users", "People", Cancellation);
            tab.MoveTo("People");

            await tree.LoadAsync(Cancellation);
        }
        finally
        {
            logger.Continue();
            await earlierLoad;
        }

        Assert.Equal("People", tab.Destination);
        var folder = Assert.Single(tree.Nodes, node => node.IsFolder);
        Assert.Equal("People", folder.Path);
        Assert.Same(tab, Assert.Single(folder.Children).Tab);
        await tree.LoadAsync(Cancellation);
        Assert.Equal("People", tab.Destination);
    }

    [Fact]
    public async Task SaveAsync_WhenAnOlderReadFinishesAfterTheFirstSave_ThenKeepsTheSavedRowAndId()
    {
        using var harness = new Harness(new FakeDialogs(answer: "Users/Saved"));
        using var logger = new BlockedReadLogger(Cancellation);
        await harness.Library.CreateFolderAsync("Users", Cancellation);
        var tree = new RequestTreeViewModel(harness.Library, harness.Dialogs, harness.Translator, logger);
        await tree.LoadAsync(Cancellation);
        var tab = new RequestTabViewModel(harness.Services, ApiRequest.New(), destination: "Users");
        tree.Follow([tab]);
        File.WriteAllText(Path.Combine(harness.Folder.Requests, "Broken.json"), "{");
        var earlierLoad = tree.LoadAsync(Cancellation);
        try
        {
            await logger.Reading.Task.WaitAsync(Cancellation);

            await tab.SaveAsync();
        }
        finally
        {
            logger.Continue();
            await earlierLoad;
        }

        Assert.Equal(tab.Id, tree.IdOf("users/saved"));
        var row = Assert.Single(RequestTreeViewModel.Flatten(tree.Nodes), node => node.Tab == tab);
        Assert.Equal("Users/Saved", row.Path);
        Assert.False(row.IsDraft);
    }

    sealed class BlockedReadLogger(CancellationToken cancellationToken) : ILogger<RequestTreeViewModel>, IDisposable
    {
        readonly ManualResetEventSlim _continue = new();
        int _warnings;

        public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel != LogLevel.Warning || Interlocked.Increment(ref _warnings) != 1)
            {
                return;
            }
            Reading.SetResult();
            _continue.Wait(cancellationToken);
        }

        public void Continue() => _continue.Set();

        public void Dispose() => _continue.Dispose();
    }

    [Theory]
    [InlineData("Users")]
    [InlineData("Users/Admin")]
    [InlineData("Users/Admin/Private")]
    public async Task Follow_WhenTabsAreAddedAndRemoved_ThenShowsOnlyDraftsAndKeepsTheirIdentity(string destination)
    {
        using var harness = new Harness();
        await harness.Library.SaveAsync($"{destination}/New request (1)", ApiRequest.New(), Cancellation);
        var tree = Tree(harness);
        await tree.LoadAsync(Cancellation);
        var tabs = tree.Follow([]);
        var first = new RequestTabViewModel(harness.Services, ApiRequest.New(), destination: destination) { Number = 1 };
        var second = new RequestTabViewModel(harness.Services, ApiRequest.New(), destination: destination) { Number = 1 };
        tabs.Add(harness.Tab());
        tabs.Add(new(harness.Services, ApiRequest.New(), suggestedName: first.DraftName, historyName: "call.json"));
        tabs.Add(first);
        tabs.Add(second);

        var folder = RequestTreeViewModel.Flatten(tree.Nodes).Single(node => node.Path == destination);
        Assert.Equal([null, first, second], folder.Children.Select(node => node.Tab));
        tree.Activate(second);
        Assert.Same(second, Assert.Single(RequestTreeViewModel.Flatten(tree.Nodes), node => node.IsActive).Tab);
        first.Method = "POST";
        harness.Translator.Use(Translation.Danish);
        first.Relabel();
        Assert.Equal("POST", folder.Children[1].Tab?.Method);
        Assert.Equal("Ny request (1)", folder.Children[1].Tab?.Title);
        tabs.Remove(first);
        Assert.Equal([null, second], folder.Children.Select(node => node.Tab));
        await tree.LoadAsync(Cancellation);
        Assert.Same(second, Assert.Single(RequestTreeViewModel.Flatten(tree.Nodes), node => node.IsDraft).Tab);
        tabs.Clear();
        Assert.DoesNotContain(RequestTreeViewModel.Flatten(tree.Nodes), node => node.IsDraft || node.IsActive);
    }

    [Theory]
    [InlineData("A/B/C")]
    [InlineData("A/B")]
    [InlineData("A")]
    [InlineData(null)]
    public async Task LoadAsync_WhenADestinationDisappears_ThenUsesTheNearestExistingAncestor(string? remaining)
    {
        using var harness = new Harness();
        await harness.Library.CreateFolderAsync("A/B/C", Cancellation);
        var tree = Tree(harness);
        await tree.LoadAsync(Cancellation);
        var tabs = tree.Follow([]);
        var tab = new RequestTabViewModel(harness.Services, ApiRequest.New(), destination: "A/B/C") { Number = 3 };
        tab.Body = "content";
        tabs.Add(tab);
        tree.Activate(tab);
        foreach (var folder in RequestTreeViewModel.Flatten(tree.Nodes).Where(node => node.IsFolder))
        {
            folder.IsExpanded = false;
        }
        if (remaining != "A/B/C")
        {
            var removed = remaining is null ? "A" : remaining == "A" ? "A/B" : "A/B/C";
            await harness.Library.DeleteFolderAsync(removed, Cancellation);
        }
        var reveals = 0;
        tree.Revealed += _ => reveals++;

        await tree.LoadAsync(Cancellation);

        Assert.Equal(remaining, tab.Destination);
        Assert.Equal((true, true, "content", 3), (tab.IsDraft, tab.IsDirty, tab.Body, tab.Number));
        var row = Assert.Single(RequestTreeViewModel.Flatten(tree.Nodes), node => node.IsDraft);
        Assert.Equal(remaining, RequestLibrary.ParentOf(row.Path));
        Assert.True(row.IsActive);
        Assert.DoesNotContain(RequestTreeViewModel.Flatten(tree.Nodes), node => node.IsExpanded);
        Assert.Equal(0, reveals);
        Assert.Equal(remaining?.Split('/').Length ?? 0, RequestTreeViewModel.Flatten(tree.Nodes).Count(node => node.IsFolder));
    }

    [Theory]
    [InlineData("Users/Saved")]
    [InlineData("Other/Deep/Saved")]
    [InlineData("Saved")]
    public async Task SaveAsync_WhenADraftIsSaved_ThenImmediatelyReplacesItsRowAndRevealsOnlyOnce(string name)
    {
        using var harness = new Harness(new FakeDialogs(answer: name));
        await harness.Library.CreateFolderAsync("Users", Cancellation);
        var tree = Tree(harness);
        await tree.LoadAsync(Cancellation);
        var tabs = tree.Follow([]);
        var tab = new RequestTabViewModel(harness.Services, ApiRequest.New(), destination: "Users");
        tabs.Add(tab);
        tree.Activate(tab);
        var reveals = new List<RequestNodeViewModel>();
        tree.Revealed += reveals.Add;

        await tab.SaveAsync();

        var row = Assert.Single(RequestTreeViewModel.Flatten(tree.Nodes), node => !node.IsFolder);
        Assert.Equal(name, row.Path);
        Assert.False(row.IsDraft);
        Assert.True(row.IsActive);
        Assert.Same(tab, row.Tab);
        Assert.Same(row, Assert.Single(reveals));
        Assert.All(RequestTreeViewModel.Flatten(tree.Nodes).Where(node => node.IsFolder && name.StartsWith($"{node.Path}/")), node => Assert.True(node.IsExpanded));
        tab.Method = "POST";
        await tab.SaveAsync();
        await tree.LoadAsync(Cancellation);
        Assert.Single(reveals);
        Assert.Single(RequestTreeViewModel.Flatten(tree.Nodes), node => !node.IsFolder);
    }

    [Fact]
    public async Task Follow_WhenASavedTabChangesOrCloses_ThenKeepsTheNodeAndReturnsToItsDiskMethod()
    {
        using var harness = new Harness();
        var request = ApiRequest.New();
        await harness.Library.SaveAsync("Users/Get", request, Cancellation);
        var tree = Tree(harness);
        await tree.LoadAsync(Cancellation);
        var tabs = tree.Follow([]);
        var row = Assert.Single(Assert.Single(tree.Nodes).Children);
        var tab = harness.Tab(request, "users/get");
        tabs.Add(tab);
        tree.Activate(tab);

        tab.Method = "DELETE";

        Assert.Same(row, Assert.Single(Assert.Single(tree.Nodes).Children));
        Assert.Equal("DELETE", row.Tab?.Method);
        Assert.True(row.Tab?.IsUnsaved);
        Assert.True(row.IsActive);
        tabs.Remove(tab);
        Assert.Null(row.Tab);
        Assert.Equal("GET", row.Method);
        Assert.False(row.IsActive);
        tabs.Add(tab);
        tab.Unlink();
        Assert.Null(row.Tab);
    }

    [Fact]
    public async Task Activate_WhenSelectingTabs_ThenRevealsOnlyRowsAndOnlyOpensTheirAncestors()
    {
        using var harness = new Harness();
        await harness.Library.SaveAsync("Users/Admin/Get", ApiRequest.New(), Cancellation);
        await harness.Library.CreateFolderAsync("Other", Cancellation);
        var tree = Tree(harness);
        await tree.LoadAsync(Cancellation);
        var tabs = tree.Follow([]);
        var tab = harness.Tab(name: "Users/Admin/Get");
        tabs.Add(tab);
        var reveals = 0;
        tree.Revealed += _ => reveals++;

        tree.Activate(tab);

        Assert.Equal(["Users", "Users/Admin"], RequestTreeViewModel.Flatten(tree.Nodes).Where(node => node.IsExpanded).Select(node => node.Path));
        tree.Activate(harness.Tab());
        tree.Activate(null);
        Assert.DoesNotContain(RequestTreeViewModel.Flatten(tree.Nodes), node => node.IsActive);
        Assert.Equal(1, reveals);
    }

    [Fact]
    public async Task LoadAsync_WhenARequestCannotBeRead_ThenKeepsTheDraftAndItsDestination()
    {
        using var harness = new Harness();
        await harness.Library.CreateFolderAsync("Users", Cancellation);
        var tree = Tree(harness);
        await tree.LoadAsync(Cancellation);
        var tab = new RequestTabViewModel(harness.Services, ApiRequest.New(), destination: "Users");
        tree.Follow([tab]);
        File.WriteAllText(Path.Combine(harness.Folder.Requests, "Users", "Broken.json"), "{");

        await tree.LoadAsync(Cancellation);

        Assert.Same(tab, Assert.Single(Assert.Single(tree.Nodes).Children, node => node.IsDraft).Tab);
        Assert.Equal("Users", tab.Destination);
    }

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
