using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class AuthEditor : UserControl
{
    IAuthFields? _fields;

    // Listening only while shown keeps a closed dialog from being held in memory by the view model it edited.
    public AuthEditor()
    {
        InitializeComponent();
        Loaded += (_, _) => Follow(DataContext as IAuthFields);
        Unloaded += (_, _) => Follow(null);
        DataContextChanged += (_, e) =>
        {
            if (IsLoaded)
            {
                Follow(e.NewValue as IAuthFields);
            }
        };
    }

    IAuthFields Fields => (IAuthFields)DataContext;

    void Follow(IAuthFields? fields)
    {
        if (_fields is not null)
        {
            _fields.PropertyChanged -= Fields_PropertyChanged;
        }
        _fields = fields;
        if (fields is not null)
        {
            fields.PropertyChanged += Fields_PropertyChanged;
            PasswordBox.Password = fields.Password;
        }
    }

    // A password box cannot be bound, so it is kept in step with the view model by hand.
    void Fields_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IAuthFields.Password) && PasswordBox.Password != Fields.Password)
        {
            PasswordBox.Password = Fields.Password;
        }
    }

    void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => Fields.Password = PasswordBox.Password;
}
