using System.Windows;
using Hoboman.ViewModels;

namespace Hoboman.Desktop;

public sealed class SystemClipboard : IClipboard
{
    public string? Text() => Clipboard.ContainsText() ? Clipboard.GetText() : null;

    public void Put(string text) => Clipboard.SetText(text);
}
