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
    public void Of_WhenTheTextIsAlreadyAJsonString_ThenLeavesItAlone()
    {
        // Act
        var json = JsonString.Of("\"already a JSON string\"");

        // Assert
        Assert.Equal("\"already a JSON string\"", json);
    }

    [Theory]
    [InlineData("{\"data\":\"name\"}")]
    [InlineData("plain text")]
    [InlineData("\"already a JSON string\"")]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("quotes \" and slash \\ and Ærø\n")]
    public void Of_WhenUsedAgain_ThenLeavesTheFirstResultAlone(string text)
    {
        // Arrange
        var stringified = JsonString.Of(text);

        // Act
        var stringifiedAgain = JsonString.Of(stringified);

        // Assert
        Assert.Equal(stringified, stringifiedAgain);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("{\\\"data\\\":\\\"name\\\"}")]
    [InlineData("quotes \" and slash \\ and Ærø\n")]
    public void From_WhenGivenTextMadeByOf_ThenGivesTheOriginalText(string text)
    {
        // Act
        var parsed = JsonString.From(JsonString.Of(text));

        // Assert
        Assert.Equal(text, parsed);
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
    public void From_WhenGivenAStringHoldingAJsonString_ThenRemovesOneLayer()
    {
        // Act
        var text = JsonString.From("\"\\\"hello\\\"\"");

        // Assert
        Assert.Equal("\"hello\"", text);
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
    [InlineData("null")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\"unterminated")]
    public void From_WhenTheTextIsNotAJsonString_ThenGivesNull(string clipboard)
    {
        // Act
        var text = JsonString.From(clipboard);

        // Assert
        Assert.Null(text);
    }
}
