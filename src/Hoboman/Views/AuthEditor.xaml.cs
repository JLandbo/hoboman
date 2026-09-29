using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class AuthEditor : UserControl
{
    public AuthEditor()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is IAuthFields old)
            {
                old.PropertyChanged -= Fields_PropertyChanged;
            }
            if (e.NewValue is IAuthFields fields)
            {
                fields.PropertyChanged += Fields_PropertyChanged;
                PasswordBox.Password = fields.Password;
            }
        };
    }

    IAuthFields Fields => (IAuthFields)DataContext;

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
