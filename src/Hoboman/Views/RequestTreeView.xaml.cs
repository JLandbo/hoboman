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
        var node = NodeOf(sender);
        if (node.IsFolder)
        {
            node.IsExpanded = !node.IsExpanded;
            return;
        }
        await ViewModel.OpenAsync(node);
    }

    async void Rename_Click(object sender, RoutedEventArgs e) => await ViewModel.RenameAsync(NodeOf(sender));

    async void Delete_Click(object sender, RoutedEventArgs e) => await ViewModel.DeleteAsync(NodeOf(sender));

    async void FolderAuth_Click(object sender, RoutedEventArgs e) => await ViewModel.EditFolderAuthAsync(NodeOf(sender));

    static RequestNodeViewModel NodeOf(object sender) => (RequestNodeViewModel)((FrameworkElement)sender).DataContext;
}
