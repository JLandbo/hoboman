using Hoboman.Tests.Requests;
using Microsoft.Extensions.Logging;

namespace Hoboman.Tests.ViewModels;

public sealed class RequestTreeViewModelTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static RequestTabViewModel Draft(Harness harness, Guid? folder, int number = 0) => new(harness.Services, ApiRequest.New() with { FolderId = folder }, draft: true) { Number = number };

    // Read once before the file that cannot be read is there, so the next read waits, and a newer load can finish before it.
    static async Task<RequestTreeViewModel> BlockedTreeAsync(Harness harness, BlockedReadLogger logger)
    {
        var tree = new RequestTreeViewModel(new RequestLibrary(harness.Folder, logger), new(), harness.Dialogs, harness.Translator, NullLogger<RequestTreeViewModel>.Instance);
        await tree.LoadAsync(CancellationToken.None);
        Directory.CreateDirectory(harness.Folder.Requests);
        File.WriteAllText(Path.Combine(harness.Folder.Requests, $"{Guid.NewGuid()}.json"), "{");
        return tree;
    }

    [Fact]
    public async Task LoadAsync_WhenAnOlderReadFinishesAfterAFolderIsRenamed_ThenKeepsTheNewName()
    {
        using var harness = new Harness();
        using var logger = new BlockedReadLogger(Cancellation);
        var users = await harness.Library.FolderAtAsync("Users", Cancellation);
        var blocked = await BlockedTreeAsync(harness, logger);
        var tab = Draft(harness, users);
        blocked.Follow([tab]);
        var earlierLoad = blocked.LoadAsync(Cancellation);
        try
        {
            await logger.Reading.Task.WaitAsync(Cancellation);
            await harness.Library.RenameFolderAtAsync("Users", "People", Cancellation);

            await blocked.LoadAsync(Cancellation);
        }
        finally
        {
            logger.Continue();
            await earlierLoad;
        }

        Assert.Equal(users, tab.FolderId);
        var folder = Assert.Single(blocked.Nodes, node => node.IsFolder);
        Assert.Equal("People", folder.Name);
        Assert.Same(tab, Assert.Single(folder.Children).Tab);
    }

    [Fact]
    public async Task SaveAsync_WhenAnOlderReadFinishesAfterTheFirstSave_ThenKeepsTheSavedRowAndId()
    {
        using var harness = new Harness(new FakeDialogs(answer: "Saved"));
        using var logger = new BlockedReadLogger(Cancellation);
        var users = await harness.Library.FolderAtAsync("Users", Cancellation);
        var tree = await BlockedTreeAsync(harness, logger);
        var tab = Draft(harness, users);
        tree.Follow([tab]);
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

        Assert.Equal(tab.Id, Assert.Single(tree.Collection.Find("users/saved")).Id);
        var row = Assert.Single(RequestTreeViewModel.Flatten(tree.Nodes), node => node.Tab == tab);
        Assert.Equal("Users/Saved", tree.PathOf(row));
        Assert.False(row.IsDraft);
    }

    sealed class BlockedReadLogger(CancellationToken cancellationToken) : ILogger<RequestLibrary>, IDisposable
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
        await harness.Library.SaveAtAsync($"{destination}/New request (1)", ApiRequest.New(), Cancellation);
        var folderId = await harness.Library.FolderAtAsync(destination, Cancellation);
        var tree = harness.Tree;
        await tree.LoadAsync(Cancellation);
        var tabs = tree.Follow([]);
        var first = Draft(harness, folderId, 1);
        var second = Draft(harness, folderId, 1);
        tabs.Add(harness.Tab());
        tabs.Add(new(harness.Services, ApiRequest.New() with { Name = first.Title, FolderId = folderId }, historyName: "call.json"));
        tabs.Add(first);
        tabs.Add(second);

        var folder = tree.NodeAt(destination);
        Assert.Equal([null, first, second], folder.Children.Select(node => node.Tab));
        tree.Activate(second);
        Assert.Same(second, Assert.Single(RequestTreeViewModel.Flatten(tree.Nodes), node => node.IsActive).Tab);
        first.Editor.Method = "POST";
        harness.Translator.Use(Translation.Danish);
        first.Relabel();
        Assert.Equal("POST", folder.Children[1].Tab?.Editor.Method);
        Assert.Equal("Ny request (1)", folder.Children[1].Tab?.Title);
        tabs.Remove(first);
        Assert.Equal([null, second], tree.NodeAt(destination).Children.Select(node => node.Tab));
        await tree.LoadAsync(Cancellation);
        Assert.Same(second, Assert.Single(RequestTreeViewModel.Flatten(tree.Nodes), node => node.IsDraft).Tab);
        tabs.Clear();
        Assert.DoesNotContain(RequestTreeViewModel.Flatten(tree.Nodes), node => node.IsDraft || node.IsActive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadAsync_WhenADraftsFolderIsGone_ThenMovesTheDraftToTheTop(bool deleted)
    {
        using var harness = new Harness();
        var folderId = await harness.Library.FolderAtAsync("A/B/C", Cancellation);
        var tree = harness.Tree;
        await tree.LoadAsync(Cancellation);
        var tabs = tree.Follow([]);
        var tab = Draft(harness, folderId);
        tab.Editor.Body = "content";
        tabs.Add(tab);
        tree.Activate(tab);
        foreach (var folder in RequestTreeViewModel.Flatten(tree.Nodes).Where(node => node.IsFolder))
        {
            folder.IsExpanded = false;
        }
        if (deleted)
        {
            await harness.Library.DeleteFolderAtAsync("A/B/C", Cancellation);
        }
        var reveals = 0;
        tree.Revealed += _ => reveals++;

        await tree.LoadAsync(Cancellation);

        Assert.Equal(deleted ? null : folderId, tab.FolderId);
        Assert.Equal((true, true, "content"), (tab.IsDraft, tab.IsDirty, tab.Editor.Body));
        var row = Assert.Single(RequestTreeViewModel.Flatten(tree.Nodes), node => node.IsDraft);
        Assert.Equal(deleted ? tab.Title : $"A/B/C/{tab.Title}", tree.PathOf(row));
        Assert.True(row.IsActive);
        Assert.DoesNotContain(RequestTreeViewModel.Flatten(tree.Nodes), node => node.IsExpanded);
        Assert.Equal(0, reveals);
    }

    [Theory]
    [InlineData("Users")]
    [InlineData("Other/Deep")]
    [InlineData(null)]
    public async Task SaveAsync_WhenADraftIsSaved_ThenImmediatelyReplacesItsRowAndRevealsOnlyOnce(string? destination)
    {
        using var harness = new Harness(new FakeDialogs(answer: "Saved"));
        var name = destination is null ? "Saved" : $"{destination}/Saved";
        var folderId = destination is null ? null : await harness.Library.FolderAtAsync(destination, Cancellation);
        var tree = harness.Tree;
        await tree.LoadAsync(Cancellation);
        var tabs = tree.Follow([]);
        var tab = Draft(harness, folderId);
        tabs.Add(tab);
        tree.Activate(tab);
        var reveals = new List<RequestNodeViewModel>();
        tree.Revealed += reveals.Add;

        await tab.SaveAsync();

        var row = Assert.Single(RequestTreeViewModel.Flatten(tree.Nodes), node => !node.IsFolder);
        Assert.Equal(name, tree.PathOf(row));
        Assert.False(row.IsDraft);
        Assert.True(row.IsActive);
        Assert.Same(tab, row.Tab);
        Assert.Same(row, Assert.Single(reveals));
        Assert.All(RequestTreeViewModel.Flatten(tree.Nodes).Where(node => node.IsFolder && name.StartsWith($"{tree.PathOf(node)}/")), node => Assert.True(node.IsExpanded));
        tab.Editor.Method = "POST";
        await tab.SaveAsync();
        await tree.LoadAsync(Cancellation);
        Assert.Single(reveals);
        Assert.Single(RequestTreeViewModel.Flatten(tree.Nodes), node => !node.IsFolder);
    }

    [Fact]
    public async Task Follow_WhenASavedTabChangesOrCloses_ThenKeepsTheNodeAndReturnsToItsDiskMethod()
    {
        using var harness = new Harness();
        var request = await harness.Library.SaveAtAsync("Users/Get", ApiRequest.New(), Cancellation);
        var tree = harness.Tree;
        await tree.LoadAsync(Cancellation);
        var tabs = tree.Follow([]);
        var row = Assert.Single(Assert.Single(tree.Nodes).Children);
        var tab = harness.Tab(request, saved: true);
        tabs.Add(tab);
        tree.Activate(tab);

        tab.Editor.Method = "DELETE";

        Assert.Same(row, Assert.Single(Assert.Single(tree.Nodes).Children));
        Assert.Equal("DELETE", row.Tab?.Editor.Method);
        Assert.True(row.Tab?.IsUnsaved);
        Assert.True(row.IsActive);
        tabs.Remove(tab);
        Assert.Null(row.Tab);
        Assert.Equal("GET", row.Method);
        Assert.False(row.IsActive);
        tabs.Add(tab);
        tab.Unlink();
        Assert.Null(RequestTreeViewModel.Flatten(tree.Nodes).Single(node => node.Id == request.Id).Tab);
    }

    [Fact]
    public async Task Activate_WhenSelectingTabs_ThenRevealsOnlyRowsAndOnlyOpensTheirAncestors()
    {
        using var harness = new Harness();
        var request = await harness.Library.SaveAtAsync("Users/Admin/Get", ApiRequest.New(), Cancellation);
        await harness.Library.FolderAtAsync("Other", Cancellation);
        var tree = harness.Tree;
        await tree.LoadAsync(Cancellation);
        var tabs = tree.Follow([]);
        var tab = harness.Tab(request, saved: true);
        tabs.Add(tab);
        var reveals = 0;
        tree.Revealed += _ => reveals++;

        tree.Activate(tab);

        Assert.Equal(["Users", "Users/Admin"], RequestTreeViewModel.Flatten(tree.Nodes).Where(node => node.IsExpanded).Select(tree.PathOf));
        tree.Activate(harness.Tab());
        tree.Activate(null);
        Assert.DoesNotContain(RequestTreeViewModel.Flatten(tree.Nodes), node => node.IsActive);
        Assert.Equal(1, reveals);
    }

    [Fact]
    public async Task LoadAsync_WhenARequestCannotBeRead_ThenKeepsTheDraftAndItsFolder()
    {
        using var harness = new Harness();
        var users = await harness.Library.FolderAtAsync("Users", Cancellation);
        var tree = harness.Tree;
        await tree.LoadAsync(Cancellation);
        var tab = Draft(harness, users);
        tree.Follow([tab]);
        Directory.CreateDirectory(harness.Folder.Requests);
        File.WriteAllText(Path.Combine(harness.Folder.Requests, $"{Guid.NewGuid()}.json"), "{");

        await tree.LoadAsync(Cancellation);

        Assert.Same(tab, Assert.Single(tree.Nodes.Single(node => node.IsFolder).Children, node => node.IsDraft).Tab);
        Assert.Equal(users, tab.FolderId);
    }

    [Fact]
    public async Task LoadAsync_WhenARequestCannotBeRead_ThenShowsItAtTheTopLevelByItsId()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Users/Get", ApiRequest.New(), Cancellation);
        var id = Guid.NewGuid();
        File.WriteAllText(Path.Combine(harness.Folder.Requests, $"{id}.json"), "{");
        var tree = harness.Tree;

        // Act
        await tree.LoadAsync(Cancellation);

        // Assert
        var row = Assert.Single(tree.Nodes, node => !node.IsFolder);
        Assert.Equal((id, $"Cannot be read ({$"{id}"[..8]}…)"), (row.Id, row.Name));
    }

    [Fact]
    public async Task LoadAsync_WhenARequestIsInAFolder_ThenShowsItInTheFolder()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Users/Get user", ApiRequest.New(), Cancellation);
        var tree = harness.Tree;

        // Act
        await tree.LoadAsync(Cancellation);

        // Assert
        Assert.Equal("Users/Get user", tree.PathOf(Assert.Single(Assert.Single(tree.Nodes).Children)));
    }

    [Fact]
    public async Task LoadAsync_WhenAFoldersParentIsGoneOrTheyLeadBackToEachOther_ThenShowsThemAtTheTop()
    {
        // Arrange
        using var harness = new Harness();
        var (shop, admin) = (Guid.NewGuid(), Guid.NewGuid());
        await harness.Library.SaveFolderAsync(new() { Id = shop, Name = "Shop", ParentId = admin }, Cancellation);
        await harness.Library.SaveFolderAsync(new() { Id = admin, Name = "Admin", ParentId = shop }, Cancellation);
        await harness.Library.SaveFolderAsync(new() { Id = Guid.NewGuid(), Name = "Orphan", ParentId = Guid.NewGuid() }, Cancellation);
        var tree = harness.Tree;

        // Act
        await tree.LoadAsync(Cancellation);

        // Assert
        Assert.Equal(["Admin", "Orphan", "Shop"], tree.Nodes.Select(node => node.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task LoadAsync_WhenThereAreFoldersAndRequests_ThenShowsTheFoldersFirst()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAtAsync("Users/Get user", ApiRequest.New(), Cancellation);
        var tree = harness.Tree;

        // Act
        await tree.LoadAsync(Cancellation);

        // Assert
        Assert.Equal(["Users", "Ping"], tree.Nodes.Select(tree.PathOf));
    }

    [Fact]
    public async Task LoadAsync_WhenANameHasASlash_ThenShowsItAsOneName()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAsync(ApiRequest.New() with { Name = "GET /users/{id}: hent" }, Cancellation);
        var tree = harness.Tree;

        // Act
        await tree.LoadAsync(Cancellation);

        // Assert
        Assert.Equal("GET /users/{id}: hent", Assert.Single(tree.Nodes).Name);
    }

    [Fact]
    public async Task LoadAsync_WhenLoadedAgain_ThenKeepsOpenFoldersOpen()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Users/Get user", ApiRequest.New(), Cancellation);
        var tree = harness.Tree;
        await tree.LoadAsync(Cancellation);
        tree.Nodes.Single().IsExpanded = true;

        // Act
        await tree.LoadAsync(Cancellation);

        // Assert
        Assert.True(tree.Nodes.Single().IsExpanded);
    }

    [Fact]
    public async Task Collection_WhenLoaded_ThenFindsARequestAndItsPathByItsId()
    {
        // Arrange
        using var harness = new Harness();
        var request = await harness.Library.SaveAtAsync("Users/Get user", ApiRequest.New(), Cancellation);
        var tree = harness.Tree;
        await tree.LoadAsync(Cancellation);

        // Act
        var found = tree.Collection.RequestOf(request.Id);

        // Assert
        Assert.Equal("Users/Get user", tree.Collection.PathOf(found!));
    }

    [Fact]
    public async Task LoadAsync_WhenTheOrderFileHoldsIds_ThenWritesNothing()
    {
        // Arrange
        using var harness = new Harness();
        var a = await harness.Library.SaveAtAsync("A", ApiRequest.New(), Cancellation);
        await harness.Library.SaveOrderAsync([$"{a.Id}"], Cancellation);
        var written = File.GetLastWriteTimeUtc(harness.Folder.RequestOrder);

        // Act
        await harness.Tree.LoadAsync(Cancellation);

        // Assert
        Assert.Equal(written, File.GetLastWriteTimeUtc(harness.Folder.RequestOrder));
    }

    [Fact]
    public async Task LoadAsync_WhenARequestIsRenamedOnDisk_ThenKeepsItsPlace()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("A", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAtAsync("B", ApiRequest.New(), Cancellation);
        await harness.Library.SaveOrderAtAsync(["B", "A"], Cancellation);
        var tree = harness.Tree;
        await tree.LoadAsync(Cancellation);
        await harness.Library.RenameAtAsync("B", "Z", Cancellation);

        // Act
        await tree.LoadAsync(Cancellation);

        // Assert
        Assert.Equal(["Z", "A"], tree.Nodes.Select(tree.PathOf));
    }

    [Fact]
    public async Task LoadAsync_WhenAFolderIsRenamedOnDisk_ThenItsRequestsKeepTheirOrder()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Folder/A", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAtAsync("Folder/B", ApiRequest.New(), Cancellation);
        await harness.Library.SaveOrderAtAsync(["Folder/", "Folder/B", "Folder/A"], Cancellation);
        var tree = harness.Tree;
        await tree.LoadAsync(Cancellation);
        await harness.Library.RenameFolderAtAsync("Folder", "Renamed", Cancellation);

        // Act
        await tree.LoadAsync(Cancellation);

        // Assert
        Assert.Equal(["Renamed/B", "Renamed/A"], tree.Nodes.Single().Children.Select(tree.PathOf));
    }

    [Fact]
    public async Task LoadAsync_WhenARequestIsCopiedInExplorer_ThenLeavesTheCopyOut()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("A", ApiRequest.New(), Cancellation);
        var original = await harness.Library.SaveAtAsync("B", ApiRequest.New(), Cancellation);
        await harness.Library.SaveOrderAtAsync(["B", "A"], Cancellation);
        var tree = harness.Tree;
        await tree.LoadAsync(Cancellation);
        File.Copy(Path.Combine(harness.Folder.Requests, $"{original.Id}.json"), Path.Combine(harness.Folder.Requests, $"{original.Id} - Copy.json"));

        // Act
        await tree.LoadAsync(Cancellation);

        // Assert
        Assert.Equal(["B", "A"], tree.Nodes.Select(tree.PathOf));
    }
}
