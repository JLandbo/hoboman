using System.Net.Http;
using Hoboman.Tests.Auth;

namespace Hoboman.Tests.ViewModels;

public sealed class RequestTabViewModelTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SendAsync_WhenTheCallSucceeds_ThenShowsTheResponse()
    {
        // Arrange
        using var harness = new Harness();
        var tab = harness.Tab();

        // Act
        await tab.SendAsync();

        // Assert
        Assert.Equal(("200 OK", "{}"), (tab.Response?.Status, tab.Response?.Body));
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
        await harness.Library.SaveAsync("Ping", ApiRequest.New() with { Url = "https://dev.local", Auth = new(AuthKind.OAuth2) }, Cancellation);
        var tab = harness.Tab(await harness.Library.LoadAsync("Ping", Cancellation), "Ping");
        await tab.Auth.FetchTokenAsync();

        // Act
        await tab.SendAsync();

        // Assert
        Assert.False(tab.IsDirty);
    }

    [Fact]
    public async Task SendAsync_WhenAnotherEnvironmentIsChosenWhileSaving_ThenSendsWithTheOneChosenAtSend()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Environments.ChooseAsync(new("Dev", []));
        var tab = harness.Tab();
        tab.Auth.Password = "hemmelig";
        var sending = tab.SendAsync();

        // Act
        await harness.Environments.ChooseAsync(new("Prod", []));
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
        tab.Url = "   ";

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
        tab.Url = "{{base}}/users";

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
        tab.Url = "https://dev.local";

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
        tab.Url = "https://dev.local";

        // Assert
        Assert.True(tab.IsDirty);
    }

    [Fact]
    public void ReloadIfChanged_WhenTheFileIsWhatTheTabLoaded_ThenKeepsTheEdits()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New() with { Url = "https://dev.local" };
        var tab = harness.Tab(request, "Ping");
        tab.Url = "https://edited.local";

        // Act
        tab.ReloadIfChanged(request);

        // Assert
        Assert.Equal("https://edited.local", tab.Url);
    }

    [Fact]
    public void ReloadIfChanged_WhenTheFileIsWhatTheTabLoaded_ThenSaysNothingChanged()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New() with { Url = "https://dev.local" };
        var tab = harness.Tab(request, "Ping");
        tab.Url = "https://edited.local";

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
        var tab = harness.Tab(request, "Ping");
        tab.Url = "https://edited.local";

        // Act
        tab.ReloadIfChanged(request with { Url = "https://agent.local" });

        // Assert
        Assert.Equal("https://agent.local", tab.Url);
    }

    [Fact]
    public void ReloadIfChanged_WhenTheFileChanged_ThenTheTabIsSaved()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New() with { Url = "https://dev.local" };
        var tab = harness.Tab(request, "Ping");
        tab.Url = "https://edited.local";

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
        var tab = harness.Tab(request, "Ping");
        tab.Url = "https://edited.local";

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
        var tab = harness.Tab(request, "Ping");

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
        var tab = harness.Tab(new ApiRequest { Url = "https://dev.local" }, "Ping");
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
        var tab = harness.Tab(request, "Ping");
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
        Assert.NotEqual(original.Id, (await harness.Library.LoadAsync("Copy", Cancellation))?.Id);
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
        Assert.Equal("token", await harness.Secrets.OfAsync((await harness.Library.LoadAsync("Copy", Cancellation))!.Id, SecretKind.Token, Cancellation));
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
        Assert.False(harness.Library.Exists("Ping"));
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
        using var harness = new Harness(new FakeDialogs(answer: "Test/Ping"));
        var tab = harness.Tab();
        tab.Url = "https://dev.local";

        // Act
        await tab.SaveAsync();

        // Assert
        Assert.Equal("https://dev.local", (await harness.Library.LoadAsync("Test/Ping", Cancellation))?.Url);
    }

    [Fact]
    public async Task SaveAsync_WhenTheTabIsNew_ThenTakesTheGivenName()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Test/Ping"));
        var tab = harness.Tab();
        tab.Url = "https://dev.local";

        // Act
        await tab.SaveAsync();

        // Assert
        Assert.Equal("Test/Ping", tab.Name);
    }

    [Fact]
    public async Task SaveAsync_WhenTheTabIsNew_ThenIsNoLongerUnsaved()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Test/Ping"));
        var tab = harness.Tab();
        tab.Url = "https://dev.local";

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
        tab.Url = "https://dev.local";

        // Act
        await tab.SaveAsync();

        // Assert
        Assert.Empty(await harness.Library.NamesAsync(Cancellation));
    }

    [Fact]
    public void RequestSection_WhenChanged_ThenTheTabStaysSaved()
    {
        // Arrange
        using var harness = new Harness();
        var tab = harness.Tab(ApiRequest.New(), "Ping");

        // Act
        tab.RequestSection = RequestSection.Headers;

        // Assert
        Assert.False(tab.IsDirty);
    }

    [Fact]
    public void Base64_WhenAPropertyIsChosen_ThenTheTabIsUnsaved()
    {
        // Arrange
        using var harness = new Harness();
        var tab = harness.Tab(ApiRequest.New() with { BodyKind = BodyKind.Json, Body = """{"html": "<p>"}""" }, "Ping");

        // Act
        tab.Base64.ToggleEncode("$.html");

        // Assert
        Assert.True(tab.IsDirty);
    }

    [Fact]
    public async Task SendAsync_WhenAPropertyIsChosenForBase64_ThenSendsItsPath()
    {
        // Arrange
        using var harness = new Harness();
        var tab = harness.Tab(ApiRequest.New() with { BodyKind = BodyKind.Json, Body = """{"html": "<p>"}""" });
        tab.Base64.ToggleEncode("$.html");

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
        tab.Base64.ToggleDecode("$.html");
        await tab.Formatting;

        // Assert
        Assert.Equal((Base64MarkState.Decoded, true), (tab.ResponseMarks.Single().State, tab.Response!.Body.Contains("<p>Ærø</p>")));
    }

    [Fact]
    public async Task SendAsync_WhenTheRequestInherits_ThenSendsWithTheFoldersAuth()
    {
        // Arrange
        using var harness = new Harness();
        var folder = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveFolderAsync("Users", folder, Cancellation);
        var tab = harness.Tab(ApiRequest.New(), "Users/Get user");

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
        var folder = new FolderSettings { Id = Guid.NewGuid(), Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveFolderAsync("Users", folder, Cancellation);
        var tab = new RequestTabViewModel(harness.Services, ApiRequest.New(), suggestedName: "Users/Get user", historyName: "call.json");

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
        Directory.CreateDirectory(Path.Combine(harness.Folder.Requests, "Users"));
        File.WriteAllText(Path.Combine(harness.Folder.Requests, "Users", ".folder.json"), "{");
        var tab = harness.Tab(ApiRequest.New(), "Users/Get user");

        // Act
        await tab.SendAsync();

        // Assert
        Assert.Contains(".folder.json", tab.Problem?.Details);
    }

    [Fact]
    public void ReloadIfChanged_WhenTheFileCanBeReadAgain_ThenHidesTheProblem()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New();
        var tab = harness.Tab(request, "Ping");
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
        var request = ApiRequest.New();
        await harness.Library.SaveAsync("Ping", request, Cancellation);
        var tab = harness.Tab(request, "Ping");
        tab.Url = "https://saved.local";
        Task saving;

        // Act
        using (new FileStream(Path.Combine(harness.Folder.Requests, "Ping.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            saving = tab.SaveAsync();
            tab.Url = "https://edited.local";
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
        var tab = harness.Tab(request, "Ping");
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
        var tab = harness.Tab(request, "Ping");
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
        tab.Url = "https://changed.local";

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
        var tab = harness.Tab(request, "Ping");
        tab.Auth.Password = "typed";

        // Act
        tab.ReloadIfChanged(request);

        // Assert
        Assert.True(tab.IsDirty);
    }

    [Fact]
    public async Task SaveAsync_WhenACopySharesTheId_ThenLeavesTheOriginalsTokenAlone()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New() with { Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveAsync("Ping", request, Cancellation);
        await harness.Library.SaveAsync("Copy", request, Cancellation);
        await harness.Secrets.SaveAsync(request.Id, SecretKind.Token, "ping", Cancellation);
        var tab = harness.Tab(request, "Copy");
        await tab.LoadSecretsAsync(Cancellation);
        tab.Auth.Token = "copy";

        // Act
        await tab.SaveAsync();

        // Assert
        Assert.Equal("ping", await harness.Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task SaveAsync_WhenACopySharesTheId_ThenGetsItsOwnId()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New() with { Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveAsync("Ping", request, Cancellation);
        await harness.Library.SaveAsync("Copy", request, Cancellation);
        await harness.Secrets.SaveAsync(request.Id, SecretKind.Token, "ping", Cancellation);
        var tab = harness.Tab(request, "Copy");
        await tab.LoadSecretsAsync(Cancellation);
        tab.Auth.Token = "copy";

        // Act
        await tab.SaveAsync();

        // Assert
        Assert.NotEqual(request.Id, (await harness.Library.LoadAsync("Copy", Cancellation))?.Id);
    }

    [Fact]
    public async Task SendAsync_WhenACopySharesTheId_ThenLeavesTheOriginalsTokenAlone()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New() with { Auth = new(AuthKind.Bearer) };
        await harness.Library.SaveAsync("Ping", request, Cancellation);
        await harness.Library.SaveAsync("Copy", request, Cancellation);
        await harness.Secrets.SaveAsync(request.Id, SecretKind.Token, "ping", Cancellation);
        var tab = harness.Tab(request, "Copy");
        await tab.LoadSecretsAsync(Cancellation);
        tab.Auth.Token = "copy";

        // Act
        await tab.SendAsync();

        // Assert
        Assert.Equal("ping", await harness.Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
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
        Assert.Equal(BodyFormat.Json, tab.BodyFormat);
    }

    [Fact]
    public async Task BodyFormat_WhenChanged_ThenShowsTheResponseInThatFormat()
    {
        // Arrange
        using var harness = new Harness(send: () => Task.FromResult(new ApiResponse(200, "OK", 0, 7, [new("Content-Type", "text/plain")], """{"a":1}""")));
        var tab = harness.Tab();
        await tab.SendAsync();

        // Act
        tab.BodyFormat = BodyFormat.Json;
        await tab.Formatting;

        // Assert
        Assert.Equal($"{{{Environment.NewLine}  \"a\": 1{Environment.NewLine}}}", tab.Response?.Body);
    }

    [Fact]
    public async Task ReloadIfChanged_WhenTheFileDecodesAnotherValue_ThenShowsTheResponseAgain()
    {
        // Arrange
        var body = $$"""{"html": "{{Base64Text.Encode("<p>")}}"}""";
        using var harness = new Harness(send: () => Task.FromResult(new ApiResponse(200, "OK", 0, body.Length, [new("Content-Type", "application/json")], body)));
        var request = ApiRequest.New();
        var tab = harness.Tab(request, "Ping");
        await tab.SendAsync();

        // Act
        tab.ReloadIfChanged(request with { Base64 = new() { Decode = ["$.html"] } });
        await tab.Formatting;

        // Assert
        Assert.Equal(Base64MarkState.Decoded, tab.ResponseMarks.Single().State);
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
        tab.Base64.DecodesWholeResponse = true;
        await tab.Formatting;

        // Assert
        Assert.Equal($"{{{Environment.NewLine}  \"a\": 1{Environment.NewLine}}}", tab.Response?.Body);
        Assert.Null(tab.ResponseBodyProblem);
    }

    [Fact]
    public async Task DecodesWholeResponse_WhenTheBodyIsNotBase64_ThenKeepsItAndShowsTheProblem()
    {
        // Arrange
        using var harness = new Harness(send: () => Task.FromResult(new ApiResponse(200, "OK", 0, 10, [], "not base64")));
        var tab = harness.Tab();
        await tab.SendAsync();

        // Act
        tab.Base64.DecodesWholeResponse = true;
        await tab.Formatting;

        // Assert
        Assert.Equal("not base64", tab.Response?.Body);
        Assert.Equal("The response body is not valid Base64.", tab.ResponseBodyProblem);
    }
}
