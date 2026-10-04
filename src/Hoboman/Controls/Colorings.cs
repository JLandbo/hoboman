using System.Windows;
using System.Windows.Media;
using Hoboman.ViewModels;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;

namespace Hoboman.Controls;

static class Colorings
{
    static readonly IHighlightingDefinition _json = Themed("Json",
        [("FieldName", "JsonKey"), ("String", "JsonString"), ("Number", "JsonNumber"), ("Bool", "JsonLiteral"), ("Null", "JsonLiteral"), ("Punctuation", "Text")]);

    static readonly IHighlightingDefinition _xml = Themed("XML",
        [("XmlTag", "XmlTag"), ("XmlDeclaration", "XmlTag"), ("DocType", "XmlTag"), ("AttributeName", "XmlAttribute"), ("AttributeValue", "XmlValue"),
         ("CData", "XmlValue"), ("Entity", "XmlValue"), ("BrokenEntity", "Error"), ("Comment", "XmlComment")]);

    static readonly IHighlightingDefinition _javaScript = Themed("JavaScript",
        [("Digits", "JsonNumber"), ("Comment", "XmlComment"), ("String", "JsonString"), ("Character", "JsonString"), ("Regex", "JsonString"),
         ("JavaScriptKeyWords", "JsonLiteral"), ("JavaScriptIntrinsics", "JsonKey"), ("JavaScriptLiterals", "JsonLiteral"), ("JavaScriptGlobalFunctions", "JsonKey")]);

    public static IHighlightingDefinition JavaScript => _javaScript;

    public static IHighlightingDefinition? Of(BodyFormat coloring) => coloring switch
    {
        BodyFormat.Json => _json,
        BodyFormat.Xml => _xml,
        _ => null,
    };

    // The selection and links get the theme's colors instead of AvalonEdit's own.
    public static void Theme(TextEditor editor)
    {
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        var attention = ((SolidColorBrush)editor.FindResource("Attention")).Color;
        editor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x66, attention.R, attention.G, attention.B));
        editor.TextArea.SelectionBorder = null;
        editor.TextArea.SelectionForeground = null;
    }

    // The built-in definitions are colored for a light background, so they get the theme's colors.
    static IHighlightingDefinition Themed(string name, ReadOnlySpan<(string Color, string Brush)> colors)
    {
        var definition = HighlightingManager.Instance.GetDefinition(name);
        foreach (var (color, brush) in colors)
        {
            definition.GetNamedColor(color).Foreground = new SimpleHighlightingBrush(((SolidColorBrush)Application.Current.FindResource(brush)).Color);
        }
        return definition;
    }
}
