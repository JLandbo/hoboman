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
        // A login still waiting in the browser would otherwise keep listening after the dialog is gone.
        Closed += (_, _) => viewModel.Auth.CancelFetch();
    }

    async void Save_Click(object sender, RoutedEventArgs e) => await SaveAndCloseAsync(_viewModel.SaveAsync);
}
