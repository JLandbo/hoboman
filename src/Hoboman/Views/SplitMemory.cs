using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Hoboman.Views;

// A split that is dragged is kept for views built later, such as the next tab or the workflow shown again, and with the window's layout.
// The splits are shared through the window, so every view of one kind is split the same way.
public static class SplitMemory
{
    public static readonly DependencyProperty SplitsProperty = DependencyProperty.RegisterAttached("Splits", typeof(Dictionary<string, double>), typeof(SplitMemory),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty RowsProperty = DependencyProperty.RegisterAttached("Rows", typeof(string), typeof(SplitMemory),
        new PropertyMetadata(null, (target, _) => Follow(target, rows: true)));

    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.RegisterAttached("Columns", typeof(string), typeof(SplitMemory),
        new PropertyMetadata(null, (target, _) => Follow(target, rows: false)));

    public static Dictionary<string, double>? GetSplits(DependencyObject target) => (Dictionary<string, double>?)target.GetValue(SplitsProperty);

    public static void SetSplits(DependencyObject target, Dictionary<string, double>? value) => target.SetValue(SplitsProperty, value);

    public static string? GetRows(DependencyObject target) => (string?)target.GetValue(RowsProperty);

    public static void SetRows(DependencyObject target, string? value) => target.SetValue(RowsProperty, value);

    public static string? GetColumns(DependencyObject target) => (string?)target.GetValue(ColumnsProperty);

    public static void SetColumns(DependencyObject target, string? value) => target.SetValue(ColumnsProperty, value);

    // The grid has the two panes with the splitter between them, and the share is the first pane's part of the room they have.
    static void Follow(DependencyObject target, bool rows)
    {
        if (target is not Grid grid)
        {
            return;
        }
        grid.Loaded += (_, _) => Apply(grid, rows);
        grid.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, e) =>
        {
            // A splitter in a grid inside this one tells of another split.
            if (e.OriginalSource is GridSplitter splitter && splitter.Parent == grid)
            {
                Keep(grid, rows);
            }
        }));
    }

    static string? KeyOf(Grid grid, bool rows) => rows ? GetRows(grid) : GetColumns(grid);

    static void Apply(Grid grid, bool rows)
    {
        if (GetSplits(grid) is not { } splits || KeyOf(grid, rows) is not { } key || !splits.TryGetValue(key, out var share) || share is not (> 0 and < 1))
        {
            return;
        }
        var (first, last) = (new GridLength(share, GridUnitType.Star), new GridLength(1 - share, GridUnitType.Star));
        if (rows)
        {
            (grid.RowDefinitions[0].Height, grid.RowDefinitions[2].Height) = (first, last);
        }
        else
        {
            (grid.ColumnDefinitions[0].Width, grid.ColumnDefinitions[2].Width) = (first, last);
        }
    }

    static void Keep(Grid grid, bool rows)
    {
        if (GetSplits(grid) is not { } splits || KeyOf(grid, rows) is not { } key)
        {
            return;
        }
        var (first, last) = rows
            ? (grid.RowDefinitions[0].ActualHeight, grid.RowDefinitions[2].ActualHeight)
            : (grid.ColumnDefinitions[0].ActualWidth, grid.ColumnDefinitions[2].ActualWidth);
        if (first + last > 0)
        {
            splits[key] = first / (first + last);
        }
    }
}
