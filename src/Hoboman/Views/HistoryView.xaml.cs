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

    async void Entries_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Entries.SelectedItem is not HistoryItemViewModel item)
        {
            return;
        }
        Entries.SelectedItem = null;
        await ((MainViewModel)DataContext).OpenAsync(item);
    }
}
