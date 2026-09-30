using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Hoboman.ViewModels;

namespace Hoboman.Controls;

public sealed partial class BodyView : RichTextBox
{
    // Coloring very large bodies would make the view slow, so they are shown as plain text.
    const int _coloredLength = 20_000;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(BodyView), new(null, (view, _) => ((BodyView)view).Show()));

    public static readonly DependencyProperty ColoringProperty = DependencyProperty.Register(nameof(Coloring), typeof(BodyFormat), typeof(BodyView), new(BodyFormat.Raw, (view, _) => ((BodyView)view).Show()));

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public BodyFormat Coloring
    {
        get => (BodyFormat)GetValue(ColoringProperty);
        set => SetValue(ColoringProperty, value);
    }

    void Show()
    {
        var text = Text ?? "";
        var paragraph = new Paragraph();
        switch (text.Length > _coloredLength ? BodyFormat.Raw : Coloring)
        {
            case BodyFormat.Json:
                paragraph.Inlines.AddRange(Runs.Of(text, Json().Matches(text), JsonBrushOf));
                break;
            case BodyFormat.Xml:
                paragraph.Inlines.AddRange(Runs.Of(text, Xml().Matches(text), XmlBrushOf));
                break;
            default:
                paragraph.Inlines.Add(new Run(text));
                break;
        }
        Document = new FlowDocument(paragraph) { PagePadding = new Thickness(0), FontFamily = FontFamily, FontSize = FontSize };
    }

    static string JsonBrushOf(Match token) =>
        token.Groups["key"].Success ? "JsonKey" : token.Groups["string"].Success ? "JsonString" : token.Groups["literal"].Success ? "JsonLiteral" : "JsonNumber";

    static string XmlBrushOf(Match token) =>
        token.Groups["comment"].Success ? "XmlComment" : token.Groups["tag"].Success ? "XmlTag" : token.Groups["attribute"].Success ? "XmlAttribute" : "XmlValue";

    [GeneratedRegex("""(?<key>"(?:\\.|[^"\\])*")(?=\s*:)|(?<string>"(?:\\.|[^"\\])*")|(?<literal>\b(?:true|false|null)\b)|(?<number>-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)""")]
    private static partial Regex Json();

    [GeneratedRegex("""(?<comment><!--[\s\S]*?-->)|(?<tag><[/?!]?[\w:.-]+|/?>|\?>)|(?<attribute>[\w:.-]+(?=\s*=))|(?<value>"[^"]*"|'[^']*')""")]
    private static partial Regex Xml();
}
