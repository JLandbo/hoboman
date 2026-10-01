using System.Runtime.InteropServices;
using Hoboman.Core.Base64;
using Hoboman.Core.Text;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

// Changes the text in the clipboard.
public sealed class ClipboardViewModel(IClipboard clipboard, ILogger<ClipboardViewModel> logger) : ObservableObject
{
    public bool CanChange { get; private set => Set(ref field, value); } = true;

    public Task<bool> StringifyAsync() => ChangeAsync(JsonString.Of);

    public Task<bool> ParseAsync() => ChangeAsync(JsonString.From);

    public Task<bool> EncodeBase64Async() => ChangeAsync(Base64Text.Encode);

    public Task<bool> DecodeBase64Async() => ChangeAsync(text => Base64Text.TryDecode(text, out var decoded) ? decoded : null);

    // The clipboard is read and written on the UI thread, as it must be, and a large text is turned into the other off it.
    async Task<bool> ChangeAsync(Func<string, string?> change)
    {
        if (!CanChange)
        {
            return false;
        }
        CanChange = false;
        try
        {
            if (clipboard.Text() is not { } text)
            {
                return false;
            }
            if (await Task.Run(() => change(text)) is not { } changed)
            {
                return false;
            }
            clipboard.Put(changed);
            return true;
        }
        // Another program can hold the clipboard open for longer than it is waited for.
        catch (ExternalException exception)
        {
            logger.LogWarning(exception, "Could not use the clipboard");
            return false;
        }
        finally
        {
            CanChange = true;
        }
    }

}
