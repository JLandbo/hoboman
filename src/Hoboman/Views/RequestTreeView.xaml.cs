using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class RequestTreeView : UserControl
{
    public RequestTreeView() => InitializeComponent();

    MainViewModel ViewModel => (MainViewModel)DataContext;

    async void Item_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        await ViewModel.OpenAsync(NodeOf(sender));
    }

    async void Rename_Click(object sender, RoutedEventArgs e) => await ViewModel.RenameAsync(NodeOf(sender));

    async void Delete_Click(object sender, RoutedEventArgs e) => await ViewModel.DeleteAsync(NodeOf(sender));

    static RequestNodeViewModel NodeOf(object sender) => (RequestNodeViewModel)((FrameworkElement)sender).DataContext;
}
