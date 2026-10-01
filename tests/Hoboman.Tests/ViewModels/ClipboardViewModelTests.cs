namespace Hoboman.Tests.ViewModels;

public sealed class ClipboardViewModelTests
{
    static ClipboardViewModel ClipboardOf(IClipboard clipboard) => new(clipboard, NullLogger<ClipboardViewModel>.Instance);

    [Fact]
    public async Task Stringify_WhenTheClipboardHasText_ThenPutsItBackAsAJsonString()
    {
        // Arrange
        var clipboard = new FakeClipboard("\"data\": \"name\"");
        var tools = ClipboardOf(clipboard);

        // Act
        var succeeded = await tools.StringifyAsync();

        // Assert
        Assert.Equal(("\"\\\"data\\\": \\\"name\\\"\"", true), (clipboard.Held, succeeded));
    }

    [Fact]
    public async Task Parse_WhenTheClipboardHasAJsonString_ThenPutsItsTextBack()
    {
        // Arrange
        var clipboard = new FakeClipboard("\"{\\\"a\\\":1}\"");

        // Act
        var succeeded = await ClipboardOf(clipboard).ParseAsync();

        // Assert
        Assert.Equal(("{\"a\":1}", true), (clipboard.Held, succeeded));
    }

    [Fact]
    public async Task Parse_WhenTheClipboardIsNotAJsonString_ThenLeavesItAndSaysSo()
    {
        // Arrange
        var clipboard = new FakeClipboard("plain text");
        var tools = ClipboardOf(clipboard);

        // Act
        var succeeded = await tools.ParseAsync();

        // Assert
        Assert.Equal(("plain text", false), (clipboard.Held, succeeded));
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
        Assert.Equal(("w4Zyw7g=", true), (clipboard.Held, succeeded));
    }

    [Fact]
    public async Task DecodeBase64_WhenTheClipboardHasBase64Text_ThenPutsItsTextBack()
    {
        // Arrange
        var clipboard = new FakeClipboard("w4Zyw7g=");

        // Act
        var succeeded = await ClipboardOf(clipboard).DecodeBase64Async();

        // Assert
        Assert.Equal(("Ærø", true), (clipboard.Held, succeeded));
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
        Assert.Equal(("not Base64!", false), (clipboard.Held, succeeded));
    }

    [Fact]
    public async Task Change_WhenAnotherChangeIsRunning_ThenIgnoresIt()
    {
        // Arrange
        using var clipboard = new PausedClipboard("text");
        var tools = ClipboardOf(clipboard);

        // Act
        var encodingTask = Task.Run(tools.EncodeBase64Async);
        var stringified = false;
        var encoded = false;
        try
        {
            await clipboard.FirstRead.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            stringified = await tools.StringifyAsync();
        }
        finally
        {
            clipboard.Continue();
            encoded = await encodingTask;
        }

        // Assert
        Assert.Equal((1, true, false, true), (clipboard.Reads, encoded, stringified, tools.CanChange));
    }

    [Fact]
    public async Task Stringify_WhenTheClipboardHasNoText_ThenFails()
    {
        // Arrange
        var tools = ClipboardOf(new FakeClipboard());

        // Act
        var succeeded = await tools.StringifyAsync();

        // Assert
        Assert.False(succeeded);
    }

    [Fact]
    public async Task Stringify_WhenAnotherProgramHasTheClipboard_ThenFails()
    {
        // Arrange
        var tools = ClipboardOf(new FakeClipboard(busy: true));

        // Act
        var succeeded = await tools.StringifyAsync();

        // Assert
        Assert.False(succeeded);
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
