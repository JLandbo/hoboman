using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Hoboman.ViewModels;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;

namespace Hoboman.Controls;

// AvalonEdit lays out and colors only the lines on screen, so large bodies show at once, and changing the colors leaves the selection alone.
public sealed class BodyView : TextEditor
{
    static readonly IHighlightingDefinition _json = Themed("Json",
        [("FieldName", "JsonKey"), ("String", "JsonString"), ("Number", "JsonNumber"), ("Bool", "JsonLiteral"), ("Null", "JsonLiteral"), ("Punctuation", "Text")]);

    static readonly IHighlightingDefinition _xml = Themed("XML",
        [("XmlTag", "XmlTag"), ("XmlDeclaration", "XmlTag"), ("DocType", "XmlTag"), ("AttributeName", "XmlAttribute"), ("AttributeValue", "XmlValue"),
         ("CData", "XmlValue"), ("Entity", "XmlValue"), ("BrokenEntity", "Error"), ("Comment", "XmlComment")]);

    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(nameof(Body), typeof(string), typeof(BodyView),
        new(null, (view, e) => ((BodyView)view).Text = e.NewValue as string ?? ""));

    public static readonly DependencyProperty ColoringProperty = DependencyProperty.Register(nameof(Coloring), typeof(BodyFormat), typeof(BodyView),
        new(BodyFormat.Raw, (view, e) => ((BodyView)view).SyntaxHighlighting = DefinitionOf((BodyFormat)e.NewValue)));

    public BodyView()
    {
        IsReadOnly = true;
        WordWrap = true;
        // Links would be drawn in AvalonEdit's own blue instead of the theme's colors.
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        TextArea.TextView.ElementGenerators.Add(new LongLineCut());
        // AvalonEdit keeps Tab for indenting even when read only, which would trap the focus in the body.
        var keys = TextArea.DefaultInputHandler.Editing.InputBindings;
        foreach (var tab in keys.Where(key => key is KeyBinding { Key: Key.Tab }).ToList())
        {
            keys.Remove(tab);
        }
        var attention = ((SolidColorBrush)FindResource("Attention")).Color;
        TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x66, attention.R, attention.G, attention.B));
        TextArea.SelectionBorder = null;
        TextArea.SelectionForeground = null;
    }

    public string? Body
    {
        get => (string?)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public BodyFormat Coloring
    {
        get => (BodyFormat)GetValue(ColoringProperty);
        set => SetValue(ColoringProperty, value);
    }

    static IHighlightingDefinition? DefinitionOf(BodyFormat coloring) => coloring switch
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

    // AvalonEdit lays out a whole line at once, which freezes the window for a line of a few megabytes, so only its start is drawn.
    // The rest stays in the text, so selecting and copying still gives all of it.
    sealed class LongLineCut : VisualLineElementGenerator
    {
        const int _shown = 50_000;

        public override int GetFirstInterestedOffset(int startOffset)
        {
            var line = CurrentContext.VisualLine.FirstDocumentLine;
            var cut = line.Offset + _shown;
            return line.Length > _shown && startOffset <= cut ? cut : -1;
        }

        public override VisualLineElement ConstructElement(int offset) =>
            new FormattedTextElement("…", CurrentContext.VisualLine.FirstDocumentLine.EndOffset - offset);
    }
}
