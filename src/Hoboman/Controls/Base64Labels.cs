using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Hoboman.ViewModels;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace Hoboman.Controls;

// A thin column beside the text with a filled info icon on each marked line, yellow for Base64 and green for a saved variable.
// Pointing at an icon unfolds what its line says over the text (Base64Strip), so it covers the text only while asked for.
// The column is there only while some line says something.
sealed class Base64Labels : AbstractMargin
{
    const double _width = 22;
    const double _size = 13;

    MarkedEditor? _editor;
    bool _shown;

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

    protected override Size MeasureOverride(Size availableSize) => new(_shown ? _width : 0, 0);

    protected override void OnRender(DrawingContext drawing)
    {
        // Transparent rather than empty, so pointing anywhere beside a line shows what it says.
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (_editor is not { ShowsMarks: true } editor || TextView is not { VisualLinesValid: true } textView)
        {
            return;
        }
        var dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface((FontFamily)FindResource("UiFont"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        drawing.PushOpacity(editor.MarksAreCurrent ? 1 : Base64Margin.Stale);
        foreach (var line in textView.VisualLines)
        {
            if (editor.MarkAt(line.FirstDocumentLine.LineNumber) is not { } mark || editor.TintOf(mark) is not { } tint)
            {
                continue;
            }
            // The line's tint goes on under its icon.
            var (top, height) = (line.VisualTop - textView.VerticalOffset, line.TextLines[0].Height);
            drawing.DrawRectangle(tint, null, new(0, top, ActualWidth, height));
            var center = new Point(ActualWidth / 2, top + height / 2);
            drawing.DrawEllipse(FillOf(mark), null, center, _size / 2, _size / 2);
            var sign = new FormattedText("i", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 10, (Brush)FindResource("OnAttention"), dip);
            drawing.DrawText(sign, new(center.X - sign.Width / 2, center.Y - sign.Height / 2));
        }
        drawing.Pop();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point(TextView is { VisualLinesValid: true } textView ? textView.GetVisualLineFromVisualTop(e.GetPosition(textView).Y + textView.VerticalOffset) : null);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Point(null);
    }

    // Only a line with an icon unfolds.
    internal void Point(VisualLine? line)
    {
        if (_editor is { } editor)
        {
            editor.Pointed = line?.FirstDocumentLine.LineNumber is { } number && editor.MarkAt(number) is { } mark && editor.TintOf(mark) is not null ? number : null;
        }
    }

    void Remeasure()
    {
        _shown = _editor is { ShowsMarks: true } editor && editor.ShownMarks.Any(mark => editor.TintOf(mark) is not null);
        InvalidateMeasure();
        InvalidateVisual();
    }

    // The lines move when scrolled, so what was unfolded at one goes until the mouse moves again.
    void TextView_VisualLinesChanged(object? sender, EventArgs e)
    {
        Point(null);
        InvalidateVisual();
    }

    // A line that is both Base64 and saved from is shown as Base64, and what unfolds on pointing tells both.
    Brush FillOf(Base64Mark mark) => (Brush)FindResource(mark.State switch
    {
        Base64MarkState.Failed => "Error",
        Base64MarkState.Checked or Base64MarkState.Decoded => "Attention",
        _ => "Success",
    });
}
