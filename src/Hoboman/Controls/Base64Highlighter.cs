using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Hoboman.ViewModels;
using ICSharpCode.AvalonEdit.Rendering;

namespace Hoboman.Controls;

// Tints the lines of the chosen properties under the text, and writes over the text what happens to each.
// What a line says goes right after its text when there is room, and else at the right edge on the editor's own background, so it never mixes with the code.
sealed class Base64Highlighter : IBackgroundRenderer
{
    readonly MarkedEditor _editor;
    readonly Base64Margin _margin;
    readonly bool _labels;
    readonly Brush _chosen;
    readonly Brush _failed;
    readonly Brush _saved;
    readonly Typeface _badge;
    readonly Typeface _note;

    public Base64Highlighter(MarkedEditor editor, Base64Margin margin, bool labels)
    {
        (_editor, _margin, _labels) = (editor, margin, labels);
        _chosen = Tint("Attention", 0x12);
        _failed = Tint("Error", 0x14);
        _saved = Tint("Success", 0x14);
        var font = (FontFamily)editor.FindResource("UiFont");
        _badge = new(font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        _note = new(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    }

    // The labels are drawn by their own layer over the text, so only the tint is drawn as a background.
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
            if (_margin.MarkAt(line.FirstDocumentLine.LineNumber) is not { } mark || mark is { State: not (Base64MarkState.Checked or Base64MarkState.Decoded or Base64MarkState.Failed), Saved: null })
            {
                continue;
            }
            var (top, height) = (line.VisualTop - textView.VerticalOffset, line.TextLines[0].Height);
            var tint = mark.State switch
            {
                Base64MarkState.Failed => _failed,
                Base64MarkState.Checked or Base64MarkState.Decoded => _chosen,
                _ => _saved,
            };
            if (!_labels)
            {
                drawing.DrawRectangle(tint, null, new(0, top, textView.ActualWidth, height));
                continue;
            }
            var labels = LabelsOf(mark, dip);
            if (labels.Count == 0)
            {
                continue;
            }
            var width = labels.Sum(label => label.Width) + 8 * (labels.Count - 1);
            // The line's text line can be cut at the view, so the end is taken from where its last character is.
            var textEnd = line.GetVisualPosition(line.VisualLength, VisualYPosition.TextMiddle).X - textView.HorizontalOffset;
            var left = textEnd + 24;
            if (left + width > textView.ActualWidth - 10)
            {
                left = textView.ActualWidth - 10 - width;
                var behind = new Rect(left - 12, top, textView.ActualWidth - left + 12, height);
                drawing.DrawRectangle(Brush("Input"), null, behind);
                drawing.DrawRectangle(tint, null, behind);
            }
            foreach (var label in labels)
            {
                left = Write(drawing, label, left, top, height) + 8;
            }
        }
        drawing.Pop();
    }

    sealed record Label(FormattedText Text, Brush? Background, double Width);

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
                _ => (Brush("Attention"), Tint("Attention", 0x29)),
            };
            labels.Add(LabelOf(badge, _badge, text, back, dip));
        }
        if (mark.Note is { } note)
        {
            labels.Add(LabelOf(note, _note, Brush("Muted"), null, dip));
        }
        if (mark.Saved is { } saved)
        {
            labels.Add(LabelOf(saved, _badge, Brush("Success"), Brush("SuccessSoft"), dip));
        }
        return labels;
    }

    static Label LabelOf(string text, Typeface typeface, Brush foreground, Brush? background, double dip)
    {
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

    Brush Brush(string key) => (Brush)_editor.FindResource(key);

    Brush Tint(string key, byte alpha)
    {
        var color = ((SolidColorBrush)Brush(key)).Color;
        var tint = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        tint.Freeze();
        return tint;
    }
}
