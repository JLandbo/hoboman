using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Hoboman.ViewModels;

namespace Hoboman.Controls;

// What a line says, unfolded over its text from its icon while the icon is pointed at, with the labels as they were before.
// When the text area is too narrow, what is said first is cut with …, so what is said last stays by the icon.
sealed class Base64Strip(MarkedEditor editor) : UIElement
{
    const double _pad = 8;
    const double _gap = 8;

    // What unfolds now, by its texts.
    internal IReadOnlyList<string> Unfolded => Unfolding() is var (shown, _, _) ? [.. shown.Select(label => label.Text.Text)] : [];

    protected override void OnRender(DrawingContext drawing)
    {
        if (Unfolding() is not var (shown, box, tint))
        {
            return;
        }
        drawing.PushClip(new RectangleGeometry(box));
        drawing.DrawRectangle(Brush("Input"), null, box);
        drawing.DrawRectangle(tint, null, box);
        var left = box.Left + _pad;
        foreach (var label in shown)
        {
            left = Write(drawing, label, left, box.Top, box.Height) + _gap;
        }
        drawing.Pop();
    }

    // The labels of the pointed line that have room, and where they go, or nothing when no line with an icon is pointed at.
    (IReadOnlyList<Label> Shown, Rect Box, Brush Tint)? Unfolding()
    {
        var textView = editor.TextArea.TextView;
        if (!editor.ShowsMarks || editor.Pointed is not { } number || !textView.VisualLinesValid
            || editor.MarkAt(number) is not { } mark || editor.TintOf(mark) is not { } tint || textView.GetVisualLine(number) is not { } line)
        {
            return null;
        }
        var dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var shown = Fitted(LabelsOf(mark, dip), textView.ActualWidth - 2 * _pad, dip);
        var width = Math.Min(WidthOf(shown) + 2 * _pad, textView.ActualWidth);
        return (shown, new Rect(textView.ActualWidth - width, line.VisualTop - textView.VerticalOffset, width, line.TextLines[0].Height), tint);
    }

    sealed record Label(FormattedText Text, Brush? Background, double Width);

    static double WidthOf(IReadOnlyList<Label> labels) => labels.Count == 0 ? 0 : labels.Sum(label => label.Width) + _gap * (labels.Count - 1);

    // The labels from the front are left out, behind a …, until the rest has room.
    IReadOnlyList<Label> Fitted(List<Label> labels, double room, double dip)
    {
        var cut = LabelOf("…", FontWeights.Normal, Brush("Muted"), null, dip);
        for (var skipped = 0; skipped < labels.Count - 1; skipped++)
        {
            List<Label> tried = skipped == 0 ? labels : [cut, .. labels.Skip(skipped)];
            if (WidthOf(tried) <= room)
            {
                return tried;
            }
        }
        return labels.Count > 1 ? [cut, labels[^1]] : labels;
    }

    // In the order they are read: what happens to the property, and what was saved from it.
    List<Label> LabelsOf(Base64Mark mark, double dip)
    {
        var labels = new List<Label>();
        if (mark.Badge is { } badge)
        {
            var (text, back) = mark.State == Base64MarkState.Failed
                ? (Brush("Error"), Brush("ErrorSoft"))
                : (Brush("Attention"), Base64Highlighter.Tint(Brush("Attention"), 0x29));
            labels.Add(LabelOf(badge, FontWeights.SemiBold, text, back, dip));
        }
        if (mark.Saved is { } saved)
        {
            labels.Add(LabelOf(saved, FontWeights.SemiBold, Brush("Success"), Brush("SuccessSoft"), dip));
        }
        return labels;
    }

    Label LabelOf(string text, FontWeight weight, Brush foreground, Brush? background, double dip)
    {
        var typeface = new Typeface((FontFamily)editor.FindResource("UiFont"), FontStyles.Normal, weight, FontStretches.Normal);
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

    Brush Brush(string key) => (Brush)editor.FindResource(key);
}
