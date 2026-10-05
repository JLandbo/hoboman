namespace Hoboman.Tests.ViewModels;

public sealed class ResponseDisplayTests
{
    [Fact]
    public void Format_WhenTheBodyIsJson_ThenIndentsItAndKeepsTheLetters()
    {
        // Act
        var body = ResponseDisplay.Format("""{"navn":"Ærø"}""", BodyFormat.Json).Body;

        // Assert
        Assert.Equal($"{{{Environment.NewLine}\t\"navn\": \"Ærø\"{Environment.NewLine}}}", body);
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
    public void Of_WhenTheBodyIsVeryLarge_ThenShowsAllOfIt()
    {
        // Act
        var display = ResponseDisplay.Of(new ApiResponse(200, "OK", 5, 3_000_000, [], new string('x', 3_000_000)));

        // Assert
        Assert.Equal(3_000_000, display.Body.Length);
    }

    [Fact]
    public void Format_WhenTheBodyIsXml_ThenIndentsIt()
    {
        // Act
        var body = ResponseDisplay.Format("""<svg xmlns="http://www.w3.org/2000/svg"><rect width="1"/></svg>""", BodyFormat.Xml).Body;

        // Assert
        Assert.Equal($"<svg xmlns=\"http://www.w3.org/2000/svg\">{Environment.NewLine}\t<rect width=\"1\" />{Environment.NewLine}</svg>", body);
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
        Assert.Equal($"{{{Environment.NewLine}\t\"a\": 1{Environment.NewLine}}}", display.Body);
    }

    [Fact]
    public void Of_WhenTheContentTypeIsSvg_ThenFormatsItAsXml()
    {
        // Act
        var display = ResponseDisplay.Of(new ApiResponse(200, "OK", 5, 20, [new("Content-Type", "image/svg+xml")], "<svg><g/></svg>"));

        // Assert
        Assert.Equal($"<svg>{Environment.NewLine}\t<g />{Environment.NewLine}</svg>", display.Body);
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

    [Theory]
    [InlineData("application/problem+json", BodyFormat.Json)]
    [InlineData("application/x-ndjson", BodyFormat.Raw)]
    [InlineData("application/atom+xml", BodyFormat.Xml)]
    [InlineData("image/svg+xml", BodyFormat.Xml)]
    public void FormatOf_WhenTheTypeHasASuffix_ThenChoosesByIt(string type, BodyFormat expected)
    {
        // Act
        var format = ResponseDisplay.FormatOf(new ApiResponse(200, "OK", 5, 7, [new("Content-Type", type)], ""));

        // Assert
        Assert.Equal(expected, format);
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("application/pdf")]
    public void FormatOf_WhenTheResponseIsAnImageOrAPdf_ThenChoosesTheBrowser(string type)
    {
        // Act
        var format = ResponseDisplay.FormatOf(new ApiResponse(200, "OK", 5, 7, [new("Content-Type", type)], ""));

        // Assert
        Assert.Equal(BodyFormat.Browser, format);
    }

    [Theory]
    [InlineData("text/html; charset=utf-8")]
    [InlineData("application/xhtml+xml")]
    public void FormatOf_WhenTheContentTypeIsHtml_ThenChoosesHtml(string type)
    {
        // Act
        var format = ResponseDisplay.FormatOf(new ApiResponse(200, "OK", 5, 7, [new("Content-Type", type)], "<p>hej</p>"));

        // Assert
        Assert.Equal(BodyFormat.Html, format);
    }

    [Fact]
    public void Format_WhenTheBodyIsHtml_ThenPutsEachTagOnALineAndTextInItsElement()
    {
        // Act
        var body = ResponseDisplay.Format("""<!DOCTYPE html><html><body class="a>b"><h1>Faktura</h1><br><p>a < b</p></body></html>""", BodyFormat.Html).Body;

        // Assert
        Assert.Equal(string.Join(Environment.NewLine, "<!DOCTYPE html>", "<html>", "\t<body class=\"a>b\">", "\t\t<h1>Faktura</h1>", "\t\t<br>", "\t\t<p>a < b</p>", "\t</body>", "</html>"), body);
    }

    [Fact]
    public void Format_WhenHtmlHasAScript_ThenKeepsItsCodeAsItIs()
    {
        // Act
        var body = ResponseDisplay.Format("<div><script>if (a<b) {\n  go();\n}</script></div>", BodyFormat.Html).Body;

        // Assert
        Assert.Equal($"<div>{Environment.NewLine}\t<script>if (a<b) {{\n  go();\n}}</script>{Environment.NewLine}</div>", body);
    }

    [Fact]
    public void Format_WhenHtmlHasACommentAndSelfClosingTags_ThenKeepsEachWholeOnItsLine()
    {
        // Act
        var body = ResponseDisplay.Format("<div><!-- a > b --><br/><x /><p>a</p></div>", BodyFormat.Html).Body;

        // Assert
        Assert.Equal(string.Join(Environment.NewLine, "<div>", "\t<!-- a > b -->", "\t<br/>", "\t<x />", "\t<p>a</p>", "</div>"), body);
    }

    [Fact]
    public void Format_WhenHtmlHasAStyle_ThenKeepsItAsItIs()
    {
        // Act
        var body = ResponseDisplay.Format("<head><style>td > p {\n  color: red;\n}</style></head>", BodyFormat.Html).Body;

        // Assert
        Assert.Equal($"<head>{Environment.NewLine}\t<style>td > p {{\n  color: red;\n}}</style>{Environment.NewLine}</head>", body);
    }

    [Fact]
    public void Format_WhenAnHtmlElementIsNeverClosed_ThenItHoldsTheRest()
    {
        // Act
        var body = ResponseDisplay.Format("<ul><li>a<li>b</ul><p>c</p>", BodyFormat.Html).Body;

        // Assert
        Assert.Equal(string.Join(Environment.NewLine, "<ul>", "\t<li>", "\t\ta", "\t\t<li>", "\t\t\tb", "</ul>", "<p>c</p>"), body);
    }

    [Fact]
    public void Format_WhenTheBrowserShowsTheBody_ThenLaysOutNoText()
    {
        // Act
        var body = ResponseDisplay.Format("%PDF-1.4", BodyFormat.Browser).Body;

        // Assert
        Assert.Empty(body);
    }

    [Fact]
    public void Format_WhenTheBodyIsNoHtml_ThenShowsItAsItIs()
    {
        // Act
        var (body, coloring) = ResponseDisplay.Format("\"<p>hej</p>\"", BodyFormat.Html);

        // Assert
        Assert.Equal(("\"<p>hej</p>\"", BodyFormat.Raw), (body, coloring));
    }

    [Fact]
    public void Format_WhenTheXmlHasALineBreakBetweenSomeTags_ThenIndentsEveryTag()
    {
        // Act
        var body = ResponseDisplay.Format("<svg>\n<g><rect/></g></svg>", BodyFormat.Xml).Body;

        // Assert
        Assert.Equal(string.Join(Environment.NewLine, "<svg>", "\t<g>", "\t\t<rect />", "\t</g>", "</svg>"), body);
    }

    [Fact]
    public void Format_WhenALineBreakComesBeforeTheDeclaration_ThenStillFormatsIt()
    {
        // Act
        var (_, coloring) = ResponseDisplay.Format("\n<?xml version=\"1.0\"?><a><b/></a>", BodyFormat.Xml);

        // Assert
        Assert.Equal(BodyFormat.Xml, coloring);
    }

    [Fact]
    public void Format_WhenTheXmlStartsWithAByteOrderMark_ThenStillFormatsIt()
    {
        // Act
        var (_, coloring) = ResponseDisplay.Format("\uFEFF<?xml version=\"1.0\"?><a><b/></a>", BodyFormat.Xml);

        // Assert
        Assert.Equal(BodyFormat.Xml, coloring);
    }
}
