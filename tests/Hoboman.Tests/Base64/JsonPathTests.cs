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
}
