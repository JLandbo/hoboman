using System.Windows;
using Hoboman.ViewModels;
using ICSharpCode.AvalonEdit;

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
    readonly BodyFolding _folding;

    protected MarkedEditor()
    {
        Colorings.Theme(this);
        _margin = new(this);
        TextArea.LeftMargins.Insert(0, _margin);
        TextArea.TextView.BackgroundRenderers.Add(new Base64Highlighter(this, _margin));
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

    void Recolor()
    {
        SyntaxHighlighting = Colorings.Of(Coloring);
        _margin.Refresh();
        _folding.UpdateSoon();
    }
}
