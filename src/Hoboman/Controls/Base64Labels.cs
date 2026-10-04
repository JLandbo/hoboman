using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Hoboman.ViewModels;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace Hoboman.Controls;

// What the marked lines say, right-aligned in a column of its own beside the text, so it never covers the text, also when scrolled or wrapped.
// The column is as wide as the most a line says in the whole body, so it keeps its width while scrolling, and has none when nothing is said.
sealed class Base64Labels : AbstractMargin
{
    const double _left = 12;
    const double _right = 10;
    const double _gap = 8;

    MarkedEditor? _editor;
    double _width;

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView is not null)
        {
            oldTextView.VisualLinesChanged -= TextView_VisualLinesChanged;
        }
        if (_editor is not null)
        {
            _editor.MarksRedrawn -= Remeasure;
        }
        base.OnTextViewChanged(oldTextView, newTextView);
        _editor = newTextView?.GetService(typeof(TextEditor)) as MarkedEditor;
        if (newTextView is not null)
        {
            newTextView.VisualLinesChanged += TextView_VisualLinesChanged;
        }
        if (_editor is not null)
        {
            _editor.MarksRedrawn += Remeasure;
        }
        Remeasure();
    }

    protected override Size MeasureOverride(Size availableSize) => new(_width, 0);

    protected override void OnRender(DrawingContext drawing)
    {
        if (_editor is not { ShowsMarks: true } editor || TextView is not { VisualLinesValid: true } textView)
        {
            return;
        }
        var dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        drawing.PushOpacity(editor.MarksAreCurrent ? 1 : Base64Margin.Stale);
        foreach (var line in textView.VisualLines)
        {
            if (editor.MarkAt(line.FirstDocumentLine.LineNumber) is not { } mark || editor.TintOf(mark) is not { } tint)
            {
                continue;
            }
            // The line's tint goes on under what it says.
            var (top, height) = (line.VisualTop - textView.VerticalOffset, line.TextLines[0].Height);
            drawing.DrawRectangle(tint, null, new(0, top, ActualWidth, height));
            var labels = LabelsOf(mark, dip);
            var left = ActualWidth - _right - WidthOf(labels);
            foreach (var label in labels)
            {
                left = Write(drawing, label, left, top, height) + _gap;
            }
        }
        drawing.Pop();
    }

    // Found only when the marks change, as a response can have hundreds of thousands of them,
    // and each different thing said is measured once, as a list can hold the same property many thousand times.
    void Remeasure()
    {
        _width = 0;
        if (_editor is { ShowsMarks: true } editor)
        {
            var dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var widest = editor.ShownMarks.Where(mark => editor.TintOf(mark) is not null).Select(mark => mark with { Line = 0, Path = "" }).Distinct()
                .Select(mark => WidthOf(LabelsOf(mark, dip))).DefaultIfEmpty(0).Max();
            _width = widest > 0 ? _left + widest + _right : 0;
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    void TextView_VisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    sealed record Label(FormattedText Text, Brush? Background, double Width);

    static double WidthOf(List<Label> labels) => labels.Count == 0 ? 0 : labels.Sum(label => label.Width) + _gap * (labels.Count - 1);

    // In the order they are read: what happens to the property, a note on it, and what was saved from it.
    List<Label> LabelsOf(Base64Mark mark, double dip)
    {
        var labels = new List<Label>();
        if (mark.Badge is { } badge)
        {
            var (text, back) = mark.State switch
            {
                Base64MarkState.Decoded => (Brush("Success"), Brush("SuccessSoft")),
                Base64MarkState.Failed => (Brush("Error"), Brush("ErrorSoft")),
                _ => (Brush("Attention"), Base64Highlighter.Tint(Brush("Attention"), 0x29)),
            };
            labels.Add(LabelOf(badge, FontWeights.SemiBold, text, back, dip));
        }
        if (mark.Note is { } note)
        {
            labels.Add(LabelOf(note, FontWeights.Normal, Brush("Muted"), null, dip));
        }
        if (mark.Saved is { } saved)
        {
            labels.Add(LabelOf(saved, FontWeights.SemiBold, Brush("Success"), Brush("SuccessSoft"), dip));
        }
        return labels;
    }

    Label LabelOf(string text, FontWeight weight, Brush foreground, Brush? background, double dip)
    {
        var typeface = new Typeface((FontFamily)FindResource("UiFont"), FontStyles.Normal, weight, FontStretches.Normal);
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 10, foreground, dip);
        return new(formatted, background, formatted.Width + (background is null ? 0 : 12));
    }

    // No higher than the line, and gives where it ends, so the next one goes after it.
    static double Write(DrawingContext drawing, Label label, double left, double top, double height)
    {
        var box = new Rect(left, top + (height - label.Text.Height) / 2, label.Width, label.Text.Height);
        if (label.Background is { } background)
        {
            drawing.DrawRoundedRectangle(background, null, box, 4, 4);
        }
        drawing.DrawText(label.Text, new(box.X + (label.Background is null ? 0 : 6), box.Y));
        return box.Right;
    }

    Brush Brush(string key) => (Brush)FindResource(key);
}
