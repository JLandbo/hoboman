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
        // HTML has the colors of XML, as its tags look the same, so the themes need no colors of their own for it.
        BodyFormat.Xml or BodyFormat.Html => _xml,
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

    // The built-in definitions have their own colors, so they get the theme's: each named color has a key of the definition and its name, such as Json.FieldName.
    public static void Use(Func<string, Brush> brushOf)
    {
        FoldingElementGenerator.TextBrush = brushOf("Muted");
        foreach (var definition in new[] { _json, _xml, _javaScript })
        {
            foreach (var color in definition.NamedHighlightingColors)
            {
                color.Foreground = new SimpleHighlightingBrush(((SolidColorBrush)brushOf($"{definition.Name}.{color.Name}")).Color);
            }
        }
    }
}
