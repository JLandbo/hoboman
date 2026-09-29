using System.Windows.Controls;
using System.Windows.Media;
using Hoboman.Core.Environments;

namespace Hoboman.Controls;

// WPF text boxes cannot color parts of their text, so the text is drawn by a text block behind a transparent text box.
public sealed class VariableTextBox : TextBox
{
    TextBlock? _highlight;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _highlight = GetTemplateChild("PART_Highlight") as TextBlock;
        if (GetTemplateChild("PART_ContentHost") is ScrollViewer host)
        {
            host.ScrollChanged += (_, e) => _highlight?.RenderTransform = new TranslateTransform(-e.HorizontalOffset, 0);
        }
        Highlight();
    }

    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        base.OnTextChanged(e);
        Highlight();
    }

    void Highlight()
    {
        if (_highlight is null)
        {
            return;
        }
        _highlight.Inlines.Clear();
        _highlight.Inlines.AddRange(Runs.Of(Text, ApiEnvironment.VariablesIn(Text), _ => "Attention"));
    }
}
