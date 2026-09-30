namespace Hoboman.Tests.ViewModels;

public sealed class ResponseDisplayTests
{
    [Fact]
    public void Format_WhenTheBodyIsJson_ThenIndentsItAndKeepsTheLetters()
    {
        // Act
        var body = ResponseDisplay.Format("""{"navn":"Ærø"}""", BodyFormat.Json).Body;

        // Assert
        Assert.Equal($"{{{Environment.NewLine}  \"navn\": \"Ærø\"{Environment.NewLine}}}", body);
    }

    [Fact]
    public void Format_WhenJsonIsChosenForSomethingElse_ThenKeepsIt()
    {
        // Act
        var body = ResponseDisplay.Format("<html />", BodyFormat.Json).Body;

        // Assert
        Assert.Equal("<html />", body);
    }

    [Fact]
    public void SizeOf_WhenOverAKilobyte_ThenShowsKilobytes()
    {
        // Act
        var size = ResponseDisplay.SizeOf(2048);

        // Assert
        Assert.Equal("2 KB", size);
    }

    [Fact]
    public void Of_WhenTheBodyIsVeryLarge_ThenShowsOnlyTheStart()
    {
        // Act
        var display = ResponseDisplay.Of(new ApiResponse(200, "OK", 5, 3_000_000, [], new string('x', 3_000_000)));

        // Assert
        Assert.Equal(ResponseDisplay.ShownLength, display.Body.Length);
    }

    [Fact]
    public void Of_WhenTheBodyIsVeryLarge_ThenSaysItIsCut()
    {
        // Act
        var display = ResponseDisplay.Of(new ApiResponse(200, "OK", 5, 3_000_000, [], new string('x', 3_000_000)));

        // Assert
        Assert.True(display.IsCut);
    }

    [Fact]
    public void Format_WhenTheBodyIsXml_ThenIndentsIt()
    {
        // Act
        var body = ResponseDisplay.Format("""<svg xmlns="http://www.w3.org/2000/svg"><rect width="1"/></svg>""", BodyFormat.Xml).Body;

        // Assert
        Assert.Equal($"<svg xmlns=\"http://www.w3.org/2000/svg\">{Environment.NewLine}  <rect width=\"1\" />{Environment.NewLine}</svg>", body);
    }

    [Fact]
    public void Format_WhenTheXmlHasADeclaration_ThenKeepsIt()
    {
        // Act
        var body = ResponseDisplay.Format("""<?xml version="1.0"?><a><b/></a>""", BodyFormat.Xml).Body;

        // Assert
        Assert.StartsWith("""<?xml version="1.0"?>""", body);
    }

    [Fact]
    public void Format_WhenTheBodyIsHtml_ThenKeepsIt()
    {
        // Act
        var body = ResponseDisplay.Format("<!DOCTYPE html><html><br></html>", BodyFormat.Xml).Body;

        // Assert
        Assert.Equal("<!DOCTYPE html><html><br></html>", body);
    }

    [Fact]
    public void Format_WhenTheXmlDefinesAnEntity_ThenDoesNotExpandIt()
    {
        // Act
        var body = ResponseDisplay.Format("""<!DOCTYPE a [<!ENTITY x "boom">]><a>&x;</a>""", BodyFormat.Xml).Body;

        // Assert
        Assert.Equal("""<!DOCTYPE a [<!ENTITY x "boom">]><a>&x;</a>""", body);
    }

    [Fact]
    public void Of_WhenTheContentTypeIsText_ThenDoesNotFormatJson()
    {
        // Act
        var display = ResponseDisplay.Of(new ApiResponse(200, "OK", 5, 7, [new("Content-Type", "text/plain")], """{"a":1}"""));

        // Assert
        Assert.Equal("""{"a":1}""", display.Body);
    }

    [Fact]
    public void Of_WhenTheContentTypeIsJson_ThenFormatsIt()
    {
        // Act
        var display = ResponseDisplay.Of(new ApiResponse(200, "OK", 5, 7, [new("content-type", "application/problem+json; charset=utf-8")], """{"a":1}"""));

        // Assert
        Assert.Equal($"{{{Environment.NewLine}  \"a\": 1{Environment.NewLine}}}", display.Body);
    }

    [Fact]
    public void Of_WhenTheContentTypeIsSvg_ThenFormatsItAsXml()
    {
        // Act
        var display = ResponseDisplay.Of(new ApiResponse(200, "OK", 5, 20, [new("Content-Type", "image/svg+xml")], "<svg><g/></svg>"));

        // Assert
        Assert.Equal($"<svg>{Environment.NewLine}  <g />{Environment.NewLine}</svg>", display.Body);
    }

    [Fact]
    public void Format_WhenXmlIsChosenForJson_ThenShowsItWithoutColors()
    {
        // Act
        var (_, coloring) = ResponseDisplay.Format("""{"a":1}""", BodyFormat.Xml);

        // Assert
        Assert.Equal(BodyFormat.Raw, coloring);
    }

    [Fact]
    public void FormatOf_WhenThereIsNoContentType_ThenChoosesRaw()
    {
        // Act
        var format = ResponseDisplay.FormatOf(new ApiResponse(200, "OK", 5, 7, [], """{"a":1}"""));

        // Assert
        Assert.Equal(BodyFormat.Raw, format);
    }

    [Fact]
    public void FormatOf_WhenTheContentTypeIsHtml_ThenChoosesRaw()
    {
        // Act
        var format = ResponseDisplay.FormatOf(new ApiResponse(200, "OK", 5, 7, [new("Content-Type", "text/html")], "<p>hej</p>"));

        // Assert
        Assert.Equal(BodyFormat.Raw, format);
    }
}
