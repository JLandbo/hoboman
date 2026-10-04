using System.Windows;
using System.Windows.Media;
using Hoboman.ViewModels;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;

namespace Hoboman.Controls;

static class Colorings
{
    static readonly IHighlightingDefinition _json = HighlightingManager.Instance.GetDefinition("Json");

    static readonly IHighlightingDefinition _xml = HighlightingManager.Instance.GetDefinition("XML");

    static readonly IHighlightingDefinition _javaScript = HighlightingManager.Instance.GetDefinition("JavaScript");

    static Colorings() => Use(brush => (Brush)Application.Current.FindResource(brush));

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
        editor.TextArea.SetResourceReference(TextArea.SelectionBrushProperty, "Selection");
        editor.TextArea.SelectionBorder = null;
        editor.TextArea.SelectionForeground = null;
    }

    // The built-in definitions have their own colors, so they get the theme's.
    public static void Use(Func<string, Brush> brushOf)
    {
        FoldingElementGenerator.TextBrush = brushOf("Muted");
        Recolor(_json, brushOf, [("FieldName", "JsonKey"), ("String", "JsonString"), ("Number", "JsonNumber"), ("Bool", "JsonLiteral"), ("Null", "JsonLiteral"), ("Punctuation", "Text")]);
        Recolor(_xml, brushOf,
            [("XmlTag", "XmlTag"), ("XmlDeclaration", "XmlTag"), ("DocType", "XmlTag"), ("AttributeName", "XmlAttribute"), ("AttributeValue", "XmlValue"),
             ("CData", "XmlValue"), ("Entity", "XmlValue"), ("BrokenEntity", "Error"), ("Comment", "XmlComment")]);
        Recolor(_javaScript, brushOf,
            [("Digits", "JsonNumber"), ("Comment", "XmlComment"), ("String", "JsonString"), ("Character", "JsonString"), ("Regex", "JsonString"),
             ("JavaScriptKeyWords", "JsonLiteral"), ("JavaScriptIntrinsics", "JsonKey"), ("JavaScriptLiterals", "JsonLiteral"), ("JavaScriptGlobalFunctions", "JsonKey")]);
    }

    static void Recolor(IHighlightingDefinition definition, Func<string, Brush> brushOf, ReadOnlySpan<(string Color, string Brush)> colors)
    {
        foreach (var (color, brush) in colors)
        {
            definition.GetNamedColor(color).Foreground = new SimpleHighlightingBrush(((SolidColorBrush)brushOf(brush)).Color);
        }
    }
}
