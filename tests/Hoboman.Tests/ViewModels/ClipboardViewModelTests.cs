using Microsoft.Extensions.Time.Testing;

namespace Hoboman.Tests.ViewModels;

public sealed class ClipboardViewModelTests
{
    readonly FakeTimeProvider _clock = new();
    readonly Translator _translator = new(Translation.English);

    ClipboardViewModel ClipboardOf(IClipboard clipboard) => new(clipboard, _translator, _clock, NullLogger<ClipboardViewModel>.Instance);

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
    public async Task EncodeBase64_WhenTheClipboardHasText_ThenPutsItBackAsBase64AndSaysSo()
    {
        // Arrange
        var clipboard = new FakeClipboard("Ærø");
        var tools = ClipboardOf(clipboard);

        // Act
        var succeeded = await tools.EncodeBase64Async();

        // Assert
        Assert.Equal(("w4Zyw7g=", "Copied", true), (clipboard.Held, tools.Message, succeeded));
    }

    [Fact]
    public async Task DecodeBase64_WhenTheClipboardHasBase64Text_ThenPutsItsTextBack()
    {
        // Arrange
        var clipboard = new FakeClipboard("w4Zyw7g=");

        // Act
        await ClipboardOf(clipboard).DecodeBase64Async();

        // Assert
        Assert.Equal("Ærø", clipboard.Held);
    }

    [Fact]
    public async Task DecodeBase64_WhenTheClipboardIsNotBase64_ThenLeavesItAndSaysSo()
    {
        // Arrange
        var clipboard = new FakeClipboard("not Base64!");
        var tools = ClipboardOf(clipboard);

        // Act
        var succeeded = await tools.DecodeBase64Async();

        // Assert
        Assert.Equal(("not Base64!", "The clipboard does not hold valid Base64 text", false), (clipboard.Held, tools.Message, succeeded));
    }

    [Fact]
    public async Task Change_WhenAnotherChangeIsRunning_ThenIgnoresIt()
    {
        // Arrange
        using var clipboard = new PausedClipboard("text");
        var tools = ClipboardOf(clipboard);

        // Act
        var encoding = Task.Run(tools.EncodeBase64Async);
        await clipboard.FirstRead.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        bool stringified;
        try
        {
            stringified = await tools.StringifyAsync();
        }
        finally
        {
            clipboard.Continue();
        }
        var encoded = await encoding;

        // Assert
        Assert.Equal((1, true, false, true), (clipboard.Reads, encoded, stringified, tools.CanChange));
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

    sealed class PausedClipboard(string text) : IClipboard, IDisposable
    {
        readonly ManualResetEventSlim _continue = new();
        readonly TaskCompletionSource _firstRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int _reads;

        public Task FirstRead => _firstRead.Task;

        public int Reads => _reads;

        public string? Text()
        {
            if (Interlocked.Increment(ref _reads) == 1)
            {
                _firstRead.SetResult();
                _continue.Wait();
            }
            return text;
        }

        public void Put(string text) { }

        public void Continue() => _continue.Set();

        public void Dispose() => _continue.Dispose();
    }
}
