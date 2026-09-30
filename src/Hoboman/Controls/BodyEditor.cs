using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;

namespace Hoboman.Controls;

// The request body, edited with the checkboxes beside it.
public sealed class BodyEditor : MarkedEditor
{
    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(nameof(Body), typeof(string), typeof(BodyEditor),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (editor, e) => ((BodyEditor)editor).Show(e.NewValue as string ?? "")));

    bool _typing;

    public BodyEditor()
    {
        TextArea.Caret.CaretBrush = (Brush)FindResource("Text");
        TextChanged += (_, _) =>
        {
            _typing = true;
            SetCurrentValue(BodyProperty, Text);
            _typing = false;
        };
    }

    public string Body
    {
        get => (string)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    // Another tab's body gets a new document, so undo cannot bring back the text of the tab shown before.
    void Show(string body)
    {
        if (!_typing && body != Text)
        {
            Document = new TextDocument(body);
        }
    }
}
