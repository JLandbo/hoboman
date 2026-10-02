using Microsoft.Extensions.Time.Testing;

namespace Hoboman.Tests.ViewModels;

public sealed class Base64ViewModelTests
{
    readonly FakeTimeProvider _clock = new();
    readonly Translator _translator = new(Translation.English);

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    Base64ViewModel Loaded(string body, Base64Paths? paths = null)
    {
        var base64 = new Base64ViewModel(_translator, _clock);
        base64.Load(paths, body);
        return base64;
    }

    static ApiResponse Json(string body) => new(200, "OK", 0, body.Length, [new("Content-Type", "application/json")], body);

    [Fact]
    public void ToggleEncode_WhenAPropertyIsChosen_ThenChoosesIt()
    {
        // Arrange
        var base64 = Loaded("""{"html": "<p>"}""");

        // Act
        base64.ToggleEncode("$.html");

        // Assert
        Assert.Equal(["$.html"], base64.Encode);
    }

    [Fact]
    public void ToggleEncode_WhenAPropertyIsChosen_ThenMarksItsLine()
    {
        // Arrange
        var base64 = Loaded("""{"html": "<p>"}""");

        // Act
        base64.ToggleEncode("$.html");

        // Assert
        Assert.Equal((1, Base64MarkState.Checked, "Base64"), (base64.BodyMarks.Single().Line, base64.BodyMarks.Single().State, base64.BodyMarks.Single().Badge));
    }

    [Fact]
    public void ToggleEncode_WhenAChosenPropertyIsChosenAgain_ThenLetsItGo()
    {
        // Arrange
        var base64 = Loaded("""{"html": "<p>"}""", new() { Encode = ["$.html"] });

        // Act
        base64.ToggleEncode("$.html");

        // Assert
        Assert.Empty(base64.Encode);
    }

    [Fact]
    public void ToggleEncode_WhenAnObjectIsChosen_ThenLetsGoOfWhatWasChosenInsideIt()
    {
        // Arrange
        var base64 = Loaded("""{"data": {"name": "Hobo"}}""", new() { Encode = ["$.data.name"] });

        // Act
        base64.ToggleEncode("$.data");

        // Assert
        Assert.Equal(["$.data"], base64.Encode);
    }

    [Fact]
    public void ToggleEncode_WhenAnObjectIsChosen_ThenGreysWhatIsInsideIt()
    {
        // Arrange
        var base64 = Loaded("""{"data": {"name": "Hobo"}}""");

        // Act
        base64.ToggleEncode("$.data");

        // Assert
        Assert.Equal([Base64MarkState.Checked, Base64MarkState.Inside], base64.BodyMarks.Select(mark => mark.State));
    }

    [Fact]
    public void BodyChanged_WhenTheBodyHasNotStoodStill_ThenLeavesTheMarksAlone()
    {
        // Arrange
        var base64 = Loaded("""{"html": "<p>"}""");

        // Act
        base64.BodyChanged("""{"html": """);

        // Assert
        Assert.True(base64.BodyMarksAreCurrent);
    }

    [Fact]
    public void BodyChanged_WhenTheBodyIsNotJson_ThenKeepsTheMarksButGreysThem()
    {
        // Arrange
        var base64 = Loaded("""{"html": "<p>"}""");

        // Act
        base64.BodyChanged("""{"html": """);
        _clock.Advance(TimeSpan.FromSeconds(1));

        // Assert
        Assert.Equal((false, 1), (base64.BodyMarksAreCurrent, base64.BodyMarks.Count));
    }

    [Fact]
    public void BodyChanged_WhenAChosenPropertyIsRenamed_ThenLetsItGo()
    {
        // Arrange
        var base64 = Loaded("""{"html": "<p>"}""", new() { Encode = ["$.html"] });

        // Act
        base64.BodyChanged("""{"content": "<p>"}""");
        _clock.Advance(TimeSpan.FromSeconds(1));

        // Assert
        Assert.Empty(base64.Encode);
    }

    [Fact]
    public void Load_WhenAChosenPropertyIsMissing_ThenKeepsIt()
    {
        // Act
        var base64 = Loaded("{}", new() { Encode = ["$.html"] });

        // Assert
        Assert.Equal(["$.html"], base64.Encode);
    }

