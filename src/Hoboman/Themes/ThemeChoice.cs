using System.Windows.Media;

namespace Hoboman.Themes;

// A theme in the settings, shown in its own colours.
public sealed record ThemeChoice(Theme Theme, bool Selected)
{
    public string Name => Theme.Name;

    public Brush Surface => Theme.BrushOf(nameof(Surface));

    public Brush Edge => Theme.BrushOf(nameof(Edge));

    public Brush Text => Theme.BrushOf(nameof(Text));

    public Brush Muted => Theme.BrushOf(nameof(Muted));

    public Brush Attention => Theme.BrushOf(nameof(Attention));
}
