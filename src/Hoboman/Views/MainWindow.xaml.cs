using System.ComponentModel;
using System.Windows;
using Hoboman.Core.Environments;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class MainWindow : Window
{
    readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
    }

    async void Settings_Click(object sender, RoutedEventArgs e) => await _viewModel.EditSettingsAsync();

    void NewRequest_Click(object sender, RoutedEventArgs e) => _viewModel.NewTab();

    async void NewFolder_Click(object sender, RoutedEventArgs e) => await _viewModel.NewFolderAsync();

    void CloseTab_Click(object sender, RoutedEventArgs e) => _viewModel.Close((RequestTabViewModel)((FrameworkElement)sender).DataContext);

    async void NoEnvironment_Click(object sender, RoutedEventArgs e) => await ChooseAsync(null);

    async void Environment_Click(object sender, RoutedEventArgs e) => await ChooseAsync((ApiEnvironment)((FrameworkElement)sender).DataContext);

    async void EditEnvironments_Click(object sender, RoutedEventArgs e)
    {
        EnvironmentToggle.IsChecked = false;
        await _viewModel.EditEnvironmentsAsync();
    }

    Task ChooseAsync(ApiEnvironment? environment)
    {
        EnvironmentToggle.IsChecked = false;
        return _viewModel.Environments.ChooseAsync(environment);
    }

    void Window_Closing(object? sender, CancelEventArgs e) => e.Cancel = !_viewModel.CanClose();
}
