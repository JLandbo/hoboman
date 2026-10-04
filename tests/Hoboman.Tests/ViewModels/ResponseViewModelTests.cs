namespace Hoboman.Tests.ViewModels;

public sealed class ResponseViewModelTests
{
    // Not text, as a PDF is not.
    static readonly byte[] _file = [0x25, 0x50, 0x44, 0x46, 0x00, 0xFF, 0xFE, 0x80];

    static ApiResponse Pdf(byte[]? bytes) => new(200, "OK", 1, _file.Length, [new("Content-Type", "application/pdf")], "%PDF\0���") { Bytes = bytes };

    static async Task<ResponseViewModel> ShowAsync(Harness harness, ApiResponse response)
    {
        var result = new ResponseViewModel(harness.Translator, new(harness.Translator, harness.Clock), harness.Dialogs);
        await result.ShowAsync(response);
        return result;
    }

    [Fact]
    public async Task SaveAsAsync_WhenAPathIsChosen_ThenWritesTheBodyAsTheServerSentIt()
    {
        // Arrange
        using var harness = new Harness();
        var result = await ShowAsync(harness, Pdf(_file));
        Directory.CreateDirectory(harness.Folder.Root);
        harness.Dialogs.SavePath = Path.Combine(harness.Folder.Root, "udskrift.pdf");

        // Act
        await result.SaveAsAsync();

        // Assert
        Assert.Equal("response.pdf", harness.Dialogs.SaveQuestion);
        Assert.Equal(_file, await File.ReadAllBytesAsync(harness.Dialogs.SavePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveAsAsync_WhenTheFileCannotBeWritten_ThenTellsWhy()
    {
        // Arrange
        using var harness = new Harness();
        var result = await ShowAsync(harness, Pdf(_file));
        harness.Dialogs.SavePath = Path.Combine(harness.Folder.Root, "missing", "udskrift.pdf");

        // Act
        await result.SaveAsAsync();

        // Assert
        Assert.Equal(harness.Translator.Of("Response.SaveFailed"), harness.Dialogs.Notification?.Title);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task SaveAs_WhenTheResponseHasItsBytesOrNot_ThenCanBeUsedOrNot(bool bytes, bool expected)
    {
        // Arrange
        using var harness = new Harness();

        // Act
        var result = await ShowAsync(harness, Pdf(bytes ? _file : null));

        // Assert
        Assert.Equal(expected, result.SaveAs.CanExecute(null));
    }

    [Theory]
    [InlineData("application/pdf", "response.pdf")]
    [InlineData("application/problem+json; charset=utf-8", "response.json")]
    [InlineData("TEXT/XML", "response.xml")]
    [InlineData("image/svg+xml", "response.svg")]
    [InlineData("text/plain", "response.txt")]
    [InlineData("application/octet-stream", "response")]
    [InlineData(null, "response")]
    public void FileNameOf_WhenTheContentTypeIsGiven_ThenEndsAsItsFilesDo(string? type, string expected)
    {
        // Arrange
        var response = new ApiResponse(200, "OK", 1, 0, type is null ? [] : [new("Content-Type", type)], "");

        // Act
        var name = ResponseViewModel.FileNameOf(response);

        // Assert
        Assert.Equal(expected, name);
    }
}
