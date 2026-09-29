using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class RequestEditorView : UserControl
{
    public RequestEditorView() => InitializeComponent();

    RequestTabViewModel Tab => (RequestTabViewModel)DataContext;

    void Cancel_Click(object sender, RoutedEventArgs e) => Tab.Cancel();

    void Method_Click(object sender, RoutedEventArgs e) => Choose((string)((FrameworkElement)sender).DataContext);

    void CustomMethod_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && CustomMethod.Text.Trim() is { Length: > 0 } method)
        {
            Choose(method.ToUpperInvariant());
        }
    }

    void CustomMethod_TextChanged(object sender, TextChangedEventArgs e) => CustomPlaceholder.Visibility = CustomMethod.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    void MethodPopup_Opened(object sender, EventArgs e) => CustomMethod.Clear();

    void Choose(string method)
    {
        Tab.Method = method;
        MethodToggle.IsChecked = false;
    }
}
