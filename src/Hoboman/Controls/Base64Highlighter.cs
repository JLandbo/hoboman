using System.Windows.Media;
using Hoboman.ViewModels;
using ICSharpCode.AvalonEdit.Rendering;

namespace Hoboman.Controls;

// Tints the lines of the chosen properties under the text. What they say is written beside the text by Base64Labels.
sealed class Base64Highlighter : IBackgroundRenderer
{
    readonly MarkedEditor _editor;
    readonly Base64Margin _margin;
    readonly Brush _chosen;
    readonly Brush _failed;
    readonly Brush _saved;

    public Base64Highlighter(MarkedEditor editor, Base64Margin margin)
    {
        (_editor, _margin) = (editor, margin);
        _chosen = Tint((Brush)editor.FindResource("Attention"), 0x12);
        _failed = Tint((Brush)editor.FindResource("Error"), 0x14);
        _saved = Tint((Brush)editor.FindResource("Success"), 0x14);
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawing)
    {
        if (!_editor.ShowsMarks || !textView.VisualLinesValid)
        {
            return;
        }
        drawing.PushOpacity(_editor.MarksAreCurrent ? 1 : Base64Margin.Stale);
        foreach (var line in textView.VisualLines)
        {
            if (_margin.MarkAt(line.FirstDocumentLine.LineNumber) is { } mark && TintOf(mark) is { } tint)
            {
                drawing.DrawRectangle(tint, null, new(0, line.VisualTop - textView.VerticalOffset, textView.ActualWidth, line.TextLines[0].Height));
            }
        }
        drawing.Pop();
    }

    // A property inside a chosen one only has its checkbox greyed, as tinting it too would stripe a whole chosen body.
    public Brush? TintOf(Base64Mark mark) => mark switch
    {
        { State: Base64MarkState.Failed } => _failed,
        { State: Base64MarkState.Checked or Base64MarkState.Decoded } => _chosen,
        { Saved: not null } => _saved,
        _ => null,
    };

    public static Brush Tint(Brush brush, byte alpha)
    {
        var color = ((SolidColorBrush)brush).Color;
        var tint = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        tint.Freeze();
        return tint;
    }
}
