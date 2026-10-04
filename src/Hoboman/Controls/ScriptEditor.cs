using System.Windows;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;

namespace Hoboman.Controls;

// The code of a script step, colored as JavaScript.
public sealed class ScriptEditor : TextEditor
{
    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(nameof(Code), typeof(string), typeof(ScriptEditor),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (editor, e) => ((ScriptEditor)editor).Show(e.NewValue as string ?? "")));

    bool _typing;

    public ScriptEditor()
    {
        Colorings.Theme(this);
        SyntaxHighlighting = Colorings.JavaScript;
        TextChanged += (_, _) =>
        {
            _typing = true;
            SetCurrentValue(CodeProperty, Text);
            _typing = false;
        };
    }

    public string Code
    {
        get => (string)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    // Another script gets a new document, so undo cannot bring back the code of the one shown before.
    void Show(string code)
    {
        if (!_typing && code != Text)
        {
            Document = new TextDocument(code);
        }
    }
}
