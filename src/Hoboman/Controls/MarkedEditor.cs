using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hoboman.ViewModels;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace Hoboman.Controls;

// An AvalonEdit editor with a checkbox beside each property of a JSON body, for choosing what goes as Base64.
public abstract class MarkedEditor : TextEditor
{
    public static readonly DependencyProperty ColoringProperty = DependencyProperty.Register(nameof(Coloring), typeof(BodyFormat), typeof(MarkedEditor),
        new(BodyFormat.Raw, (editor, _) => ((MarkedEditor)editor).Recolor()));

    public static readonly DependencyProperty MarksProperty = DependencyProperty.Register(nameof(Marks), typeof(IReadOnlyList<Base64Mark>), typeof(MarkedEditor),
        new(Array.Empty<Base64Mark>(), (editor, _) => ((MarkedEditor)editor)._margin.Refresh()));

    // Only drawn again, as placing the checkboxes anew would take them from the lines they followed back to the ones the body was last read at.
    public static readonly DependencyProperty MarksAreCurrentProperty = DependencyProperty.Register(nameof(MarksAreCurrent), typeof(bool), typeof(MarkedEditor),
        new(true, (editor, _) => ((MarkedEditor)editor)._margin.Redraw()));

    readonly Base64Margin _margin;
    readonly Base64Highlighter _highlighter;
    readonly Base64Strip _strip;
    readonly BodyFolding _folding;
    int? _pointed;

    protected MarkedEditor()
    {
        Colorings.Theme(this);
        _margin = new(this);
        TextArea.LeftMargins.Insert(0, _margin);
        _highlighter = new(this, _margin);
        TextArea.TextView.BackgroundRenderers.Add(_highlighter);
        // The text area's own template with a column on the right for what the marked lines say, so the text gets narrower instead of covered.
        TextArea.Template = (ControlTemplate)FindResource("MarkedTextArea");
        // What a pointed line says is drawn over the text, so it goes in a layer of its own above it.
        _strip = new(this) { IsHitTestVisible = false };
        TextArea.TextView.InsertLayer(_strip, KnownLayer.Caret, LayerInsertionPosition.Below);
        _folding = new(this);
    }

    // The view that shows the editor knows which choices a click changes.
    public event EventHandler<string>? MarkToggled;

    public BodyFormat Coloring
    {
        get => (BodyFormat)GetValue(ColoringProperty);
        set => SetValue(ColoringProperty, value);
    }

    public IReadOnlyList<Base64Mark> Marks
    {
        get => (IReadOnlyList<Base64Mark>)GetValue(MarksProperty);
        set => SetValue(MarksProperty, value);
    }

    public bool MarksAreCurrent
    {
        get => (bool)GetValue(MarksAreCurrentProperty);
        set => SetValue(MarksAreCurrentProperty, value);
    }

    // Only JSON has properties to choose.
    internal bool ShowsMarks => Coloring == BodyFormat.Json;

    internal void Toggle(string path) => MarkToggled?.Invoke(this, path);

    internal Base64Mark? MarkAt(int line) => _margin.MarkAt(line);

    internal IEnumerable<Base64Mark> ShownMarks => _margin.Shown;

    internal Brush? TintOf(Base64Mark mark) => _highlighter.TintOf(mark);

    // The line whose icon is pointed at, so what it says unfolds over its text.
    internal int? Pointed
    {
        get => _pointed;
        set
        {
            if (_pointed != value)
            {
                _pointed = value;
                _strip.InvalidateVisual();
            }
        }
    }

    // The marks changed or are drawn otherwise, so the column with their icons may come or go.
    internal event Action? MarksRedrawn
    {
        add => _margin.Redrawn += value;
        remove => _margin.Redrawn -= value;
    }

    void Recolor()
    {
        SyntaxHighlighting = Colorings.Of(Coloring);
        _margin.Refresh();
        _folding.UpdateSoon();
    }
}
