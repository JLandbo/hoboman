using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

    async void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (List.SelectedItem is not HistoryItemViewModel item)
        {
            return;
        }
        List.SelectedItem = null;
        await ((MainViewModel)DataContext).OpenAsync(item);
    }
}
