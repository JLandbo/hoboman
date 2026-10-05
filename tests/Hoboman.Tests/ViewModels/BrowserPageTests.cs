using System.Text;

namespace Hoboman.Tests.ViewModels;

public sealed class BrowserPageTests
{
    static ApiResponse Answer(byte[] bytes, params ResponseHeader[] headers) => new(200, "OK", 1, bytes.Length, headers, Encoding.UTF8.GetString(bytes)) { Bytes = bytes };

    [Fact]
    public void Of_WhenTheResponseHasAContentType_ThenShowsItsBytesAsThat()
    {
        // Arrange
        byte[] bytes = [1, 2, 3];

        // Act
        var page = BrowserPage.Of(Answer(bytes, new ResponseHeader("Content-Type", "image/png")), decodeWhole: false)!;

        // Assert
        Assert.Equal((bytes, "image/png"), (page.Bytes, page.ContentType));
    }

    [Fact]
    public void Of_WhenTheWholeBodyIsDecoded_ThenShowsTheDecodedBytesWithoutTheTypeOfTheBase64()
    {
        // Arrange
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4");

        // Act
        var page = BrowserPage.Of(Answer(Encoding.ASCII.GetBytes(Convert.ToBase64String(pdf)), new ResponseHeader("Content-Type", "text/plain")), decodeWhole: true)!;

        // Assert
        Assert.Equal(pdf, page.Bytes);
        Assert.Null(page.ContentType);
    }

    [Fact]
    public void Of_WhenTheWholeBodyIsNoBase64_ThenHasNoPage()
    {
        // Act
        var page = BrowserPage.Of(Answer(Encoding.ASCII.GetBytes("ikke base64!")), decodeWhole: true);

        // Assert
        Assert.Null(page);
    }

    [Fact]
    public void Of_WhenAFileHasOnlyItsText_ThenHasNoPage()
    {
        // Act
        var page = BrowserPage.Of(new ApiResponse(200, "OK", 1, 4, [new("Content-Type", "application/pdf")], "%PDF"), decodeWhole: false);

        // Assert
        Assert.Null(page);
    }

    [Fact]
    public void Of_WhenTheResponseHasOnlyItsText_ThenShowsThatText()
    {
        // Act
        var page = BrowserPage.Of(new ApiResponse(200, "OK", 1, 3, [], "Dør"), decodeWhole: false)!;

        // Assert
        Assert.Equal("Dør", Encoding.UTF8.GetString(page.Bytes));
    }
}
