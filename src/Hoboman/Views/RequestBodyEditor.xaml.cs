using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class RequestBodyEditor : UserControl
{
    public RequestBodyEditor() => InitializeComponent();

    RequestViewModel Request => (RequestViewModel)DataContext;

    void Body_MarkToggled(object? sender, string path) => Request.Base64.ToggleEncode(path);

    void Format_Click(object sender, RoutedEventArgs e) => Format();

    // Shift+Alt+F, as in VS Code. Alt makes it a system key.
    void Body_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.System && e.SystemKey == Key.F && Keyboard.Modifiers == (ModifierKeys.Alt | ModifierKeys.Shift))
        {
            e.Handled = true;
            Format();
        }
    }

    // Into the editor's own text, so it can be undone like typing, and only while the same request is shown.
    async void Format()
    {
        var request = Request;
        if (await request.LaidOutBodyAsync() is { } body && request == Request && body != BodyText.Text)
        {
            BodyText.Document.Text = body;
        }
    }
}
