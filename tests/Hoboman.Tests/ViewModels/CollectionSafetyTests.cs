namespace Hoboman.Tests.ViewModels;

public sealed class CollectionSafetyTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static RequestNodeViewModel Node(MainViewModel main, string path) => RequestTreeViewModel.Flatten(main.Tree.Nodes).Single(node => node.Path == path);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RenameTabAsync_WhenQueuedBehindAMove_ThenUsesTheCurrentFolder(bool folder, bool saved)
    {
        using var harness = new Harness(new FakeDialogs(answer: "Original"));
        await harness.Library.SaveFolderAsync("Source", new() { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) }, Cancellation);
        await harness.Library.SaveFolderAsync("Target", new() { Id = Guid.NewGuid(), Auth = new(AuthKind.Basic) }, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(Node(main, "Source"));
        var tab = main.SelectedTab!;
        tab.Editor.Body = "content";
        if (saved)
        {
            await tab.SaveAsync();
        }
        harness.Dialogs.Answer = "Renamed";
        var source = Node(main, folder ? "Source" : tab.Name ?? tab.DraftName!);
        var target = Node(main, "Target");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = harness.Services.CollectionChanges.RunAsync(() => release.Task);
        var moving = main.MoveAsync(source, target);
        var renaming = main.RenameTabAsync(tab);
        release.SetResult();
        await Task.WhenAll(blocked, moving, renaming).WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        await main.RequestsChangedAsync();

        var destination = folder ? "Target/Source" : "Target";
        Assert.Equal($"{destination}/Renamed", tab.Name);
        Assert.Equivalent(tab.ToRequest(), await harness.Library.LoadAsync($"{destination}/Renamed", Cancellation));
        Assert.Equal(destination, tab.InheritedAuthFolder);
        Assert.False(harness.Library.Exists("Source/Renamed"));
        Assert.Equal(!folder, harness.Library.FolderExists("Source"));
        Assert.Null(tab.Problem);
        Assert.Null(harness.Dialogs.Notification);
    }

    [Theory]
    [InlineData(FileShare.Read)]
    [InlineData(FileShare.None)]
    public async Task SaveAsync_WhenOnlyTheOrderCannotBeSaved_ThenKeepsTheSavedRequestAndReportsOnlyTheOrderFailure(FileShare sharing)
    {
        using var harness = new Harness(new FakeDialogs(answer: "Saved"));
        await harness.Library.CreateFolderAsync("Folder", Cancellation);
        await harness.Library.SaveOrderAsync(["Folder/"], Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(Node(main, "Folder"));
        var draft = main.SelectedTab!;
        draft.Editor.Body = "content";
        draft.Auth.Token = "token";
        using var locked = new FileStream(harness.Folder.RequestOrder, FileMode.Open, FileAccess.Read, sharing);

        await draft.SaveAsync();

        Assert.Equal("Folder/Saved", draft.Name);
        Assert.Equivalent(draft.ToRequest(), await harness.Library.LoadAsync("Folder/Saved", Cancellation));
        Assert.Equal("token", await harness.Secrets.OfAsync(draft.Id, SecretKind.Token, Cancellation));
        Assert.False(draft.IsDirty);
        Assert.False(draft.IsDraft);
        Assert.Null(draft.Problem);
        Assert.Equal(harness.Translator.Of("Order.SaveFailed"), harness.Dialogs.Notification!.Value.Title);
        Assert.Equal(2, harness.Dialogs.Asked);
    }

    [Theory]
    [InlineData(FileShare.Read)]
    [InlineData(FileShare.None)]
    public async Task SaveAsync_WhenOnlyAnExistingRequestChanges_ThenDoesNotWriteTheOrder(FileShare sharing)
    {
        using var harness = new Harness();
        await harness.Library.SaveAsync("Saved", ApiRequest.New(), Cancellation);
        await harness.Library.SaveOrderAsync(["Saved"], Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(Node(main, "Saved"));
        var tab = main.SelectedTab!;
        tab.Editor.Body = "edited";
        using var locked = new FileStream(harness.Folder.RequestOrder, FileMode.Open, FileAccess.Read, sharing);

        await tab.SaveAsync();

        Assert.Equal("edited", (await harness.Library.LoadAsync("Saved", Cancellation))!.Body);
        Assert.False(tab.IsDirty);
        Assert.Null(tab.Problem);
        Assert.Null(harness.Dialogs.Notification);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MoveOrRenameAsync_WhenOnlyTheOrderCannotBeSaved_ThenCompletesAndReportsOnlyTheOrderFailure(bool folder, bool rename)
    {
        using var harness = new Harness(new FakeDialogs(answer: "Renamed"));
        await harness.Library.SaveAsync("Source/Request", ApiRequest.New(), Cancellation);
        await harness.Library.CreateFolderAsync("Target", Cancellation);
        await harness.Library.SaveOrderAsync(["Source/", "Source/Request", "Target/"], Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(Node(main, "Source/Request"));
        var tab = main.SelectedTab!;
        var source = Node(main, folder ? "Source" : "Source/Request");
        using var locked = new FileStream(harness.Folder.RequestOrder, FileMode.Open, FileAccess.Read, FileShare.Read);

        if (rename)
        {
            await (folder ? main.RenameFolderAsync(source) : main.RenameAsync(source));
        }
        else
        {
            await main.MoveAsync(source, Node(main, "Target"));
        }

        var expected = rename ? folder ? "Renamed/Request" : "Source/Renamed" : folder ? "Target/Source/Request" : "Target/Request";
        Assert.Equal(expected, tab.Name);
        Assert.True(harness.Library.Exists(expected));
        Assert.False(harness.Library.Exists("Source/Request"));
        Assert.Contains(RequestTreeViewModel.Flatten(main.Tree.Nodes), node => node.Path == expected);
        Assert.Equal(harness.Translator.Of("Order.SaveFailed"), harness.Dialogs.Notification!.Value.Title);
        Assert.Equal(rename ? 2 : 1, harness.Dialogs.Asked);

        await main.MoveAsync(Node(main, folder ? RequestLibrary.ParentOf(expected)! : expected), rename ? Node(main, "Target") : null);

        Assert.Equal(rename ? 3 : 2, harness.Dialogs.Asked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteAsync_WhenOnlyTheOrderCannotBeSaved_ThenRemovesRequestsAndSecretsAndReportsOnlyTheOrderFailure(bool folder)
    {
        using var harness = new Harness(new FakeDialogs(accept: true));
        var request = ApiRequest.New();
        await harness.Library.SaveAsync("Folder/Request", request, Cancellation);
        await harness.Secrets.SaveAsync(request.Id, SecretKind.Token, "token", Cancellation);
        await harness.Library.SaveOrderAsync(["Folder/", "Folder/Request"], Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(Node(main, "Folder/Request"));
        var tab = main.SelectedTab!;
        using var locked = new FileStream(harness.Folder.RequestOrder, FileMode.Open, FileAccess.Read, FileShare.Read);

        await (folder ? main.DeleteFolderAsync(Node(main, "Folder")) : main.DeleteAsync(Node(main, "Folder/Request")));

        Assert.False(harness.Library.Exists("Folder/Request"));
        Assert.Null(await harness.Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
        Assert.DoesNotContain(tab, main.Tabs);
        Assert.Equal(harness.Translator.Of("Order.SaveFailed"), harness.Dialogs.Notification!.Value.Title);
        Assert.Equal(2, harness.Dialogs.Asked);
    }

    [Fact]
    public async Task CanClose_WhenACollectionChangeIsRunning_ThenExplainsWhyItCannotCloseYet()
    {
        using var harness = new Harness();
        var main = harness.Main();
        await main.LoadAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = harness.Services.CollectionChanges.RunAsync(() => release.Task);
        try
        {
            Assert.False(main.CanClose());
            Assert.Equal(harness.Translator.Of("Close.BusyTitle"), harness.Dialogs.Notification!.Value.Title);
            Assert.Equal(harness.Translator.Of("Close.BusyMessage"), harness.Dialogs.Notification.Value.Message);
        }
        finally
        {
            release.SetResult();
            await blocked;
        }
        Assert.True(main.CanClose());
    }

    [Fact]
    public async Task SaveAsync_WhenSeparateRequestsCannotSaveTheirOrder_ThenReportsEachFailure()
    {
        using var harness = new Harness(new FakeDialogs(answer: "First"));
        await harness.Library.SaveOrderAsync([], Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        using var locked = new FileStream(harness.Folder.RequestOrder, FileMode.Open, FileAccess.Read, FileShare.Read);

        await main.SelectedTab!.SaveAsync();
        Assert.Equal(2, harness.Dialogs.Asked);

        main.NewTab();
        harness.Dialogs.Answer = "Second";
        await main.SelectedTab!.SaveAsync();

        Assert.Equal(4, harness.Dialogs.Asked);
        Assert.Equal(harness.Translator.Of("Order.SaveFailed"), harness.Dialogs.Notification!.Value.Title);
        Assert.Equal(["First", "Second"], (await harness.Library.NamesAsync(Cancellation)).Order());
        Assert.Empty(await harness.Library.LoadOrderAsync(Cancellation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenameTabAsync_WhenTwoDraftsChooseTheSameNameWhileQueued_ThenKeepsTheFirstRequestAndRejectsTheSecond(bool inFolder)
    {
        using var harness = new Harness(new FakeDialogs(answer: "Same"));
        await harness.Library.CreateFolderAsync("Folder", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        if (inFolder)
        {
            await main.NewDraftAsync(Node(main, "Folder"));
        }
        var first = main.SelectedTab!;
        first.Editor.Body = "first";
        first.Auth.Token = "first-token";
        if (inFolder)
        {
            await main.NewDraftAsync(Node(main, "Folder"));
        }
        else
        {
            main.NewTab();
        }
        var second = main.SelectedTab!;
        second.Editor.Body = "second";
        second.Auth.Token = "second-token";
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = harness.Services.CollectionChanges.RunAsync(() => release.Task);
        var firstRename = main.RenameTabAsync(first);
        var secondRename = main.RenameTabAsync(second);
        release.SetResult();
        await Task.WhenAll(blocked, firstRename, secondRename).WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

        var name = inFolder ? "Folder/Same" : "Same";
        Assert.Equivalent(first.ToRequest(), await harness.Library.LoadAsync(name, Cancellation));
        Assert.Equal(name, first.Name);
        Assert.Equal("first-token", await harness.Secrets.OfAsync(first.Id, SecretKind.Token, Cancellation));
        Assert.Null(second.Name);
        Assert.Equal(inFolder ? "Folder" : null, second.Destination);
        Assert.Equal("second", second.Editor.Body);
        Assert.True(second.IsDirty);
        Assert.Equal(harness.Translator.Of("Save.Exists"), second.Problem!.Details);
        Assert.Null(await harness.Secrets.OfAsync(second.Id, SecretKind.Token, Cancellation));
        Assert.Equal([name], await harness.Library.NamesAsync(Cancellation));
    }

    [Fact]
    public async Task SaveAsync_WhenAnotherWriterTakesTheNewNameAfterValidation_ThenKeepsThatRequestAndAllowsRetryUnderAnotherName()
    {
        using var harness = new Harness(new FakeDialogs(answer: "Taken"));
        var main = harness.Main();
        await main.LoadAsync();
        var draft = main.SelectedTab!;
        draft.Editor.Body = "draft";
        draft.Auth.Token = "token";
        var original = ApiRequest.New() with { Body = "other request" };
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = harness.AuthRefresh.SaveAsync(() => release.Task, Cancellation);
        var saving = draft.SaveAsync();
        try
        {
            await harness.Library.SaveAsync("Taken", original, Cancellation);
        }
        finally
        {
            release.SetResult();
        }
        await Task.WhenAll(blocked, saving).WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

        Assert.Equivalent(original, await harness.Library.LoadAsync("Taken", Cancellation));
        Assert.Null(draft.Name);
        Assert.True(draft.IsDirty);
        Assert.NotNull(draft.Problem);
        harness.Dialogs.Answer = "Available";
        await draft.SaveAsync();
        Assert.Equivalent(draft.ToRequest(), await harness.Library.LoadAsync("Available", Cancellation));
        Assert.Equal("token", await harness.Secrets.OfAsync(draft.Id, SecretKind.Token, Cancellation));
        Assert.Equivalent(original, await harness.Library.LoadAsync("Taken", Cancellation));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DeleteFolderAsync_WhenADraftIsSavedAheadOfTheQueuedDeletion_ThenUsesTheCurrentContents(bool nested, bool accept)
    {
        var destination = nested ? "Folder/Nested" : "Folder";
        using var harness = new Harness(new FakeDialogs(answer: "Saved", accept: accept));
        await harness.Library.CreateFolderAsync(destination, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(Node(main, destination));
        var draft = main.SelectedTab!;
        draft.Editor.Body = "saved content";
        draft.Auth.Token = "token";
        var folder = Node(main, "Folder");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = harness.Services.CollectionChanges.RunAsync(() => release.Task);
        var saving = draft.SaveAsync();
        var deleting = main.DeleteFolderAsync(folder);
        release.SetResult();
        await Task.WhenAll(blocked, saving, deleting).WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

        Assert.Equal(!accept, harness.Library.Exists($"{destination}/Saved"));
        Assert.Equal(!accept, main.Tabs.Contains(draft));
        Assert.Equal(accept ? null : "token", await harness.Secrets.OfAsync(draft.Id, SecretKind.Token, Cancellation));
        Assert.Equal(harness.Translator.Format("DeleteFolder.Message", "Folder", 1), harness.Dialogs.ConfirmQuestion!.Value.Message);
        Assert.Empty(harness.Dialogs.ConfirmQuestion.Value.Items);
        if (accept)
        {
            await draft.SaveAsync();
            Assert.False(harness.Library.Exists($"{destination}/Saved"));
            Assert.Null(await harness.Secrets.OfAsync(draft.Id, SecretKind.Token, Cancellation));
        }
    }

    [Theory]
    [InlineData(FileShare.Read)]
    [InlineData(FileShare.None)]
    public async Task CloneAsync_WhenOnlyTheOrderCannotBeSaved_ThenOpensTheCompleteCopyAndReportsOnlyTheOrderFailure(FileShare sharing)
    {
        using var harness = new Harness();
        var original = ApiRequest.New() with { Body = "body", Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveAsync("Request", original, Cancellation);
        await harness.Library.SaveOrderAsync(["Request"], Cancellation);
        await harness.Secrets.SaveAsync(original.Id, SecretKind.Token, "token", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        using var locked = new FileStream(harness.Folder.RequestOrder, FileMode.Open, FileAccess.Read, sharing);

        await main.CloneAsync(Node(main, "Request"));

        var clone = (await harness.Library.LoadAsync("Request (1)", Cancellation))!;
        Assert.NotEqual(original.Id, clone.Id);
        Assert.Equivalent(original with { Id = clone.Id }, clone);
        Assert.Equal("Request (1)", main.SelectedTab!.Name);
        Assert.Equal("token", main.SelectedTab.Auth.Token);
        Assert.Equal("token", await harness.Secrets.OfAsync(clone.Id, SecretKind.Token, Cancellation));
        Assert.Equivalent(original, await harness.Library.LoadAsync("Request", Cancellation));
        Assert.Equal(["Request", "Request (1)"], (await harness.Library.NamesAsync(Cancellation)).Order());
        Assert.Equal(harness.Translator.Of("Order.SaveFailed"), harness.Dialogs.Notification!.Value.Title);
        Assert.Equal(1, harness.Dialogs.Asked);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task SaveAsync_WhenMovingOrDeletingAtTheSameTime_ThenNeverRecreatesTheOldPath(bool deleting, bool folder, bool saveFirst)
    {
        using var harness = new Harness(new FakeDialogs(accept: true));
        var request = ApiRequest.New();
        await harness.Library.SaveAsync("Folder/Request", request, Cancellation);
        await harness.Library.CreateFolderAsync("Target", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(Node(main, "Folder/Request"));
        var tab = main.SelectedTab!;
        tab.Editor.Body = "edited";
        tab.Auth.Token = "secret";
        var source = Node(main, folder ? "Folder" : "Folder/Request");
        var target = Node(main, "Target");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = harness.AuthRefresh.SaveAsync(() => release.Task, Cancellation);
        Task ChangeAsync() => deleting ? folder ? main.DeleteFolderAsync(source) : main.DeleteAsync(source) : main.MoveAsync(source, target);
        var first = saveFirst ? tab.SaveAsync() : ChangeAsync();
        var second = saveFirst ? ChangeAsync() : tab.SaveAsync();
        var wasQueued = !second.IsCompleted;
        release.SetResult();
        await Task.WhenAll(blocked, first, second).WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

        Assert.True(wasQueued);
        Assert.False(harness.Library.Exists("Folder/Request"));
        Assert.False(main.IsChangingCollection);
        if (deleting)
        {
            Assert.DoesNotContain(tab, main.Tabs);
            Assert.Null(await harness.Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
            return;
        }
        var name = folder ? "Target/Folder/Request" : "Target/Request";
        Assert.Equal(name, tab.Name);
        Assert.Equal("edited", (await harness.Library.LoadAsync(name, Cancellation))!.Body);
        Assert.Equal("secret", await harness.Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
        Assert.Single(await harness.Library.NamesAsync(Cancellation));
    }

    [Fact]
    public async Task LoadAsync_WhenSecretCleanupPreviouslyFailed_ThenRetriesItWithoutRecreatingTheRequest()
    {
        using var harness = new Harness(new FakeDialogs(accept: true));
        var request = ApiRequest.New();
        await harness.Library.SaveAsync("Request", request, Cancellation);
        await harness.Secrets.SaveAsync(request.Id, SecretKind.Token, "secret", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        using (var locked = new FileStream(harness.Folder.Secrets, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await main.DeleteAsync(Node(main, "Request"));
        }
        Assert.False(harness.Library.Exists("Request"));
        Assert.Equal("secret", await harness.Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));

        await harness.Main().LoadAsync();

        Assert.Null(await harness.Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
        Assert.False(harness.Library.Exists("Request"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadAsync_WhenOrderIsUnavailable_ThenShowsRequestsAndRestoresSavedTabs(bool malformed)
    {
        using var harness = new Harness();
        await harness.Library.SaveAsync("A", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAsync("B", ApiRequest.New(), Cancellation);
        await harness.Library.SaveOrderAsync(["B", "A"], Cancellation);
        await harness.SettingsStore.UpdateAsync(settings => settings with { Session = new(["B", "A"], "B") }, Cancellation);
        if (malformed)
        {
            await File.WriteAllTextAsync(harness.Folder.RequestOrder, "{", Cancellation);
        }
        using var locked = malformed ? null : new FileStream(harness.Folder.RequestOrder, FileMode.Open, FileAccess.Read, FileShare.None);
        var main = harness.Main();

        await main.LoadAsync();

        Assert.Equal(["A", "B"], main.Tree.Nodes.Select(node => node.Path));
        Assert.Equal(["B", "A"], main.Tabs.Select(tab => tab.Name));
        Assert.Equal("B", main.SelectedTab!.Name);
    }

    [Fact]
    public async Task RequestsChangedAsync_WhenOrderBecomesUnreadable_ThenKeepsTheKnownOrderAndDrafts()
    {
        using var harness = new Harness();
        await harness.Library.SaveAsync("Folder/A", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAsync("Folder/B", ApiRequest.New(), Cancellation);
        await harness.Library.SaveOrderAsync(["Folder/", "Folder/B", "Folder/A"], Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(Node(main, "Folder"));
        var draft = main.SelectedTab!;
        await main.MoveAsync(Node(main, draft.DraftName!), Node(main, "Folder/A"), DropPosition.Before);
        using var locked = new FileStream(harness.Folder.RequestOrder, FileMode.Open, FileAccess.Read, FileShare.None);

        await main.RequestsChangedAsync();
        await main.RequestsChangedAsync();

        Assert.Equal(["B", draft.Title, "A"], Node(main, "Folder").Children.Select(node => node.Name));
        Assert.Equal("Folder", draft.Destination);
        Assert.Single(Node(main, "Folder").Children, node => node.IsDraft);
    }
}
