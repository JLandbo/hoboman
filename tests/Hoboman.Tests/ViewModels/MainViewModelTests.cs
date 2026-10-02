using Hoboman.Tests.Auth;

namespace Hoboman.Tests.ViewModels;

public sealed class MainViewModelTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteAsync_WhenADraftWasJustSaved_ThenDeletesItsSecretsWithoutWaitingForTheWatcher(bool deleteFolder)
    {
        using var harness = new Harness(new FakeDialogs(answer: "Users/Saved", accept: true));
        await harness.Library.CreateFolderAsync("Users", Cancellation);
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

        Assert.False(harness.Library.Exists("Users/Saved"));
        Assert.Null(await harness.Secrets.OfAsync(tab.Id, SecretKind.Password, Cancellation));
    }

    [Fact]
    public async Task Close_WhenTheMethodChangedDuringADraftsFirstSave_ThenShowsTheMethodWrittenToDisk()
    {
        using var harness = new Harness(new FakeDialogs(answer: "Users/Saved", accept: true));
        await harness.Library.CreateFolderAsync("Users", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(NodeOf(main, "Users"));
        var tab = main.SelectedTab!;
        Task saving;
        using (new FileStream(Path.Combine(harness.Folder.Requests, "Users", "Saved.json.tmp"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            saving = tab.SaveAsync();
            tab.Method = "POST";
        }
        await saving;
        Assert.True(tab.IsDirty);
        Assert.Equal("POST", tab.Method);

        main.Close(tab);

        Assert.Equal("GET", (await harness.Library.LoadAsync("Users/Saved", Cancellation))?.Method);
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
        await harness.Library.CreateFolderAsync(destination, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        main.NewTab();
        var global = main.SelectedTab;

        await main.NewDraftAsync(NodeOf(main, destination));
        var first = main.SelectedTab!;
        await main.NewDraftAsync(NodeOf(main, destination));
        var second = main.SelectedTab!;

        Assert.Equal((3, 4), (first.Number, second.Number));
        Assert.Equal(destination, second.Destination);
        Assert.Equal((true, false, true, AuthKind.Inherit), (second.IsDraft, second.IsDirty, second.IsUnsaved, second.Auth.Kind));
        Assert.Equal([first, second], NodeOf(main, destination).Children.Select(node => node.Tab));
        Assert.True(NodeOf(main, destination).IsExpanded);
        Assert.False(global!.IsDraft);
        Assert.Equal(0, harness.Dialogs.Asked);
        Assert.Empty(await harness.Library.NamesAsync(Cancellation));
        main.Close(second);
        Assert.Same(first, main.SelectedTab);
        Assert.Same(first, Assert.Single(NodeOf(main, destination).Children).Tab);
        Assert.Equal(0, harness.Dialogs.Asked);
    }

    [Theory]
    [InlineData(" Admin ", null)]
    [InlineData("", "Save.Invalid")]
    [InlineData("   ", "Save.Invalid")]
    [InlineData("../Other", "Save.Invalid")]
    [InlineData("One/Two", "Save.Invalid")]
    [InlineData("Bad?", "Save.Invalid")]
    [InlineData("taken", "Folder.Exists")]
    [InlineData(null, null)]
    public async Task NewSubfolderAsync_WhenGivenAName_ThenValidatesAndCreatesOnlyOneChild(string? name, string? problem)
    {
        using var harness = new Harness(new FakeDialogs(answer: name));
        await harness.Library.CreateFolderAsync("Users/Private/Taken", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        await main.NewSubfolderAsync(NodeOf(main, "Users/Private"));

        var created = name is not null && problem is null;
        var question = harness.Dialogs.NameQuestion!.Value;
        Assert.Equal(("New folder in Users/Private", "", "Create", false), (question.Title, question.Name, question.Confirm, question.SelectLastPart));
        if (problem is not null)
        {
            Assert.Equal(harness.Translator.Of(problem), question.ProblemOf(name!));
        }
        Assert.Equal(created, harness.Library.FolderExists("Users/Private/Admin"));
        Assert.Equal(created, NodeOf(main, "Users/Private").IsExpanded);
        Assert.Equal(created ? 4 : 3, (await harness.Library.FoldersAsync(Cancellation)).Count);
    }

    [Theory]
    [InlineData("Archive/People")]
    [InlineData("People")]
    [InlineData("users")]
    public async Task RenameFolderAsync_WhenItContainsDrafts_ThenMovesOnlyTheirDestinations(string name)
    {
        using var harness = new Harness(new FakeDialogs(answer: name));
        await harness.Library.CreateFolderAsync("Users/Admin", Cancellation);
        await harness.Library.CreateFolderAsync("Users2", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(NodeOf(main, "Users"));
        var first = main.SelectedTab!;
        await main.NewDraftAsync(NodeOf(main, "Users/Admin"));
        var second = main.SelectedTab!;
        second.Body = "content";
        await main.NewDraftAsync(NodeOf(main, "Users2"));
        var other = main.SelectedTab!;
        main.SelectedTab = second;
        var titles = main.Tabs.Select(tab => tab.Title).ToList();

        await main.RenameFolderAsync(NodeOf(main, "Users"));
        await main.RequestsChangedAsync();

        Assert.Equal((name, $"{name}/Admin", "Users2"), (first.Destination, second.Destination, other.Destination));
        Assert.Equal(titles, main.Tabs.Select(tab => tab.Title));
        Assert.Equal("content", second.Body);
        Assert.True(second.IsDirty);
        Assert.Same(second, Assert.Single(RequestTreeViewModel.Flatten(main.Tree.Nodes), node => node.IsActive).Tab);
        Assert.Equal(3, RequestTreeViewModel.Flatten(main.Tree.Nodes).Count(node => node.IsDraft));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteFolderAsync_WhenItContainsDrafts_ThenListsThemAndKeepsTheirContentAfterConfirmation(bool accept)
    {
        using var harness = new Harness(new FakeDialogs(accept: accept));
        await harness.Library.SaveAsync("Users/Admin/Get", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAsync("Users/Clean", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(NodeOf(main, "Users"));
        var untouched = main.SelectedTab!;
        await main.OpenAsync(NodeOf(main, "Users/Admin/Get"));
        var saved = main.SelectedTab!;
        saved.Body = "saved edit";
        await main.OpenAsync(NodeOf(main, "Users/Clean"));
        var clean = main.SelectedTab!;
        await main.NewDraftAsync(NodeOf(main, "Users/Admin"));
        var draft = main.SelectedTab!;
        draft.Body = "draft content";
        var tabs = main.Tabs.ToList();

        await main.DeleteFolderAsync(NodeOf(main, "Users"));

        Assert.Equal(tabs, main.Tabs);
        Assert.Same(draft, main.SelectedTab);
        Assert.Equal("draft content", draft.Body);
        Assert.Equal("saved edit", saved.Body);
        Assert.Equal([untouched.Title, saved.Title, draft.Title], harness.Dialogs.ConfirmQuestion!.Value.Items);
        Assert.Equal(harness.Translator.Format("DeleteFolder.Message", "Users", 2), harness.Dialogs.ConfirmQuestion.Value.Message);
        Assert.Equal(!accept, draft.IsDraft);
        Assert.Equal(!accept, untouched.IsDraft);
        Assert.Equal(accept, untouched.IsDirty);
        Assert.Equal(accept ? null : "Users/Admin", draft.Destination);
        Assert.Equal(accept ? null : "Users/Clean", clean.Name);
        Assert.Equal(accept ? 0 : 2, RequestTreeViewModel.Flatten(main.Tree.Nodes).Count(node => node.IsDraft));
        Assert.Equal(accept ? null : "Users / Admin /", draft.Folder);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Close_WhenATabIsADraft_ThenOnlyPromptsForEdits(bool edited, bool accept)
    {
        using var harness = new Harness(new FakeDialogs(accept: accept));
        await harness.Library.CreateFolderAsync("Users", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(NodeOf(main, "Users"));
        var draft = main.SelectedTab!;
        if (edited)
        {
            draft.Body = "content";
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
        Assert.Empty(await harness.Library.NamesAsync(Cancellation));
    }

    [Fact]
    public async Task OpenAsync_WhenDraftsAndFilesShareAName_ThenOpensTheCorrectTabWithoutDuplicates()
    {
        using var harness = new Harness();
        await harness.Library.SaveAsync("Users/New request (2)", ApiRequest.New(), Cancellation);
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
        Assert.Null(main.SelectedTab.Destination);
        Assert.DoesNotContain(RequestTreeViewModel.Flatten(main.Tree.Nodes), node => node.IsActive);
        Assert.Single(RequestTreeViewModel.Flatten(main.Tree.Nodes), node => node.IsDraft);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MoveAsync_WhenDroppingOnADraft_ThenMovesTheFileToItsDestination(bool root)
    {
        using var harness = new Harness();
        await harness.Library.CreateFolderAsync("Users", Cancellation);
        await harness.Library.SaveAsync("Other/Get", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(NodeOf(main, "Users"));
        var draft = main.SelectedTab!;
        if (root)
        {
            await harness.Library.DeleteFolderAsync("Users", Cancellation);
            await main.RequestsChangedAsync();
        }
        var row = Assert.Single(RequestTreeViewModel.Flatten(main.Tree.Nodes), node => node.IsDraft);

        await main.MoveAsync(NodeOf(main, "Other/Get"), row);

        Assert.Equal([root ? "Get" : "Users/Get"], await harness.Library.NamesAsync(Cancellation));
        Assert.Same(draft, Assert.Single(RequestTreeViewModel.Flatten(main.Tree.Nodes), node => node.IsDraft).Tab);
        Assert.True(draft.IsUnsaved);
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public async Task DeleteFolderAsync_WhenOnlyPartCanBeDeleted_ThenOnlyUnlinksDraftsWhoseFoldersAreGone()
    {
        using var harness = new Harness(new FakeDialogs(accept: true));
        await harness.Library.CreateFolderAsync("Users/Empty", Cancellation);
        await harness.Library.SaveAsync("Users/Locked/Get", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(NodeOf(main, "Users/Empty"));
        var empty = main.SelectedTab!;
        await main.NewDraftAsync(NodeOf(main, "Users/Locked"));
        var locked = main.SelectedTab!;
        using var file = new FileStream(Path.Combine(harness.Folder.Requests, "Users", "Locked", "Get.json"), FileMode.Open, FileAccess.Read, FileShare.Read);

        await main.DeleteFolderAsync(NodeOf(main, "Users"));

        Assert.Equal(harness.Library.FolderExists("Users/Empty"), empty.IsDraft);
        Assert.Equal(harness.Library.FolderExists("Users/Locked"), locked.IsDraft);
        Assert.True(locked.IsDraft);
        Assert.Equal("Users/Locked", locked.Destination);
        Assert.Equal(2, harness.Dialogs.Asked);
        Assert.Same(locked, main.SelectedTab);
        Assert.Equal(main.Tabs.Count(tab => tab.IsDraft), RequestTreeViewModel.Flatten(main.Tree.Nodes).Count(node => node.IsDraft));
    }

    [Fact]
    public async Task NewSubfolderAsync_WhenCreationFails_ThenReportsItWithoutExpandingTheParent()
    {
        using var harness = new Harness(new FakeDialogs(answer: "Child"));
        await harness.Library.CreateFolderAsync("Users", Cancellation);
        File.WriteAllText(Path.Combine(harness.Folder.Requests, "Users", "Child"), "blocked");
        var main = harness.Main();
        await main.LoadAsync();

        await main.NewSubfolderAsync(NodeOf(main, "Users"));

        Assert.False(NodeOf(main, "Users").IsExpanded);
        Assert.False(harness.Library.FolderExists("Users/Child"));
        Assert.Equal(2, harness.Dialogs.Asked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Taken")]
    public async Task RenameFolderAsync_WhenCancelledOrRejected_ThenLeavesDraftsWhereTheyAre(string? answer)
    {
        using var harness = new Harness(new FakeDialogs(answer: answer));
        await harness.Library.CreateFolderAsync("Users/Admin", Cancellation);
        await harness.Library.CreateFolderAsync("Taken", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(NodeOf(main, "Users/Admin"));
        var draft = main.SelectedTab!;

        await main.RenameFolderAsync(NodeOf(main, "Users"));

        Assert.Equal("Users/Admin", draft.Destination);
        Assert.Same(draft, Assert.Single(RequestTreeViewModel.Flatten(main.Tree.Nodes), node => node.IsDraft).Tab);
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenTheFoldersAuthChanges_ThenUpdatesDraftsWithoutChangingTheirContent()
    {
        using var harness = new Harness(new FakeDialogs(answer: "People"));
        await harness.Library.SaveFolderAsync("Users", new() { Id = Guid.NewGuid(), Auth = new(AuthKind.Basic, "user") }, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(NodeOf(main, "Users"));
        var draft = main.SelectedTab!;
        draft.Body = "content";
        await draft.SendAsync();
        var response = draft.Response;

        await harness.Library.SaveFolderAsync("Users", new() { Id = Guid.NewGuid(), Auth = new(AuthKind.OAuth2) }, Cancellation);
        await main.RequestsChangedAsync();
        await main.RenameFolderAsync(NodeOf(main, "Users"));
        await main.RequestsChangedAsync();

        Assert.True(draft.HasOAuth);
        Assert.Equal("People", draft.InheritedAuthFolder);
        Assert.Equal(harness.Translator.Format("Auth.InheritedFrom", "People"), draft.AuthSourceTip);
        Assert.Equal("content", draft.Body);
        Assert.Same(response, draft.Response);
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
        await harness.Library.SaveAsync("Ping", ApiRequest.New() with { Url = "https://dev.local" }, Cancellation);
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
        await harness.Library.SaveAsync("Ping", ApiRequest.New() with { Url = "https://dev.local" }, Cancellation);
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
        await harness.Library.SaveAsync("Ping", ApiRequest.New(), Cancellation);
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
        await harness.Library.SaveAsync("Ping", request, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
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
        await main.LoadAsync();
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
    public async Task RequestsChangedAsync_WhenAnOpenRequestIsMovedOnDisk_ThenTheTabFollows()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAsync("Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());
        await harness.Library.RenameAsync("Ping", "Moved/Ping", Cancellation);

        // Act
        await main.RequestsChangedAsync();

        // Assert
        Assert.Equal("Moved/Ping", main.SelectedTab?.Name);
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenAnOpenRequestIsDeletedOnDisk_ThenTheTabIsNoLongerLinked()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAsync("Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());
        await harness.Library.DeleteAsync("Ping", Cancellation);

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
        await harness.Library.SaveAsync("Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());
        await harness.Library.DeleteAsync("Ping", Cancellation);

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
        tab.Url = "https://dev.local";

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
        main.SelectedTab!.Url = "https://dev.local";

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
        main.SelectedTab!.Url = "https://dev.local";

        // Act
        var canClose = main.CanClose();

        // Assert
        Assert.False(canClose);
    }

    [Fact]
    public async Task NewFolderAsync_WhenANameIsGiven_ThenShowsTheFolder()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Users"));
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.NewFolderAsync();

        // Assert
        Assert.Equal("Users", Assert.Single(main.Tree.Nodes).Path);
    }

    [Fact]
    public async Task RenameAsync_WhenOnlyTheCaseChanges_ThenRenamesTheFile()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "PING"));
        await harness.Library.SaveAsync("Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.RenameAsync(main.Tree.Nodes.Single());

        // Assert
        Assert.Equal(["PING"], await harness.Library.NamesAsync(Cancellation));
    }

    [Fact]
    public async Task RenameAsync_WhenTheRequestIsOpen_ThenTheTabFollows()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Health/Ping"));
        await harness.Library.SaveAsync("Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());

        // Act
        await main.RenameAsync(main.Tree.Nodes.Single());

        // Assert
        Assert.Equal("Health/Ping", main.SelectedTab?.Name);
    }

    [Fact]
    public async Task MoveAsync_WhenDroppedOnAFolder_ThenMovesTheRequestIntoIt()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAsync("Ping", ApiRequest.New(), Cancellation);
        await harness.Library.CreateFolderAsync("Health", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.MoveAsync(NodeOf(main, "Ping"), NodeOf(main, "Health"));

        // Assert
        Assert.Equal(["Health/Ping"], await harness.Library.NamesAsync(Cancellation));
    }

    [Fact]
    public async Task MoveAsync_WhenDroppedOnAClosedFolder_ThenOpensIt()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAsync("Ping", ApiRequest.New(), Cancellation);
        await harness.Library.CreateFolderAsync("Health", Cancellation);
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
        await harness.Library.SaveAsync("Ping", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAsync("Health/Status", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.MoveAsync(NodeOf(main, "Ping"), NodeOf(main, "Health/Status"));

        // Assert
        Assert.Equal(["Health/Ping", "Health/Status"], (await harness.Library.NamesAsync(Cancellation)).Order());
    }

    [Fact]
    public async Task MoveAsync_WhenDroppedOutsideTheFolders_ThenMovesItToTheTop()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAsync("Health/Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.MoveAsync(NodeOf(main, "Health/Ping"), null);

        // Assert
        Assert.Equal(["Ping"], await harness.Library.NamesAsync(Cancellation));
    }

    [Fact]
    public async Task MoveAsync_WhenDroppedInItsOwnFolder_ThenSaysNothing()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAsync("Health/Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.MoveAsync(NodeOf(main, "Health/Ping"), NodeOf(main, "Health"));

        // Assert
        Assert.Equal(0, harness.Dialogs.Asked);
    }

    [Fact]
    public async Task MoveAsync_WhenTheFolderHasARequestWithTheName_ThenKeepsBothAndSaysSo()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAsync("Ping", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAsync("Health/Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.MoveAsync(NodeOf(main, "Ping"), NodeOf(main, "Health"));

        // Assert
        Assert.Equal((2, 1), ((await harness.Library.NamesAsync(Cancellation)).Count, harness.Dialogs.Asked));
    }

    [Fact]
    public async Task MoveAsync_WhenTheRequestInherits_ThenSendsWithTheNewFoldersAuth()
    {
        // Arrange
        using var harness = new Harness();
        var users = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) };
        var admin = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveAsync("Users/Ping", ApiRequest.New(), Cancellation);
        await harness.Library.SaveFolderAsync("Users", users, Cancellation);
        await harness.Library.SaveFolderAsync("Admin", admin, Cancellation);
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
        await harness.Library.SaveAsync("Ping", ApiRequest.New(), Cancellation);
        await harness.Library.CreateFolderAsync("Health", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(NodeOf(main, "Ping"));

        // Act
        await main.MoveAsync(NodeOf(main, "Ping"), NodeOf(main, "Health"));

        // Assert
        Assert.Equal("Health/Ping", main.SelectedTab?.Name);
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

    static RequestNodeViewModel NodeOf(MainViewModel main, string path) => RequestTreeViewModel.Flatten(main.Tree.Nodes).Single(node => node.Path == path);

    [Fact]
    public async Task RenameFolderAsync_WhenGivenAName_ThenMovesTheFolder()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "People"));
        await harness.Library.SaveAsync("Users/Get", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.RenameFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.Equal(["People/Get"], await harness.Library.NamesAsync(Cancellation));
    }

    [Fact]
    public async Task RenameFolderAsync_WhenARequestInItIsOpen_ThenTheTabFollows()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "People"));
        await harness.Library.SaveAsync("Users/Admin/List", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(NodeOf(main, "Users/Admin/List"));

        // Act
        await main.RenameFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.Equal("People/Admin/List", main.SelectedTab?.Name);
    }

    [Fact]
    public async Task RenameFolderAsync_WhenItsFoldersAreOpen_ThenTheyStayOpen()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "People"));
        await harness.Library.SaveAsync("Users/Admin/List", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        NodeOf(main, "Users").IsExpanded = NodeOf(main, "Users/Admin").IsExpanded = true;

        // Act
        await main.RenameFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.Equal((true, true), (NodeOf(main, "People").IsExpanded, NodeOf(main, "People/Admin").IsExpanded));
    }

    [Fact]
    public async Task RenameFolderAsync_WhenMovedIntoAClosedFolder_ThenOpensIt()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Archive/Users"));
        await harness.Library.SaveAsync("Users/Get", ApiRequest.New(), Cancellation);
        await harness.Library.CreateFolderAsync("Archive", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.RenameFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.True(NodeOf(main, "Archive").IsExpanded);
    }

    [Theory]
    [InlineData("Taken")]
    [InlineData("Users/Inside")]
    public async Task RenameFolderAsync_WhenTheNameCannotBeUsed_ThenKeepsTheFolder(string answer)
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: answer));
        await harness.Library.SaveAsync("Users/Get", ApiRequest.New(), Cancellation);
        await harness.Library.CreateFolderAsync("Taken", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.RenameFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.Equal(["Users/Get"], await harness.Library.NamesAsync(Cancellation));
    }

    [Fact]
    public async Task DeleteFolderAsync_WhenConfirmed_ThenDeletesTheFolderWithItsRequests()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        await harness.Library.SaveAsync("Users/Get", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAsync("Users/Admin/List", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.DeleteFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.Equal((0, false), ((await harness.Library.NamesAsync(Cancellation)).Count, harness.Library.FolderExists("Users")));
    }

    [Fact]
    public async Task DeleteFolderAsync_WhenNotConfirmed_ThenKeepsTheFolder()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: false));
        await harness.Library.SaveAsync("Users/Get", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.DeleteFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.Equal(["Users/Get"], await harness.Library.NamesAsync(Cancellation));
    }

    [Fact]
    public async Task DeleteFolderAsync_WhenConfirmed_ThenDeletesTheSecretsOfItsRequests()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        var request = ApiRequest.New() with { Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveAsync("Users/Admin/List", request, Cancellation);
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
        var admin = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveAsync("Users/Admin/List", ApiRequest.New(), Cancellation);
        await harness.Library.SaveFolderAsync("Users/Admin", admin, Cancellation);
        await harness.Secrets.SaveAsync(admin.Id, SecretKind.Token, "token", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.DeleteFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.Null(await harness.Secrets.OfAsync(admin.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task DeleteFolderAsync_WhenItsSettingsCannotBeRead_ThenDeletesItAnyway()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        await harness.Library.SaveAsync("Users/Get", ApiRequest.New(), Cancellation);
        File.WriteAllText(Path.Combine(harness.Folder.Requests, "Users", ".folder.json"), "{");
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.DeleteFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.False(harness.Library.FolderExists("Users"));
    }

    [Fact]
    public async Task DeleteFolderAsync_WhenAFileCannotBeDeleted_ThenDeletesTheRest()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        await harness.Library.SaveAsync("Users/Get", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAsync("Users/Admin/List", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        using var locked = new FileStream(Path.Combine(harness.Folder.Requests, "Users", "Admin", "List.json"), FileMode.Open, FileAccess.Read, FileShare.Read);

        // Act
        await main.DeleteFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.Equal(["Users/Admin/List"], await harness.Library.NamesAsync(Cancellation));
    }

    [Fact]
    public async Task DeleteFolderAsync_WhenAFileCannotBeDeleted_ThenDeletesOnlyTheSecretsOfWhatIsGone()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        var get = ApiRequest.New() with { Auth = new(AuthKind.Bearer) };
        var list = ApiRequest.New() with { Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveAsync("Users/Get", get, Cancellation);
        await harness.Library.SaveAsync("Users/Admin/List", list, Cancellation);
        await harness.Secrets.SaveAsync(get.Id, SecretKind.Token, "get", Cancellation);
        await harness.Secrets.SaveAsync(list.Id, SecretKind.Token, "list", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        using var locked = new FileStream(Path.Combine(harness.Folder.Requests, "Users", "Admin", "List.json"), FileMode.Open, FileAccess.Read, FileShare.Read);

        // Act
        await main.DeleteFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.Equal((null, "list"), (await harness.Secrets.OfAsync(get.Id, SecretKind.Token, Cancellation), await harness.Secrets.OfAsync(list.Id, SecretKind.Token, Cancellation)));
    }

    [Fact]
    public async Task DeleteFolderAsync_WhenAFileCannotBeDeleted_ThenOnlyTheTabsOfWhatIsGoneBecomeUnsaved()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        await harness.Library.SaveAsync("Users/Get", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAsync("Users/Admin/List", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(NodeOf(main, "Users/Get"));
        var get = main.SelectedTab!;
        await main.OpenAsync(NodeOf(main, "Users/Admin/List"));
        var list = main.SelectedTab!;
        using var locked = new FileStream(Path.Combine(harness.Folder.Requests, "Users", "Admin", "List.json"), FileMode.Open, FileAccess.Read, FileShare.Read);

        // Act
        await main.DeleteFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.Equal((null, "Users/Admin/List"), (get.Name, list.Name));
    }

    [Fact]
    public async Task DeleteFolderAsync_WhenACopyOutsideSharesTheId_ThenKeepsItsSecrets()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        var request = ApiRequest.New() with { Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveAsync("Users/Get", request, Cancellation);
        await harness.Library.SaveAsync("Copy", request, Cancellation);
        await harness.Secrets.SaveAsync(request.Id, SecretKind.Token, "token", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.DeleteFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.Equal("token", await harness.Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task DeleteFolderAsync_WhenARequestInItIsOpen_ThenTheTabKeepsItAsUnsaved()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        await harness.Library.SaveAsync("Users/Get", ApiRequest.New() with { Url = "https://dev.local" }, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(NodeOf(main, "Users").Children.Single());
        var tab = main.SelectedTab!;

        // Act
        await main.DeleteFolderAsync(NodeOf(main, "Users"));

        // Assert
        Assert.Equal((null, true, "https://dev.local"), (tab.Name, tab.IsDirty, tab.Url));
    }

    [Fact]
    public async Task DeleteAsync_WhenConfirmed_ThenDeletesTheRequest()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        await harness.Library.SaveAsync("Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.DeleteAsync(main.Tree.Nodes.Single());

        // Assert
        Assert.Empty(await harness.Library.NamesAsync(Cancellation));
    }

    [Fact]
    public async Task DeleteAsync_WhenNotConfirmed_ThenKeepsTheRequest()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: false));
        await harness.Library.SaveAsync("Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.DeleteAsync(main.Tree.Nodes.Single());

        // Assert
        Assert.Equal(["Ping"], await harness.Library.NamesAsync(Cancellation));
    }

    [Fact]
    public async Task DeleteAsync_WhenTheRequestIsOpen_ThenTheTabBecomesUnsaved()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        await harness.Library.SaveAsync("Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());

        // Act
        await main.DeleteAsync(main.Tree.Nodes.Single());

        // Assert
        Assert.True(main.SelectedTab!.IsDirty);
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
        await harness.Library.SaveFolderAsync("Users", new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) }, Cancellation);
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
        await harness.Library.SaveAsync("Ping", request, Cancellation);
        await harness.Secrets.SaveAsync(request.Id, SecretKind.Token, "token", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.DeleteAsync(main.Tree.Nodes.Single());

        // Assert
        Assert.Null(await harness.Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task DeleteAsync_WhenACopySharesTheId_ThenKeepsTheSecrets()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        var request = ApiRequest.New();
        await harness.Library.SaveAsync("Ping", request, Cancellation);
        await harness.Library.SaveAsync("Copy", request, Cancellation);
        await harness.Secrets.SaveAsync(request.Id, SecretKind.Token, "token", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.DeleteAsync(main.Tree.Nodes.Single(node => node.Path == "Ping"));

        // Assert
        Assert.Equal("token", await harness.Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task DeleteAsync_WhenTheTabIsSavedAgain_ThenSavesItsSecretsAgain()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Ping", accept: true));
        var request = ApiRequest.New() with { Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveAsync("Ping", request, Cancellation);
        await harness.Secrets.SaveAsync(request.Id, SecretKind.Token, "token", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());
        var tab = main.SelectedTab!;
        await main.DeleteAsync(main.Tree.Nodes.Single());

        // Act
        await tab.SaveAsync();

        // Assert
        Assert.Equal("token", await harness.Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
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
        await harness.Library.SaveAsync("Ping", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());
        var file = Path.Combine(harness.Folder.Requests, "Ping.json");
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
    public async Task RequestsChangedAsync_WhenAFileIsMovedOntoAnOpenTab_ThenTheMovedTabIsUnlinked()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAsync("A", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAsync("B", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single(node => node.Path == "A"));
        await main.OpenAsync(main.Tree.Nodes.Single(node => node.Path == "B"));
        var b = main.SelectedTab!;
        File.Move(Path.Combine(harness.Folder.Requests, "B.json"), Path.Combine(harness.Folder.Requests, "A.json"), overwrite: true);

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
        Assert.Equal("200 OK", main.SelectedTab?.Response?.Status);
    }

    [Fact]
    public async Task RenameAsync_WhenTheNewNameIsTaken_ThenKeepsBothFiles()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Taken"));
        await harness.Library.SaveAsync("Ping", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAsync("Taken", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();

        // Act
        await main.RenameAsync(main.Tree.Nodes.Single(node => node.Path == "Ping"));

        // Assert
        Assert.Equal(["Ping", "Taken"], (await harness.Library.NamesAsync(Cancellation)).Order());
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
        main.SelectedTab!.Url = "https://changed.local";

        // Act
        await main.OpenAsync(new HistoryItemViewModel(new("call.json", entry), "Today"));

        // Assert
        Assert.Equal("https://dev.local", main.SelectedTab?.Url);
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenARequestWithACopyIsDeleted_ThenTheTabKeepsItsEdits()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(accept: true));
        var request = ApiRequest.New() with { Url = "https://a.local" };
        await harness.Library.SaveAsync("A", request, Cancellation);
        await harness.Library.SaveAsync("B", request with { Url = "https://b.local" }, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single(node => node.Path == "A"));
        var tab = main.SelectedTab!;
        tab.Url = "https://edited.local";
        await main.DeleteAsync(main.Tree.Nodes.Single(node => node.Path == "A"));

        // Act
        await main.RequestsChangedAsync();

        // Assert
        Assert.Equal("https://edited.local", tab.Url);
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenTheOriginalOfACopyIsDeletedOnDisk_ThenTheTabDoesNotJumpToTheCopy()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New();
        await harness.Library.SaveAsync("A", request, Cancellation);
        await harness.Library.SaveAsync("B", request, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single(node => node.Path == "A"));
        var tab = main.SelectedTab!;
        File.Delete(Path.Combine(harness.Folder.Requests, "A.json"));

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
        await harness.Library.SaveAsync("Ping", request, Cancellation);
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
        File.WriteAllText(Path.Combine(harness.Folder.Requests, "Ping.json"), $$"""{"id": "{{Guid.NewGuid()}}", "url": "https://dev.local", "headers": [{"name": ""}]}""");
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
        File.WriteAllText(Path.Combine(harness.Folder.Requests, "Ping.json"), $$"""{"id": "{{Guid.NewGuid()}}", "url": "https://dev.local", "headers": [{"name": ""}]}""");
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());
        main.SelectedTab!.Url = "https://edited.local";

        // Act
        await main.RequestsChangedAsync();

        // Assert
        Assert.Equal("https://edited.local", main.SelectedTab!.Url);
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenTheTabSavedItselfAndWasEditedAfter_ThenStaysUnsaved()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAsync("Ping", ApiRequest.New() with { Url = "https://dev.local" }, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());
        var tab = main.SelectedTab!;
        tab.Url = "https://saved.local";
        await tab.SaveAsync();
        tab.Url = "https://newer.local";

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
        Assert.Equal("https://b.local", main.SelectedTab?.Url);
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
        main.SelectedTab!.Url = "https://changed.local";

        // Act
        await main.OpenAsync(HistoryItem("b.json"));

        // Assert
        Assert.Equal(["https://changed.local", "https://b.local"], main.Tabs.Select(tab => tab.Url));
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
    public async Task DeleteAsync_WhenTheTabIsSavedAgain_ThenSavesItsPasswordAgain()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Ping", accept: true));
        var request = ApiRequest.New() with { Auth = new(AuthKind.Basic, "hobo") };
        await harness.Library.SaveAsync("Ping", request, Cancellation);
        await harness.Secrets.SaveAsync(request.Id, SecretKind.Password, "hemmelig", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.Tree.Nodes.Single());
        var tab = main.SelectedTab!;
        await main.DeleteAsync(main.Tree.Nodes.Single());

        // Act
        await tab.SaveAsync();

        // Assert
        Assert.Equal("hemmelig", await harness.Secrets.OfAsync(request.Id, SecretKind.Password, Cancellation));
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

    // "a.json" is a call to https://a.local.
    static HistoryItemViewModel HistoryItem(string file)
    {
        var address = $"{Path.GetFileNameWithoutExtension(file)}.local";
        return new(new(file, new(DateTimeOffset.Now, HistorySource.App, address, ApiRequest.New() with { Url = $"https://{address}" })), "Today");
    }
}
