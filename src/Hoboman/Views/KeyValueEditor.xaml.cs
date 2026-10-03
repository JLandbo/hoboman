using System.Windows;
using System.Windows.Controls;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class KeyValueEditor : UserControl
{
    public static readonly DependencyProperty NameHeaderProperty = DependencyProperty.Register(nameof(NameHeader), typeof(string), typeof(KeyValueEditor));

    public static readonly DependencyProperty ValueHeaderProperty = DependencyProperty.Register(nameof(ValueHeader), typeof(string), typeof(KeyValueEditor));

    public static readonly DependencyProperty CanDisableProperty = DependencyProperty.Register(nameof(CanDisable), typeof(bool), typeof(KeyValueEditor), new(true));

    public KeyValueEditor()
    {
        InitializeComponent();
        SetResourceReference(NameHeaderProperty, "Table.Name");
        SetResourceReference(ValueHeaderProperty, "Table.Value");
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

    // Lists whose rows cannot be turned off, such as a workflow's parameters, leave out the box for it.
    public bool CanDisable
    {
        get => (bool)GetValue(CanDisableProperty);
        set => SetValue(CanDisableProperty, value);
    }

    void Remove_Click(object sender, RoutedEventArgs e) => ((KeyValueListViewModel)DataContext).Remove((KeyValueRowViewModel)((FrameworkElement)sender).DataContext);
}
