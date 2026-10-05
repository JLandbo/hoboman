using System.Net.Http;
using Hoboman.Tests.Auth;
using Hoboman.Tests.Requests;

namespace Hoboman.Tests.ViewModels;

public sealed class RequestTabViewModelTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // A draft in the folder at the path, which is made when it is not there. The tree is read, as a tab shows its folder from it.
    static async Task<RequestTabViewModel> DraftAsync(Harness harness, string? destination, int number = 0, ApiRequest? request = null)
    {
        var folder = destination is null ? null : await harness.Library.FolderAtAsync(destination);
        await harness.Tree.LoadAsync(CancellationToken.None);
        return new(harness.Services, (request ?? ApiRequest.New()) with { FolderId = folder }, draft: true) { Number = number };
    }

    [Theory]
    [InlineData("Users", 1, false)]
    [InlineData("Users/Admin", 4, true)]
    [InlineData("Users/Admin/Private", 12, false)]
    [InlineData(null, 2, true)]
    public async Task Draft_WhenSentAndRelabelled_ThenAsksForTheCurrentTitleAndStaysADraft(string? destination, int number, bool danish)
    {
        using var harness = new Harness();
        var tab = await DraftAsync(harness, destination, number);
        harness.Translator.Use(danish ? Translation.Danish : Translation.English);
        tab.Relabel();
        var title = harness.Translator.Format("Tab.New", number);

        await tab.SendAsync();
        await tab.SaveAsync();

        Assert.Equal(destination is null ? null : $"{destination.Replace("/", " / ")} /", tab.Folder);
        var question = harness.Dialogs.NameQuestion!.Value;
        Assert.Equal(title, question.Name);
        Assert.Equal(harness.Translator.Of("Save.Title"), question.Title);
        Assert.Equal(harness.Translator.Of("Common.Save"), question.Confirm);
        Assert.True(tab.IsDraft);
        Assert.True(tab.IsUnsaved);
        Assert.False(tab.IsDirty);
        Assert.Empty(await harness.Library.PathsAsync(Cancellation));
        harness.Translator.Use(danish ? Translation.English : Translation.Danish);
        tab.Relabel();
        Assert.NotEqual(title, tab.Title);
        Assert.Equal(number, tab.Number);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task IsUnsaved_WhenEditedAndSent_ThenIncludesDraftsEvenWithoutEdits(bool draft, bool fail)
    {
        using var harness = new Harness(send: fail ? () => throw new HttpRequestException("Failed") : null);
        var tab = draft ? await DraftAsync(harness, "Users") : harness.Tab();
        var changes = new List<string?>();
        tab.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        Assert.Equal(draft, tab.IsUnsaved);
        Assert.False(tab.IsDirty);
        Assert.Equal(AuthKind.Inherit, tab.Auth.Kind);
        Assert.Equal("GET", tab.Editor.Method);
        Assert.Equal("", tab.Editor.Url);
        Assert.Equal("", tab.Editor.Body);

        tab.Editor.Body = "content";
        await tab.SendAsync();
        await tab.UpdateAuthSourceAsync();

        Assert.True(tab.IsDirty);
        Assert.True(tab.IsUnsaved);
        Assert.Contains(nameof(RequestTabViewModel.IsUnsaved), changes);
        Assert.Equal(fail, tab.Problem is not null);
        Assert.Empty(await harness.Library.PathsAsync(Cancellation));
    }

    [Fact]
    public async Task Draft_WhenFetchingTokensAndSending_ThenKeepsItsDotWithoutCreatingARequestFile()
    {
        using var harness = new Harness();
        var tab = await DraftAsync(harness, "Users", request: ApiRequest.New() with { Auth = new(AuthKind.OAuth2) });

        await tab.Auth.FetchTokenAsync();
        await tab.SendAsync();
        await tab.UpdateAuthSourceAsync();

        Assert.True(tab.IsDraft);
        Assert.True(tab.IsUnsaved);
        Assert.False(tab.IsDirty);
        Assert.Empty(await harness.Library.PathsAsync(Cancellation));
        Assert.NotNull(await harness.Secrets.OfAsync(tab.Id, SecretKind.OAuthToken, Cancellation));
    }

    [Fact]
    public async Task Draft_WhenItsFolderChanges_ThenUsesTheNearestFoldersAuth()
    {
        using var harness = new Harness();
        var parent = new RequestFolder { Id = Guid.NewGuid(), Auth = new(AuthKind.OAuth2) };
        var child = new RequestFolder { Id = Guid.NewGuid(), Auth = new(AuthKind.Basic, "user") };
        await harness.Library.SaveFolderAtAsync("Users", parent, Cancellation);
        await harness.Library.SaveFolderAtAsync("Users/Admin", child, Cancellation);
        var tab = await DraftAsync(harness, "Users/Admin/Private");
        await tab.UpdateAuthSourceAsync();
        Assert.Equal("Users / Admin", tab.InheritedAuthFolder);
        await tab.SendAsync();
        Assert.Equal(child.Id, harness.Sender.Auth?.SecretsId);

        tab.MoveTo(await harness.Library.FolderAtAsync("Users/Other", Cancellation));
        await tab.UpdateAuthSourceAsync();

        Assert.True(tab.HasInheritedAuth);
        Assert.True(tab.HasOAuth);
        Assert.Equal(harness.Translator.Format("Auth.InheritedFrom", "Users"), tab.AuthSourceTip);
        Assert.Equal(harness.Translator.Format("OAuth.Reauthenticate", "Users", harness.Translator.Of("Environment.None")), tab.RefreshAuthTip);
        await tab.SendAsync();
        Assert.Equal(parent.Id, harness.Sender.Auth?.SecretsId);
        tab.MoveTo(null);
        await tab.UpdateAuthSourceAsync();
        Assert.False(tab.HasInheritedAuth);
        Assert.Equal("Auth (None)", tab.AuthHeader);
    }

    [Theory]
    [InlineData("Users")]
    [InlineData("Other")]
    [InlineData(null)]
    [InlineData("New/Deep")]
    public async Task SaveAsync_WhenSavingADraft_ThenKeepsItsFolderAndUsesOnlyTheEnteredName(string? destination)
    {
        using var harness = new Harness(new FakeDialogs(answer: "Saved"));
        var name = destination is null ? "Saved" : $"{destination}/Saved";
        await harness.Library.SaveFolderAtAsync("Other", new() { Id = Guid.NewGuid(), Auth = new(AuthKind.Basic, "user") }, Cancellation);
        if (destination is not null)
        {
            await harness.Library.SaveAtAsync("Saved", ApiRequest.New() with { Body = "root request" }, Cancellation);
        }
        // Without a folder the tab is no draft, but a new request at the top.
        var tab = destination is null ? new RequestTabViewModel(harness.Services, ApiRequest.New()) { Number = 3 } : await DraftAsync(harness, destination, 3);
        tab.Editor.Body = "content";

        await tab.SaveAsync();

        Assert.Equal(("Saved", name), (tab.Name, harness.Tree.PathOf(tab)));
        Assert.Equal(destination is null ? "" : "New request (3)", harness.Dialogs.NameQuestion!.Value.Name);
        Assert.Equal("Saved", tab.Title);
        Assert.False(tab.IsDraft);
        Assert.False(tab.IsUnsaved);
        Assert.Equal(destination, harness.Tree.PathOfFolder(tab.FolderId));
        Assert.Equal("content", (await harness.Library.LoadAtAsync(name, Cancellation))?.Body);
        Assert.Equal(name.StartsWith("Other/") ? "Other" : null, tab.InheritedAuthFolder);
        if (destination is not null)
        {
            Assert.Equal("root request", (await harness.Library.LoadAtAsync("Saved", Cancellation))!.Body);
        }
        tab.Unlink();
        Assert.False(tab.IsDraft);
        Assert.True(tab.IsUnsaved);
        Assert.Equal(destination, harness.Tree.PathOfFolder(tab.FolderId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("Get\tuser")]
    public async Task SaveAsync_WhenADraftNameIsCancelledOrInvalid_ThenKeepsTheDraft(string? answer)
    {
        using var harness = new Harness(new FakeDialogs(answer: answer));
        await harness.Library.SaveAtAsync("Users/Taken", ApiRequest.New(), Cancellation);
        var tab = await DraftAsync(harness, "Users", 3);
        tab.Editor.Body = "content";

        await tab.SaveAsync();

        Assert.Equal((true, true, true, "Users", "New request (3)", "content"), (tab.IsDraft, tab.IsDirty, tab.IsUnsaved, harness.Tree.PathOfFolder(tab.FolderId), tab.Title, tab.Editor.Body));
        Assert.Null(tab.Name);
        Assert.Equal(["Users/Taken"], await harness.Library.PathsAsync(Cancellation));
        var problemOf = harness.Dialogs.NameQuestion!.Value.ProblemOf;
        Assert.Null(problemOf("taken"));
        Assert.Equal(harness.Translator.Of("Save.Invalid"), problemOf(" "));
        Assert.Null(problemOf("Other/Saved: v2?"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveAsync_WhenADraftCannotBeWritten_ThenKeepsItsFolderAndContent(bool secrets)
    {
        using var harness = new Harness(new FakeDialogs(answer: "Saved"));
        var tab = await DraftAsync(harness, "Users", 3);
        tab.Editor.Body = "content";
        Directory.CreateDirectory(harness.Folder.Root);
        if (secrets)
        {
            Directory.CreateDirectory(harness.Folder.Secrets);
            tab.Auth.Password = "secret";
        }
        else
        {
            Directory.CreateDirectory(Path.Combine(harness.Folder.Requests, $"{tab.Id}.json"));
        }

        await tab.SaveAsync();

        Assert.NotNull(tab.Problem);
        Assert.Null(tab.Name);
        Assert.Equal((true, true, "Users", "New request (3)", "content"), (tab.IsDraft, tab.IsUnsaved, harness.Tree.PathOfFolder(tab.FolderId), tab.Title, tab.Editor.Body));
        Assert.Empty(await harness.Library.PathsAsync(Cancellation));
    }

    [Fact]
    public async Task Unlink_WhenItsFileIsRemoved_ThenKeepsContentAsAnUnsavedTabInItsFolder()
    {
        using var harness = new Harness();
        await harness.Library.SaveFolderAtAsync("Users", new() { Auth = new(AuthKind.Basic, "user") }, Cancellation);
        var request = await harness.Library.SaveAtAsync("Users/Get", ApiRequest.New(), Cancellation);
        await harness.Tree.LoadAsync(Cancellation);
        var tab = harness.Tab(request, saved: true);
        await tab.UpdateAuthSourceAsync();
        tab.Editor.Body = "content";

        tab.Unlink();
        await tab.UpdateAuthSourceAsync();
        await tab.SaveAsync();

        Assert.Equal((false, true, false, "content", "Get"), (tab.IsDraft, tab.IsDirty, tab.IsSaved, tab.Editor.Body, tab.Title));
        Assert.Equal("Users /", tab.Folder);
        Assert.True(tab.HasInheritedAuth);
        Assert.Equal("Get", harness.Dialogs.NameQuestion!.Value.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveAsync_WhenANonDraftHasNoFile_ThenUsesItsSuggestedFolderButOnlyShowsTheName(bool history)
    {
        using var harness = new Harness(new FakeDialogs(answer: "Copy"));
        var users = await harness.Library.FolderAtAsync("Users", Cancellation);
        await harness.Tree.LoadAsync(Cancellation);
        var tab = new RequestTabViewModel(harness.Services, history ? ApiRequest.New() with { Name = "Get", FolderId = users } : ApiRequest.New(), historyName: history ? "call.json" : null);

        await tab.SaveAsync();

        Assert.Equal(history ? "Get" : "", harness.Dialogs.NameQuestion!.Value.Name);
        Assert.Equal("Copy", tab.Name);
        Assert.Equal([history ? "Users/Copy" : "Copy"], await harness.Library.PathsAsync(Cancellation));
        Assert.True(harness.Library.Exists(tab.Id));
    }

    [Fact]
    public async Task SaveAsync_WhenADraftIsEditedDuringItsFirstSave_ThenKeepsTheNewEditsUnsaved()
    {
        using var harness = new Harness(new FakeDialogs(answer: "Saved"));
        var tab = await DraftAsync(harness, "Users");
        tab.Editor.Body = "saved content";

        var saving = tab.SaveAsync();
        tab.Editor.Body = "new content";
        await saving;

        Assert.False(tab.IsDraft);
        Assert.Equal("Users", harness.Tree.PathOfFolder(tab.FolderId));
        Assert.True(tab.IsDirty);
        Assert.True(tab.IsUnsaved);
        Assert.Equal("new content", tab.Editor.Body);
        Assert.Equal("saved content", (await harness.Library.LoadAtAsync("Users/Saved", Cancellation))?.Body);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendAsync_WhenADraftCannotUseItsFoldersAuth_ThenKeepsItsContentAndFolder(bool invalidFile)
    {
        using var harness = new Harness(send: invalidFile ? null : () => throw new MissingSecretException(SecretKind.OAuthToken));
        var tab = await DraftAsync(harness, "Users");
        if (invalidFile)
        {
            File.WriteAllText(Path.Combine(harness.Folder.Folders, $"{tab.FolderId}.json"), "{");
        }
        tab.Editor.Body = "content";

        await tab.SendAsync();

        Assert.NotNull(tab.Problem);
        Assert.Contains(invalidFile ? $"{tab.FolderId}.json" : "OAuth", tab.Problem.Details);
        Assert.Equal((true, "content"), (tab.IsDraft, tab.Editor.Body));
        Assert.Empty(await harness.Library.PathsAsync(Cancellation));
    }

    [Fact]
    public async Task SendAsync_WhenTheCallSucceeds_ThenShowsTheResponse()
    {
        // Arrange
        using var harness = new Harness();
        var tab = harness.Tab();

        // Act
        await tab.SendAsync();

        // Assert
        Assert.Equal(("200 OK", "{}"), (tab.Result.Response?.Status, tab.Result.Response?.Body));
    }

    [Fact]
    public async Task SendAsync_WhenTheTokenIsMissing_ThenShowsTheProblem()
    {
        // Arrange
        using var harness = new Harness(send: () => throw new MissingSecretException(SecretKind.Token));
        var tab = harness.Tab();

        // Act
        await tab.SendAsync();

        // Assert
        Assert.Equal(new ProblemMessage("The request could not be sent", "No token is saved for the request or its folder."), tab.Problem);
    }

    [Fact]
    public async Task SendAsync_WhenTheOAuthTokenIsMissing_ThenSaysHowToGetOne()
    {
        // Arrange
        using var harness = new Harness(send: () => throw new MissingSecretException(SecretKind.OAuthToken));
        var tab = harness.Tab();

        // Act
        await tab.SendAsync();

        // Assert
        Assert.Equal("No OAuth token has been fetched for the request or its folder in the chosen environment. Get one under Auth.", tab.Problem?.Details);
    }

    [Fact]
    public async Task SendAsync_WhenTheOAuthTokenHasExpired_ThenSaysWhen()
    {
        // Arrange
        var expired = new DateTimeOffset(2026, 9, 30, 11, 0, 0, TimeSpan.Zero);
        using var harness = new Harness(send: () => throw new ExpiredTokenException(expired));
        var tab = harness.Tab();

        // Act
        await tab.SendAsync();

        // Assert
        Assert.Equal($"The OAuth token expired {expired.ToLocalTime().ToString("g", Translation.English.Culture)}. Get a new one under Auth.", tab.Problem?.Details);
    }

    [Fact]
    public async Task SendAsync_WhenTheOAuthTokenIsMissingAndCanBeFetchedUnasked_ThenFetchesOneAndSendsAgain()
    {
        // Arrange
        var calls = 0;
        using var harness = new Harness(send: () => ++calls == 1 ? throw new MissingSecretException(SecretKind.OAuthToken) : Task.FromResult(new ApiResponse(200, "OK", 0, 2, [], "{}")));
        var tab = harness.Tab(ApiRequest.New() with { Auth = new(AuthKind.OAuth2) });

        // Act
        await tab.SendAsync();

        // Assert
        Assert.Equal(("200 OK", 2), (tab.Result.Response?.Status, calls));
    }

    [Fact]
    public async Task SendAsync_WhenAnOAuthTokenWasFetched_ThenSavesItFirst()
    {
        // Arrange
        Func<Task<string?>> savedToken = () => Task.FromResult<string?>(null);
        string? seenWhenSending = null;
        using var harness = new Harness(send: async () =>
        {
            seenWhenSending = await savedToken();
            return new ApiResponse(200, "OK", 0, 2, [], "{}");
        });
        var tab = harness.Tab(ApiRequest.New() with { Auth = new(AuthKind.OAuth2) });
        savedToken = () => harness.Secrets.OfAsync(tab.Id, SecretKind.OAuthToken, CancellationToken.None);
        await tab.Auth.FetchTokenAsync();

        // Act
        await tab.SendAsync();

        // Assert
        Assert.NotNull(seenWhenSending);
    }

    [Fact]
    public async Task SendAsync_WhenOnlyAFetchedTokenWasUnsaved_ThenTheRequestIsNoLongerMarkedUnsaved()
    {
        // Arrange
        using var harness = new Harness();
        var request = await harness.Library.SaveAtAsync("Ping", ApiRequest.New() with { Url = "https://dev.local", Auth = new(AuthKind.OAuth2) }, Cancellation);
        var tab = harness.Tab(request, saved: true);
        await tab.Auth.FetchTokenAsync();

        // Act
        await tab.SendAsync();

        // Assert
        Assert.False(tab.IsDirty);
    }

    [Theory]
    [InlineData(RequestProblemKind.NetworkFailed, "RequestProblem.NetworkFailed")]
    [InlineData(RequestProblemKind.MissingOAuthToken, "Response.MissingOAuthToken")]
    public async Task ShowAsync_WhenACallFromTheHistoryFailed_ThenTellsItsProblemInTheLanguage(RequestProblemKind kind, string key)
    {
        // Arrange
        using var harness = new Harness();
        harness.Translator.Use(Translation.Danish);
        var tab = harness.Tab();
        var entry = new HistoryEntry(DateTimeOffset.Now, HistorySource.App, "dev.local", ApiRequest.New(), Error: "No such host is known.", Problem: kind);

        // Act
        await tab.ShowAsync(entry);

        // Assert
        Assert.Equal(harness.Translator.Of(key), tab.Problem?.Details);
    }

    [Fact]
    public async Task FetchToken_WhenFetchedByHandForASavedRequest_ThenSavesItAndIsNoEdit()
    {
        // Arrange
        using var harness = new Harness();
        var request = await harness.Library.SaveAtAsync("Ping", ApiRequest.New() with { Url = "https://dev.local", Auth = new(AuthKind.OAuth2) }, Cancellation);
        var tab = harness.Tab(request, saved: true);

        // Act
        await tab.Auth.OwnerFetch!();

        // Assert
        Assert.Equal((false, 1), (tab.IsDirty, (await harness.Secrets.OfEachEnvironmentAsync(tab.Id, SecretKind.OAuthToken, Cancellation)).Count));
    }

    [Fact]
    public async Task SendAsync_WhenAnotherEnvironmentIsChosenWhileSaving_ThenSendsWithTheOneChosenAtSend()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Environments.ChooseAsync(new("Dev", []) { Id = Guid.NewGuid() });
        var tab = harness.Tab();
        tab.Auth.Password = "hemmelig";
        var sending = tab.SendAsync();

        // Act
        await harness.Environments.ChooseAsync(new("Prod", []) { Id = Guid.NewGuid() });
        await sending;

        // Assert
        Assert.Equal("Dev", harness.Sender.Environment?.Name);
    }

    [Fact]
    public async Task Cancel_WhenATokenIsBeingFetched_ThenOnlyStopsTheSend()
    {
        // Arrange
        var login = new TaskCompletionSource<OAuthToken>();
        using var harness = new Harness(oauth: new FakeOAuthClient(cancellationToken => login.Task.WaitAsync(cancellationToken)));
        var tab = harness.Tab();
        var fetching = tab.Auth.FetchTokenAsync();

        // Act
        tab.Cancel();
        login.SetResult(FakeOAuthClient.Token);
        await fetching;

        // Assert
        Assert.Equal(FakeOAuthClient.Token, tab.Auth.AccessToken);
    }

    [Fact]
    public async Task SendAsync_WhenTheServerCannotBeFound_ThenSaysSoWithTheCauseBelow()
    {
        // Arrange
        using var harness = new Harness(send: () => throw new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known."));
        var tab = harness.Tab();

        // Act
        await tab.SendAsync();

        // Assert
        Assert.Equal($"The server could not be found.{Environment.NewLine}No such host is known.", tab.Problem?.Details);
    }

    [Fact]
    public async Task Cancel_WhenSending_ThenStopsWithoutAProblem()
    {
        // Arrange
        using var harness = new Harness(send: () => new TaskCompletionSource<ApiResponse>().Task);
        var tab = harness.Tab();
        var sending = tab.SendAsync();

        // Act
        tab.Cancel();
        await sending.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

        // Assert
        Assert.Null(tab.Problem);
    }

    [Fact]
    public void Send_WhenTheUrlIsEmpty_ThenCannotBeUsed()
    {
        // Arrange
        using var harness = new Harness();
        var tab = harness.Tab();

        // Act
        tab.Editor.Url = "   ";

        // Assert
        Assert.False(tab.Send.CanExecute(null));
    }

    [Fact]
    public void Send_WhenAUrlIsTyped_ThenCanBeUsed()
    {
        // Arrange
        using var harness = new Harness();
        var tab = harness.Tab();

        // Act
        tab.Editor.Url = "{{base}}/users";

        // Assert
        Assert.True(tab.Send.CanExecute(null));
    }

    [Fact]
    public void Url_WhenChanged_ThenTellsTheSendButton()
    {
        // Arrange
        using var harness = new Harness();
        var tab = harness.Tab();
        var told = false;
        tab.Send.CanExecuteChanged += (_, _) => told = true;

        // Act
        tab.Editor.Url = "https://dev.local";

        // Assert
        Assert.True(told);
    }

    [Fact]
    public void Url_WhenChanged_ThenMarksTheTabUnsaved()
    {
        // Arrange
        using var harness = new Harness();
        var tab = harness.Tab();

        // Act
        tab.Editor.Url = "https://dev.local";

        // Assert
        Assert.True(tab.IsDirty);
    }

    [Fact]
    public void ReloadIfChanged_WhenTheFileIsWhatTheTabLoaded_ThenKeepsTheEdits()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New() with { Url = "https://dev.local" };
        var tab = harness.Tab(request with { Name = "Ping" }, saved: true);
        tab.Editor.Url = "https://edited.local";

        // Act
        tab.ReloadIfChanged(request);

        // Assert
        Assert.Equal("https://edited.local", tab.Editor.Url);
    }

    [Fact]
    public void ReloadIfChanged_WhenTheFileIsWhatTheTabLoaded_ThenSaysNothingChanged()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New() with { Url = "https://dev.local" };
        var tab = harness.Tab(request with { Name = "Ping" }, saved: true);
        tab.Editor.Url = "https://edited.local";

        // Act
        var reloaded = tab.ReloadIfChanged(request);

        // Assert
        Assert.False(reloaded);
    }

    [Fact]
    public void ReloadIfChanged_WhenTheFileChanged_ThenShowsTheFile()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New() with { Url = "https://dev.local" };
        var tab = harness.Tab(request with { Name = "Ping" }, saved: true);
        tab.Editor.Url = "https://edited.local";

        // Act
        tab.ReloadIfChanged(request with { Url = "https://agent.local" });

        // Assert
        Assert.Equal("https://agent.local", tab.Editor.Url);
    }

    [Fact]
    public void ReloadIfChanged_WhenTheFileChanged_ThenTheTabIsSaved()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New() with { Url = "https://dev.local" };
        var tab = harness.Tab(request with { Name = "Ping" }, saved: true);
        tab.Editor.Url = "https://edited.local";

        // Act
        tab.ReloadIfChanged(request with { Url = "https://agent.local" });

        // Assert
        Assert.False(tab.IsDirty);
    }

    [Fact]
    public void ReloadIfChanged_WhenTheFileChanged_ThenSaysSo()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New() with { Url = "https://dev.local" };
        var tab = harness.Tab(request with { Name = "Ping" }, saved: true);
        tab.Editor.Url = "https://edited.local";

        // Act
        var reloaded = tab.ReloadIfChanged(request with { Url = "https://agent.local" });

        // Assert
        Assert.True(reloaded);
    }

    [Fact]
    public async Task LoadSecretsAsync_WhenATokenIsSaved_ThenShowsIt()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New() with { Auth = new(AuthKind.Bearer) };
        await harness.Secrets.SaveAsync(request.Id, SecretKind.Token, "token", Cancellation);
        var tab = harness.Tab(request with { Name = "Ping" }, saved: true);

        // Act
        await tab.LoadSecretsAsync(Cancellation);

        // Assert
        Assert.Equal("token", tab.Auth.Token);
    }

    [Fact]
    public async Task LoadSecretsAsync_WhenTheRequestHasNoId_ThenClearsTheSecrets()
    {
        // Arrange
        using var harness = new Harness();
        var tab = harness.Tab(new ApiRequest { Name = "Ping", Url = "https://dev.local" }, saved: true);
        tab.Auth.Token = "old";

        // Act
        await tab.LoadSecretsAsync(Cancellation);

        // Assert
        Assert.Empty(tab.Auth.Token);
    }

    [Fact]
    public async Task SaveAsync_WhenTheTokenChanged_ThenSavesIt()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New() with { Auth = new(AuthKind.Bearer) };
        var tab = harness.Tab(request with { Name = "Ping" }, saved: true);
        tab.Auth.Token = "token";

        // Act
        await tab.SaveAsync();

        // Assert
        Assert.Equal("token", await harness.Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task SendAsync_WhenAHistoryTabChangesTheToken_ThenLeavesTheOriginalTokenAlone()
    {
        // Arrange
        using var harness = new Harness();
        var original = ApiRequest.New() with { Auth = new(AuthKind.Bearer) };
        await harness.Secrets.SaveAsync(original.Id, SecretKind.Token, "original", Cancellation);
        var tab = new RequestTabViewModel(harness.Services, original, historyName: "call.json");
        await tab.LoadSecretsAsync(Cancellation);
        tab.Auth.Token = "changed";

        // Act
        await tab.SendAsync();

        // Assert
        Assert.Equal("original", await harness.Secrets.OfAsync(original.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task SendAsync_WhenAHistoryTabChangesTheToken_ThenSavesItUnderItsOwnId()
    {
        // Arrange
        using var harness = new Harness();
        var original = ApiRequest.New() with { Auth = new(AuthKind.Bearer) };
        await harness.Secrets.SaveAsync(original.Id, SecretKind.Token, "original", Cancellation);
        var tab = new RequestTabViewModel(harness.Services, original, historyName: "call.json");
        await tab.LoadSecretsAsync(Cancellation);
        tab.Auth.Token = "changed";

        // Act
        await tab.SendAsync();

        // Assert
        Assert.Equal("changed", await harness.Secrets.OfAsync(tab.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task SaveAsync_WhenAHistoryTabIsSaved_ThenGetsItsOwnId()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Copy"));
        var original = ApiRequest.New() with { Auth = new(AuthKind.Bearer) };
        await harness.Secrets.SaveAsync(original.Id, SecretKind.Token, "token", Cancellation);
        var tab = new RequestTabViewModel(harness.Services, original, historyName: "call.json");
        await tab.LoadSecretsAsync(Cancellation);

        // Act
        await tab.SaveAsync();

        // Assert
        Assert.NotEqual(original.Id, (await harness.Library.LoadAtAsync("Copy", Cancellation))?.Id);
    }

    [Fact]
    public async Task SaveAsync_WhenAHistoryTabIsSaved_ThenTakesTheSecretsAlong()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Copy"));
        var original = ApiRequest.New() with { Auth = new(AuthKind.Bearer) };
        await harness.Secrets.SaveAsync(original.Id, SecretKind.Token, "token", Cancellation);
        var tab = new RequestTabViewModel(harness.Services, original, historyName: "call.json");
        await tab.LoadSecretsAsync(Cancellation);

        // Act
        await tab.SaveAsync();

        // Assert
        Assert.Equal("token", await harness.Secrets.OfAsync((await harness.Library.LoadAtAsync("Copy", Cancellation))!.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task SaveAsync_WhenTheSecretsCannotBeSaved_ThenWritesNoRequestFile()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Ping"));
        Directory.CreateDirectory(harness.Folder.Root);
        File.WriteAllText(harness.Folder.Secrets, "{");
        var tab = harness.Tab();
        tab.Auth.Token = "token";

        // Act
        await tab.SaveAsync();

        // Assert
        Assert.False(harness.Library.ExistsAt("Ping"));
    }

    [Fact]
    public async Task SaveAsync_WhenTheSecretsCannotBeSaved_ThenShowsTheProblem()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Ping"));
        Directory.CreateDirectory(harness.Folder.Root);
        File.WriteAllText(harness.Folder.Secrets, "{");
        var tab = harness.Tab();
        tab.Auth.Token = "token";

        // Act
        await tab.SaveAsync();

        // Assert
        Assert.Equal("The request could not be saved", tab.Problem?.Title);
    }

    [Fact]
    public async Task SaveAsync_WhenTheTabIsNew_ThenSavesUnderTheGivenName()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Ping"));
        var tab = harness.Tab();
        tab.Editor.Url = "https://dev.local";

        // Act
        await tab.SaveAsync();

        // Assert
        Assert.Equal("https://dev.local", (await harness.Library.LoadAtAsync("Ping", Cancellation))?.Url);
    }

    [Fact]
    public async Task SaveAsync_WhenTheTabIsNew_ThenTakesTheGivenName()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Ping"));
        var tab = harness.Tab();
        tab.Editor.Url = "https://dev.local";

        // Act
        await tab.SaveAsync();

        // Assert
        Assert.Equal("Ping", tab.Name);
    }

    [Fact]
    public async Task SaveAsync_WhenTheTabIsNew_ThenIsNoLongerUnsaved()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Ping"));
        var tab = harness.Tab();
        tab.Editor.Url = "https://dev.local";

        // Act
        await tab.SaveAsync();

        // Assert
        Assert.False(tab.IsDirty);
    }

    [Fact]
    public async Task SaveAsync_WhenTheNameIsNotGiven_ThenSavesNothing()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: null));
        var tab = harness.Tab();
        tab.Editor.Url = "https://dev.local";

        // Act
        await tab.SaveAsync();

        // Assert
        Assert.Empty(await harness.Library.PathsAsync(Cancellation));
    }

    [Fact]
    public void RequestSection_WhenChanged_ThenTheTabStaysSaved()
    {
        // Arrange
        using var harness = new Harness();
        var tab = harness.Tab(ApiRequest.New() with { Name = "Ping" }, saved: true);

        // Act
        tab.RequestSection = RequestSection.Headers;

        // Assert
        Assert.False(tab.IsDirty);
    }

    [Fact]
    public async Task LaidOutBodyAsync_WhenTheBodyIsNotJson_ThenSaysSo()
    {
        // Arrange
        using var harness = new Harness();
        var tab = harness.Tab(ApiRequest.New() with { BodyKind = BodyKind.Json, Body = "{\"a\": " });

        // Act
        var laidOut = await tab.Editor.LaidOutBodyAsync();

        // Assert
        Assert.Equal((null, "The body is not valid JSON"), (laidOut, tab.Editor.BodyLayoutProblem));
    }

    [Fact]
    public async Task LaidOutBodyAsync_WhenAskedAgainWhileItWorks_ThenLeavesTheSecondOut()
    {
        // Arrange
        using var harness = new Harness();
        var body = $"[{string.Join(",", Enumerable.Repeat("""{"a":1}""", 50_000))}]";
        var tab = harness.Tab(ApiRequest.New() with { BodyKind = BodyKind.Json, Body = body });
        var first = tab.Editor.LaidOutBodyAsync();

        // Act
        var second = await tab.Editor.LaidOutBodyAsync();

        // Assert
        Assert.Equal((true, true), (second is null, await first is not null));
    }

    [Fact]
    public async Task LaidOutBodyAsync_WhenTheBodyIsOnlyValidWithItsVariablesFilledIn_ThenSaysSo()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Environments.ChooseAsync(new("Dev", [new("decimal", "5")]));
        var tab = harness.Tab(ApiRequest.New() with { BodyKind = BodyKind.Json, Body = "{\"amount\": 1.{{decimal}}}", UseEnvironmentVariablesInBody = true });

        // Act
        await tab.Editor.LaidOutBodyAsync();

        // Assert
        Assert.Equal("The body is only valid once its variables are filled in, so it cannot be laid out", tab.Editor.BodyLayoutProblem);
    }

    [Fact]
    public async Task Relabel_WhenTheBodyCouldNotBeLaidOut_ThenSaysWhyInTheNewLanguage()
    {
        // Arrange
        using var harness = new Harness();
        var tab = harness.Tab(ApiRequest.New() with { BodyKind = BodyKind.Json, Body = "{\"a\": " });
        await tab.Editor.LaidOutBodyAsync();
        harness.Translator.Use(Translation.Danish);

        // Act
        tab.Relabel();

        // Assert
        Assert.Equal("Bodyen er ikke gyldig JSON", tab.Editor.BodyLayoutProblem);
    }

    [Fact]
    public async Task Body_WhenChangedAfterItCouldNotBeLaidOut_ThenForgetsWhy()
    {
        // Arrange
        using var harness = new Harness();
        var tab = harness.Tab(ApiRequest.New() with { BodyKind = BodyKind.Json, Body = "{\"a\": " });
        await tab.Editor.LaidOutBodyAsync();

        // Act
        tab.Editor.Body = "{\"a\": 1}";

        // Assert
        Assert.Null(tab.Editor.BodyLayoutProblem);
    }

    [Fact]
    public void Base64_WhenAPropertyIsChosen_ThenTheTabIsUnsaved()
    {
        // Arrange
        using var harness = new Harness();
        var tab = harness.Tab(ApiRequest.New() with { Name = "Ping", BodyKind = BodyKind.Json, Body = """{"html": "<p>"}""" }, saved: true);

        // Act
        tab.Editor.Base64.ToggleEncode("$.html");

        // Assert
        Assert.True(tab.IsDirty);
    }

    [Fact]
    public async Task SendAsync_WhenAPropertyIsChosenForBase64_ThenSendsItsPath()
    {
        // Arrange
        using var harness = new Harness();
        var tab = harness.Tab(ApiRequest.New() with { BodyKind = BodyKind.Json, Body = """{"html": "<p>"}""" });
        tab.Editor.Base64.ToggleEncode("$.html");

        // Act
        await tab.SendAsync();

        // Assert
        Assert.Equal(["$.html"], harness.Sender.Request!.Base64!.Encode);
    }

    [Fact]
    public async Task SendAsync_WhenAChosenPropertyIsMissing_ThenSaysWhich()
    {
        // Arrange
        using var harness = new Harness(send: () => throw new MissingBase64PathException("$.html"));
        var tab = harness.Tab();

        // Act
        await tab.SendAsync();

        // Assert
        Assert.Equal("The property $.html is not in the body, so it cannot be sent as Base64.", tab.Problem?.Details);
    }

    [Fact]
    public async Task Base64_WhenAResponseValueIsChosen_ThenShowsItDecoded()
    {
        // Arrange
        var body = $$"""{"html": "{{Base64Text.Encode("<p>Ærø</p>")}}"}""";
        using var harness = new Harness(send: () => Task.FromResult(new ApiResponse(200, "OK", 0, body.Length, [new("Content-Type", "application/json")], body)));
        var tab = harness.Tab();
        await tab.SendAsync();

        // Act
        tab.Editor.Base64.ToggleDecode("$.html");
        await tab.Result.Formatting;

        // Assert
        Assert.Equal((Base64MarkState.Decoded, true), (tab.Result.ResponseMarks.Single().State, tab.Result.Response!.Body.Contains("<p>Ærø</p>")));
    }

    [Fact]
    public async Task SendAsync_WhenTheRequestInherits_ThenSendsWithTheFoldersAuth()
    {
        // Arrange
        using var harness = new Harness();
        var folder = await harness.Library.SaveFolderAtAsync("Users", new() { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) }, Cancellation);
        var tab = harness.Tab(ApiRequest.New() with { Name = "Get user", FolderId = folder.Id }, saved: true);

        // Act
        await tab.SendAsync();

        // Assert
        Assert.Equal(folder.Id, harness.Sender.Auth?.SecretsId);
    }

    [Fact]
    public async Task SendAsync_WhenAHistoryTabInherits_ThenUsesTheFolderOfItsRequest()
    {
        // Arrange
        using var harness = new Harness();
        var folder = await harness.Library.SaveFolderAtAsync("Users", new() { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) }, Cancellation);
        var tab = new RequestTabViewModel(harness.Services, ApiRequest.New() with { Name = "Get user", FolderId = folder.Id }, historyName: "call.json");

        // Act
        await tab.SendAsync();

        // Assert
        Assert.Equal(folder.Id, harness.Sender.Auth?.SecretsId);
    }

    [Fact]
    public async Task SendAsync_WhenAFolderFileIsInvalid_ThenNamesTheFile()
    {
        // Arrange
        using var harness = new Harness();
        var users = await harness.Library.FolderAtAsync("Users", Cancellation);
        File.WriteAllText(Path.Combine(harness.Folder.Folders, $"{users}.json"), "{");
        var tab = harness.Tab(ApiRequest.New() with { Name = "Get user", FolderId = users }, saved: true);

        // Act
        await tab.SendAsync();

        // Assert
        Assert.Contains($"{users}.json", tab.Problem?.Details);
    }

    [Fact]
    public void ReloadIfChanged_WhenTheFileCanBeReadAgain_ThenHidesTheProblem()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New();
        var tab = harness.Tab(request with { Name = "Ping" }, saved: true);
        tab.ShowFileProblem("Ping.json is not valid");

        // Act
        tab.ReloadIfChanged(request);

        // Assert
        Assert.Null(tab.Problem);
    }

    [Fact]
    public async Task SaveAsync_WhenEditedWhileSaving_ThenStaysUnsaved()
    {
        // Arrange
        using var harness = new Harness();
        var request = await harness.Library.SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        var tab = harness.Tab(request, saved: true);
        tab.Editor.Url = "https://saved.local";
        Task saving;

        // Act
        using (new FileStream(Path.Combine(harness.Folder.Requests, $"{request.Id}.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            saving = tab.SaveAsync();
            tab.Editor.Url = "https://edited.local";
        }
        await saving;

        // Assert
        Assert.True(tab.IsDirty);
    }

    [Fact]
    public async Task SaveAsync_WhenAPasswordIsTypedWhileSaving_ThenSavesItNextTime()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New() with { Auth = new(AuthKind.Basic, "hobo") };
        var tab = harness.Tab(request with { Name = "Ping" }, saved: true);
        tab.Auth.Password = "first";
        Directory.CreateDirectory(harness.Folder.Root);
        File.WriteAllText(harness.Folder.Secrets, "{}");
        Task saving;
        using (new FileStream(harness.Folder.Secrets, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            saving = tab.SaveAsync();
            tab.Auth.Password = "second";
        }
        await saving;

        // Act
        await tab.SaveAsync();

        // Assert
        Assert.Equal("second", await harness.Secrets.OfAsync(request.Id, SecretKind.Password, Cancellation));
    }

    [Fact]
    public async Task SaveAsync_WhenThePasswordChanged_ThenSavesIt()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New() with { Auth = new(AuthKind.Basic, "hobo") };
        var tab = harness.Tab(request with { Name = "Ping" }, saved: true);
        tab.Auth.Password = "hemmelig";

        // Act
        await tab.SaveAsync();

        // Assert
        Assert.Equal("hemmelig", await harness.Secrets.OfAsync(request.Id, SecretKind.Password, Cancellation));
    }

    [Fact]
    public void Url_WhenAHistoryTabIsChanged_ThenIsNoLongerFromTheHistory()
    {
        // Arrange
        using var harness = new Harness();
        var tab = new RequestTabViewModel(harness.Services, ApiRequest.New(), historyName: "call.json");

        // Act
        tab.Editor.Url = "https://changed.local";

        // Assert
        Assert.False(tab.FromHistory);
    }

    [Fact]
    public async Task SendAsync_WhenTheTabIsFromTheHistory_ThenNoLongerStandsForThatCall()
    {
        // Arrange
        using var harness = new Harness();
        var tab = new RequestTabViewModel(harness.Services, ApiRequest.New(), historyName: "call.json");

        // Act
        await tab.SendAsync();

        // Assert
        Assert.False(tab.FromHistory);
    }

    [Fact]
    public void ReloadIfChanged_WhenAPasswordIsTypedButNotSaved_ThenStaysUnsaved()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New() with { Auth = new(AuthKind.Basic, "hobo") };
        var tab = harness.Tab(request with { Name = "Ping" }, saved: true);
        tab.Auth.Password = "typed";

        // Act
        tab.ReloadIfChanged(request);

        // Assert
        Assert.True(tab.IsDirty);
    }

    [Fact]
    public async Task SendAsync_WhenTheResponseIsJson_ThenChoosesJson()
    {
        // Arrange
        using var harness = new Harness(send: () => Task.FromResult(new ApiResponse(200, "OK", 0, 7, [new("Content-Type", "application/json")], """{"a":1}""")));
        var tab = harness.Tab();

        // Act
        await tab.SendAsync();

        // Assert
        Assert.Equal(BodyFormat.Json, tab.Result.BodyFormat);
    }

    [Fact]
    public async Task BodyFormat_WhenChanged_ThenShowsTheResponseInThatFormat()
    {
        // Arrange
        using var harness = new Harness(send: () => Task.FromResult(new ApiResponse(200, "OK", 0, 7, [new("Content-Type", "text/plain")], """{"a":1}""")));
        var tab = harness.Tab();
        await tab.SendAsync();

        // Act
        tab.Result.BodyFormat = BodyFormat.Json;
        await tab.Result.Formatting;

        // Assert
        Assert.Equal($"{{{Environment.NewLine}\t\"a\": 1{Environment.NewLine}}}", tab.Result.Response?.Body);
    }

    [Fact]
    public async Task ReloadIfChanged_WhenTheFileDecodesAnotherValue_ThenShowsTheResponseAgain()
    {
        // Arrange
        var body = $$"""{"html": "{{Base64Text.Encode("<p>")}}"}""";
        using var harness = new Harness(send: () => Task.FromResult(new ApiResponse(200, "OK", 0, body.Length, [new("Content-Type", "application/json")], body)));
        var request = ApiRequest.New();
        var tab = harness.Tab(request with { Name = "Ping" }, saved: true);
        await tab.SendAsync();

        // Act
        tab.ReloadIfChanged(request with { Base64 = new() { Decode = ["$.html"] } });
        await tab.Result.Formatting;

        // Assert
        Assert.Equal(Base64MarkState.Decoded, tab.Result.ResponseMarks.Single().State);
    }

    [Fact]
    public async Task DecodesWholeResponse_WhenSet_ThenShowsTheDecodedBody()
    {
        // Arrange
        var encoded = Base64Text.Encode("""{"a":1}""");
        using var harness = new Harness(send: () => Task.FromResult(new ApiResponse(200, "OK", 0, encoded.Length, [new("Content-Type", "application/json")], encoded)));
        var tab = harness.Tab();
        await tab.SendAsync();

        // Act
        tab.Editor.Base64.DecodesWholeResponse = true;
        await tab.Result.Formatting;

        // Assert
        Assert.Equal($"{{{Environment.NewLine}\t\"a\": 1{Environment.NewLine}}}", tab.Result.Response?.Body);
        Assert.Null(tab.Result.ResponseBodyProblem);
    }

    [Fact]
    public async Task DecodesWholeResponse_WhenTheBodyIsNotBase64_ThenKeepsItAndShowsTheProblem()
    {
        // Arrange
        using var harness = new Harness(send: () => Task.FromResult(new ApiResponse(200, "OK", 0, 10, [], "not base64")));
        var tab = harness.Tab();
        await tab.SendAsync();

        // Act
        tab.Editor.Base64.DecodesWholeResponse = true;
        await tab.Result.Formatting;

        // Assert
        Assert.Equal("not base64", tab.Result.Response?.Body);
        Assert.Equal("The response body is not valid Base64.", tab.Result.ResponseBodyProblem);
    }
}
