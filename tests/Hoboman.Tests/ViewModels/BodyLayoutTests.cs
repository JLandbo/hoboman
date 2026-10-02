namespace Hoboman.Tests.ViewModels;

public sealed class BodyLayoutTests
{
    static string Lines(params string[] lines) => string.Join(Environment.NewLine, lines);

    [Fact]
    public void Of_WhenTheJsonIsOnOneLine_ThenLaysItOutLikeAResponse()
    {
        // Act
        var laidOut = BodyLayout.Of("""{"a":1,"b":{"c":"Ærø"}}""", BodyKind.Json);

        // Assert
        Assert.Equal(Lines("{", "\t\"a\": 1,", "\t\"b\": {", "\t\t\"c\": \"Ærø\"", "\t}", "}"), laidOut);
    }

    [Fact]
    public void Of_WhenAVariableIsOutsideQuotes_ThenKeepsItThere()
    {
        // Act
        var laidOut = BodyLayout.Of("""{"count":{{count}},"name":"{{name}}"}""", BodyKind.Json, useVariables: true);

        // Assert
        Assert.Equal(Lines("{", "\t\"count\": {{count}},", "\t\"name\": \"{{name}}\"", "}"), laidOut);
    }

    [Fact]
    public void Of_WhenTheXmlIsOnOneLine_ThenLaysItOut()
    {
        // Act
        var laidOut = BodyLayout.Of("<order id=\"{{id}}\"><line /></order>", BodyKind.Xml);

        // Assert
        Assert.Equal(Lines("<order id=\"{{id}}\">", "\t<line />", "</order>"), laidOut);
    }

    [Fact]
    public void Of_WhenAVariableStandsForAnXmlName_ThenLaysItOut()
    {
        // Act
        var laidOut = BodyLayout.Of("<{{root}}><{{child}} {{name}}=\"1\" /></{{root}}>", BodyKind.Xml, useVariables: true);

        // Assert
        Assert.Equal(Lines("<{{root}}>", "\t<{{child}} {{name}}=\"1\" />", "</{{root}}>"), laidOut);
    }

    [Theory]
    [InlineData("{\"amount\": 1.{{decimal}}}", BodyKind.Json)]
    [InlineData("<item id={{quotedId}} />", BodyKind.Xml)]
    public void NeedsVariables_WhenTheBodyIsOnlyValidWithTheVariablesFilledIn_ThenSaysSo(string body, BodyKind kind)
    {
        // Arrange
        var environment = new ApiEnvironment("Dev", [new("decimal", "5"), new("quotedId", "\"17\"")]);

        // Act
        var needs = BodyLayout.NeedsVariables(body, kind, environment);

        // Assert
        Assert.True(needs);
    }

    [Fact]
    public void NeedsVariables_WhenTheBodyIsInvalidEvenWithTheVariablesFilledIn_ThenSaysNot()
    {
        // Act
        var needs = BodyLayout.NeedsVariables("{\"amount\": {{decimal}}", BodyKind.Json, new("Dev", [new("decimal", "5")]));

        // Assert
        Assert.False(needs);
    }

    [Theory]
    [InlineData("{\"a\": ", BodyKind.Json)]
    [InlineData("<order>", BodyKind.Xml)]
    [InlineData("{\"a\": 1}", BodyKind.Text)]
    public void Of_WhenTheBodyIsNotOfItsKind_ThenGivesNull(string body, BodyKind kind)
    {
        // Act
        var laidOut = BodyLayout.Of(body, kind);

        // Assert
        Assert.Null(laidOut);
    }
}
