using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class RequestEditorView : UserControl
{
    public RequestEditorView() => InitializeComponent();

    RequestTabViewModel Tab => (RequestTabViewModel)DataContext;

    void Cancel_Click(object sender, RoutedEventArgs e) => Tab.Cancel();

    void Body_MarkToggled(object? sender, string path) => Tab.Base64.ToggleEncode(path);

    void Method_Click(object sender, RoutedEventArgs e) => Choose((string)((FrameworkElement)sender).DataContext);

    void CustomMethod_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && CustomMethod.Text.Trim() is { Length: > 0 } method)
        {
            Choose(method.ToUpperInvariant());
        }
    }

    void CustomMethod_TextChanged(object sender, TextChangedEventArgs e) => CustomPlaceholder.Visibility = CustomMethod.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    // A menu is a window of its own, so the keyboard only gets into it when focus is moved there, once its items are made.
    void MethodPopup_Opened(object sender, EventArgs e)
    {
        CustomMethod.Clear();
        Dispatcher.InvokeAsync(() => MethodMenu.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)), DispatcherPriority.Loaded);
    }

    void MethodMenu_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseMethodMenu();
        }
    }

    void CloseMethodMenu()
    {
        MethodToggle.IsChecked = false;
        MethodToggle.Focus();
    }

    void Choose(string method)
    {
        Tab.Method = method;
        CloseMethodMenu();
    }
}
