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
        await ActivateAsync(sender);
    }

    async void Item_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await ActivateAsync(sender);
        }
    }

    Task ActivateAsync(object sender)
    {
        if (NodeOf(sender) is not { } node)
        {
            return Task.CompletedTask;
        }
        if (node.IsFolder)
        {
            node.IsExpanded = !node.IsExpanded;
            return Task.CompletedTask;
        }
        return ViewModel.OpenAsync(node);
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
