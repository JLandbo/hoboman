using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Hoboman.Core.Requests;
using Hoboman.Controls;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class RequestTreeView : UserControl
{
    // How close to the top or bottom of the tree a dragged request scrolls it.
    const double _scrollEdge = 20;

    public RequestTreeView()
    {
        InitializeComponent();
        _expandFolder.Tick += (_, _) =>
        {
            _expandFolder.Stop();
            if (_hoveredFolder is { } folder)
            {
                folder.IsExpanded = true;
            }
        };
        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is MainViewModel previous)
            {
                previous.Tree.Revealed -= Reveal;
            }
            if (args.NewValue is MainViewModel current)
            {
                current.Tree.Revealed += Reveal;
            }
        };
    }

    MainViewModel ViewModel => (MainViewModel)DataContext;

    (RequestNodeViewModel Node, Point At)? _pressed;
    DropIndicator? _indicator;
    RequestNodeViewModel? _hoveredFolder;
    readonly DispatcherTimer _expandFolder = new() { Interval = TimeSpan.FromMilliseconds(600) };

    void Reveal(RequestNodeViewModel row)
    {
        if (!IsVisible)
        {
            return;
        }
        Dispatcher.InvokeAsync(() =>
        {
            if (!IsVisible)
            {
                return;
            }
            var parents = new Stack<string>();
            for (var parent = RequestLibrary.ParentOf(row.Path); parent is not null; parent = RequestLibrary.ParentOf(parent))
            {
                parents.Push(parent);
            }
            ItemsControl container = Tree;
            foreach (var path in parents)
            {
                var folder = container.Items.OfType<RequestNodeViewModel>().FirstOrDefault(node => node.IsFolder && string.Equals(node.Path, path, StringComparison.OrdinalIgnoreCase));
                if (container.ItemContainerGenerator.ContainerFromItem(folder) is not TreeViewItem item)
                {
                    return;
                }
                container = item;
            }
            (container.ItemContainerGenerator.ContainerFromItem(row) as TreeViewItem)?.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    async void NewRequest_Click(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node)
        {
            await ViewModel.NewDraftAsync(node);
        }
    }

    async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node)
        {
            await ViewModel.NewSubfolderAsync(node);
        }
    }

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
            if (node.IsDraft)
            {
                await ViewModel.RenameTabAsync(node.Tab!);
                return;
            }
            await ViewModel.RenameAsync(node);
        }
    }

    async void Clone_Click(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node)
        {
            await ViewModel.CloneAsync(node);
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
        _pressed = DragElements.Ancestor<ButtonBase>(e.OriginalSource) is null && NodeAt(e.OriginalSource) is { } node ? (node, e.GetPosition(this)) : null;

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
        RootDropTarget.Visibility = Visibility.Visible;
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, pressed.Node, DragDropEffects.Move);
        }
        finally
        {
            RootDropTarget.Visibility = Visibility.Collapsed;
            ClearDrop();
        }
    }

    void Tree_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (e.Data.GetData(typeof(RequestNodeViewModel)) is not RequestNodeViewModel source)
        {
            ClearDrop();
            return;
        }
        // The tree does not scroll by itself while dragging, so a folder out of sight could not be reached.
        var scroller = Tree.Template.FindName("_tv_scrollviewer_", Tree) as ScrollViewer;
        var y = e.GetPosition(Tree).Y;
        if (y < _scrollEdge)
        {
            scroller?.LineUp();
        }
        else if (y > Tree.ActualHeight - _scrollEdge)
        {
            scroller?.LineDown();
        }
        var target = DropAt(e.GetPosition(this), sender == RootDropTarget);
        if (!ViewModel.CanMove(source, target.Node, target.Position))
        {
            ClearDrop();
            return;
        }
        e.Effects = DragDropEffects.Move;
        (_indicator ??= new(this)).Show(target.Bounds, target.Position == DropPosition.Inside && (target.Node is not null || sender == RootDropTarget));
        Hover(target.Position == DropPosition.Inside ? target.Node : null);
    }

    async void Tree_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var target = DropAt(e.GetPosition(this), sender == RootDropTarget);
        ClearDrop();
        e.Effects = DragDropEffects.None;
        if (e.Data.GetData(typeof(RequestNodeViewModel)) is RequestNodeViewModel node && ViewModel.CanMove(node, target.Node, target.Position))
        {
            e.Effects = DragDropEffects.Move;
            await ViewModel.MoveAsync(node, target.Node, target.Position);
        }
    }

    internal (RequestNodeViewModel? Node, DropPosition Position, Rect Bounds) DropAt(Point point, bool root = false)
    {
        if (root)
        {
            return (null, DropPosition.Inside, DragElements.Bounds(RootDropTarget, this));
        }
        var item = DragElements.Ancestor<TreeViewItem>(InputHitTest(point));
        while (item is not null)
        {
            var row = (Border)item.Template.FindName("Row", item);
            var bounds = DragElements.Bounds(row, this);
            if (point.X >= bounds.Left && point.Y >= bounds.Top && point.Y <= bounds.Bottom && item.DataContext is RequestNodeViewModel node)
            {
                var fraction = (point.Y - bounds.Top) / bounds.Height;
                var position = node.IsFolder && fraction is >= 0.25 and <= 0.75 ? DropPosition.Inside : fraction < 0.5 ? DropPosition.Before : DropPosition.After;
                if (position == DropPosition.After && node.IsFolder && node.IsExpanded)
                {
                    position = DropPosition.Inside;
                }
                return (node, position, position == DropPosition.Inside ? bounds : new Rect(bounds.Left, position == DropPosition.Before ? bounds.Top : bounds.Bottom, bounds.Width, 0));
            }
            item = DragElements.Ancestor<TreeViewItem>(VisualTreeHelper.GetParent(item));
        }
        var tree = DragElements.Bounds(Tree, this);
        var last = Tree.Items.Count > 0 ? Tree.ItemContainerGenerator.ContainerFromIndex(Tree.Items.Count - 1) as TreeViewItem : null;
        var bottom = last is null ? tree.Top + 2 : Math.Clamp(DragElements.Bounds(last, this).Bottom, tree.Top + 2, tree.Bottom - 2);
        return (null, DropPosition.Inside, new Rect(tree.Left + 2, bottom, Math.Max(0, tree.Width - 4), 0));
    }

    void Hover(RequestNodeViewModel? folder)
    {
        if (_hoveredFolder == folder)
        {
            return;
        }
        _expandFolder.Stop();
        _hoveredFolder = folder;
        if (folder is { IsFolder: true, IsExpanded: false })
        {
            _expandFolder.Start();
        }
    }

    void ClearDrop()
    {
        _indicator?.Clear();
        Hover(null);
    }

    void Tree_DragLeave(object sender, DragEventArgs e) => ClearDrop();

    static RequestNodeViewModel? NodeAt(object source)
    {
        return DragElements.Ancestor<TreeViewItem>(source)?.DataContext as RequestNodeViewModel;
    }

    // A menu left open while the tree reloads points at a node that is no longer in the tree.
    static RequestNodeViewModel? NodeOf(object sender) => ((FrameworkElement)sender).DataContext as RequestNodeViewModel;
}
