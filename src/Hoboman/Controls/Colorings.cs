using System.Windows;
using System.Windows.Media;
using Hoboman.ViewModels;
using ICSharpCode.AvalonEdit.Highlighting;

namespace Hoboman.Controls;

static class Colorings
{
    static readonly IHighlightingDefinition _json = Themed("Json",
        [("FieldName", "JsonKey"), ("String", "JsonString"), ("Number", "JsonNumber"), ("Bool", "JsonLiteral"), ("Null", "JsonLiteral"), ("Punctuation", "Text")]);

    static readonly IHighlightingDefinition _xml = Themed("XML",
        [("XmlTag", "XmlTag"), ("XmlDeclaration", "XmlTag"), ("DocType", "XmlTag"), ("AttributeName", "XmlAttribute"), ("AttributeValue", "XmlValue"),
         ("CData", "XmlValue"), ("Entity", "XmlValue"), ("BrokenEntity", "Error"), ("Comment", "XmlComment")]);

    public static IHighlightingDefinition? Of(BodyFormat coloring) => coloring switch
    {
        BodyFormat.Json => _json,
        BodyFormat.Xml => _xml,
        _ => null,
    };

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
