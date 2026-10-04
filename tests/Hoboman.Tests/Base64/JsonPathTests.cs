using System.Text.Json;

namespace Hoboman.Tests.Base64;

public sealed class JsonPathTests
{
    [Fact]
    public void Member_WhenTheNameIsPlain_ThenUsesADot()
    {
        // Act
        var path = JsonPath.Member(JsonPath.Root, "html");

        // Assert
        Assert.Equal("$.html", path);
    }

    [Fact]
    public void Member_WhenTheNameHasOtherCharacters_ThenUsesBrackets()
    {
        // Act
        var path = JsonPath.Member(JsonPath.Root, @"it's a\b");

        // Assert
        Assert.Equal(@"$['it\'s a\\b']", path);
    }

    [Fact]
    public void StepsOf_WhenGivenAPath_ThenGivesItsSteps()
    {
        // Act
        var steps = JsonPath.StepsOf(@"$.items[*]['it\'s a\\b']");

        // Assert
        Assert.Equal(["items", null, @"it's a\b"], steps);
    }

    [Fact]
    public void StepsOf_WhenGivenTheRoot_ThenGivesNoSteps()
    {
        // Act
        var steps = JsonPath.StepsOf(JsonPath.Root);

        // Assert
        Assert.Equal([], steps);
    }

    [Theory]
    [InlineData("html")]
    [InlineData("$.")]
    [InlineData("$[0]")]
    [InlineData("$['open")]
    public void StepsOf_WhenNotAPath_ThenGivesNull(string path)
    {
        // Act
        var steps = JsonPath.StepsOf(path);

        // Assert
        Assert.Null(steps);
    }

    [Theory]
    [InlineData("$.data.name", "$.data", true)]
    [InlineData("$.items[*].html", "$.items", true)]
    [InlineData("$.database", "$.data", false)]
    [InlineData("$.data", "$.data", false)]
    public void IsInside_WhenGivenTwoPaths_ThenSaysWhetherTheFirstIsWithinTheSecond(string path, string container, bool expected)
    {
        // Act
        var inside = JsonPath.IsInside(path, container);

        // Assert
        Assert.Equal(expected, inside);
    }

    [Theory]
    [InlineData("$['token']", "$.token")]
    [InlineData("$.items[0]['first name']", "$.items[0]['first name']")]
    [InlineData("$.items[*].id", null)]
    [InlineData("status", null)]
    public void PlaceOf_WhenGivenAPath_ThenWritesItAsABodysPlaces(string path, string? expected)
    {
        // Act
        var place = JsonPath.PlaceOf(path);

        // Assert
        Assert.Equal(expected, place);
    }

    [Fact]
    public void TrySelect_WhenThePathHasAnIndex_ThenSelectsTheElement()
    {
        // Arrange
        using var json = JsonDocument.Parse("""{"items": [{"id": 1}, {"id": 2}]}""");

        // Act
        var found = JsonPath.TrySelect(json.RootElement, "$.items[1].id", out var value);

        // Assert
        Assert.Equal((true, "2"), (found, value.GetRawText()));
    }

    [Fact]
    public void TrySelect_WhenTheValueIsNull_ThenFindsNull()
    {
        // Arrange
        using var json = JsonDocument.Parse("""{"id": null}""");

        // Act
        var found = JsonPath.TrySelect(json.RootElement, "$.id", out var value);

        // Assert
        Assert.Equal((true, JsonValueKind.Null), (found, value.ValueKind));
    }

    [Theory]
    [InlineData("$", """{"a":{"it's":[7]}}""")]
    [InlineData("$.a", """{"it's":[7]}""")]
    [InlineData(@"$.a['it\'s'][0]", "7")]
    public void TrySelect_WhenGivenAPath_ThenSelectsItsValue(string path, string expected)
    {
        // Arrange
        using var json = JsonDocument.Parse("""{"a":{"it's":[7]}}""");

        // Act
        JsonPath.TrySelect(json.RootElement, path, out var value);

        // Assert
        Assert.Equal(expected, value.GetRawText());
    }

    [Theory]
    [InlineData("$.missing")]
    [InlineData("$.items[2]")]
    [InlineData("$.items.id")]
    [InlineData("$.items[0].id.more")]
    [InlineData("$.items[*]")]
    [InlineData("$.items[-1]")]
    [InlineData("items")]
    public void TrySelect_WhenThePathLeadsNowhere_ThenFindsNothing(string path)
    {
        // Arrange
        using var json = JsonDocument.Parse("""{"items": [{"id": 1}, {"id": 2}]}""");

        // Act
        var found = JsonPath.TrySelect(json.RootElement, path, out _);

        // Assert
        Assert.False(found);
    }
}
