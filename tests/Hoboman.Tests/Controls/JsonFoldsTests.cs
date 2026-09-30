using Hoboman.Controls;

namespace Hoboman.Tests.Controls;

public sealed class JsonFoldsTests
{
    [Fact]
    public void Of_WhenAnObjectSpansLines_ThenFoldsItsInsideAndCountsItsProperties()
    {
        // Arrange
        var text = "{\n  \"a\": 1,\n  \"b\": 2\n}";

        // Act
        var folds = JsonFolds.Of(text);

        // Assert
        Assert.Equal([new JsonFold(1, text.Length - 1, 2, IsList: false)], folds);
    }

    [Fact]
    public void Of_WhenAListSpansLines_ThenCountsItsElements()
    {
        // Act
        var folds = JsonFolds.Of("[\n  1,\n  2,\n  3\n]");

        // Assert
        Assert.Equal((3, true), (folds.Single().Count, folds.Single().IsList));
    }

    [Fact]
    public void Of_WhenObjectsAreInsideEachOther_ThenGivesTheOuterOneFirst()
    {
        // Act
        var folds = JsonFolds.Of("{\n  \"data\": {\n    \"name\": \"Hobo\"\n  },\n  \"b\": 2\n}");

        // Assert
        Assert.Equal([2, 1], folds.Select(fold => fold.Count));
    }

    [Fact]
    public void Of_WhenBracketsAndCommasAreInsideText_ThenLeavesThemOut()
    {
        // Act
        var folds = JsonFolds.Of("{\n  \"a\": \"{[, \\\" ]}\"\n}");

        // Assert
        Assert.Equal(1, folds.Single().Count);
    }

    [Theory]
    [InlineData("{\"a\": 1}")]
    [InlineData("{\n}")]
    public void Of_WhenAnObjectIsOnOneLineOrEmpty_ThenDoesNotFoldIt(string text)
    {
        // Act
        var folds = JsonFolds.Of(text);

        // Assert
        Assert.Empty(folds);
    }

    [Fact]
    public void Of_WhenTheJsonIsUnfinished_ThenStillFoldsWhatIsClosed()
    {
        // Act
        var folds = JsonFolds.Of("{\n  \"a\": {\n    \"b\": 1\n  },\n  \"c\": ");

        // Assert
        Assert.Equal(1, folds.Single().Count);
    }
}
