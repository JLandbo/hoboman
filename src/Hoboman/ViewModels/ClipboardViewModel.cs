using System.Runtime.InteropServices;
using Hoboman.Core.Languages;
using Hoboman.Core.Text;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

// Turns the text in the clipboard into a JSON string and back, and says for a moment how it went.
public sealed class ClipboardViewModel(IClipboard clipboard, Translator translator, TimeProvider clock, ILogger<ClipboardViewModel> logger) : ObservableObject
{
    static readonly TimeSpan _shown = TimeSpan.FromSeconds(1.5);

    readonly UiThread _ui = new();
    Outcome? _outcome;
    ITimer? _hiding;
    // Counts the messages, so the timer of an older one cannot hide a newer one.
    int _showing;

    enum Outcome { Copied, NoText, NotAString, Busy }

    // Kept as what happened, so it is written in the language of the moment.
    public string? Message => _outcome switch
    {
        Outcome.Copied => translator.Of("Clipboard.Copied"),
        Outcome.NoText => translator.Of("Clipboard.NoText"),
        Outcome.NotAString => translator.Of("Clipboard.NotAString"),
        Outcome.Busy => translator.Of("Clipboard.Busy"),
        _ => null,
    };

    public bool HasMessage => _outcome is not null;

    public Task StringifyAsync() => ChangeAsync(JsonString.Of);

    public Task ParseAsync() => ChangeAsync(JsonString.From);

    public void Relabel() => OnPropertyChanged(nameof(Message));

    // The clipboard is read and written on the UI thread, as it must be, and a large text is turned into the other off it.
    async Task ChangeAsync(Func<string, string?> change)
    {
        try
        {
            if (clipboard.Text() is not { } text)
            {
                Show(Outcome.NoText);
                return;
            }
            if (await Task.Run(() => change(text)) is not { } changed)
            {
                Show(Outcome.NotAString);
                return;
            }
            clipboard.Put(changed);
            Show(Outcome.Copied);
        }
        // Another program can hold the clipboard open for longer than it is waited for.
        catch (ExternalException exception)
        {
            logger.LogWarning(exception, "Could not use the clipboard");
            Show(Outcome.Busy);
        }
    }

    void Show(Outcome? outcome)
    {
        var showing = ++_showing;
        _outcome = outcome;
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(HasMessage));
        _hiding?.Dispose();
        if (outcome is not null)
        {
            _hiding = clock.CreateTimer(_ => _ui.Post(() =>
            {
                if (showing == _showing)
                {
                    Show(null);
                }
            }), null, _shown, Timeout.InfiniteTimeSpan);
        }
    }
}
