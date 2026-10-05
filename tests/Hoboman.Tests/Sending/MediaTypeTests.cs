namespace Hoboman.Tests.Sending;

public sealed class MediaTypeTests
{
    [Fact]
    public void Of_WhenTheTypeHasParametersAndCapitals_ThenGivesItsTypeAndSubtype()
    {
        // Act
        var type = MediaType.Of("Application/Problem+JSON; charset=utf-8");

        // Assert
        Assert.Equal(new MediaType("application", "problem+json"), type);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("json")]
    public void Of_WhenItIsNoType_ThenGivesNone(string? contentType)
    {
        // Act
        var type = MediaType.Of(contentType);

        // Assert
        Assert.Null(type);
    }

    [Theory]
    [InlineData("application/json", true)]
    [InlineData("application/hal+json", true)]
    [InlineData("application/x-ndjson", false)]
    [InlineData("application/jsonlines", false)]
    public void Is_WhenTheSubtypeIsTheFormatOrHasItAsASuffix_ThenSaysSo(string contentType, bool expected)
    {
        // Act
        var isJson = MediaType.Of(contentType)!.Value.Is("json");

        // Assert
        Assert.Equal(expected, isJson);
    }
}
