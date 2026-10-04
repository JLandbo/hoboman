using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class AuthEditor : UserControl
{
    public static readonly DependencyProperty CanInheritProperty = DependencyProperty.Register(nameof(CanInherit), typeof(bool), typeof(AuthEditor), new(true));

    // A workflow step inherits from its workflow instead of a folder.
    public static readonly DependencyProperty InheritsFromWorkflowProperty = DependencyProperty.Register(nameof(InheritsFromWorkflow), typeof(bool), typeof(AuthEditor),
        new(false, (target, e) => ((AuthEditor)target).ShowInheritance((bool)e.NewValue)));

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
        }
        _auth = auth;
        if (auth is not null)
        {
            auth.PropertyChanged += Auth_PropertyChanged;
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

    void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => _auth?.Password = PasswordBox.Password;

    void ClientSecretBox_PasswordChanged(object sender, RoutedEventArgs e) => _auth?.ClientSecret = ClientSecretBox.Password;

    void CancelFetch_Click(object sender, RoutedEventArgs e) => _auth?.CancelFetch();
}
