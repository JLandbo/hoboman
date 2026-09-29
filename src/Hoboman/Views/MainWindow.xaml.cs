using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Hoboman.Core.Environments;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class MainWindow : Window
{
    readonly MainViewModel _viewModel;
    readonly SettingsViewModel _settings;

    public MainWindow(MainViewModel viewModel, SettingsViewModel settings)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _settings = settings;
    }

    async void Settings_Click(object sender, RoutedEventArgs e)
    {
        await _settings.LoadAsync(CancellationToken.None);
        new SettingsWindow(_settings) { Owner = this }.ShowDialog();
    }

    void NewRequest_Click(object sender, RoutedEventArgs e) => _viewModel.NewTab();

    async void NewFolder_Click(object sender, RoutedEventArgs e) => await _viewModel.NewFolderAsync();

    void CloseTab_Click(object sender, RoutedEventArgs e) => _viewModel.Close((RequestTabViewModel)((FrameworkElement)sender).DataContext);

    void Environment_Click(object sender, RoutedEventArgs e)
    {
        var environments = _viewModel.Environments;
        var menu = new ContextMenu { Style = (Style)FindResource("PopupMenu"), PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        foreach (var environment in environments.All.Prepend<ApiEnvironment?>(null))
        {
            var item = new MenuItem { Header = environment?.Name, IsChecked = environment == environments.Selected };
            if (environment is null)
            {
                item.SetResourceReference(HeaderedItemsControl.HeaderProperty, "Environment.None");
            }
            item.Click += (_, _) => environments.Choose(environment);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    void Window_Closing(object? sender, CancelEventArgs e) => e.Cancel = !_viewModel.CanClose();
}
