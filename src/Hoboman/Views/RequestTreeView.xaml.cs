using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class RequestTreeView : UserControl
{
    // How close to the top or bottom of the tree a dragged request scrolls it.
    const double _scrollEdge = 20;

    public RequestTreeView() => InitializeComponent();

    MainViewModel ViewModel => (MainViewModel)DataContext;

    (RequestNodeViewModel Node, Point At)? _pressed;

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

    async void RenameFolder_Click(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node)
        {
            await ViewModel.RenameFolderAsync(node);
        }
    }

    async void DeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node)
        {
            await ViewModel.DeleteFolderAsync(node);
        }
    }

    async void FolderAuth_Click(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node)
        {
            await ViewModel.EditFolderAuthAsync(node);
        }
    }

    // The request is taken from where the button went down, as a quick drag is already over the next one when it starts.
    void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _pressed = NodeAt(e.OriginalSource) is { IsFolder: false } node ? (node, e.GetPosition(this)) : null;

    // Otherwise a later press outside the tree, moved into it, would drag the request clicked before.
    void Tree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _pressed = null;

    // A request is only dragged once the mouse has moved a bit with the button held, so a click still opens it.
    void Tree_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressed is not { } pressed)
        {
            return;
        }
        var moved = e.GetPosition(this) - pressed.At;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }
        _pressed = null;
        DragDrop.DoDragDrop((DependencyObject)sender, pressed.Node, DragDropEffects.Move);
    }

    void Tree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(RequestNodeViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
        // The tree does not scroll by itself while dragging, so a folder out of sight could not be reached.
        if (Tree.Template.FindName("_tv_scrollviewer_", Tree) is not ScrollViewer scroller)
        {
            return;
        }
        var y = e.GetPosition(Tree).Y;
        if (y < _scrollEdge)
        {
            scroller.LineUp();
        }
        else if (y > Tree.ActualHeight - _scrollEdge)
        {
            scroller.LineDown();
        }
    }

    async void Tree_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(RequestNodeViewModel)) is RequestNodeViewModel node)
        {
            await ViewModel.MoveAsync(node, NodeAt(e.OriginalSource));
        }
    }

    static RequestNodeViewModel? NodeAt(object source)
    {
        for (var element = source as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is TreeViewItem item)
            {
                return item.DataContext as RequestNodeViewModel;
            }
        }
        return null;
    }

    // A menu left open while the tree reloads points at a node that is no longer in the tree.
    static RequestNodeViewModel? NodeOf(object sender) => ((FrameworkElement)sender).DataContext as RequestNodeViewModel;
}
