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
    readonly Func<EnvironmentEditorViewModel> _environmentEditor;

    public MainWindow(MainViewModel viewModel, SettingsViewModel settings, Func<EnvironmentEditorViewModel> environmentEditor)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _settings = settings;
        _environmentEditor = environmentEditor;
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
        var edit = new MenuItem();
        edit.SetResourceReference(HeaderedItemsControl.HeaderProperty, "Environment.Edit");
        edit.Click += async (_, _) => await EditEnvironmentsAsync();
        // A separator with its own style is left alone by the menu's item style, which only fits menu items.
        menu.Items.Add(new Separator { Style = (Style)FindResource("MenuSeparator") });
        menu.Items.Add(edit);
        menu.IsOpen = true;
    }

    async Task EditEnvironmentsAsync()
    {
        var editor = _environmentEditor();
        await editor.LoadAsync(CancellationToken.None);
        if (new EnvironmentsWindow(editor) { Owner = this }.ShowDialog() == true)
        {
            await _viewModel.Environments.LoadAsync(CancellationToken.None);
        }
    }

    void Window_Closing(object? sender, CancelEventArgs e) => e.Cancel = !_viewModel.CanClose();
}
