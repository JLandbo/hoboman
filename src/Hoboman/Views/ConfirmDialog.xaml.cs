using System.Windows;
using System.Windows.Input;

namespace Hoboman.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string confirm, IReadOnlyList<string> items, bool isQuestion)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ItemList.ItemsSource = items;
        ConfirmButton.Content = confirm;
        if (isQuestion)
        {
            ConfirmButton.SetResourceReference(BackgroundProperty, "Error");
        }
        else
        {
            CancelButton.Visibility = Visibility.Collapsed;
        }
    }

    void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
