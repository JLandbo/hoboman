using System.Windows;

namespace Hoboman.Views;

public partial class ConfirmDialog : DialogWindow
{
    public ConfirmDialog(string title, string message, string confirm, IReadOnlyList<string> items, bool canCancel)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        ItemList.ItemsSource = items;
        ConfirmButton.Content = confirm;
        if (canCancel)
        {
            ConfirmButton.SetResourceReference(BackgroundProperty, "Error");
        }
        else
        {
            CancelButton.Visibility = Visibility.Collapsed;
        }
    }

    void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
