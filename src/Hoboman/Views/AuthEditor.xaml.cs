using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class AuthEditor : UserControl
{
    public static readonly DependencyProperty CanInheritProperty = DependencyProperty.Register(nameof(CanInherit), typeof(bool), typeof(AuthEditor), new(true));

    // A workflow step inherits from its workflow instead of a folder.
    public static readonly DependencyProperty InheritsFromWorkflowProperty = DependencyProperty.Register(nameof(InheritsFromWorkflow), typeof(bool), typeof(AuthEditor),
        new(false, (target, e) => ((AuthEditor)target).ShowInheritance((bool)e.NewValue)));

    // A saved credential is always some auth, and is copied from.
    public static readonly DependencyProperty IsCredentialProperty = DependencyProperty.Register(nameof(IsCredential), typeof(bool), typeof(AuthEditor),
        new(false, (target, e) => ((AuthEditor)target).NoneChoice.Visibility = (bool)e.NewValue ? Visibility.Collapsed : Visibility.Visible));

    public static readonly DependencyProperty ClipboardProperty = DependencyProperty.Register(nameof(Clipboard), typeof(IClipboard), typeof(AuthEditor));

    AuthViewModel? _auth;
    bool _clearing;
    Window? _window;

    // Listening only while shown keeps a closed dialog from being held in memory by the view model it edited.
    public AuthEditor()
    {
        InitializeComponent();
        Loaded += (_, _) => Follow(DataContext as AuthViewModel);
        Unloaded += (_, _) =>
        {
            CloseCredentials();
            Watch();
            Follow(null);
        };
        IsVisibleChanged += (_, _) => CloseCredentials();
        DataContextChanged += (_, e) =>
        {
            if (IsLoaded)
            {
                Follow(e.NewValue as AuthViewModel);
            }
        };
    }

    public bool CanInherit
    {
        get => (bool)GetValue(CanInheritProperty);
        set => SetValue(CanInheritProperty, value);
    }

    public bool InheritsFromWorkflow
    {
        get => (bool)GetValue(InheritsFromWorkflowProperty);
        set => SetValue(InheritsFromWorkflowProperty, value);
    }

    public bool IsCredential
    {
        get => (bool)GetValue(IsCredentialProperty);
        set => SetValue(IsCredentialProperty, value);
    }

    public IClipboard? Clipboard
    {
        get => (IClipboard?)GetValue(ClipboardProperty);
        set => SetValue(ClipboardProperty, value);
    }

    void ShowInheritance(bool fromWorkflow)
    {
        if (fromWorkflow)
        {
            InheritChoice.SetResourceReference(ContentProperty, "Workflow.AuthInherit");
        }
        else
        {
            InheritChoice.SetResourceReference(ContentProperty, "Auth.Inherit");
        }
    }

    void Follow(AuthViewModel? auth)
    {
        if (_auth is not null)
        {
            _auth.PropertyChanged -= Auth_PropertyChanged;
            _auth.Credentials?.PropertyChanged -= Credentials_PropertyChanged;
        }
        _auth = auth;
        if (auth is not null)
        {
            auth.PropertyChanged += Auth_PropertyChanged;
            auth.Credentials?.PropertyChanged += Credentials_PropertyChanged;
            PasswordBox.Password = auth.Password;
            ClientSecretBox.Password = auth.ClientSecret;
        }
    }

    // A password box cannot be bound, so it is kept in step with the view model by hand.
    void Auth_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_auth is not { } auth)
        {
            return;
        }
        if (e.PropertyName == nameof(AuthViewModel.Password) && PasswordBox.Password != auth.Password)
        {
            PasswordBox.Password = auth.Password;
        }
        if (e.PropertyName == nameof(AuthViewModel.ClientSecret) && ClientSecretBox.Password != auth.ClientSecret)
        {
            ClientSecretBox.Password = auth.ClientSecret;
        }
    }

    // The name shown follows the saved credentials as they are saved again.
    void Credentials_PropertyChanged(object? sender, PropertyChangedEventArgs e) => _auth?.RelabelCredential();

    void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => _auth?.Password = PasswordBox.Password;

    void ClientSecretBox_PasswordChanged(object sender, RoutedEventArgs e) => _auth?.ClientSecret = ClientSecretBox.Password;

    void CancelFetch_Click(object sender, RoutedEventArgs e) => _auth?.CancelFetch();

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        Blink.Show(button, Put((string)button.Tag));
    }

    // Another program can hold the clipboard open for longer than it is waited for.
    bool Put(string text)
    {
        try
        {
            Clipboard?.Put(text);
            return Clipboard is not null;
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    // Below the kinds when both do not fit on one line.
    void KindRow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var below = Kinds.ActualWidth + 12 + CredentialPicker.Width > KindRow.ActualWidth;
        Grid.SetRow(CredentialPicker, below ? 1 : 0);
        Grid.SetColumn(CredentialPicker, below ? 0 : 1);
        Grid.SetColumnSpan(CredentialPicker, below ? 2 : 1);
        CredentialPicker.HorizontalAlignment = below ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        CredentialPicker.Margin = new(0, below ? 8 : 0, 0, 0);
    }

    void CredentialField_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        CredentialSearch.Focus();
        if (!CredentialPopup.IsOpen)
        {
            ShowCredentials();
        }
    }

    void CredentialSearch_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => Watch();

    void CredentialSearch_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CloseCredentials();
        Watch();
    }

    void CredentialSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_clearing)
        {
            ShowCredentials();
        }
    }

    // The first one is marked, so Enter picks it when the typing leaves one. Enter never reaches a dialog's default button.
    void CredentialSearch_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when !CredentialPopup.IsOpen:
                ShowCredentials();
                break;
            case Key.Down or Key.Up when CredentialList.Items.Count > 0:
                CredentialList.SelectedIndex = Math.Clamp(CredentialList.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, CredentialList.Items.Count - 1);
                CredentialList.ScrollIntoView(CredentialList.SelectedItem);
                break;
            case Key.Enter:
                if (CredentialPopup.IsOpen && CredentialList.SelectedItem is CredentialChoice chosen)
                {
                    Pick(chosen);
                }
                break;
            case Key.Escape when CredentialPopup.IsOpen:
                CloseCredentials();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    void CredentialList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is CredentialChoice chosen)
        {
            Pick(chosen);
        }
    }

    // The list does not hold the mouse, so a click outside the field goes where it was meant to, as in a browser, and ends the search on the way.
    // A click in the list passes the window too, as the list belongs to it.
    void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!Within(e.OriginalSource, CredentialField) && !Within(e.OriginalSource, CredentialPopup.Child))
        {
            CloseCredentials();
            LeaveField();
        }
    }

    static bool Within(object source, DependencyObject area)
    {
        for (var current = source as DependencyObject; current is not null; current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current == area)
            {
                return true;
            }
        }
        return false;
    }

    // A list left open would float over other windows, or away from the field.
    void Window_Changed(object? sender, EventArgs e) => CloseCredentials();

    async void EditCredentials_Click(object sender, RoutedEventArgs e)
    {
        CloseCredentials();
        if (_auth?.Credentials is { } credentials)
        {
            await credentials.EditAsync();
        }
    }

    void ShowCredentials()
    {
        _auth?.FindCredentials(CredentialSearch.Text);
        CredentialList.SelectedIndex = CredentialList.Items.Count > 0 ? 0 : -1;
        CredentialPopup.IsOpen = true;
        Watch();
    }

    async void Pick(CredentialChoice chosen)
    {
        CloseCredentials();
        if (_auth is { } auth)
        {
            await auth.UseAsync(chosen);
        }
    }

    // The search is over once the list is closed, so the field lets go of the keyboard.
    void CloseCredentials()
    {
        if (!CredentialPopup.IsOpen)
        {
            return;
        }
        CredentialPopup.IsOpen = false;
        ClearSearch();
        LeaveField();
        Watch();
    }

    // A window gives the keyboard back to the element that had it last, so the field is forgotten first. Shortcuts keep working from the window.
    void LeaveField()
    {
        if (CredentialSearch.IsKeyboardFocusWithin && Window.GetWindow(this) is { } window)
        {
            FocusManager.SetFocusedElement(FocusManager.GetFocusScope(CredentialSearch), null);
            window.Focus();
        }
    }

    // The window is watched while the field is in use, and only then.
    void Watch()
    {
        var window = IsLoaded && (CredentialPopup.IsOpen || CredentialSearch.IsKeyboardFocusWithin) ? Window.GetWindow(this) : null;
        if (window == _window)
        {
            return;
        }
        if (_window is { } watched)
        {
            watched.PreviewMouseDown -= Window_PreviewMouseDown;
            watched.Deactivated -= Window_Changed;
            watched.LocationChanged -= Window_Changed;
            watched.SizeChanged -= Window_Changed;
        }
        _window = window;
        if (window is not null)
        {
            window.PreviewMouseDown += Window_PreviewMouseDown;
            window.Deactivated += Window_Changed;
            window.LocationChanged += Window_Changed;
            window.SizeChanged += Window_Changed;
        }
    }

    void ClearSearch()
    {
        _clearing = true;
        CredentialSearch.Clear();
        _clearing = false;
    }
}
