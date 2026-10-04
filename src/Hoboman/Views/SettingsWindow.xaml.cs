using System.Diagnostics;
using System.IO;
using System.Windows;
using Hoboman.Themes;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class SettingsWindow : DialogWindow
{
    readonly SettingsViewModel _settings;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _settings = viewModel;
        viewModel.ShowThemes();
    }

    void Theme_Click(object sender, RoutedEventArgs e) => _settings.ChooseTheme(((ThemeChoice)((FrameworkElement)sender).DataContext).Theme);

    void OpenThemes_Click(object sender, RoutedEventArgs e) => Process.Start("explorer.exe", $"\"{Directory.CreateDirectory(_settings.ThemesFolder).FullName}\"");
}
