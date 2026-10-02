using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Hoboman.Views;

static class DragElements
{
    public static T? Ancestor<T>(object? source) where T : DependencyObject
    {
        for (var element = source as DependencyObject; element is not null; element = element is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(element))
        {
            if (element is T found)
            {
                return found;
            }
        }
        return null;
    }

    public static Rect Bounds(FrameworkElement element, Visual relativeTo) => element.TransformToAncestor(relativeTo).TransformBounds(new Rect(element.RenderSize));

    public static ScrollViewer? Scroller(DependencyObject root)
    {
        if (root is ScrollViewer scroller)
        {
            return scroller;
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (Scroller(VisualTreeHelper.GetChild(root, index)) is { } found)
            {
                return found;
            }
        }
        return null;
    }
}
