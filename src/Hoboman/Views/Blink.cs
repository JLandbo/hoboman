using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Hoboman.Views;

// A button blinks green or red after what it did, then fades back to its own background.
static class Blink
{
    static readonly TimeSpan _shown = TimeSpan.FromMilliseconds(200);
    static readonly TimeSpan _fade = TimeSpan.FromMilliseconds(300);

    public static void Show(Button button, bool succeeded)
    {
        // A blink still fading is replaced, so it fades back to the button's own colour and not to its own.
        button.ClearValue(Control.BackgroundProperty);
        var normal = (button.Background as SolidColorBrush)?.Color ?? Colors.Transparent;
        var background = new SolidColorBrush(((SolidColorBrush)button.FindResource(succeeded ? "ClipboardSuccess" : "ClipboardError")).Color);
        button.Background = background;
        var fade = new ColorAnimation(normal, _fade) { BeginTime = _shown };
        fade.Completed += (_, _) =>
        {
            if (ReferenceEquals(button.Background, background))
            {
                button.ClearValue(Control.BackgroundProperty);
            }
        };
        background.BeginAnimation(SolidColorBrush.ColorProperty, fade);
    }
}
