namespace Hoboman.Tests.Base64;

public sealed class JsonOutlineTests
{
    [Fact]
    public void Of_WhenGivenJson_ThenGivesTheLineAndPathOfEachProperty()
    {
        // Act
        var outline = JsonOutline.Of("{\n  \"html\": \"<p>\",\n  \"data\": {\n    \"name\": \"Hobo\"\n  }\n}");

        // Assert
        Assert.Equal([(2, "$.html"), (3, "$.data"), (4, "$.data.name")], outline!.Select(property => (property.Line, property.Path)));
    }

    [Fact]
    public void Of_WhenAPropertyHoldsAnObjectOrAList_ThenSaysItHoldsMore()
    {
        // Act
        var outline = JsonOutline.Of("""{"html": "<p>", "data": {}, "items": []}""");

        // Assert
        Assert.Equal([false, true, true], outline!.Select(property => property.HoldsMore));
    }

    [Fact]
    public void Of_WhenAPropertyIsInAList_ThenItsPathCoversEveryElementAndItsPlaceIsItsOwn()
    {
        // Act
        var outline = JsonOutline.Of("""{"items": [{"html": "a"}, {"html": "b"}]}""");

        // Assert
        Assert.Equal([("$.items", "$.items"), ("$.items[*].html", "$.items[0].html"), ("$.items[*].html", "$.items[1].html")], outline!.Select(property => (property.Path, property.Place)));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"a": }""")]
    [InlineData("not json")]
    [InlineData("{} {}")]
    public void Of_WhenTheTextIsNotJson_ThenGivesNull(string text)
    {
        // Act
        var outline = JsonOutline.Of(text);

        // Assert
        Assert.Null(outline);
    }
}
