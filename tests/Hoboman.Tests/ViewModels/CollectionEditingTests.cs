namespace Hoboman.Tests.ViewModels;

public sealed class CollectionEditingTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static RequestNodeViewModel Node(MainViewModel main, string path) => RequestTreeViewModel.Flatten(main.Tree.Nodes).Single(node => node.Path == path);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewRequest_WhenCreated_ThenStartsOnBodyWithJsonAndVariablesOff(bool inFolder)
    {
        using var harness = new Harness();
        await harness.Library.CreateFolderAsync("Folder", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        if (inFolder)
        {
            await main.NewDraftAsync(Node(main, "Folder"));
        }
        Assert.Equal(RequestSection.Body, main.SelectedTab!.RequestSection);
        Assert.Equal(BodyKind.Json, main.SelectedTab.Editor.BodyKind);
        Assert.False(main.SelectedTab.Editor.UseEnvironmentVariablesInBody);
        Assert.False(main.SelectedTab.IsDirty);
    }

    [Theory]
    [InlineData(BodyKind.None)]
    [InlineData(BodyKind.Json)]
    [InlineData(BodyKind.Xml)]
    [InlineData(BodyKind.Text)]
    public async Task OpenAsync_WhenSelectingARequest_ThenShowsBodyAndKeepsItsChosenFormatAndEdits(BodyKind kind)
    {
        using var harness = new Harness();
        await harness.Library.SaveAsync("Request", ApiRequest.New() with { BodyKind = kind, Body = "saved" }, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(Node(main, "Request"));
        var tab = main.SelectedTab!;
        Assert.Equal(RequestSection.Body, tab.RequestSection);
        Assert.Equal(kind, tab.Editor.BodyKind);
        Assert.False(tab.IsDirty);
        tab.RequestSection = RequestSection.Auth;
        tab.Editor.Body = "unsaved";

        await main.OpenAsync(Node(main, "Request"));

        Assert.Same(tab, main.SelectedTab);
        Assert.Equal(RequestSection.Body, tab.RequestSection);
        Assert.Equal(kind, tab.Editor.BodyKind);
        Assert.Equal("unsaved", tab.Editor.Body);
        Assert.Equal("saved", (await harness.Library.LoadAsync("Request", Cancellation))!.Body);
    }

    [Fact]
    public async Task OpenAsync_WhenSelectingAHistoryRequest_ThenShowsBodyWithoutChangingTheSavedFormat()
    {
        using var harness = new Harness();
        await harness.History().AddAsync(new(DateTimeOffset.Now, HistorySource.App, "https://example.test", ApiRequest.New() with { BodyKind = BodyKind.Xml, Body = "<saved />" }), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.History.Items.Single());
        var tab = main.SelectedTab!;
        Assert.Equal(RequestSection.Body, tab.RequestSection);
        tab.RequestSection = RequestSection.Headers;

        await main.OpenAsync(main.History.Items.Single());

        Assert.Same(tab, main.SelectedTab);
        Assert.Equal(RequestSection.Body, tab.RequestSection);
        Assert.Equal(BodyKind.Xml, tab.Editor.BodyKind);
        Assert.False(tab.IsDirty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteAsync_WhenTheTabIsDirtyAndSending_ThenClosesAndCancelsItWithoutAnotherPrompt(bool folder)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = new Harness(new FakeDialogs(accept: true), send: () =>
        {
            started.SetResult();
            return new TaskCompletionSource<ApiResponse>().Task;
        });
        await harness.Library.SaveAsync("Folder/Request", ApiRequest.New() with { Url = "https://example.test" }, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(Node(main, "Folder/Request"));
        var tab = main.SelectedTab!;
        tab.Editor.Body = "unsaved";
        var sending = tab.SendAsync();
        try
        {
            await started.Task.WaitAsync(Cancellation);
            if (folder)
            {
                await main.DeleteFolderAsync(Node(main, "Folder"));
            }
            else
            {
                await main.DeleteAsync(Node(main, "Folder/Request"));
            }
            await sending.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
            Assert.DoesNotContain(tab, main.Tabs);
            Assert.False(tab.IsSending);
            Assert.Equal(1, harness.Dialogs.Asked);
        }
        finally
        {
            tab.Cancel();
            await sending;
        }
    }

    [Fact]
    public async Task DeleteAsync_WhenTheFileCannotBeDeleted_ThenKeepsItsTabAndEdits()
    {
        using var harness = new Harness(new FakeDialogs(accept: true));
        await harness.Library.SaveAsync("Request", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(Node(main, "Request"));
        var tab = main.SelectedTab!;
        tab.Editor.Body = "unsaved";
        using var locked = new FileStream(Path.Combine(harness.Folder.Requests, "Request.json"), FileMode.Open, FileAccess.Read, FileShare.Read);

        await main.DeleteAsync(Node(main, "Request"));

        Assert.Contains(tab, main.Tabs);
        Assert.Same(tab, main.SelectedTab);
        Assert.Equal("Request", tab.Name);
        Assert.Equal("unsaved", tab.Editor.Body);
        Assert.True(harness.Library.Exists("Request"));
    }

    [Theory]
    [InlineData(DropPosition.Before)]
    [InlineData(DropPosition.After)]
    public async Task MoveAsync_WhenReorderingMixedSiblings_ThenPersistsOrderWithoutSavingEdits(DropPosition position)
    {
        using var harness = new Harness();
        await harness.Library.CreateFolderAsync("Folder", Cancellation);
        await harness.Library.SaveAsync("A", ApiRequest.New() with { Body = "saved" }, Cancellation);
        await harness.Library.SaveAsync("B", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(Node(main, "A"));
        var open = main.SelectedTab!;
        open.Editor.Body = "unsaved";
        var original = await File.ReadAllTextAsync(Path.Combine(harness.Folder.Requests, "A.json"), Cancellation);

        await main.MoveAsync(Node(main, "Folder"), Node(main, "A"), position);

        var expected = position == DropPosition.Before ? new[] { "Folder", "A", "B" } : ["A", "Folder", "B"];
        Assert.Equal(expected, main.Tree.Nodes.Select(node => node.Path));
        Assert.Equal("unsaved", open.Editor.Body);
        Assert.True(open.IsDirty);
        Assert.Equal(original, await File.ReadAllTextAsync(Path.Combine(harness.Folder.Requests, "A.json"), Cancellation));
        var restarted = harness.Main();
        await restarted.LoadAsync();
        Assert.Equal(expected, restarted.Tree.Nodes.Select(node => node.Path));
    }

    [Fact]
    public async Task MoveAsync_WhenMovingNestedFolder_ThenPreservesRequestsDraftsSecretsAndChildOrder()
    {
        using var harness = new Harness();
        var owner = Guid.NewGuid();
        await harness.Library.SaveFolderAsync("Parent", new() { Id = owner, Auth = new(AuthKind.Bearer) }, Cancellation);
        await harness.Secrets.SaveAsync(owner, SecretKind.Token, "parent-token", Cancellation);
        await harness.Library.SaveAsync("Folder/Child/A", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAsync("Folder/Child/B", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.MoveAsync(Node(main, "Folder/Child/B"), Node(main, "Folder/Child/A"), DropPosition.Before);
        await main.OpenAsync(Node(main, "Folder/Child/A"));
        var open = main.SelectedTab!;
        open.Editor.Body = "unsaved";
        await main.NewDraftAsync(Node(main, "Folder/Child"));
        var draft = main.SelectedTab!;
        draft.Editor.Body = "draft";

        await main.MoveAsync(Node(main, "Folder"), Node(main, "Parent"));

        Assert.False(harness.Library.FolderExists("Folder"));
        Assert.Equal("Parent/Folder/Child/A", open.Name);
        Assert.Equal("unsaved", open.Editor.Body);
        Assert.Equal("Parent/Folder/Child", draft.Destination);
        Assert.Equal("draft", draft.Editor.Body);
        Assert.Equal(["B", "A", draft.Title], Node(main, "Parent/Folder/Child").Children.Select(node => node.Name));
        Assert.Equal("Parent", open.InheritedAuthFolder);
        Assert.Equal("Parent", draft.InheritedAuthFolder);
        Assert.Equal("parent-token", await harness.Secrets.OfAsync(owner, SecretKind.Token, Cancellation));
        await main.MoveAsync(Node(main, "Parent/Folder"), null);
        Assert.True(harness.Library.Exists("Folder/Child/A"));
        Assert.Equal("Folder/Child", draft.Destination);
        Assert.Null(open.InheritedAuthFolder);
    }

    [Theory]
    [InlineData("Folder", "Folder")]
    [InlineData("Folder", "Folder/Child")]
    [InlineData("Folder", "Target")]
    public async Task MoveAsync_WhenDestinationIsInvalid_ThenLeavesFoldersUntouched(string source, string target)
    {
        using var harness = new Harness();
        await harness.Library.CreateFolderAsync("Folder/Child", Cancellation);
        await harness.Library.CreateFolderAsync("Target/Folder", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        Assert.False(main.CanMove(Node(main, source), Node(main, target), DropPosition.Inside));
        await main.MoveAsync(Node(main, source), Node(main, target));
        Assert.True(harness.Library.FolderExists("Folder/Child"));
        Assert.True(harness.Library.FolderExists("Target/Folder"));
    }

    [Fact]
    public async Task MoveAsync_WhenMovingDraftThenSaving_ThenKeepsDestinationAndPositionWithoutSavingEarly()
    {
        using var harness = new Harness(new FakeDialogs(answer: "Saved"));
        await harness.Library.CreateFolderAsync("Source", Cancellation);
        await harness.Library.SaveAsync("Target/A", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAsync("Target/B", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(Node(main, "Source"));
        var draft = main.SelectedTab!;
        await main.MoveAsync(Node(main, $"Source/{draft.Title}"), Node(main, "Target/B"), DropPosition.Before);
        Assert.Equal("Target", draft.Destination);
        Assert.Equal(["A", draft.Title, "B"], Node(main, "Target").Children.Select(node => node.Name));
        Assert.False(harness.Library.Exists($"Target/{draft.Title}"));
        await main.RequestsChangedAsync();
        Assert.Equal(["A", draft.Title, "B"], Node(main, "Target").Children.Select(node => node.Name));

        await draft.SaveAsync();
        var restarted = harness.Main();
        await restarted.LoadAsync();
        Assert.Equal(["A", "Saved", "B"], Node(restarted, "Target").Children.Select(node => node.Name));
        Assert.DoesNotContain(await harness.Library.LoadOrderAsync(Cancellation), key => key.StartsWith('\0'));
    }

    [Fact]
    public async Task RenameFolderAsync_WhenOrderWasCustomized_ThenKeepsItsOwnAndItsChildrensPositions()
    {
        using var harness = new Harness(new FakeDialogs(answer: "Renamed"));
        await harness.Library.SaveAsync("Folder/A", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAsync("Folder/B", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAsync("Root", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.MoveAsync(Node(main, "Folder"), Node(main, "Root"), DropPosition.After);
        await main.MoveAsync(Node(main, "Folder/B"), Node(main, "Folder/A"), DropPosition.Before);
        await main.RenameFolderAsync(Node(main, "Folder"));
        var restarted = harness.Main();
        await restarted.LoadAsync();
        Assert.Equal(["Root", "Renamed"], restarted.Tree.Nodes.Select(node => node.Path));
        Assert.Equal(["B", "A"], Node(restarted, "Renamed").Children.Select(node => node.Name));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloneAsync_WhenRequestHasSettingsAndSecrets_ThenMakesAnIndependentCompleteCopy(bool open)
    {
        using var harness = new Harness();
        var original = ApiRequest.New() with { Method = "POST", Url = "https://{{host}}", Body = "{\"value\":\"saved\"}", UseEnvironmentVariablesInBody = true, Headers = [new("X-Test", "{{value}}")], Query = [new("query", "value")], Base64 = new() { Encode = ["/value"], Decode = ["/result"] }, Auth = new(AuthKind.OAuth2, OAuth: new() { TokenUrl = "https://auth.test", ClientId = "client", Scope = "scope" }) };
        await harness.Library.SaveAsync("Folder/Preview", original, Cancellation);
        var developmentToken = new OAuthToken("dev-token", "Bearer", harness.Clock.GetUtcNow().AddHours(1), "scope").ToJson();
        var productionToken = new OAuthToken("prod-token", "Bearer", harness.Clock.GetUtcNow().AddHours(2), "scope").ToJson();
        var dev = new ApiEnvironment("dev", []) { Id = Guid.NewGuid() };
        var prod = Guid.NewGuid();
        await harness.Secrets.SaveAsync(original.Id, SecretKind.ClientSecret, "saved-secret", Cancellation);
        await harness.Secrets.SaveAsync(original.Id, SecretKind.OAuthToken, dev.Id, developmentToken, Cancellation);
        await harness.Secrets.SaveAsync(original.Id, SecretKind.OAuthToken, prod, productionToken, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        RequestTabViewModel? source = null;
        if (open)
        {
            await main.OpenAsync(Node(main, "Folder/Preview"));
            source = main.SelectedTab!;
            source.Editor.Body = "{\"value\":\"edited\"}";
            source.Auth.ClientSecret = "edited-secret";
            Assert.True(await source.Auth.FetchTokenAsync(dev, saveSecrets: false, Cancellation));
        }

        await main.CloneAsync(Node(main, "Folder/Preview"));

        var copy = (await harness.Library.LoadAsync("Folder/Preview (1)", Cancellation))!;
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equivalent((source?.ToRequest() ?? original) with { Id = copy.Id }, copy);
        Assert.Equal(open ? "edited-secret" : "saved-secret", await harness.Secrets.OfAsync(copy.Id, SecretKind.ClientSecret, Cancellation));
        Assert.Equal(open ? Hoboman.Tests.Auth.FakeOAuthClient.Token.ToJson() : developmentToken, await harness.Secrets.OfAsync(copy.Id, SecretKind.OAuthToken, dev.Id, Cancellation));
        Assert.Equal(productionToken, await harness.Secrets.OfAsync(copy.Id, SecretKind.OAuthToken, prod, Cancellation));
        Assert.Equal(developmentToken, await harness.Secrets.OfAsync(original.Id, SecretKind.OAuthToken, dev.Id, Cancellation));
        Assert.Equivalent(original, await harness.Library.LoadAsync("Folder/Preview", Cancellation));
        Assert.Equal("saved-secret", await harness.Secrets.OfAsync(original.Id, SecretKind.ClientSecret, Cancellation));
        Assert.Equal(["Preview", "Preview (1)"], Node(main, "Folder").Children.Select(node => node.Name));
        Assert.Equal("Folder/Preview (1)", main.SelectedTab!.Name);
        Assert.Null(main.SelectedTab.Result.Response);
        if (source is not null)
        {
            Assert.True(source.IsDirty);
        }
    }

    [Fact]
    public async Task CloneAsync_WhenCloningConcurrentlyOrCloningACopy_ThenUsesUniqueIncrementingNames()
    {
        using var harness = new Harness();
        await harness.Library.SaveAsync("Preview", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        var source = Node(main, "Preview");
        await Task.WhenAll(main.CloneAsync(source), main.CloneAsync(source));
        await main.CloneAsync(Node(main, "Preview (1)"));
        Assert.Equal(["Preview", "Preview (1)", "Preview (2)", "Preview (3)"], (await harness.Library.NamesAsync(Cancellation)).Order());
        var ids = new HashSet<Guid>();
        foreach (var name in await harness.Library.NamesAsync(Cancellation))
        {
            Assert.True(ids.Add((await harness.Library.LoadAsync(name, Cancellation))!.Id));
        }
    }

    [Fact]
    public async Task CloneAsync_WhenAuthIsInherited_ThenKeepsInheritanceInsteadOfCopyingTheParentsIdentity()
    {
        using var harness = new Harness();
        var owner = Guid.NewGuid();
        await harness.Library.SaveFolderAsync("Folder", new() { Id = owner, Auth = new(AuthKind.Bearer) }, Cancellation);
        await harness.Library.SaveAsync("Folder/Request", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.CloneAsync(Node(main, "Folder/Request"));
        Assert.Equal(AuthKind.Inherit, main.SelectedTab!.Auth.Kind);
        Assert.Equal("Folder", main.SelectedTab.InheritedAuthFolder);
        Assert.NotEqual(owner, main.SelectedTab.Id);
    }

    [Theory]
    [InlineData(false, FileShare.Read)]
    [InlineData(true, FileShare.Read)]
    [InlineData(false, FileShare.None)]
    [InlineData(true, FileShare.None)]
    public async Task DeleteAsync_WhenTheOrderFileIsLocked_ThenStillRemovesSecretsForDeletedRequestsAndFolders(bool folder, FileShare sharing)
    {
        using var harness = new Harness(new FakeDialogs(accept: true));
        var request = ApiRequest.New();
        var nested = ApiRequest.New();
        var outside = ApiRequest.New();
        var owner = Guid.NewGuid();
        var prod = Guid.NewGuid();
        await harness.Library.SaveFolderAsync("Folder/Nested", new() { Id = owner, Auth = new(AuthKind.Bearer) }, Cancellation);
        await harness.Library.SaveAsync("Folder/Request", request, Cancellation);
        await harness.Library.SaveAsync("Folder/Nested/Child", nested, Cancellation);
        await harness.Library.SaveAsync("Outside", outside, Cancellation);
        foreach (var id in new[] { request.Id, nested.Id, outside.Id, owner })
        {
            await harness.Secrets.SaveAsync(id, SecretKind.Token, "token", Cancellation);
            await harness.Secrets.SaveAsync(id, SecretKind.ClientSecret, "secret", Cancellation);
            await harness.Secrets.SaveAsync(id, SecretKind.OAuthToken, prod, "oauth-token", Cancellation);
        }
        var main = harness.Main();
        await main.LoadAsync();
        await main.Tree.SaveOrderAsync();
        using var locked = new FileStream(harness.Folder.RequestOrder, FileMode.Open, FileAccess.Read, sharing);

        if (folder)
        {
            await main.DeleteFolderAsync(Node(main, "Folder"));
        }
        else
        {
            await main.DeleteAsync(Node(main, "Folder/Request"));
        }

        Assert.False(harness.Library.Exists("Folder/Request"));
        foreach (var id in folder ? new[] { request.Id, nested.Id, owner } : [request.Id])
        {
            Assert.Null(await harness.Secrets.OfAsync(id, SecretKind.Token, Cancellation));
            Assert.Null(await harness.Secrets.OfAsync(id, SecretKind.ClientSecret, Cancellation));
            Assert.Null(await harness.Secrets.OfAsync(id, SecretKind.OAuthToken, prod, Cancellation));
        }
        Assert.Equal("token", await harness.Secrets.OfAsync(outside.Id, SecretKind.Token, Cancellation));
        if (!folder)
        {
            Assert.Equal("token", await harness.Secrets.OfAsync(nested.Id, SecretKind.Token, Cancellation));
            Assert.Equal("token", await harness.Secrets.OfAsync(owner, SecretKind.Token, Cancellation));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteAsync_WhenTheOrderFileIsLockedAndSecretsAreShared_ThenKeepsTheOtherRequestsSecrets(bool folder)
    {
        using var harness = new Harness(new FakeDialogs(accept: true));
        var request = ApiRequest.New();
        await harness.Library.SaveAsync("Folder/Request", request, Cancellation);
        await harness.Library.SaveAsync("Outside", request, Cancellation);
        await harness.Secrets.SaveAsync(request.Id, SecretKind.Token, "shared-token", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.Tree.SaveOrderAsync();
        using var locked = new FileStream(harness.Folder.RequestOrder, FileMode.Open, FileAccess.Read, FileShare.None);

        if (folder)
        {
            await main.DeleteFolderAsync(Node(main, "Folder"));
        }
        else
        {
            await main.DeleteAsync(Node(main, "Folder/Request"));
        }

        Assert.False(harness.Library.Exists("Folder/Request"));
        Assert.True(harness.Library.Exists("Outside"));
        Assert.Equal("shared-token", await harness.Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenameTabAsync_WhenRequestIsSavedOrDraft_ThenKeepsItsFolderAndSavesOnlyTheDraft(bool saved)
    {
        using var harness = new Harness(new FakeDialogs(answer: "Renamed"));
        await harness.Library.SaveAsync("Folder/Original", ApiRequest.New() with { Body = "saved" }, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        if (saved)
        {
            await main.OpenAsync(Node(main, "Folder/Original"));
        }
        else
        {
            await main.NewDraftAsync(Node(main, "Folder"));
        }
        var tab = main.SelectedTab!;
        tab.Editor.Body = "unsaved";
        await main.RenameTabAsync(tab);
        Assert.Equal("Renamed", tab.Title);
        Assert.Equal("unsaved", tab.Editor.Body);
        Assert.Equal(saved, tab.IsDirty);
        Assert.Equal("Folder/Renamed", tab.Name);
        Assert.Null(tab.Destination);
        Assert.False(tab.IsDraft);
        Assert.Same(tab, Node(main, "Folder/Renamed").Tab);
        Assert.Equal(saved ? "saved" : "unsaved", (await harness.Library.LoadAsync("Folder/Renamed", Cancellation))!.Body);
        Assert.Equal(1, harness.Dialogs.Asked);
    }

    [Fact]
    public async Task Session_WhenRestarting_ThenRestoresSavedTabsInOrderAndDropsDraftsAndDiscardedEdits()
    {
        using var harness = new Harness(new FakeDialogs(accept: true));
        foreach (var name in new[] { "A", "B", "C" })
        {
            await harness.Library.SaveAsync(name, ApiRequest.New() with { Body = name }, Cancellation);
        }
        var main = harness.Main();
        await main.LoadAsync();
        foreach (var name in new[] { "A", "B", "C" })
        {
            await main.OpenAsync(Node(main, name));
        }
        var active = main.SelectedTab!;
        active.Editor.Body = "discarded";
        main.MoveTab(active, main.Tabs.Single(tab => tab.Name == "A"), after: false);
        Assert.Same(active, main.SelectedTab);
        main.NewTab();
        main.SelectedTab!.Editor.Body = "ungemte data";
        main.NewTab();
        main.SelectedTab = active;
        Assert.True(main.CanClose());
        await harness.SettingsStore.UpdateAsync(settings => settings with { Session = main.Session }, Cancellation);

        var restarted = harness.Main();
        await restarted.LoadAsync();

        Assert.Equal(["C", "A", "B"], restarted.Tabs.Select(tab => tab.Name));
        Assert.Equal("C", restarted.SelectedTab!.Name);
        Assert.Equal("C", restarted.SelectedTab.Editor.Body);
        Assert.All(restarted.Tabs, tab => Assert.False(tab.IsDirty));
    }

    [Fact]
    public async Task Session_WhenSomeRequestsAreMissingOrRepeated_ThenRestoresOnlyExistingRequestsOnce()
    {
        using var harness = new Harness();
        await harness.Library.SaveAsync("A", ApiRequest.New(), Cancellation);
        await harness.SettingsStore.UpdateAsync(settings => settings with { Session = new(["Missing", "A", "a"], "Missing") }, Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        Assert.Equal("A", Assert.Single(main.Tabs).Name);
        Assert.Same(main.Tabs[0], main.SelectedTab);
    }

    [Fact]
    public async Task MoveAsync_WhenDroppingAtRootInTheSameParent_ThenMovesToTheEnd()
    {
        using var harness = new Harness();
        await harness.Library.SaveAsync("A", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAsync("B", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.MoveAsync(Node(main, "A"), null);
        Assert.Equal(["B", "A"], main.Tree.Nodes.Select(node => node.Path));
        var restarted = harness.Main();
        await restarted.LoadAsync();
        Assert.Equal(["B", "A"], restarted.Tree.Nodes.Select(node => node.Path));
    }

    [Fact]
    public async Task MoveAsync_WhenARequestAndFolderShareAName_ThenKeepsTheirOrderKeysSeparate()
    {
        using var harness = new Harness();
        await harness.Library.CreateFolderAsync("Same", Cancellation);
        await harness.Library.SaveAsync("Same", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.MoveAsync(main.Tree.Nodes.Single(node => !node.IsFolder), main.Tree.Nodes.Single(node => node.IsFolder), DropPosition.Before);
        var restarted = harness.Main();
        await restarted.LoadAsync();
        Assert.Equal([false, true], restarted.Tree.Nodes.Select(node => node.IsFolder));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Other")]
    [InlineData("bad/name")]
    public async Task RenameTabAsync_WhenCancelledOrInvalid_ThenLeavesTheSavedRequestAlone(string? answer)
    {
        using var harness = new Harness(new FakeDialogs(answer: answer));
        await harness.Library.SaveAsync("Original", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAsync("Other", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(Node(main, "Original"));
        await main.RenameTabAsync(main.SelectedTab!);
        Assert.Equal("Original", main.SelectedTab!.Name);
        Assert.True(harness.Library.Exists("Original"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenameTabAsync_WhenOpenedFromHistory_ThenSavesAnIndependentCopyInTheSameFolder(bool inFolder)
    {
        using var harness = new Harness(new FakeDialogs(answer: "Copy"));
        var request = ApiRequest.New();
        var original = inFolder ? "Folder/Original" : "Original";
        var renamed = inFolder ? "Folder/Copy" : "Copy";
        await harness.Library.SaveFolderAsync("Folder", new() { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) }, Cancellation);
        await harness.Library.SaveAsync(original, request, Cancellation);
        await harness.History().AddAsync(new(DateTimeOffset.Now, HistorySource.App, "dev", request, Name: original), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.OpenAsync(main.History.Items.Single());
        await main.RenameTabAsync(main.SelectedTab!);
        Assert.Equal("Copy", main.SelectedTab!.Title);
        Assert.Equal(renamed, main.SelectedTab.Name);
        Assert.False(main.SelectedTab.IsPreview);
        Assert.False(main.SelectedTab.IsDirty);
        Assert.Equal(inFolder ? "Folder" : null, main.SelectedTab.InheritedAuthFolder);
        Assert.NotEqual(request.Id, main.SelectedTab.Id);
        Assert.Equivalent(request, await harness.Library.LoadAsync(original, Cancellation));
        Assert.Equivalent(request with { Id = main.SelectedTab.Id }, await harness.Library.LoadAsync(renamed, Cancellation));
        Assert.Equal([$"{main.SelectedTab.Id}"], main.Session.Requests);
        Assert.Equal(1, harness.Dialogs.Asked);
    }

    [Fact]
    public async Task RenameTabAsync_WhenTheDraftIsAtRoot_ThenSavesItsBodyAndSecrets()
    {
        using var harness = new Harness(new FakeDialogs(answer: "Saved"));
        var main = harness.Main();
        await main.LoadAsync();
        var tab = main.SelectedTab!;
        tab.Editor.Body = "body";
        tab.Auth.Kind = AuthKind.Bearer;
        tab.Auth.Token = "secret";

        await main.RenameTabAsync(tab);

        Assert.Equal("Saved", tab.Name);
        Assert.False(tab.IsUnsaved);
        Assert.Equivalent(tab.ToRequest(), await harness.Library.LoadAsync("Saved", Cancellation));
        Assert.Equal("secret", await harness.Secrets.OfAsync(tab.Id, SecretKind.Token, Cancellation));
        Assert.Equal(1, harness.Dialogs.Asked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Taken")]
    [InlineData("bad/name")]
    public async Task RenameTabAsync_WhenDraftNamingIsCancelledOrInvalid_ThenDoesNotSave(string? answer)
    {
        using var harness = new Harness(new FakeDialogs(answer: answer));
        await harness.Library.SaveAsync("Folder/Taken", ApiRequest.New(), Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(Node(main, "Folder"));
        var tab = main.SelectedTab!;
        tab.Editor.Body = "unsaved";

        await main.RenameTabAsync(tab);

        Assert.Null(tab.Name);
        Assert.True(tab.IsDraft);
        Assert.Equal("Folder", tab.Destination);
        Assert.Equal("unsaved", tab.Editor.Body);
        Assert.Equal(["Folder/Taken"], await harness.Library.NamesAsync(Cancellation));
    }

    [Fact]
    public async Task RenameTabAsync_WhenTheDraftCannotBeSaved_ThenKeepsItsContentAndDestination()
    {
        using var harness = new Harness(new FakeDialogs(answer: "Blocked"));
        await harness.Library.CreateFolderAsync("Folder", Cancellation);
        var main = harness.Main();
        await main.LoadAsync();
        await main.NewDraftAsync(Node(main, "Folder"));
        var tab = main.SelectedTab!;
        tab.Editor.Body = "unsaved";
        Directory.CreateDirectory(Path.Combine(harness.Folder.Requests, "Folder", "Blocked.json"));

        await main.RenameTabAsync(tab);

        Assert.Null(tab.Name);
        Assert.True(tab.IsDraft);
        Assert.True(tab.IsDirty);
        Assert.Equal("Folder", tab.Destination);
        Assert.Equal("unsaved", tab.Editor.Body);
        Assert.NotNull(tab.Problem);
        Assert.False(harness.Library.Exists("Folder/Blocked"));
    }

    [Fact]
    public async Task CreateAsync_WhenTheNameWasTaken_ThenNeverOverwritesTheExistingRequest()
    {
        using var harness = new Harness();
        var original = ApiRequest.New() with { Body = "original" };
        await harness.Library.SaveAsync("Taken", original, Cancellation);
        await Assert.ThrowsAsync<IOException>(() => harness.Library.CreateAsync("Taken", ApiRequest.New(), Cancellation));
        Assert.Equivalent(original, await harness.Library.LoadAsync("Taken", Cancellation));
    }

    [Fact]
    public async Task MoveTab_WhenARequestIsSending_ThenKeepsTheSameRequestAndItsResponse()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<ApiResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = new Harness(send: () =>
        {
            started.SetResult();
            return response.Task;
        });
        var main = harness.Main();
        await main.LoadAsync();
        var first = main.SelectedTab!;
        main.NewTab();
        var active = main.SelectedTab!;
        active.Editor.Url = "https://example.test";
        var sending = active.SendAsync();
        try
        {
            await started.Task.WaitAsync(Cancellation);
            main.MoveTab(active, first, after: false);
            Assert.Same(active, main.Tabs[0]);
            Assert.Same(active, main.SelectedTab);
            Assert.True(active.IsSending);
        }
        finally
        {
            response.SetResult(new(200, "OK", 0, 2, [], "{}"));
            await sending;
        }
        Assert.NotNull(active.Result.Response);
        Assert.False(active.IsSending);
    }
}
