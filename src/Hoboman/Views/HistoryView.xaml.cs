using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel viewModel)
            {
                CollectionViewSource.GetDefaultView(viewModel.History.Items).GroupDescriptions.Add(new PropertyGroupDescription(nameof(HistoryItemViewModel.Day)));
            }
        };
    }

    // Opening on click, like the tree, so moving through the list with the arrow keys does not open a tab for every call.
    async void Entries_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(Entries, (DependencyObject)e.OriginalSource) is not ListBoxItem { DataContext: HistoryItemViewModel item })
        {
            return;
        }
        Entries.SelectedItem = null;
        await ((MainViewModel)DataContext).OpenAsync(item);
    }
}
