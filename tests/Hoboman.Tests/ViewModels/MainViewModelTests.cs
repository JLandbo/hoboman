using Hoboman.Tests.Auth;
using Hoboman.Tests.Requests;

namespace Hoboman.Tests.ViewModels;

public sealed class MainViewModelTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteAsync_WhenADraftWasJustSaved_ThenDeletesItsSecretsWithoutWaitingForTheWatcher(bool deleteFolder)
    {
        using var harness = new Harness(new FakeDialogs(answer: "Saved", accept: true));
        await harness.Library.FolderAtAsync("Users", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(NodeOf(main, "Users"));
        var tab = main.SelectedTab!;
        tab.Auth.Kind = AuthKind.Basic;
        tab.Auth.Password = "secret";
        await tab.SaveAsync();
        Assert.Equal("secret", await harness.Secrets.OfAsync(tab.Id, SecretKind.Password, Cancellation));

        if (deleteFolder)
        {
            await main.DeleteFolderAsync(NodeOf(main, "Users"));
        }
        else
        {
            await main.DeleteAsync(NodeOf(main, "Users/Saved"));
        }

        Assert.False(harness.Library.ExistsAt("Users/Saved"));
        Assert.Null(await harness.Secrets.OfAsync(tab.Id, SecretKind.Password, Cancellation));
    }

    [Fact]
    public async Task Close_WhenTheMethodChangedDuringADraftsFirstSave_ThenShowsTheMethodWrittenToDisk()
    {
        using var harness = new Harness(new FakeDialogs(answer: "Saved", accept: true));
        await harness.Library.FolderAtAsync("Users", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(NodeOf(main, "Users"));
        var tab = main.SelectedTab!;
        Task saving;
        Directory.CreateDirectory(harness.Folder.Requests);
        using (new FileStream(Path.Combine(harness.Folder.Requests, $"{tab.Id}.json.tmp"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            saving = tab.SaveAsync();
            tab.Editor.Method = "POST";
        }
        await saving;
        Assert.True(tab.IsDirty);
        Assert.Equal("POST", tab.Editor.Method);

        main.Close(tab);

        Assert.Equal("GET", (await harness.Library.LoadAtAsync("Users/Saved", Cancellation))?.Method);
        Assert.Null(NodeOf(main, "Users/Saved").Tab);
        Assert.Equal("GET", NodeOf(main, "Users/Saved").Method);
    }

    [Theory]
    [InlineData("Users")]
    [InlineData("Users/Admin")]
    [InlineData("Users/Admin/Private")]
    public async Task NewDraftAsync_WhenCreatedInAFolder_ThenAddsAndSelectsAnUnsavedInheritingTab(string destination)
    {
        using var harness = new Harness();
        await harness.Library.FolderAtAsync(destination, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        main.NewTab();
        var global = main.SelectedTab;

        await main.NewDraftAsync(NodeOf(main, destination));
        var first = main.SelectedTab!;
        await main.NewDraftAsync(NodeOf(main, destination));
        var second = main.SelectedTab!;

        Assert.Equal((3, 4), (first.Number, second.Number));
        Assert.Equal(destination, main.Tree.PathOfFolder(second.FolderId));
        Assert.Equal((true, false, true, AuthKind.Inherit), (second.IsDraft, second.IsDirty, second.IsUnsaved, second.Auth.Kind));
        Assert.Equal([first, second], NodeOf(main, destination).Children.Select(node => node.Tab));
        Assert.True(NodeOf(main, destination).IsExpanded);
        Assert.False(global!.IsDraft);
        Assert.Equal(0, harness.Dialogs.Asked);
        Assert.Empty(await harness.Library.PathsAsync(Cancellation));
        main.Close(second);
        Assert.Same(first, main.SelectedTab);
        Assert.Same(first, Assert.Single(NodeOf(main, destination).Children).Tab);
        Assert.Equal(0, harness.Dialogs.Asked);
    }

    [Theory]
    [InlineData(" Admin ", null)]
    [InlineData("", "Save.Invalid")]
    [InlineData("   ", "Save.Invalid")]
    [InlineData("One/Two: v2?", null)]
    [InlineData(null, null)]
    public async Task NewSubfolderAsync_WhenGivenAName_ThenValidatesAndCreatesOnlyOneChild(string? name, string? problem)
    {
        using var harness = new Harness(new FakeDialogs(answer: name));
        await harness.Library.FolderAtAsync("Users/Private/Taken", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        await main.NewSubfolderAsync(NodeOf(main, "Users/Private"));

        var created = name is not null && problem is null;
        var question = harness.Dialogs.NameQuestion!.Value;
        Assert.Equal(("New folder in Users/Private", "", "Create"), (question.Title, question.Name, question.Confirm));
        if (problem is not null)
        {
            Assert.Equal(harness.Translator.Of(problem), question.ProblemOf(name!));
        }
        Assert.Equal(created, harness.Library.FolderExistsAt($"Users/Private/{name?.Trim()}"));
        Assert.Equal(created, NodeOf(main, "Users/Private").IsExpanded);
        Assert.Equal(created ? 4 : 3, (await harness.Library.LoadAllAsync(Cancellation)).Folders.Count);
    }

    [Theory]
    [InlineData("Archive")]
    [InlineData("People")]
    [InlineData("users")]
    public async Task RenameFolderAsync_WhenItContainsDrafts_ThenTheirPathsFollowAndTheirContentStays(string name)
    {
        using var harness = new Harness(new FakeDialogs(answer: name));
        await harness.Library.FolderAtAsync("Users/Admin", Cancellation);
        await harness.Library.FolderAtAsync("Users2", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(NodeOf(main, "Users"));
        var first = main.SelectedTab!;
        await main.NewDraftAsync(NodeOf(main, "Users/Admin"));
        var second = main.SelectedTab!;
        second.Editor.Body = "content";
        await main.NewDraftAsync(NodeOf(main, "Users2"));
        var other = main.SelectedTab!;
        main.SelectedTab = second;
        var titles = main.Tabs.Select(tab => tab.Title).ToList();

        await main.RenameFolderAsync(NodeOf(main, "Users"));
        await main.RequestsChangedAsync();

        Assert.Equal((name, $"{name}/Admin", "Users2"), (main.Tree.PathOfFolder(first.FolderId), main.Tree.PathOfFolder(second.FolderId), main.Tree.PathOfFolder(other.FolderId)));
        Assert.Equal(titles, main.Tabs.Select(tab => tab.Title));
        Assert.Equal("content", second.Editor.Body);
        Assert.True(second.IsDirty);
        Assert.Same(second, Assert.Single(RequestTreeViewModel.Flatten(main.Tree.Nodes), node => node.IsActive).Tab);
        Assert.Equal(3, RequestTreeViewModel.Flatten(main.Tree.Nodes).Count(node => node.IsDraft));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteFolderAsync_WhenItContainsDrafts_ThenClosesThemOnlyAfterConfirmation(bool accept)
    {
        using var harness = new Harness(new FakeDialogs(accept: accept));
        await harness.Library.SaveAtAsync("Users/Admin/Get", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAtAsync("Users/Clean", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(NodeOf(main, "Users"));
        var untouched = main.SelectedTab!;
        await main.OpenAsync(NodeOf(main, "Users/Admin/Get"));
        var saved = main.SelectedTab!;
        saved.Editor.Body = "saved edit";
        await main.OpenAsync(NodeOf(main, "Users/Clean"));
        var clean = main.SelectedTab!;
        await main.NewDraftAsync(NodeOf(main, "Users/Admin"));
        var draft = main.SelectedTab!;
        draft.Editor.Body = "draft content";
        var tabs = main.Tabs.ToList();

        await main.DeleteFolderAsync(NodeOf(main, "Users"));

        Assert.Equal([untouched.Title, saved.Title, draft.Title], harness.Dialogs.ConfirmQuestion!.Value.Items);
        Assert.Equal(harness.Translator.Format("DeleteFolder.Message", "Users", 2), harness.Dialogs.ConfirmQuestion.Value.Message);
        Assert.Equal(1, harness.Dialogs.Asked);
        Assert.Equal(tabs.Where(tab => !accept || tab != untouched && tab != saved && tab != clean && tab != draft), main.Tabs);
        Assert.Equal(accept ? 0 : 2, RequestTreeViewModel.Flatten(main.Tree.Nodes).Count(node => node.IsDraft));
        Assert.Same(accept ? main.Tabs[0] : draft, main.SelectedTab);
        if (!accept)
        {
            Assert.Equal("draft content", draft.Editor.Body);
            Assert.Equal("saved edit", saved.Editor.Body);
            Assert.Equal("Users/Admin", main.Tree.PathOfFolder(draft.FolderId));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Close_WhenATabIsADraft_ThenOnlyPromptsForEdits(bool edited, bool accept)
    {
        using var harness = new Harness(new FakeDialogs(accept: accept));
        await harness.Library.FolderAtAsync("Users", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(NodeOf(main, "Users"));
        var draft = main.SelectedTab!;
        if (edited)
        {
            draft.Editor.Body = "content";
        }
        Assert.Equal(!edited || accept, main.CanClose());
        if (edited)
        {
            Assert.Equal([draft.Title], harness.Dialogs.ConfirmQuestion!.Value.Items);
        }

        main.Close(draft);

        Assert.Equal(edited && !accept, main.Tabs.Contains(draft));
        Assert.Equal(edited && !accept ? 1 : 0, RequestTreeViewModel.Flatten(main.Tree.Nodes).Count(node => node.IsDraft));
        Assert.Equal(edited ? 2 : 0, harness.Dialogs.Asked);
        Assert.Empty(await harness.Library.PathsAsync(Cancellation));
    }

    [Fact]
    public async Task OpenAsync_WhenDraftsAndFilesShareAName_ThenOpensTheCorrectTabWithoutDuplicates()
    {
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Users/New request (2)", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(NodeOf(main, "Users"));
        var draft = main.SelectedTab!;
        var draftRow = Assert.Single(NodeOf(main, "Users").Children, node => node.IsDraft);
        var fileRow = Assert.Single(NodeOf(main, "Users").Children, node => !node.IsDraft);

        await main.OpenAsync(fileRow);
        var saved = main.SelectedTab;
        await main.OpenAsync(draftRow);
        await main.OpenAsync(draftRow);

        Assert.Same(draft, main.SelectedTab);
        Assert.Equal(3, main.Tabs.Count);
        await main.OpenAsync(fileRow);
        Assert.Same(saved, main.SelectedTab);
        await draft.SendAsync();
        await main.HistoryChangedAsync();
        await main.OpenAsync(Assert.Single(main.History.Items));
        Assert.False(main.SelectedTab!.IsDraft);
        Assert.Equal("Users", main.Tree.PathOfFolder(main.SelectedTab.FolderId));
        Assert.DoesNotContain(RequestTreeViewModel.Flatten(main.Tree.Nodes), node => node.IsActive);
        Assert.Single(RequestTreeViewModel.Flatten(main.Tree.Nodes), node => node.IsDraft);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MoveAsync_WhenDroppingOnADraft_ThenMovesTheFileToItsDestination(bool root)
    {
        using var harness = new Harness();
        await harness.Library.FolderAtAsync("Users", Cancellation);
        await harness.Library.SaveAtAsync("Other/Get", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(NodeOf(main, "Users"));
        var draft = main.SelectedTab!;
        if (root)
        {
            await harness.Library.DeleteFolderAtAsync("Users", Cancellation);
            await main.RequestsChangedAsync();
        }
        var row = Assert.Single(RequestTreeViewModel.Flatten(main.Tree.Nodes), node => node.IsDraft);

        await main.MoveAsync(NodeOf(main, "Other/Get"), row);

        Assert.Equal([root ? "Get" : "Users/Get"], await harness.Library.PathsAsync(Cancellation));
        Assert.Same(draft, Assert.Single(RequestTreeViewModel.Flatten(main.Tree.Nodes), node => node.IsDraft).Tab);
        Assert.True(draft.IsUnsaved);
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public async Task DeleteFolderAsync_WhenOnlyPartCanBeDeleted_ThenOnlyClosesDraftsWhoseFoldersAreGone()
    {
        using var harness = new Harness(new FakeDialogs(accept: true));
        await harness.Library.FolderAtAsync("Users/Empty", Cancellation);
        var get = await harness.Library.SaveAtAsync("Users/Locked/Get", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(NodeOf(main, "Users/Empty"));
        var empty = main.SelectedTab!;
        await main.NewDraftAsync(NodeOf(main, "Users/Locked"));
        var locked = main.SelectedTab!;
        using var file = new FileStream(Path.Combine(harness.Folder.Requests, $"{get.Id}.json"), FileMode.Open, FileAccess.Read, FileShare.Read);

        await main.DeleteFolderAsync(NodeOf(main, "Users"));

        Assert.Equal(harness.Library.FolderExistsAt("Users/Empty"), main.Tabs.Contains(empty));
        Assert.Equal(harness.Library.FolderExistsAt("Users/Locked"), main.Tabs.Contains(locked));
        Assert.True(locked.IsDraft);
        Assert.Equal("Users/Locked", main.Tree.PathOfFolder(locked.FolderId));
        Assert.Equal(2, harness.Dialogs.Asked);
        Assert.Same(locked, main.SelectedTab);
        Assert.Equal(main.Tabs.Count(tab => tab.IsDraft), RequestTreeViewModel.Flatten(main.Tree.Nodes).Count(node => node.IsDraft));
    }

    [Fact]
    public async Task NewSubfolderAsync_WhenCreationFails_ThenReportsItWithoutExpandingTheParent()
    {
        using var harness = new Harness(new FakeDialogs(answer: "Child"));
        await harness.Library.FolderAtAsync("Users", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        Directory.Delete(harness.Folder.Folders, recursive: true);
        File.WriteAllText(harness.Folder.Folders, "blocked");

        await main.NewSubfolderAsync(NodeOf(main, "Users"));

        Assert.False(NodeOf(main, "Users").IsExpanded);
        Assert.False(harness.Library.FolderExistsAt("Users/Child"));
        Assert.Equal(2, harness.Dialogs.Asked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task RenameFolderAsync_WhenCancelledOrRejected_ThenLeavesDraftsWhereTheyAre(string? answer)
    {
        using var harness = new Harness(new FakeDialogs(answer: answer));
        await harness.Library.FolderAtAsync("Users/Admin", Cancellation);
        await harness.Library.FolderAtAsync("Taken", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(NodeOf(main, "Users/Admin"));
        var draft = main.SelectedTab!;

        await main.RenameFolderAsync(NodeOf(main, "Users"));

        Assert.Equal("Users/Admin", main.Tree.PathOfFolder(draft.FolderId));
        Assert.Same(draft, Assert.Single(RequestTreeViewModel.Flatten(main.Tree.Nodes), node => node.IsDraft).Tab);
    }

    [Fact]
    public async Task IsAuthRefreshing_WhenTheFolderItInheritsFromFetchesAToken_ThenIsTrue()
    {
        // Arrange
        var login = new TaskCompletionSource<OAuthToken>();
        using var harness = new Harness(oauth: new(cancellationToken => login.Task.WaitAsync(cancellationToken)));
        var folder = await harness.Library.SaveFolderAtAsync("Users", new() { Auth = new(AuthKind.OAuth2) }, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(NodeOf(main, "Users"));

        // Act
        var fetching = harness.Services.AuthRefresh.RefreshFolderAsync(new(folder.Id, folder.Auth), ApiEnvironment.None, Cancellation);
        var refreshing = main.SelectedTab!.IsAuthRefreshing;
        login.SetResult(FakeOAuthClient.Token);
        await fetching;

        // Assert
        Assert.True(refreshing);
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenTheFoldersAuthChanges_ThenUpdatesDraftsWithoutChangingTheirContent()
    {
        using var harness = new Harness(new FakeDialogs(answer: "People"));
        await harness.Library.SaveFolderAtAsync("Users", new() { Id = Guid.NewGuid(), Auth = new(AuthKind.Basic, "user") }, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(NodeOf(main, "Users"));
        var draft = main.SelectedTab!;
        draft.Editor.Body = "content";
        await draft.SendAsync();
        var response = draft.Result.Response;

        await harness.Library.SaveFolderAtAsync("Users", new() { Auth = new(AuthKind.OAuth2) }, Cancellation);
        await main.RequestsChangedAsync();
        await main.RenameFolderAsync(NodeOf(main, "Users"));
        await main.RequestsChangedAsync();

        Assert.True(draft.HasOAuth);
        Assert.Equal("People", draft.InheritedAuthFolder);
        Assert.Equal(harness.Translator.Format("Auth.InheritedFrom", "People"), draft.AuthSourceTip);
        Assert.Equal("content", draft.Editor.Body);
        Assert.Same(response, draft.Result.Response);
        Assert.True(draft.IsDirty);
    }

    [Fact]
    public async Task OpenAsync_WhenTheHistoryTabIsFetchingAToken_ThenKeepsIt()
    {
        // Arrange
        var login = new TaskCompletionSource<OAuthToken>();
        using var harness = new Harness(oauth: new FakeOAuthClient(cancellationToken => login.Task.WaitAsync(cancellationToken)));
        var main = harness.Main();
        await main.OpenAsync(HistoryItem("a.json"));
        _ = main.SelectedTab!.Auth.FetchTokenAsync();

        // Act
        await main.OpenAsync(HistoryItem("b.json"));

        // Assert
        Assert.Equal(2, main.Tabs.Count);
    }

    [Fact]
    public async Task OpenAsync_WhenTheHistoryRequestWasMoved_ThenUsesItsCurrentPath()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New();
        await harness.Library.SaveAtAsync("Admin/Get", request, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        var item = new HistoryItemViewModel(new("call.json", new(DateTimeOffset.Now, HistorySource.App, "dev.local", request, "Users/Get")), "Today");

        // Act
        await main.OpenAsync(item);

        // Assert
        Assert.Equal(("Get", "Admin"), (main.SelectedTab!.SuggestedName, main.Tree.PathOfFolder(main.SelectedTab.FolderId)));
    }

    [Fact]
    public async Task OpenAsync_WhenTheHistoryRequestIsGone_ThenUsesTheRecordedName()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        await main.LoadAsync();
        var item = new HistoryItemViewModel(new("call.json", new(DateTimeOffset.Now, HistorySource.App, "dev.local", ApiRequest.New() with { Name = "Get", FolderId = Guid.NewGuid() }, "Users/Get")), "Today");

        // Act
        await main.OpenAsync(item);

        // Assert
        Assert.Equal("Get", main.SelectedTab!.SuggestedName);
    }

    [Fact]
    public async Task Close_WhenATokenIsBeingFetched_ThenStopsTheLogin()
    {
        // Arrange
        var login = new TaskCompletionSource<OAuthToken>();
        using var harness = new Harness(oauth: new FakeOAuthClient(cancellationToken => login.Task.WaitAsync(cancellationToken)));
        var main = harness.Main();
        main.NewTab();
        var fetching = main.SelectedTab!.Auth.FetchTokenAsync();

        // Act
        main.Close(main.SelectedTab!);

        // Assert
        Assert.Null(await Record.ExceptionAsync(() => fetching.WaitAsync(TimeSpan.FromSeconds(5), Cancellation)));
    }

    [Fact]
    public void Close_WhenTheLastNewTabIsClosed_ThenTheFreshOneStartsAtOneAgain()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        main.NewTab();

        // Act
        main.Close(main.SelectedTab!);

        // Assert
        Assert.Equal("New request (1)", Assert.Single(main.Tabs).Title);
    }

    [Fact]
    public async Task EnvironmentChosen_WhenATabHasAResponse_ThenDoesNotShowItAgain()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        main.NewTab();
        main.SelectedTab!.Editor.Url = "https://dev.local";
        await main.SelectedTab.SendAsync();
        var formatting = main.SelectedTab.Result.Formatting;

        // Act
        main.EnvironmentChosen();

        // Assert
        Assert.Same(formatting, main.SelectedTab.Result.Formatting);
    }

    [Fact]
    public void EnvironmentChosen_WhenATabIsOpen_ThenTellsItsTokenChanged()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        main.NewTab();
        var changed = false;
        main.SelectedTab!.Auth.PropertyChanged += (_, e) => changed |= e.PropertyName == nameof(AuthViewModel.TokenStatus);

        // Act
        main.EnvironmentChosen();

        // Assert
        Assert.True(changed);
    }

    [Fact]
    public async Task OpenAsync_WhenTheRequestIsAlreadyOpen_ThenSelectsItsTab()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Ping", ApiRequest.New() with { Url = "https://dev.local" }, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        var node = main.Tree.Nodes.Single();
        await main.OpenAsync(node);
        main.NewTab();

        // Act
        await main.OpenAsync(node);

        // Assert
        Assert.Equal("Ping", main.SelectedTab?.Name);
    }

    [Fact]
    public async Task OpenAsync_WhenTheRequestIsAlreadyOpen_ThenOpensNoNewTab()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Ping", ApiRequest.New() with { Url = "https://dev.local" }, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        var node = main.Tree.Nodes.Single();
        await main.OpenAsync(node);
        main.NewTab();

        // Act
        await main.OpenAsync(node);

        // Assert
        Assert.Equal(3, main.Tabs.Count);
    }

    [Fact]
    public async Task OpenAsync_WhenOpenedTwiceAtOnce_ThenOpensOneTab()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        var node = main.Tree.Nodes.Single();

        // Act
        var first = main.OpenAsync(node);
        var second = main.OpenAsync(node);
        await first;
        await second;

        // Assert
        Assert.Single(main.Tabs, tab => tab.Name == "Ping");
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenAnOpenRequestChangedOnDisk_ThenReloadsIt()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New() with { Url = "https://dev.local" };
        await harness.Library.SaveAtAsync("Ping", request, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());
        await harness.Library.SaveAtAsync("Ping", request with { Url = "https://agent.local" }, Cancellation);

        // Act
        await main.RequestsChangedAsync();

        // Assert
        Assert.Equal("https://agent.local", main.SelectedTab?.Editor.Url);
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenTheTabSavedItself_ThenKeepsNewerEdits()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Ping", ApiRequest.New() with { Url = "https://dev.local" }, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());
        var tab = main.SelectedTab!;
        tab.Editor.Url = "https://saved.local";
        await tab.SaveAsync();
        tab.Editor.Url = "https://newer.local";

        // Act
        await main.RequestsChangedAsync();

        // Assert
        Assert.Equal("https://newer.local", tab.Editor.Url);
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenAnOpenRequestIsMovedOnDisk_ThenTheTabFollows()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());
        await harness.Library.RenameAtAsync("Ping", "Moved/Ping", Cancellation);

        // Act
        await main.RequestsChangedAsync();

        // Assert
        Assert.Equal("Moved/Ping", main.Tree.PathOf(main.SelectedTab!));
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenAnOpenRequestIsDeletedOnDisk_ThenTheTabIsNoLongerLinked()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());
        await harness.Library.DeleteAtAsync("Ping", Cancellation);

        // Act
        await main.RequestsChangedAsync();

        // Assert
        Assert.Null(main.SelectedTab!.Name);
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenAnOpenRequestIsDeletedOnDisk_ThenTheTabKeepsItsTitle()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());
        await harness.Library.DeleteAtAsync("Ping", Cancellation);

        // Act
        await main.RequestsChangedAsync();

        // Assert
        Assert.Equal("Ping", main.SelectedTab?.Title);
    }

    [Fact]
    public async Task Close_WhenATabInTheBackgroundIsClosed_ThenKeepsTheSelection()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        await main.LoadAsync();
        var selected = main.SelectedTab;
        main.NewTab();
        var background = main.SelectedTab!;
        main.SelectedTab = selected;

        // Act
        main.Close(background);

        // Assert
        Assert.Same(selected, main.SelectedTab);
    }

    [Fact]
    public async Task Close_WhenTheLastTabIsClosed_ThenOpensANewOne()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        await main.LoadAsync();
        var first = main.SelectedTab!;

        // Act
        main.Close(first);

        // Assert
        Assert.NotSame(first, Assert.Single(main.Tabs));
    }

    [Fact]
    public async Task Close_WhenTheTabIsUnsavedAndTheUserSaysNo_ThenKeepsIt()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: false));
        var main = harness.Main();
        await main.LoadAsync();
        var tab = main.SelectedTab!;
        tab.Editor.Url = "https://dev.local";

        // Act
        main.Close(tab);

        // Assert
        Assert.Same(tab, Assert.Single(main.Tabs));
    }

    [Fact]
    public async Task CanClose_WhenATabIsUnsaved_ThenAsksFirst()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: false));
        var main = harness.Main();
        await main.LoadAsync();
        main.SelectedTab!.Editor.Url = "https://dev.local";

        // Act
        main.CanClose();

        // Assert
        Assert.Equal(1, harness.Dialogs.Asked);
    }

    [Fact]
    public async Task CanClose_WhenATabIsUnsavedAndTheUserSaysNo_ThenIsFalse()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: false));
        var main = harness.Main();
        await main.LoadAsync();
        main.SelectedTab!.Editor.Url = "https://dev.local";

        // Act
        var canClose = main.CanClose();

        // Assert
        Assert.False(canClose);
    }

    [Theory]
    [InlineData("Users", true)]
    [InlineData("Other/Users: v2", true)]
    [InlineData("  ", false)]
    public async Task NewFolderAsync_WhenANameIsGiven_ThenMakesOneFolderWithIt(string name, bool accepted)
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: name));
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.NewFolderAsync();

        // Assert
        Assert.Equal(accepted ? [name] : Array.Empty<string>(), main.Tree.Nodes.Select(node => node.Name));
    }

    [Fact]
    public async Task NewFolderAsync_WhenTheNameIsTaken_ThenMakesAnotherFolderWithIt()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Users"));
        await harness.Library.FolderAtAsync("Users", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.NewFolderAsync();

        // Assert
        Assert.Null(harness.Dialogs.NameQuestion!.Value.ProblemOf("Users"));
        Assert.Equal(["Users", "Users"], main.Tree.Nodes.Select(node => node.Name));
    }

    [Fact]
    public async Task RenameAsync_WhenOnlyTheCaseChanges_ThenRenamesIt()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "PING"));
        await harness.Library.SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.RenameAsync(main.Tree.Nodes.Single());

        // Assert
        Assert.Equal(["PING"], await harness.Library.PathsAsync(Cancellation));
    }

    [Fact]
    public async Task RenameAsync_WhenTheRequestIsOpen_ThenTheTabFollows()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Renamed"));
        await harness.Library.SaveAtAsync("Health/Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(NodeOf(main, "Health/Ping"));

        // Act
        await main.RenameAsync(NodeOf(main, "Health/Ping"));

        // Assert
        Assert.Equal(("Renamed", "Health/Renamed"), (main.SelectedTab?.Name, main.Tree.PathOf(main.SelectedTab!)));
        Assert.Equal("Ping", harness.Dialogs.NameQuestion!.Value.Name);
    }

    [Fact]
    public async Task MoveAsync_WhenDroppedOnAFolder_ThenMovesTheRequestIntoIt()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        await harness.Library.FolderAtAsync("Health", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.MoveAsync(NodeOf(main, "Ping"), NodeOf(main, "Health"));

        // Assert
        Assert.Equal(["Health/Ping"], await harness.Library.PathsAsync(Cancellation));
    }

    [Fact]
    public async Task MoveAsync_WhenDroppedOnAClosedFolder_ThenOpensIt()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        await harness.Library.FolderAtAsync("Health", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.MoveAsync(NodeOf(main, "Ping"), NodeOf(main, "Health"));

        // Assert
        Assert.True(NodeOf(main, "Health").IsExpanded);
    }

    [Fact]
    public async Task MoveAsync_WhenDroppedOnARequestInAFolder_ThenMovesItIntoThatFolder()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAtAsync("Health/Status", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.MoveAsync(NodeOf(main, "Ping"), NodeOf(main, "Health/Status"));

        // Assert
        Assert.Equal(["Health/Ping", "Health/Status"], (await harness.Library.PathsAsync(Cancellation)).Order());
    }

    [Fact]
    public async Task MoveAsync_WhenDroppedOutsideTheFolders_ThenMovesItToTheTop()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Health/Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.MoveAsync(NodeOf(main, "Health/Ping"), null);

        // Assert
        Assert.Equal(["Ping"], await harness.Library.PathsAsync(Cancellation));
    }

    [Fact]
    public async Task MoveAsync_WhenDroppedInItsOwnFolder_ThenSaysNothing()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Health/Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.MoveAsync(NodeOf(main, "Health/Ping"), NodeOf(main, "Health"));

        // Assert
        Assert.Equal(0, harness.Dialogs.Asked);
    }

    [Fact]
    public async Task MoveAsync_WhenTheFolderHasARequestWithTheName_ThenMovesItAndKeepsBoth()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAtAsync("Health/Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.MoveAsync(NodeOf(main, "Ping"), NodeOf(main, "Health"));

        // Assert
        Assert.Equal(["Health/Ping", "Health/Ping"], await harness.Library.PathsAsync(Cancellation));
        Assert.Equal(0, harness.Dialogs.Asked);
    }

    [Fact]
    public async Task MoveAsync_WhenTheRequestInherits_ThenSendsWithTheNewFoldersAuth()
    {
        // Arrange
        using var harness = new Harness();
        var users = new RequestFolder { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) };
        var admin = new RequestFolder { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveFolderAtAsync("Users", users, Cancellation);
        await harness.Library.SaveFolderAtAsync("Admin", admin, Cancellation);
        await harness.Library.SaveAtAsync("Users/Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(NodeOf(main, "Users/Ping"));
        await main.MoveAsync(NodeOf(main, "Users/Ping"), NodeOf(main, "Admin"));

        // Act
        await main.SelectedTab!.SendAsync();

        // Assert
        Assert.Equal(admin.Id, harness.Sender.Auth?.SecretsId);
    }

    [Fact]
    public async Task MoveAsync_WhenTheRequestIsOpen_ThenTheTabFollows()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        await harness.Library.FolderAtAsync("Health", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(NodeOf(main, "Ping"));

        // Act
        await main.MoveAsync(NodeOf(main, "Ping"), NodeOf(main, "Health"));

        // Assert
        Assert.Equal("Health/Ping", main.Tree.PathOf(main.SelectedTab!));
    }

    [Fact]
    public async Task DeleteHistoryAsync_WhenTheFileCannotBeDeleted_ThenSaysSo()
    {
        // Arrange
        using var harness = new Harness();
        await harness.History().AddAsync(new(DateTimeOffset.Now, HistorySource.App, "dev.local", ApiRequest.New()), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        var item = main.History.Items.Single();
        using var locked = new FileStream(Path.Combine(harness.Folder.History, item.File.Name), FileMode.Open, FileAccess.Read, FileShare.Read);

        // Act
        await main.DeleteHistoryAsync(item);

        // Assert
        Assert.Equal(1, harness.Dialogs.Asked);
    }

    static RequestNodeViewModel NodeOf(MainViewModel main, string path) => RequestTreeViewModel.Flatten(main.Tree.Nodes).Single(node => main.Tree.PathOf(node) == path);

    [Theory]
    [InlineData("Users", "People")]
    [InlineData("Parent/Users", "Parent/People")]
    public async Task RenameFolderAsync_WhenGivenAName_ThenKeepsTheParentFolder(string original, string renamed)
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "People"));
        await harness.Library.SaveAtAsync($"{original}/Get", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.RenameFolderAsync(NodeOf(main, original));

        // Assert
        Assert.Equal([$"{renamed}/Get"], await harness.Library.PathsAsync(Cancellation));
        Assert.Equal("Users", harness.Dialogs.NameQuestion!.Value.Name);
    }

    [Fact]
    public async Task RenameFolderAsync_WhenARequestInItIsOpen_ThenTheTabFollows()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "People"));
        await harness.Library.SaveAtAsync("Users/Admin/List", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(NodeOf(main, "Users/Admin/List"));

        // Act
        await main.RenameFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.Equal(("People/Admin/List", "People / Admin /"), (main.Tree.PathOf(main.SelectedTab!), main.SelectedTab!.Folder));
    }

    [Fact]
    public async Task RenameFolderAsync_WhenItsFoldersAreOpen_ThenTheyStayOpen()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "People"));
        await harness.Library.SaveAtAsync("Users/Admin/List", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        NodeOf(main, "Users").IsExpanded = NodeOf(main, "Users/Admin").IsExpanded = true;

        // Act
        await main.RenameFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.Equal((true, true), (NodeOf(main, "People").IsExpanded, NodeOf(main, "People/Admin").IsExpanded));
    }

    [Fact]
    public async Task MoveAsync_WhenMovingAFolderIntoAClosedFolder_ThenOpensIt()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Users/Get", ApiRequest.New(), Cancellation);
        await harness.Library.FolderAtAsync("Archive", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.MoveAsync(NodeOf(main, "Users"), NodeOf(main, "Archive"));

        // Assert
        Assert.True(NodeOf(main, "Archive").IsExpanded);
    }

    [Theory]
    [InlineData("Taken", "Taken/Get")]
    [InlineData("  ", "Users/Get")]
    [InlineData("Other/Users", "Other/Users/Get")]
    public async Task RenameFolderAsync_WhenANameIsGiven_ThenRefusesOnlyAnEmptyName(string answer, string expected)
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: answer));
        await harness.Library.SaveAtAsync("Users/Get", ApiRequest.New(), Cancellation);
        await harness.Library.FolderAtAsync("Taken", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.RenameFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.Equal([expected], await harness.Library.PathsAsync(Cancellation));
    }

    [Fact]
    public async Task DeleteFolderAsync_WhenConfirmed_ThenDeletesTheFolderWithItsRequests()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        await harness.Library.SaveAtAsync("Users/Get", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAtAsync("Users/Admin/List", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.DeleteFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.Equal((0, false), ((await harness.Library.PathsAsync(Cancellation)).Count, harness.Library.FolderExistsAt("Users")));
    }

    [Fact]
    public async Task DeleteFolderAsync_WhenNotConfirmed_ThenKeepsTheFolder()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: false));
        await harness.Library.SaveAtAsync("Users/Get", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.DeleteFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.Equal(["Users/Get"], await harness.Library.PathsAsync(Cancellation));
    }

    [Fact]
    public async Task DeleteFolderAsync_WhenConfirmed_ThenDeletesTheSecretsOfItsRequests()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        var request = ApiRequest.New() with { Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveAtAsync("Users/Admin/List", request, Cancellation);
        await harness.Secrets.SaveAsync(request.Id, SecretKind.Token, "token", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.DeleteFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.Null(await harness.Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task DeleteFolderAsync_WhenConfirmed_ThenDeletesTheSecretsOfTheFoldersAuth()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        var admin = new RequestFolder { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveFolderAtAsync("Users/Admin", admin, Cancellation);
        await harness.Library.SaveAtAsync("Users/Admin/List", ApiRequest.New(), Cancellation);
        await harness.Secrets.SaveAsync(admin.Id, SecretKind.Token, "token", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.DeleteFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.Null(await harness.Secrets.OfAsync(admin.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task DeleteFolderAsync_WhenAFileCannotBeDeleted_ThenDeletesTheRest()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        await harness.Library.SaveAtAsync("Users/Get", ApiRequest.New(), Cancellation);
        var list = await harness.Library.SaveAtAsync("Users/Admin/List", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        using var locked = new FileStream(Path.Combine(harness.Folder.Requests, $"{list.Id}.json"), FileMode.Open, FileAccess.Read, FileShare.Read);

        // Act
        await main.DeleteFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.Equal(["Users/Admin/List"], await harness.Library.PathsAsync(Cancellation));
    }

    [Fact]
    public async Task DeleteFolderAsync_WhenAFileCannotBeDeleted_ThenDeletesOnlyTheSecretsOfWhatIsGone()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        var get = ApiRequest.New() with { Auth = new(AuthKind.Bearer) };
        var list = ApiRequest.New() with { Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveAtAsync("Users/Get", get, Cancellation);
        await harness.Library.SaveAtAsync("Users/Admin/List", list, Cancellation);
        await harness.Secrets.SaveAsync(get.Id, SecretKind.Token, "get", Cancellation);
        await harness.Secrets.SaveAsync(list.Id, SecretKind.Token, "list", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        using var locked = new FileStream(Path.Combine(harness.Folder.Requests, $"{list.Id}.json"), FileMode.Open, FileAccess.Read, FileShare.Read);

        // Act
        await main.DeleteFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.Equal((null, "list"), (await harness.Secrets.OfAsync(get.Id, SecretKind.Token, Cancellation), await harness.Secrets.OfAsync(list.Id, SecretKind.Token, Cancellation)));
    }

    [Fact]
    public async Task DeleteFolderAsync_WhenAFileCannotBeDeleted_ThenOnlyClosesTheTabsOfWhatIsGone()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        await harness.Library.SaveAtAsync("Users/Get", ApiRequest.New(), Cancellation);
        var saved = await harness.Library.SaveAtAsync("Users/Admin/List", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(NodeOf(main, "Users/Get"));
        var get = main.SelectedTab!;
        await main.OpenAsync(NodeOf(main, "Users/Admin/List"));
        var list = main.SelectedTab!;
        using var locked = new FileStream(Path.Combine(harness.Folder.Requests, $"{saved.Id}.json"), FileMode.Open, FileAccess.Read, FileShare.Read);

        // Act
        await main.DeleteFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.DoesNotContain(get, main.Tabs);
        Assert.Contains(list, main.Tabs);
        Assert.Equal("Users/Admin/List", main.Tree.PathOf(list));
    }

    [Fact]
    public async Task DeleteFolderAsync_WhenARequestInItIsOpen_ThenClosesItsTab()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        await harness.Library.SaveAtAsync("Users/Get", ApiRequest.New() with { Url = "https://dev.local" }, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(NodeOf(main, "Users").Children.Single());
        var tab = main.SelectedTab!;
        main.Close(main.Tabs.Single(open => open.Name is null));

        // Act
        await main.DeleteFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.DoesNotContain(tab, main.Tabs);
        Assert.NotSame(tab, main.SelectedTab);
        Assert.Null(Assert.Single(main.Tabs).Name);
        Assert.Equal(BodyKind.Json, main.SelectedTab!.Editor.BodyKind);
        Assert.Equal(RequestSection.Body, main.SelectedTab.RequestSection);
    }

    [Fact]
    public async Task DeleteAsync_WhenConfirmed_ThenDeletesTheRequest()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        await harness.Library.SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.DeleteAsync(main.Tree.Nodes.Single());

        // Assert
        Assert.Empty(await harness.Library.PathsAsync(Cancellation));
    }

    [Fact]
    public async Task DeleteAsync_WhenNotConfirmed_ThenKeepsTheRequest()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: false));
        await harness.Library.SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());
        var tab = main.SelectedTab!;
        tab.Editor.Body = "unsaved";

        // Act
        await main.DeleteAsync(main.Tree.Nodes.Single());

        // Assert
        Assert.Equal(["Ping"], await harness.Library.PathsAsync(Cancellation));
        Assert.Same(tab, main.SelectedTab);
        Assert.Equal("unsaved", tab.Editor.Body);
        Assert.Contains(tab, main.Tabs);
    }

    [Fact]
    public async Task DeleteAsync_WhenTheRequestIsOpen_ThenClosesItsTab()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        await harness.Library.SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());
        var tab = main.SelectedTab!;

        // Act
        await main.DeleteAsync(main.Tree.Nodes.Single());

        // Assert
        Assert.DoesNotContain(tab, main.Tabs);
        Assert.False(main.SelectedTab!.IsDirty);
    }

    [Fact]
    public async Task EditSettingsAsync_WhenCalled_ThenShowsTheSettings()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();

        // Act
        await main.EditSettingsAsync();

        // Assert
        Assert.IsType<SettingsViewModel>(harness.Dialogs.Shown);
    }

    [Fact]
    public async Task EditEnvironmentsAsync_WhenCalled_ThenShowsTheSavedEnvironments()
    {
        // Arrange
        using var harness = new Harness();
        await harness.EnvironmentStore.SaveAsync([new("Dev", [])], Cancellation);
        var main = harness.Main();

        // Act
        await main.EditEnvironmentsAsync();

        // Assert
        Assert.Equal("Dev", Assert.Single(Assert.IsType<EnvironmentEditorViewModel>(harness.Dialogs.Shown).Environments).Name);
    }

    [Fact]
    public async Task EditFolderAuthAsync_WhenCalled_ThenShowsTheFoldersAuth()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveFolderAtAsync("Users", new RequestFolder { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) }, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.EditFolderAuthAsync(main.Tree.Nodes.Single());

        // Assert
        Assert.Equal(AuthKind.Bearer, Assert.IsType<FolderAuthViewModel>(harness.Dialogs.Shown).Auth.Kind);
    }

    [Fact]
    public async Task DeleteAsync_WhenConfirmed_ThenDeletesTheSecrets()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        var request = ApiRequest.New();
        await harness.Library.SaveAtAsync("Ping", request, Cancellation);
        await harness.Secrets.SaveAsync(request.Id, SecretKind.Token, "token", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.DeleteAsync(main.Tree.Nodes.Single());

        // Assert
        Assert.Null(await harness.Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task DeleteAsync_WhenTheTabHasAToken_ThenClosesItAndRemovesTheSecret()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Ping", accept: true));
        var request = ApiRequest.New() with { Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveAtAsync("Ping", request, Cancellation);
        await harness.Secrets.SaveAsync(request.Id, SecretKind.Token, "token", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());
        var tab = main.SelectedTab!;
        // Act
        await main.DeleteAsync(main.Tree.Nodes.Single());

        // Assert
        Assert.DoesNotContain(tab, main.Tabs);
        Assert.False(harness.Library.ExistsAt("Ping"));
        Assert.Null(await harness.Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task Close_WhenTheTabIsSending_ThenCancelsIt()
    {
        // Arrange
        using var harness = new Harness(send: () => new TaskCompletionSource<ApiResponse>().Task);
        var main = harness.Main();
        await main.LoadAsync();
        var tab = main.SelectedTab!;
        var sending = tab.SendAsync();

        // Act
        main.Close(tab);
        await sending.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Assert
        Assert.False(tab.IsSending);
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenTheDeletedFileComesBack_ThenTheTabIsNotUnsaved()
    {
        // Arrange
        using var harness = new Harness();
        var request = await harness.Library.SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());
        var file = Path.Combine(harness.Folder.Requests, $"{request.Id}.json");
        var saved = File.ReadAllText(file);
        File.Delete(file);
        await main.RequestsChangedAsync();
        File.WriteAllText(file, saved);

        // Act
        await main.RequestsChangedAsync();

        // Assert
        Assert.False(main.SelectedTab!.IsDirty);
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenTheDeletedFileComesBackWhileAnotherTabHasItOpen_ThenLeavesItToThatTab()
    {
        // Arrange
        using var harness = new Harness();
        var request = await harness.Library.SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());
        var file = Path.Combine(harness.Folder.Requests, $"{request.Id}.json");
        var saved = File.ReadAllText(file);
        File.Delete(file);
        await main.RequestsChangedAsync();
        File.WriteAllText(file, saved);
        await main.Tree.LoadAsync(Cancellation);
        await main.OpenAsync(main.Tree.Nodes.Single());

        // Act
        await main.RequestsChangedAsync();

        // Assert
        Assert.Single(main.Tabs, tab => tab.IsSaved && tab.Id == request.Id);
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenAFileIsMovedOntoAnOpenTab_ThenTheMovedTabIsUnlinked()
    {
        // Arrange
        using var harness = new Harness();
        var a = await harness.Library.SaveAtAsync("A", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAtAsync("B", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single(node => main.Tree.PathOf(node) == "A"));
        await main.OpenAsync(main.Tree.Nodes.Single(node => main.Tree.PathOf(node) == "B"));
        var b = main.SelectedTab!;
        File.Move(Path.Combine(harness.Folder.Requests, $"{b.Id}.json"), Path.Combine(harness.Folder.Requests, $"{a.Id}.json"), overwrite: true);

        // Act
        await main.RequestsChangedAsync();

        // Assert
        Assert.Null(b.Name);
    }

    [Fact]
    public async Task Close_WhenTheSelectedTabIsClosed_ThenSelectsTheNextTab()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        await main.LoadAsync();
        var first = main.SelectedTab!;
        main.NewTab();
        var second = main.SelectedTab;
        main.SelectedTab = first;

        // Act
        main.Close(first);

        // Assert
        Assert.Same(second, main.SelectedTab);
    }

    [Fact]
    public async Task OpenAsync_WhenAHistoryItemIsOpened_ThenShowsItsResponse()
    {
        // Arrange
        using var harness = new Harness();
        var entry = new HistoryEntry(DateTimeOffset.Now, HistorySource.App, "dev.local", ApiRequest.New(), Response: new(200, "OK", 5, 2, [], "{}"));
        var main = harness.Main();

        // Act
        await main.OpenAsync(new HistoryItemViewModel(new("call.json", entry), "Today"));

        // Assert
        Assert.Equal("200 OK", main.SelectedTab?.Result.Response?.Status);
    }

    [Theory]
    [InlineData("Taken", "Taken")]
    [InlineData("  ", "Ping")]
    [InlineData("Other/Renamed: v2", "Other/Renamed: v2")]
    public async Task RenameAsync_WhenANameIsGiven_ThenRefusesOnlyAnEmptyName(string name, string expected)
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: name));
        await harness.Library.SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAtAsync("Taken", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.RenameAsync(main.Tree.Nodes.Single(node => main.Tree.PathOf(node) == "Ping"));

        // Assert
        Assert.Equal(new[] { expected, "Taken" }.Order(StringComparer.Ordinal), (await harness.Library.LoadAllAsync(Cancellation)).Requests.Select(request => request.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task OpenAsync_WhenTheHistoryItemIsAlreadyOpen_ThenSelectsItsTab()
    {
        // Arrange
        using var harness = new Harness();
        var entry = new HistoryEntry(DateTimeOffset.Now, HistorySource.App, "dev.local", ApiRequest.New());
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(new HistoryItemViewModel(new("call.json", entry), "Today"));
        var opened = main.SelectedTab;
        main.NewTab();

        // Act
        await main.OpenAsync(new HistoryItemViewModel(new("call.json", entry), "Today"));

        // Assert
        Assert.Same(opened, main.SelectedTab);
    }

    [Fact]
    public async Task OpenAsync_WhenTheHistoryItemIsAlreadyOpen_ThenOpensNoNewTab()
    {
        // Arrange
        using var harness = new Harness();
        var entry = new HistoryEntry(DateTimeOffset.Now, HistorySource.App, "dev.local", ApiRequest.New());
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(new HistoryItemViewModel(new("call.json", entry), "Today"));

        // Act
        await main.OpenAsync(new HistoryItemViewModel(new("call.json", entry), "Today"));

        // Assert
        Assert.Equal(2, main.Tabs.Count);
    }

    [Fact]
    public async Task OpenAsync_WhenTheOpenHistoryTabWasChanged_ThenOpensTheCallAgain()
    {
        // Arrange
        using var harness = new Harness();
        var entry = new HistoryEntry(DateTimeOffset.Now, HistorySource.App, "dev.local", ApiRequest.New() with { Url = "https://dev.local" });
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(new HistoryItemViewModel(new("call.json", entry), "Today"));
        main.SelectedTab!.Editor.Url = "https://changed.local";

        // Act
        await main.OpenAsync(new HistoryItemViewModel(new("call.json", entry), "Today"));

        // Assert
        Assert.Equal("https://dev.local", main.SelectedTab?.Editor.Url);
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenAnEditedRequestIsDeleted_ThenDoesNotReopenItsTab()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        var request = ApiRequest.New() with { Url = "https://a.local" };
        await harness.Library.SaveAtAsync("A", request, Cancellation);
        await harness.Library.SaveAtAsync("B", ApiRequest.New() with { Url = "https://b.local" }, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single(node => main.Tree.PathOf(node) == "A"));
        var tab = main.SelectedTab!;
        tab.Editor.Url = "https://edited.local";
        await main.DeleteAsync(main.Tree.Nodes.Single(node => main.Tree.PathOf(node) == "A"));

        // Act
        await main.RequestsChangedAsync();

        // Assert
        Assert.DoesNotContain(tab, main.Tabs);
        Assert.DoesNotContain(main.Tabs, open => open.Id == request.Id);
        Assert.Equal("https://b.local", (await harness.Library.LoadAtAsync("B", Cancellation))!.Url);
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenTheFileIsDeletedOnDisk_ThenTheTabIsUnlinked()
    {
        // Arrange
        using var harness = new Harness();
        var request = await harness.Library.SaveAtAsync("A", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAtAsync("B", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single(node => main.Tree.PathOf(node) == "A"));
        var tab = main.SelectedTab!;
        File.Delete(Path.Combine(harness.Folder.Requests, $"{request.Id}.json"));

        // Act
        await main.RequestsChangedAsync();

        // Assert
        Assert.Null(tab.Name);
    }

    [Fact]
    public async Task OpenAsync_WhenAHistoryItemIsOpenedTwiceAtOnce_ThenOpensOneTab()
    {
        // Arrange
        using var harness = new Harness();
        var entry = new HistoryEntry(DateTimeOffset.Now, HistorySource.App, "dev.local", ApiRequest.New(), Response: new(200, "OK", 5, 2, [], "{}"));
        var main = harness.Main();

        // Act
        var first = main.OpenAsync(new HistoryItemViewModel(new("call.json", entry), "Today"));
        var second = main.OpenAsync(new HistoryItemViewModel(new("call.json", entry), "Today"));
        await first;
        await second;

        // Assert
        Assert.Single(main.Tabs, tab => tab.HistoryName == "call.json");
    }

    [Fact]
    public async Task DeleteAsync_WhenAHistoryTabUsesTheRequest_ThenItSavesItsSecretsAgainWhenSending()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        var request = ApiRequest.New() with { Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveAtAsync("Ping", request, Cancellation);
        await harness.Secrets.SaveAsync(request.Id, SecretKind.Token, "secret", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(new HistoryItemViewModel(new("call.json", new(DateTimeOffset.Now, HistorySource.App, "dev.local", request, "Ping")), "Today"));
        var tab = main.SelectedTab!;
        await main.DeleteAsync(main.Tree.Nodes.Single());

        // Act
        await tab.SendAsync();

        // Assert
        Assert.Equal("secret", await harness.Secrets.OfAsync(harness.Sender.Auth!.SecretsId, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenTheFileHasABlankHeader_ThenTheTabStaysSaved()
    {
        // Arrange
        using var harness = new Harness();
        Directory.CreateDirectory(harness.Folder.Requests);
        File.WriteAllText(Path.Combine(harness.Folder.Requests, $"{Guid.NewGuid()}.json"), """{"name": "Ping", "url": "https://dev.local", "headers": [{"name": ""}]}""");
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());

        // Act
        await main.RequestsChangedAsync();

        // Assert
        Assert.False(main.SelectedTab!.IsDirty);
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenTheFileHasABlankHeader_ThenKeepsTheEdits()
    {
        // Arrange
        using var harness = new Harness();
        Directory.CreateDirectory(harness.Folder.Requests);
        File.WriteAllText(Path.Combine(harness.Folder.Requests, $"{Guid.NewGuid()}.json"), """{"name": "Ping", "url": "https://dev.local", "headers": [{"name": ""}]}""");
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());
        main.SelectedTab!.Editor.Url = "https://edited.local";

        // Act
        await main.RequestsChangedAsync();

        // Assert
        Assert.Equal("https://edited.local", main.SelectedTab!.Editor.Url);
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenTheTabSavedItselfAndWasEditedAfter_ThenStaysUnsaved()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Ping", ApiRequest.New() with { Url = "https://dev.local" }, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());
        var tab = main.SelectedTab!;
        tab.Editor.Url = "https://saved.local";
        await tab.SaveAsync();
        tab.Editor.Url = "https://newer.local";

        // Act
        await main.RequestsChangedAsync();

        // Assert
        Assert.True(tab.IsDirty);
    }

    [Fact]
    public async Task OpenAsync_WhenAnotherHistoryItemIsOpen_ThenOpensTheClickedOne()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        await main.OpenAsync(new HistoryItemViewModel(new("a.json", new(DateTimeOffset.Now, HistorySource.App, "a.local", ApiRequest.New() with { Url = "https://a.local" })), "Today"));

        // Act
        await main.OpenAsync(new HistoryItemViewModel(new("b.json", new(DateTimeOffset.Now, HistorySource.App, "b.local", ApiRequest.New() with { Url = "https://b.local" })), "Today"));

        // Assert
        Assert.Equal("https://b.local", main.SelectedTab?.Editor.Url);
    }

    [Fact]
    public async Task OpenAsync_WhenAnUntouchedHistoryTabIsOpen_ThenReplacesIt()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        await main.OpenAsync(HistoryItem("a.json"));

        // Act
        await main.OpenAsync(HistoryItem("b.json"));

        // Assert
        Assert.Equal(["b.json"], main.Tabs.Select(tab => tab.HistoryName));
    }

    [Fact]
    public async Task OpenAsync_WhenAnUntouchedHistoryTabIsReplaced_ThenTheNewOneTakesItsPlace()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        main.NewTab();
        await main.OpenAsync(HistoryItem("a.json"));
        main.NewTab();

        // Act
        await main.OpenAsync(HistoryItem("b.json"));

        // Assert
        Assert.Equal([null, "b.json", null], main.Tabs.Select(tab => tab.HistoryName));
    }

    [Fact]
    public async Task OpenAsync_WhenTheHistoryTabIsPinned_ThenKeepsIt()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        await main.OpenAsync(HistoryItem("a.json"));
        main.SelectedTab!.Pin();

        // Act
        await main.OpenAsync(HistoryItem("b.json"));

        // Assert
        Assert.Equal(["a.json", "b.json"], main.Tabs.Select(tab => tab.HistoryName));
    }

    [Fact]
    public async Task OpenAsync_WhenTheHistoryTabWasChanged_ThenKeepsIt()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        await main.OpenAsync(HistoryItem("a.json"));
        main.SelectedTab!.Editor.Url = "https://changed.local";

        // Act
        await main.OpenAsync(HistoryItem("b.json"));

        // Assert
        Assert.Equal(["https://changed.local", "https://b.local"], main.Tabs.Select(tab => tab.Editor.Url));
    }

    [Fact]
    public async Task OpenAsync_WhenTheHistoryTabWasSaved_ThenKeepsIt()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Saved"));
        var main = harness.Main();
        await main.OpenAsync(HistoryItem("a.json"));
        await main.SelectedTab!.SaveAsync();

        // Act
        await main.OpenAsync(HistoryItem("b.json"));

        // Assert
        Assert.Equal(["Saved", null], main.Tabs.Select(tab => tab.Name));
    }

    [Fact]
    public async Task OpenAsync_WhenTheHistoryTabWasSent_ThenKeepsIt()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        await main.OpenAsync(HistoryItem("a.json"));
        await main.SelectedTab!.SendAsync();

        // Act
        await main.OpenAsync(HistoryItem("b.json"));

        // Assert
        Assert.Equal(2, main.Tabs.Count);
    }

    [Fact]
    public async Task DeleteAsync_WhenTheTabHasAPassword_ThenClosesItAndRemovesTheSecret()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Ping", accept: true));
        var request = ApiRequest.New() with { Auth = new(AuthKind.Basic, "hobo") };
        await harness.Library.SaveAtAsync("Ping", request, Cancellation);
        await harness.Secrets.SaveAsync(request.Id, SecretKind.Password, "hemmelig", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());
        var tab = main.SelectedTab!;
        // Act
        await main.DeleteAsync(main.Tree.Nodes.Single());

        // Assert
        Assert.DoesNotContain(tab, main.Tabs);
        Assert.False(harness.Library.ExistsAt("Ping"));
        Assert.Null(await harness.Secrets.OfAsync(request.Id, SecretKind.Password, Cancellation));
    }

    [Fact]
    public void NewTab_WhenTwoTabsAreOpened_ThenNumbersThem()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();

        // Act
        main.NewTab();
        main.NewTab();

        // Assert
        Assert.Equal(["New request (1)", "New request (2)"], main.Tabs.Select(tab => tab.Title));
    }

    [Fact]
    public async Task OpenAsync_WhenTheHistoryCallHasNoName_ThenNumbersItsTab()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        main.NewTab();

        // Act
        await main.OpenAsync(HistoryItem("a.json"));

        // Assert
        Assert.Equal("New request (2)", main.SelectedTab?.Title);
    }

    [Fact]
    public async Task LanguageChangedAsync_WhenATabIsOpen_ThenTellsItsTitleChanged()
    {
        // Arrange
        using var harness = new Harness();
        var main = harness.Main();
        main.NewTab();
        var changed = false;
        main.SelectedTab!.PropertyChanged += (_, e) => changed |= e.PropertyName == nameof(RequestTabViewModel.Title);

        // Act
        await main.LanguageChangedAsync();

        // Assert
        Assert.True(changed);
    }

    [Fact]
    public async Task LanguageChangedAsync_WhenARequestAndAWorkflowCannotBeRead_ThenShowsThemInTheNewLanguage()
    {
        // Arrange
        using var harness = new Harness();
        var (request, workflow) = (Guid.NewGuid(), Guid.NewGuid());
        Directory.CreateDirectory(harness.Folder.Requests);
        File.WriteAllText(Path.Combine(harness.Folder.Requests, $"{request}.json"), "{");
        Directory.CreateDirectory(Path.Combine(harness.Folder.Workflows, $"{workflow}"));
        File.WriteAllText(Path.Combine(harness.Folder.Workflows, $"{workflow}", "workflow.json"), "{");
        var main = harness.Main();
        await main.LoadAsync();
        harness.Translator.Use(Translation.Danish);

        // Act
        await main.LanguageChangedAsync();

        // Assert
        Assert.Equal(Translation.Danish.Format("Tree.Unreadable", $"{request}"[..8]), Assert.Single(main.Tree.Nodes).Name);
        Assert.Equal(Translation.Danish.Format("Tree.Unreadable", $"{workflow}"[..8]), Assert.Single(main.Workflows.Items).Name);
    }

    [Fact]
    public async Task RenameAsync_WhenRenamed_ThenKeepsItsPlace()
    {
        // Arrange
        const string name = "B";
        using var harness = new Harness(new FakeDialogs(answer: "Renamed"));
        await harness.Library.SaveAtAsync("A", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAtAsync(name, ApiRequest.New(), Cancellation);
        await harness.Library.SaveOrderAtAsync([name, "A"], Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.RenameAsync(NodeOf(main, name));

        // Assert
        Assert.Equal(["Renamed", "A"], main.Tree.Nodes.Select(main.Tree.PathOf));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanMove_WhenSearchingOrNot_ThenAllowsAMoveOnlyWhenNot(bool searching)
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Users/Get", ApiRequest.New(), Cancellation);
        await harness.Library.FolderAtAsync("Admin", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        main.Tree.Search = searching ? "Get" : "";

        // Act
        var canMove = main.CanMove(NodeOf(main, "Users/Get"), NodeOf(main, "Admin"), DropPosition.Inside);

        // Assert
        Assert.Equal(!searching, canMove);
    }

    [Fact]
    public async Task LoadAsync_WhenWholeFoldersWereTurnedOff_ThenTheSearchStartsWithThemOff()
    {
        // Arrange
        using var harness = new Harness();
        await harness.SettingsStore.UpdateAsync(settings => settings with { SearchWholeFolders = false }, Cancellation);
        var main = harness.Main();

        // Act
        await main.LoadAsync();

        // Assert
        Assert.False(main.Tree.ShowWholeFolders);
    }

    [Fact]
    public async Task LoadAsync_WhenASessionRequestWasRenamedOnDiskWhileClosed_ThenReopensItsTab()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("A", ApiRequest.New(), Cancellation);
        var closed = harness.Main();
        await closed.LoadAsync();
        await closed.OpenAsync(NodeOf(closed, "A"));
        await harness.SettingsStore.UpdateAsync(settings => settings with { Session = closed.Session }, Cancellation);
        await harness.Library.RenameAtAsync("A", "B", Cancellation);
        var main = harness.Main();

        // Act
        await main.LoadAsync();

        // Assert
        Assert.Equal("B", Assert.Single(main.Tabs).Name);
        Assert.Same(main.Tabs[0], main.SelectedTab);
    }

    // "a.json" is a call to https://a.local.
    static HistoryItemViewModel HistoryItem(string file)
    {
        var address = $"{Path.GetFileNameWithoutExtension(file)}.local";
        return new(new(file, new(DateTimeOffset.Now, HistorySource.App, address, ApiRequest.New() with { Url = $"https://{address}" })), "Today");
    }
}
