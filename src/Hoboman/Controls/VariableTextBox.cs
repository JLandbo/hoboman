using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Hoboman.Controls;

// WPF text boxes cannot color parts of their text, so the text is drawn by a text block behind a transparent text box.
public sealed partial class VariableTextBox : TextBox
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
        var start = 0;
        foreach (Match match in Variable().Matches(Text))
        {
            _highlight.Inlines.Add(new Run(Text[start..match.Index]));
            var variable = new Run(match.Value);
            variable.SetResourceReference(TextElement.ForegroundProperty, "Attention");
            _highlight.Inlines.Add(variable);
            start = match.Index + match.Length;
        }
        _highlight.Inlines.Add(new Run(Text[start..]));
    }

    [GeneratedRegex(@"\{\{[^{}]+\}\}")]
    private static partial Regex Variable();
}
