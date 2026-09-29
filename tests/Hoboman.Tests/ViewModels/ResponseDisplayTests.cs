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
}
