using Microsoft.Extensions.Time.Testing;

namespace Hoboman.Tests.ViewModels;

public sealed class ClipboardViewModelTests
{
    readonly FakeTimeProvider _clock = new();
    readonly Translator _translator = new(Translation.English);

    ClipboardViewModel ClipboardOf(FakeClipboard clipboard) => new(clipboard, _translator, _clock, NullLogger<ClipboardViewModel>.Instance);

    [Fact]
    public async Task Stringify_WhenTheClipboardHasText_ThenPutsItBackAsAJsonStringAndSaysSo()
    {
        // Arrange
        var clipboard = new FakeClipboard("\"data\": \"name\"");
        var tools = ClipboardOf(clipboard);

        // Act
        await tools.StringifyAsync();

        // Assert
        Assert.Equal(("\"\\\"data\\\": \\\"name\\\"\"", "Copied"), (clipboard.Held, tools.Message));
    }

    [Fact]
    public async Task Parse_WhenTheClipboardHasAJsonString_ThenPutsItsTextBack()
    {
        // Arrange
        var clipboard = new FakeClipboard("\"{\\\"a\\\":1}\"");

        // Act
        await ClipboardOf(clipboard).ParseAsync();

        // Assert
        Assert.Equal("{\"a\":1}", clipboard.Held);
    }

    [Fact]
    public async Task Parse_WhenTheClipboardIsNotAJsonString_ThenLeavesItAndSaysSo()
    {
        // Arrange
        var clipboard = new FakeClipboard("plain text");
        var tools = ClipboardOf(clipboard);

        // Act
        await tools.ParseAsync();

        // Assert
        Assert.Equal(("plain text", "The clipboard does not hold a JSON string"), (clipboard.Held, tools.Message));
    }

    [Fact]
    public async Task Stringify_WhenTheClipboardHasNoText_ThenSaysSo()
    {
        // Arrange
        var tools = ClipboardOf(new FakeClipboard());

        // Act
        await tools.StringifyAsync();

        // Assert
        Assert.Equal("There is no text in the clipboard", tools.Message);
    }

    [Fact]
    public async Task Stringify_WhenAnotherProgramHasTheClipboard_ThenSaysSo()
    {
        // Arrange
        var tools = ClipboardOf(new FakeClipboard(busy: true));

        // Act
        await tools.StringifyAsync();

        // Assert
        Assert.Equal("Another program is using the clipboard", tools.Message);
    }

    [Fact]
    public async Task Message_WhenItHasBeenShownForAWhile_ThenGoesAway()
    {
        // Arrange
        var tools = ClipboardOf(new FakeClipboard("text"));
        await tools.StringifyAsync();

        // Act
        _clock.Advance(TimeSpan.FromSeconds(2));

        // Assert
        Assert.Null(tools.Message);
    }

    [Fact]
    public async Task Relabel_WhenAMessageIsShown_ThenWritesItInTheNewLanguage()
    {
        // Arrange
        var tools = ClipboardOf(new FakeClipboard("text"));
        await tools.StringifyAsync();
        _translator.Use(Translation.Danish);

        // Act
        tools.Relabel();

        // Assert
        Assert.Equal("Kopieret", tools.Message);
    }
}
