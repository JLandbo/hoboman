using System.Windows.Media;
using Hoboman.ViewModels;
using ICSharpCode.AvalonEdit.Rendering;

namespace Hoboman.Controls;

// Tints the lines of the chosen properties under the text. Their icons are beside the text (Base64Labels), and what they say unfolds over it (Base64Strip).
sealed class Base64Highlighter : IBackgroundRenderer
{
    readonly MarkedEditor _editor;
    readonly Base64Margin _margin;

    public Base64Highlighter(MarkedEditor editor, Base64Margin margin) => (_editor, _margin) = (editor, margin);

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
        { State: Base64MarkState.Failed } => Brush("ErrorTint"),
        { State: Base64MarkState.Checked or Base64MarkState.Decoded } => Brush("AttentionTint"),
        { Saved: not null } => Brush("SuccessTint"),
        _ => null,
    };

    Brush Brush(string key) => (Brush)_editor.FindResource(key);
}
