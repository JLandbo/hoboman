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
}