    [Fact]
    public void Load_WhenAVariableStandsForANumber_ThenStillMarksTheBody()
    {
        // Act
        var base64 = Loaded("{}");
        base64.Load(null, """{"count": {{count}}}""", useVariables: true);

        // Assert
        Assert.Equal("$.count", base64.BodyMarks.Single().Path);
    }

    [Fact]
    public void EncodesWholeBody_WhenSet_ThenChoosesTheWholeBodyInsteadOfItsProperties()
    {
        // Arrange
        var base64 = Loaded("""{"html": "<p>"}""", new() { Encode = ["$.html"] });

        // Act
        base64.EncodesWholeBody = true;

        // Assert
        Assert.Equal([JsonPath.Root], base64.Encode);
    }

    [Fact]
    public void EncodesWholeBody_WhenSet_ThenGreysEveryProperty()
    {
        // Arrange
        var base64 = Loaded("""{"html": "<p>", "data": {}}""");

        // Act
        base64.EncodesWholeBody = true;

        // Assert
        Assert.Equal([Base64MarkState.Inside, Base64MarkState.Inside], base64.BodyMarks.Select(mark => mark.State));
    }

    [Fact]
    public void BodyChanged_WhenTheWholeBodyIsChosen_ThenKeepsIt()
    {
        // Arrange
        var base64 = Loaded("""{"html": "<p>"}""", new() { Encode = [JsonPath.Root] });

        // Act
        base64.BodyChanged("""{"content": "<p>"}""");
        _clock.Advance(TimeSpan.FromSeconds(1));

        // Assert
        Assert.Equal([JsonPath.Root], base64.Encode);
    }

    [Fact]
    public void DecodesWholeResponse_WhenSet_ThenChoosesTheWholeBody()
    {
        // Arrange
        var base64 = Loaded("");

        // Act
        base64.DecodesWholeResponse = true;

        // Assert
        Assert.Equal([JsonPath.Root], base64.Decode);
    }

    [Fact]
    public void ShowResponse_WhenNothingIsChosen_ThenStillMarksEachProperty()
    {
        // Act
        var shown = Base64ViewModel.ShowResponse(Json("""{"a": 1}"""), BodyFormat.Json, [], _translator, Cancellation);

        // Assert
        Assert.Equal(("$.a", Base64MarkState.Unchecked), (shown.Marks.Single().Path, shown.Marks.Single().State));
    }

    [Fact]
    public void ShowResponse_WhenAChosenValueIsBase64_ThenShowsItDecodedAndMarked()
    {
        // Act
        var shown = Base64ViewModel.ShowResponse(Json($$"""{"html": "{{Base64Text.Encode("<p>")}}"}"""), BodyFormat.Json, ["$.html"], _translator, Cancellation);

        // Assert
        Assert.Equal(("decoded", true), (shown.Marks.Single().Badge, shown.Display.Body.Contains("<p>")));
    }

    [Fact]
    public void ShowResponse_WhenAChosenValueIsNotBase64_ThenMarksItAsFailed()
    {
        // Act
        var shown = Base64ViewModel.ShowResponse(Json("""{"token": "not base64!"}"""), BodyFormat.Json, ["$.token"], _translator, Cancellation);

        // Assert
        Assert.Equal(Base64MarkState.Failed, shown.Marks.Single().State);
    }

    [Fact]
    public void ShowResponse_WhenTheWholeBodyIsNotBase64_ThenSaysSo()
    {
        // Act
        var shown = Base64ViewModel.ShowResponse(Json("not base64!"), BodyFormat.Json, [JsonPath.Root], _translator, Cancellation);

        // Assert
        Assert.Equal("The response body is not valid Base64.", shown.Problem);
    }

    [Fact]
    public void ShowResponse_WhenCancelled_ThenStops()
    {
        // Act
        var showing = () => Base64ViewModel.ShowResponse(Json("""{"a": 1}"""), BodyFormat.Json, [], _translator, new CancellationToken(canceled: true));

        // Assert
        Assert.Throws<OperationCanceledException>(showing);
    }
}
