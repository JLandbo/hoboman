using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Hoboman.Controls;

public sealed partial class JsonView : RichTextBox
{
    // Coloring very large bodies would make the view slow, so they are shown as plain text.
    const int _coloredLength = 20_000;

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
            paragraph.Inlines.AddRange(Runs.Of(text, Token().Matches(text), BrushOf));
        }
        Document = new FlowDocument(paragraph) { PagePadding = new Thickness(0), FontFamily = FontFamily, FontSize = FontSize };
    }

    static string BrushOf(Match token) =>
        token.Groups["key"].Success ? "JsonKey" : token.Groups["string"].Success ? "JsonString" : token.Groups["literal"].Success ? "JsonLiteral" : "JsonNumber";

    [GeneratedRegex("""(?<key>"(?:\\.|[^"\\])*")(?=\s*:)|(?<string>"(?:\\.|[^"\\])*")|(?<literal>\b(?:true|false|null)\b)|(?<number>-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)""")]
    private static partial Regex Token();
}
