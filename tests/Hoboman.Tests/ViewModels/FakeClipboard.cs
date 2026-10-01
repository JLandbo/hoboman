using System.Runtime.InteropServices;

namespace Hoboman.Tests.ViewModels;

public sealed class FakeClipboard(string? text = null, bool busy = false) : IClipboard
{
    public string? Held { get; private set; } = text;

    public string? Text() => busy ? throw new ExternalException("Another program has the clipboard open") : Held;

    public void Put(string text) => Held = text;
}
