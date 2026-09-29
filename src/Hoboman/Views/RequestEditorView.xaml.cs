using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class RequestEditorView : UserControl
{
    public RequestEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is RequestTabViewModel old)
            {
                old.PropertyChanged -= Tab_PropertyChanged;
            }
            if (e.NewValue is RequestTabViewModel tab)
            {
                tab.PropertyChanged += Tab_PropertyChanged;
                PasswordBox.Password = tab.Password;
            }
        };
    }

    RequestTabViewModel Tab => (RequestTabViewModel)DataContext;

    // A password box cannot be bound, so it is kept in step with the view model by hand.
    void Tab_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RequestTabViewModel.Password) && PasswordBox.Password != Tab.Password)
        {
            PasswordBox.Password = Tab.Password;
        }
    }

    void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => Tab.Password = PasswordBox.Password;

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
