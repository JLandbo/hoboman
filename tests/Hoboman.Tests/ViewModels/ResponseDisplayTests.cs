namespace Hoboman.Tests.ViewModels;

public sealed class ResponseDisplayTests
{
    [Fact]
    public void PrettyOf_WhenTheBodyIsJson_ThenIndentsItAndKeepsTheLetters()
    {
        // Act
        var body = ResponseDisplay.PrettyOf("""{"navn":"Ærø"}""");

        // Assert
        Assert.Equal($"{{{Environment.NewLine}  \"navn\": \"Ærø\"{Environment.NewLine}}}", body);
    }

    [Fact]
    public void PrettyOf_WhenTheBodyIsNotJson_ThenKeepsIt()
    {
        // Act
        var body = ResponseDisplay.PrettyOf("<html />");

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
    public void PrettyOf_WhenTheBodyIsXml_ThenIndentsIt()
    {
        // Act
        var body = ResponseDisplay.PrettyOf("""<svg xmlns="http://www.w3.org/2000/svg"><rect width="1"/></svg>""");

        // Assert
        Assert.Equal($"<svg xmlns=\"http://www.w3.org/2000/svg\">{Environment.NewLine}  <rect width=\"1\" />{Environment.NewLine}</svg>", body);
    }

    [Fact]
    public void PrettyOf_WhenTheXmlHasADeclaration_ThenKeepsIt()
    {
        // Act
        var body = ResponseDisplay.PrettyOf("""<?xml version="1.0"?><a><b/></a>""");

        // Assert
        Assert.StartsWith("""<?xml version="1.0"?>""", body);
    }

    [Fact]
    public void PrettyOf_WhenTheBodyIsHtml_ThenKeepsIt()
    {
        // Act
        var body = ResponseDisplay.PrettyOf("<!DOCTYPE html><html><br></html>");

        // Assert
        Assert.Equal("<!DOCTYPE html><html><br></html>", body);
    }

    [Fact]
    public void PrettyOf_WhenTheXmlDefinesAnEntity_ThenDoesNotExpandIt()
    {
        // Act
        var body = ResponseDisplay.PrettyOf("""<!DOCTYPE a [<!ENTITY x "boom">]><a>&x;</a>""");

        // Assert
        Assert.Equal("""<!DOCTYPE a [<!ENTITY x "boom">]><a>&x;</a>""", body);
    }
}
