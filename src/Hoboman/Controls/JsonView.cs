using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Hoboman.Controls;

public sealed partial class JsonView : RichTextBox
{
    // Coloring very large bodies would make the view slow, so they are shown as plain text.
    const int _coloredLength = 200_000;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(JsonView), new(null, (view, _) => ((JsonView)view).Show()));

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    void Show()
    {
        var text = Text ?? "";
        var paragraph = new Paragraph();
        if (text.Length > _coloredLength || text.TrimStart() is not ['{' or '[', ..])
        {
            paragraph.Inlines.Add(new Run(text));
        }
        else
        {
            var start = 0;
            foreach (Match match in Token().Matches(text))
            {
                paragraph.Inlines.Add(new Run(text[start..match.Index]));
                var token = new Run(match.Value);
                token.SetResourceReference(TextElement.ForegroundProperty, match.Groups["key"].Success ? "JsonKey" : match.Groups["string"].Success ? "JsonString" : match.Groups["literal"].Success ? "JsonLiteral" : "JsonNumber");
                paragraph.Inlines.Add(token);
                start = match.Index + match.Length;
            }
            paragraph.Inlines.Add(new Run(text[start..]));
        }
        Document = new FlowDocument(paragraph) { PagePadding = new Thickness(0), FontFamily = FontFamily, FontSize = FontSize };
    }

    [GeneratedRegex("""(?<key>"(?:\\.|[^"\\])*")(?=\s*:)|(?<string>"(?:\\.|[^"\\])*")|(?<literal>\b(?:true|false|null)\b)|(?<number>-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)""")]
    private static partial Regex Token();
}
