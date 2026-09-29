using System.Windows;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class FolderAuthWindow : DialogWindow
{
    readonly FolderAuthViewModel _viewModel;

    public FolderAuthWindow(FolderAuthViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
    }

    async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (await _viewModel.SaveAsync())
        {
            DialogResult = true;
        }
    }
}
