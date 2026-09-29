using System.Windows;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class MainWindow : Window
{
    readonly SettingsViewModel _settings;

    public MainWindow(MainViewModel viewModel, SettingsViewModel settings)
    {
        InitializeComponent();
        DataContext = viewModel;
        _settings = settings;
    }

    async void Settings_Click(object sender, RoutedEventArgs e)
    {
        await _settings.LoadAsync(CancellationToken.None);
        new SettingsWindow(_settings) { Owner = this }.ShowDialog();
    }
}
