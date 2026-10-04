using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class KeyValueEditor : UserControl
{
    public static readonly DependencyProperty NameHeaderProperty = DependencyProperty.Register(nameof(NameHeader), typeof(string), typeof(KeyValueEditor));

    public static readonly DependencyProperty ValueHeaderProperty = DependencyProperty.Register(nameof(ValueHeader), typeof(string), typeof(KeyValueEditor));

    public static readonly DependencyProperty CanDisableProperty = DependencyProperty.Register(nameof(CanDisable), typeof(bool), typeof(KeyValueEditor),
        new(true, (editor, e) => ((KeyValueEditor)editor).CheckWidth = new((bool)e.NewValue ? 30 : 0)));

    static readonly DependencyPropertyKey _checkWidthKey = DependencyProperty.RegisterReadOnly(nameof(CheckWidth), typeof(GridLength), typeof(KeyValueEditor), new(new GridLength(30)));

    public static readonly DependencyProperty CheckWidthProperty = _checkWidthKey.DependencyProperty;

    public static readonly DependencyProperty NameWidthProperty = DependencyProperty.Register(nameof(NameWidth), typeof(GridLength), typeof(KeyValueEditor), new(new GridLength(1, GridUnitType.Star)));

    public static readonly DependencyProperty ValueWidthProperty = DependencyProperty.Register(nameof(ValueWidth), typeof(GridLength), typeof(KeyValueEditor), new(new GridLength(1, GridUnitType.Star)));

    // Neither column can be dragged smaller than this.
    const double _narrowest = 40;

    public KeyValueEditor()
    {
        InitializeComponent();
        SetResourceReference(NameHeaderProperty, "Table.Name");
        SetResourceReference(ValueHeaderProperty, "Table.Value");
        Loaded += (_, _) => Restore();
    }

    public string? NameHeader
    {
        get => (string?)GetValue(NameHeaderProperty);
        set => SetValue(NameHeaderProperty, value);
    }

    public string? ValueHeader
    {
        get => (string?)GetValue(ValueHeaderProperty);
        set => SetValue(ValueHeaderProperty, value);
    }

    // Lists whose rows cannot be turned off, such as a workflow's parameters, leave out the box for it and the room it takes.
    public bool CanDisable
    {
        get => (bool)GetValue(CanDisableProperty);
        set => SetValue(CanDisableProperty, value);
    }

    public GridLength CheckWidth
    {
        get => (GridLength)GetValue(CheckWidthProperty);
        private set => SetValue(_checkWidthKey, value);
    }

    // The name's share of the room, so the columns keep their proportion when the table gets wider or narrower.
    public GridLength NameWidth
    {
        get => (GridLength)GetValue(NameWidthProperty);
        set => SetValue(NameWidthProperty, value);
    }

    public GridLength ValueWidth
    {
        get => (GridLength)GetValue(ValueWidthProperty);
        set => SetValue(ValueWidthProperty, value);
    }

    // A table with SplitMemory.Columns set keeps its divider where it was dragged, for every table of that kind.
    void Restore()
    {
        if (SplitMemory.GetColumns(this) is { } key && SplitMemory.GetSplits(this)?.GetValueOrDefault(key) is double share and > 0 and < 1)
        {
            Divide(share);
        }
    }

    void Divider_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var columns = ((Grid)((FrameworkElement)sender).Parent).ColumnDefinitions;
        var (name, room) = (columns[1].ActualWidth, columns[1].ActualWidth + columns[2].ActualWidth);
        if (room > 2 * _narrowest)
        {
            Divide(Math.Clamp(name + e.HorizontalChange, _narrowest, room - _narrowest) / room);
        }
    }

    void Divider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (SplitMemory.GetColumns(this) is { } key && SplitMemory.GetSplits(this) is { } splits)
        {
            splits[key] = NameWidth.Value;
        }
    }

    void Divide(double share) => (NameWidth, ValueWidth) = (new(share, GridUnitType.Star), new(1 - share, GridUnitType.Star));

    void Remove_Click(object sender, RoutedEventArgs e) => ((KeyValueListViewModel)DataContext).Remove((KeyValueRowViewModel)((FrameworkElement)sender).DataContext);
}
