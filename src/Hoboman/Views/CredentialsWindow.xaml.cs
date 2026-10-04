using System.Windows;
using System.Windows.Controls;
using Hoboman.ViewModels;

namespace Hoboman.Views;

// It can stay open beside the main window, so a credential can be saved and tried there at once.
public partial class CredentialsWindow : DialogWindow
{
    readonly CredentialEditorViewModel _viewModel;

    public CredentialsWindow(CredentialEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
    }

    void Add_Click(object sender, RoutedEventArgs e) => _viewModel.Add();

    void Remove_Click(object sender, RoutedEventArgs e) => _viewModel.Remove();

    async void Save_Click(object sender, RoutedEventArgs e) => Blink.Show((Button)sender, await _viewModel.SaveAsync());
}
