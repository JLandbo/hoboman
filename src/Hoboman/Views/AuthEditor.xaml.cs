using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class AuthEditor : UserControl
{
    AuthViewModel? _auth;

    // Listening only while shown keeps a closed dialog from being held in memory by the view model it edited.
    public AuthEditor()
    {
        InitializeComponent();
        Loaded += (_, _) => Follow(DataContext as AuthViewModel);
        Unloaded += (_, _) => Follow(null);
        DataContextChanged += (_, e) =>
        {
            if (IsLoaded)
            {
                Follow(e.NewValue as AuthViewModel);
            }
        };
    }

    void Follow(AuthViewModel? auth)
    {
        if (_auth is not null)
        {
            _auth.PropertyChanged -= Auth_PropertyChanged;
        }
        _auth = auth;
        if (auth is not null)
        {
            auth.PropertyChanged += Auth_PropertyChanged;
            PasswordBox.Password = auth.Password;
        }
    }

    // A password box cannot be bound, so it is kept in step with the view model by hand.
    void Auth_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AuthViewModel.Password) && _auth is { } auth && PasswordBox.Password != auth.Password)
        {
            PasswordBox.Password = auth.Password;
        }
    }

    void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => _auth?.Password = PasswordBox.Password;
}
