using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class RequestEditorView : UserControl
{
    public RequestEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RefreshAuthButton.ClearValue(Control.BackgroundProperty);
    }

    RequestTabViewModel Tab => (RequestTabViewModel)DataContext;

    void Cancel_Click(object sender, RoutedEventArgs e) => Tab.Cancel();

    async void RefreshAuth_Click(object sender, RoutedEventArgs e)
    {
        var tab = Tab;
        var succeeded = await tab.RefreshAuthAsync();
        if (tab != DataContext)
        {
            return;
        }
        var button = (Button)sender;
        var background = new SolidColorBrush(((SolidColorBrush)FindResource(succeeded ? "ClipboardSuccess" : "ClipboardError")).Color);
        button.Background = background;
        var fade = new ColorAnimation(Colors.Transparent, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(200) };
        fade.Completed += (_, _) =>
        {
            if (ReferenceEquals(button.Background, background))
            {
                button.ClearValue(Control.BackgroundProperty);
            }
        };
        background.BeginAnimation(SolidColorBrush.ColorProperty, fade);
    }

    void Body_MarkToggled(object? sender, string path) => Tab.Base64.ToggleEncode(path);

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

    // Into the editor's own text, so it can be undone like typing, and only while the same tab is shown.
    async void Format()
    {
        var tab = Tab;
        if (await tab.LaidOutBodyAsync() is { } body && tab == Tab && body != BodyText.Text)
        {
            BodyText.Document.Text = body;
        }
    }

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
