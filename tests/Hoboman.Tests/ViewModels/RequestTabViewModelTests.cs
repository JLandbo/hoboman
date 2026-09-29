namespace Hoboman.Tests.ViewModels;

public sealed class RequestTabViewModelTests
{
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
        Assert.Null(tab.Problem);
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
        Assert.Equal(new ProblemMessage("The request could not be sent", "No token is saved for this request."), tab.Problem);
        Assert.Null(tab.Response);
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
    public async Task SaveAsync_WhenTheTabIsNew_ThenAsksForANameAndSaves()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: "Test/Ping"));
        var tab = harness.Tab();
        tab.Url = "https://dev.local";

        // Act
        await tab.SaveAsync();

        // Assert
        Assert.Equal(("Test/Ping", false), (tab.Name, tab.IsDirty));
        Assert.Equal("https://dev.local", (await harness.Library.LoadAsync("Test/Ping", TestContext.Current.CancellationToken))?.Url);
    }

    [Fact]
    public async Task SaveAsync_WhenTheNameIsNotGiven_ThenSavesNothing()
    {
        // Arrange
        using var harness = new Harness(new FakeDialogs(answer: null));
        var tab = harness.Tab();
        tab.Url = "https://dev.local";

        // Act
        var saved = await tab.SaveAsync();

        // Assert
        Assert.False(saved);
        Assert.Empty(await harness.Library.NamesAsync(TestContext.Current.CancellationToken));
    }
}
