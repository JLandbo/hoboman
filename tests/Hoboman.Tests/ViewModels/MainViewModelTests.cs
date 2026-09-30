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
