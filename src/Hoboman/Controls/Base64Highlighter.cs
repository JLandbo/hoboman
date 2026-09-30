using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Hoboman.ViewModels;
using ICSharpCode.AvalonEdit.Rendering;

namespace Hoboman.Controls;

// Tints the lines of the chosen properties, and writes at the end of each what happens to it.
sealed class Base64Highlighter : IBackgroundRenderer
{
    readonly MarkedEditor _editor;
    readonly Base64Margin _margin;
    readonly Brush _chosen;
    readonly Brush _failed;
    readonly Typeface _badge;
    readonly Typeface _note;

    public Base64Highlighter(MarkedEditor editor, Base64Margin margin)
    {
        (_editor, _margin) = (editor, margin);
        _chosen = Tint("Attention", 0x12);
        _failed = Tint("Error", 0x14);
        var font = (FontFamily)editor.FindResource("UiFont");
        _badge = new(font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        _note = new(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawing)
    {
        if (!_editor.ShowsMarks || !textView.VisualLinesValid)
        {
            return;
        }
        var dip = VisualTreeHelper.GetDpi(textView).PixelsPerDip;
        drawing.PushOpacity(_editor.MarksAreCurrent ? 1 : Base64Margin.Stale);
        foreach (var line in textView.VisualLines)
        {
            // A property inside a chosen one only has its checkbox greyed, as tinting it too would stripe a whole chosen body.
            if (_margin.MarkAt(line.FirstDocumentLine.LineNumber) is not { State: Base64MarkState.Checked or Base64MarkState.Decoded or Base64MarkState.Failed } mark)
            {
                continue;
            }
            var (top, height) = (line.VisualTop - textView.VerticalOffset, line.TextLines[0].Height);
            drawing.DrawRectangle(mark.State == Base64MarkState.Failed ? _failed : _chosen, null, new(0, top, textView.ActualWidth, height));
            var right = textView.ActualWidth - 10;
            if (mark.Note is { } note)
            {
                right = Write(drawing, note, _note, Brush("Muted"), null, right, top, height, dip) - 8;
            }
            if (mark.Badge is { } badge)
            {
                var (text, back) = mark.State switch
                {
                    Base64MarkState.Decoded => (Brush("Success"), Brush("SuccessSoft")),
                    Base64MarkState.Failed => (Brush("Error"), Brush("ErrorSoft")),
                    _ => (Brush("Attention"), Tint("Attention", 0x29)),
                };
                Write(drawing, badge, _badge, text, back, right, top, height, dip);
            }
        }
        drawing.Pop();
    }

    // Right-aligned and no higher than the line, and gives where it starts, so the next text goes before it.
    static double Write(DrawingContext drawing, string text, Typeface typeface, Brush foreground, Brush? background, double right, double top, double height, double dip)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 10, foreground, dip);
        var padding = background is null ? 0 : 6;
        var box = new Rect(right - formatted.Width - 2 * padding, top + (height - formatted.Height) / 2, formatted.Width + 2 * padding, formatted.Height);
        if (background is not null)
        {
            drawing.DrawRoundedRectangle(background, null, box, 4, 4);
        }
        drawing.DrawText(formatted, new(box.X + padding, box.Y));
        return box.X;
    }

    Brush Brush(string key) => (Brush)_editor.FindResource(key);

    Brush Tint(string key, byte alpha)
    {
        var color = ((SolidColorBrush)Brush(key)).Color;
        var tint = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        tint.Freeze();
        return tint;
    }
}
