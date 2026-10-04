using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Hoboman.ViewModels;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace Hoboman.Controls;

// The checkboxes beside the properties of a JSON body. In an editable body each is held by an anchor, so it stays beside its line
// while lines are added or removed above it, until the body is read again.
sealed class Base64Margin(MarkedEditor editor) : AbstractMargin
{
    // How the checkboxes and what the lines say look while the body is not JSON.
    internal const double Stale = 0.45;
    const double _width = 30;
    // A line of code is about 15 px high, so the checkboxes leave a little room between them.
    const double _size = 12;

    // An unchosen checkbox is faint until its line is pointed at.
    const double _faint = 0.3;

    readonly Dictionary<int, Base64Mark> _byLine = [];
    int? _pointed;
    readonly List<(TextAnchor Anchor, Base64Mark Mark)> _anchored = [];

    public Base64Mark? MarkAt(int line) => editor.IsReadOnly ? FirstOn(editor.Marks, line) : _byLine.GetValueOrDefault(line);

    // The marks that have a line of their own to be shown on.
    public IEnumerable<Base64Mark> Shown => editor.IsReadOnly ? editor.Marks.Where((mark, index) => index == 0 || editor.Marks[index - 1].Line != mark.Line) : _byLine.Values;

    public void Refresh()
    {
        _anchored.Clear();
        _byLine.Clear();
        if (!editor.IsReadOnly && Document is { } document)
        {
            foreach (var mark in editor.Marks.Where(mark => mark.Line <= document.LineCount))
            {
                // At the property itself, after the indentation, so a line break or text typed before it moves the anchor along.
                var anchor = document.CreateAnchor(TextUtilities.GetLeadingWhitespace(document, document.GetLineByNumber(mark.Line)).EndOffset);
                anchor.MovementType = AnchorMovementType.AfterInsertion;
                _anchored.Add((anchor, mark));
            }
            Follow();
        }
        InvalidateMeasure();
        Redraw();
    }

    // The column with what the lines say is drawn again too.
    public event Action? Redrawn;

    public void Redraw()
    {
        InvalidateVisual();
        TextView?.InvalidateLayer(KnownLayer.Background);
        Redrawn?.Invoke();
    }

    protected override void OnDocumentChanged(TextDocument oldDocument, TextDocument newDocument)
    {
        if (oldDocument is not null)
        {
            oldDocument.Changed -= Document_Changed;
        }
        base.OnDocumentChanged(oldDocument, newDocument);
        if (newDocument is not null)
        {
            newDocument.Changed += Document_Changed;
        }
        Refresh();
    }

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView is not null)
        {
            oldTextView.VisualLinesChanged -= TextView_VisualLinesChanged;
        }
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null)
        {
            newTextView.VisualLinesChanged += TextView_VisualLinesChanged;
        }
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(editor.ShowsMarks ? _width : 0, 0);

    protected override void OnRender(DrawingContext drawing)
    {
        // Transparent rather than empty, so a click between the checkboxes still lands on the margin.
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (!editor.ShowsMarks || TextView is not { VisualLinesValid: true } textView)
        {
            return;
        }
        drawing.PushOpacity(editor.MarksAreCurrent ? 1 : Stale);
        foreach (var line in textView.VisualLines)
        {
            if (MarkAt(line.FirstDocumentLine.LineNumber) is { } mark)
            {
                var middle = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextMiddle) - textView.VerticalOffset;
                Draw(drawing, new((_width - _size) / 2, middle - _size / 2, _size, _size), mark.State, line.FirstDocumentLine.LineNumber == _pointed);
            }
        }
        drawing.Pop();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Clickable(MarkUnder(e)) is { } mark)
        {
            editor.Toggle(mark.Path);
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var mark = MarkUnder(e);
        // The path tells what a checkbox picks, such as every element of a list.
        ToolTip = mark?.Path;
        Cursor = Clickable(mark) is null ? null : Cursors.Hand;
        Point(mark?.Line);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Point(null);
    }

    void Point(int? line)
    {
        if (line != _pointed)
        {
            _pointed = line;
            InvalidateVisual();
        }
    }

    void Document_Changed(object? sender, DocumentChangeEventArgs e)
    {
        if (_anchored.Count > 0)
        {
            Follow();
            InvalidateVisual();
        }
    }

    void TextView_VisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    void Follow() => Place(_anchored.Where(held => !held.Anchor.IsDeleted).Select(held => (held.Anchor.Line, held.Mark)));

    // A shown body never moves its lines, and its marks come in the order of their lines, so one is found by halving
    // instead of putting hundreds of thousands into a table on the UI thread. It is the first on the line, as in an editable body.
    static Base64Mark? FirstOn(IReadOnlyList<Base64Mark> marks, int line)
    {
        var (low, high) = (0, marks.Count);
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (marks[middle].Line < line)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        return low < marks.Count && marks[low].Line == line ? marks[low] : null;
    }

    // A line has room for one checkbox, so it is the first property's.
    void Place(IEnumerable<(int Line, Base64Mark Mark)> marks)
    {
        _byLine.Clear();
        foreach (var (line, mark) in marks)
        {
            _byLine.TryAdd(line, mark);
        }
    }

    // A property inside a chosen one goes along with it, and a body that is not JSON has nothing to choose from.
    Base64Mark? Clickable(Base64Mark? mark) => mark is { State: not Base64MarkState.Inside } && editor.MarksAreCurrent ? mark : null;

    Base64Mark? MarkUnder(MouseEventArgs e) =>
        TextView is { VisualLinesValid: true } textView && textView.GetVisualLineFromVisualTop(e.GetPosition(textView).Y + textView.VerticalOffset) is { } line
            ? MarkAt(line.FirstDocumentLine.LineNumber)
            : null;

    void Draw(DrawingContext drawing, Rect box, Base64MarkState state, bool pointed)
    {
        drawing.PushOpacity(state switch
        {
            Base64MarkState.Inside => 0.35,
            Base64MarkState.Unchecked when !pointed => _faint,
            _ => 1,
        });
        switch (state)
        {
            case Base64MarkState.Checked or Base64MarkState.Decoded:
                drawing.DrawRoundedRectangle(Brush("Attention"), null, box, 3, 3);
                Sign(drawing, box, "\uE73E", "IconFont");
                break;
            case Base64MarkState.Failed:
                drawing.DrawRoundedRectangle(Brush("Error"), null, box, 3, 3);
                Sign(drawing, box, "!", "UiFont");
                break;
            default:
                drawing.DrawRoundedRectangle(Brush("SolidSurface"), new Pen(Brush("Handle"), 1), box, 3, 3);
                break;
        }
        drawing.Pop();
    }

    void Sign(DrawingContext drawing, Rect box, string sign, string font)
    {
        var text = new FormattedText(sign, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface((FontFamily)FindResource(font), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 9, Brush("OnAttention"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawing.DrawText(text, new(box.X + (box.Width - text.Width) / 2, box.Y + (box.Height - text.Height) / 2));
    }

    Brush Brush(string key) => (Brush)FindResource(key);
}
