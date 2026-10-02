using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Hoboman.Controls;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class MainWindow
{
    (RequestTabViewModel Tab, Point At)? _pressedTab;
    DropIndicator? _tabIndicator;

    async void Tabs_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressedTab = null;
        if (DragElements.Ancestor<ButtonBase>(e.OriginalSource) is not null || DragElements.Ancestor<ListBoxItem>(e.OriginalSource)?.DataContext is not RequestTabViewModel tab)
        {
            return;
        }
        if (e.ClickCount == 2)
        {
            e.Handled = true;
            await _viewModel.RenameTabAsync(tab);
            return;
        }
        _pressedTab = (tab, e.GetPosition(RequestTabs));
    }

    void Tabs_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _pressedTab = null;

    void Tabs_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressedTab is not { } pressed)
        {
            return;
        }
        var moved = e.GetPosition(RequestTabs) - pressed.At;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }
        _pressedTab = null;
        try
        {
            DragDrop.DoDragDrop(RequestTabs, pressed.Tab, DragDropEffects.Move);
        }
        finally
        {
            _tabIndicator?.Clear();
        }
    }

    internal (RequestTabViewModel? Tab, bool After, Rect Bounds) TabDropAt(Point point)
    {
        var item = DragElements.Ancestor<ListBoxItem>(RequestTabs.InputHitTest(point));
        if (item?.DataContext is RequestTabViewModel tab)
        {
            var bounds = DragElements.Bounds(item, RequestTabs);
            var after = point.X >= bounds.Left + bounds.Width / 2;
            return (tab, after, new Rect(Math.Clamp(after ? bounds.Right : bounds.Left, 2, Math.Max(2, RequestTabs.ActualWidth - 2)), bounds.Top + 2, 0, Math.Max(0, bounds.Height - 4)));
        }
        var last = RequestTabs.ItemContainerGenerator.ContainerFromIndex(RequestTabs.Items.Count - 1) as ListBoxItem;
        var end = last is null ? new Rect(2, 2, 0, 20) : DragElements.Bounds(last, RequestTabs);
        return (null, true, new Rect(Math.Clamp(end.Right, 2, Math.Max(2, RequestTabs.ActualWidth - 2)), end.Top + 2, 0, Math.Max(0, end.Height - 4)));
    }

    void Tabs_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (e.Data.GetData(typeof(RequestTabViewModel)) is not RequestTabViewModel source || !_viewModel.Tabs.Contains(source))
        {
            _tabIndicator?.Clear();
            return;
        }
        var point = e.GetPosition(RequestTabs);
        var scroller = DragElements.Scroller(RequestTabs);
        if (point.X < 20)
        {
            scroller?.LineLeft();
        }
        else if (point.X > RequestTabs.ActualWidth - 20)
        {
            scroller?.LineRight();
        }
        var target = TabDropAt(point);
        if (target.Tab == source)
        {
            _tabIndicator?.Clear();
            return;
        }
        e.Effects = DragDropEffects.Move;
        (_tabIndicator ??= new(RequestTabs)).Show(target.Bounds, box: false);
    }

    void Tabs_DragLeave(object sender, DragEventArgs e) => _tabIndicator?.Clear();

    void Tabs_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        _tabIndicator?.Clear();
        e.Effects = DragDropEffects.None;
        if (e.Data.GetData(typeof(RequestTabViewModel)) is RequestTabViewModel tab && _viewModel.Tabs.Contains(tab))
        {
            var target = TabDropAt(e.GetPosition(RequestTabs));
            e.Effects = target.Tab == tab ? DragDropEffects.None : DragDropEffects.Move;
            _viewModel.MoveTab(tab, target.Tab, target.After);
        }
    }
}
