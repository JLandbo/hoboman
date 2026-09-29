using System.Net.Http;

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
        Assert.Equal("token", tab.Token);
    }

    [Fact]
    public async Task LoadSecretsAsync_WhenTheRequestHasNoId_ThenClearsTheSecrets()
    {
        // Arrange
        using var harness = new Harness();
        var tab = harness.Tab(new ApiRequest { Url = "https://dev.local" }, "Ping");
        tab.Token = "old";

        // Act
        await tab.LoadSecretsAsync(Cancellation);

        // Assert
        Assert.Empty(tab.Token);
    }

    [Fact]
    public async Task SaveAsync_WhenTheTokenChanged_ThenSavesIt()
    {
        // Arrange
        using var harness = new Harness();
        var request = ApiRequest.New() with { Auth = new(AuthKind.Bearer) };
        var tab = harness.Tab(request, "Ping");
        tab.Token = "token";

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
        tab.Token = "changed";

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
        tab.Token = "changed";

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
        tab.Token = "token";

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
        tab.Token = "token";

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
        tab.Password = "first";
        Directory.CreateDirectory(harness.Folder.Root);
        File.WriteAllText(harness.Folder.Secrets, "{}");
        Task saving;
        using (new FileStream(harness.Folder.Secrets, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            saving = tab.SaveAsync();
            tab.Password = "second";
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
        tab.Password = "hemmelig";

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
    public async Task SaveAsync_WhenAPasswordIsTypedWhileSaving_ThenStaysUnsaved()
    {
        // Arrange
        using var harness = new Harness();
        var tab = harness.Tab(ApiRequest.New() with { Auth = new(AuthKind.Basic, "hobo") }, "Ping");
        tab.Password = "first";
        Directory.CreateDirectory(harness.Folder.Root);
        File.WriteAllText(harness.Folder.Secrets, "{}");
        Task saving;

        // Act
        using (new FileStream(harness.Folder.Secrets, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            saving = tab.SaveAsync();
            tab.Password = "second";
        }
        await saving;

        // Assert
        Assert.True(tab.IsDirty);
    }
}
