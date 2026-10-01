namespace Hoboman.Tests.Text;

public sealed class JsonStringTests
{
    [Fact]
    public void Of_WhenTheTextIsJson_ThenMakesItCompactFirst()
    {
        // Act
        var json = JsonString.Of("{\n\t\"data\": \"name\"\n}");

        // Assert
        Assert.Equal("\"{\\\"data\\\":\\\"name\\\"}\"", json);
    }

    [Fact]
    public void Of_WhenTheTextIsNotJson_ThenEscapesItAsItIs()
    {
        // Act
        var json = JsonString.Of("\"data\": \"name\"");

        // Assert
        Assert.Equal("\"\\\"data\\\": \\\"name\\\"\"", json);
    }

    [Fact]
    public void Of_WhenTheTextHasBreaksAndLetters_ThenEscapesTheBreaksAndKeepsTheLetters()
    {
        // Act
        var json = JsonString.Of("Ærø\nog\ttab");

        // Assert
        Assert.Equal("\"Ærø\\nog\\ttab\"", json);
    }

    [Fact]
    public void From_WhenGivenAJsonString_ThenGivesItsText()
    {
        // Act
        var text = JsonString.From("\"{\\\"data\\\":\\\"name\\\"}\"");

        // Assert
        Assert.Equal("{\"data\":\"name\"}", text);
    }

    [Fact]
    public void From_WhenTheQuotesAreMissing_ThenStillGivesTheText()
    {
        // Act
        var text = JsonString.From("{\\\"data\\\":\\\"name\\\"}");

        // Assert
        Assert.Equal("{\"data\":\"name\"}", text);
    }

    [Theory]
    [InlineData("{\"a\": 1}")]
    [InlineData("plain text")]
    [InlineData("123")]
    public void From_WhenTheTextIsNotAJsonString_ThenGivesNull(string clipboard)
    {
        // Act
        var text = JsonString.From(clipboard);

        // Assert
        Assert.Null(text);
    }
}
