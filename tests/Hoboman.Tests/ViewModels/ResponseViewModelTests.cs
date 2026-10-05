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
    public async Task ShowAsync_WhenTheResponseIsAPdf_ThenShowsItsBytesInTheBrowser()
    {
        // Arrange
        using var harness = new Harness();

        // Act
        var result = await ShowAsync(harness, Pdf(_file));

        // Assert
        Assert.Equal((BodyFormat.Browser, _file, "application/pdf"), (result.BodyFormat, result.Page!.Bytes, result.Page.ContentType));
    }

    [Fact]
    public async Task ShowAsync_WhenAFileHasOnlyItsText_ThenTellsTheBrowserCannotShowIt()
    {
        // Arrange
        using var harness = new Harness();

        // Act
        var result = await ShowAsync(harness, Pdf(null));

        // Assert
        Assert.Equal((null, harness.Translator.Of("Response.OnlyText")), (result.Page, result.ResponseBodyProblem));
    }

    [Fact]
    public async Task BodyFormat_WhenTheBrowserShowsInvalidBase64_ThenTellsIt()
    {
        // Arrange
        using var harness = new Harness();
        var result = await ShowAsync(harness, new(200, "OK", 1, 3, [new("Content-Type", "text/plain")], "ikke base64!"));
        result.Base64.DecodesWholeResponse = true;

        // Act
        result.BodyFormat = BodyFormat.Browser;
        await result.Formatting;

        // Assert
        Assert.Equal((null, harness.Translator.Of("Response.InvalidBase64")), (result.Page, result.ResponseBodyProblem));
    }

    [Fact]
    public async Task BodyFormat_WhenAnotherThanTheBrowserIsChosen_ThenHasNoPage()
    {
        // Arrange
        using var harness = new Harness();
        var result = await ShowAsync(harness, Pdf(_file));

        // Act
        result.BodyFormat = BodyFormat.Raw;
        await result.Formatting;

        // Assert
        Assert.Null(result.Page);
    }

    [Fact]
    public async Task BodyFormat_WhenTheBrowserShowsAFileSentAsBase64_ThenShowsItDecodedWithoutAProblem()
    {
        // Arrange
        using var harness = new Harness();
        var base64 = Convert.ToBase64String(_file);
        var result = await ShowAsync(harness, new(200, "OK", 1, base64.Length, [new("Content-Type", "text/plain")], base64));
        result.Base64.DecodesWholeResponse = true;

        // Act
        result.BodyFormat = BodyFormat.Browser;
        await result.Formatting;

        // Assert
        Assert.Equal(_file, result.Page!.Bytes);
        Assert.Null(result.ResponseBodyProblem);
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
