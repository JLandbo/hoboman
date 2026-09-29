using System.Windows;
using System.Windows.Controls;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class KeyValueEditor : UserControl
{
    public KeyValueEditor() => InitializeComponent();

    void Remove_Click(object sender, RoutedEventArgs e) => ((KeyValueListViewModel)DataContext).Remove((KeyValueRowViewModel)((FrameworkElement)sender).DataContext);
}
