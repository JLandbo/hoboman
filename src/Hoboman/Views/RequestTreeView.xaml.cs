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
        if (NodeOf(sender) is not { } node)
        {
            return;
        }
        if (node.IsFolder)
        {
            node.IsExpanded = !node.IsExpanded;
            return;
        }
        await ViewModel.OpenAsync(node);
    }

    async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node)
        {
            await ViewModel.RenameAsync(node);
        }
    }

    async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node)
        {
            await ViewModel.DeleteAsync(node);
        }
    }

    async void FolderAuth_Click(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node)
        {
            await ViewModel.EditFolderAuthAsync(node);
        }
    }

    // A menu left open while the tree reloads points at a node that is no longer in the tree.
    static RequestNodeViewModel? NodeOf(object sender) => ((FrameworkElement)sender).DataContext as RequestNodeViewModel;
}
