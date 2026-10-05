using Hoboman.Controls;

namespace Hoboman.Tests.Controls;

public sealed class HtmlFoldsTests
{
    [Fact]
    public void Of_WhenElementsSpanLines_ThenFoldsEachFromItsStartTagToItsEndTag()
    {
        // Arrange
        var text = "<html>\n\t<body>\n\t\t<p>a</p>\n\t</body>\n</html>";

        // Act
        var folds = HtmlFolds.Of(text);

        // Assert
        Assert.Equal([(0, text.Length, "<html>"), (8, text.Length - 8, "<body>")], folds.Select(fold => (fold.StartOffset, fold.EndOffset, fold.Name)));
    }

    [Fact]
    public void Of_WhenAnElementFitsOnALine_ThenHasNoFold()
    {
        // Act
        var folds = HtmlFolds.Of("<p>a</p>\n<br>");

        // Assert
        Assert.Empty(folds);
    }

    [Fact]
    public void Of_WhenAnElementIsNeverClosed_ThenTheOneAroundItClosesBoth()
    {
        // Arrange
        var text = "<ul>\n<li>a\n</ul>\n</ul>";

        // Act
        var folds = HtmlFolds.Of(text);

        // Assert
        Assert.Equal((0, text.IndexOf("</ul>") + 5, "<ul>"), folds.Select(fold => (fold.StartOffset, fold.EndOffset, fold.Name)).Single());
    }
}
