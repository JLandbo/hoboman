using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using Hoboman.Core.Environments;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

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
        TextArea.TextView.LineTransformers.Add(new VariableColorizer((Brush)FindResource("Attention")));
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

    sealed class VariableColorizer(Brush foreground) : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line)
        {
            foreach (Match variable in ApiEnvironment.VariablesIn(CurrentContext.Document.GetText(line)))
            {
                ChangeLinePart(line.Offset + variable.Index, line.Offset + variable.Index + variable.Length, element => element.TextRunProperties.SetForegroundBrush(foreground));
            }
        }
    }
}
