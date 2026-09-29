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

    void Settings_Click(object sender, RoutedEventArgs e) => new SettingsWindow(_settings) { Owner = this }.ShowDialog();
}
