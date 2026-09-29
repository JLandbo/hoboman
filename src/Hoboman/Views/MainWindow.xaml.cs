using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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

    void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems is [var tab, ..])
        {
            ((ListBox)sender).ScrollIntoView(tab);
        }
    }

    void CloseTab_Click(object sender, RoutedEventArgs e) => _viewModel.Close((RequestTabViewModel)((FrameworkElement)sender).DataContext);

    async void NoEnvironment_Click(object sender, RoutedEventArgs e) => await ChooseAsync(null);

    async void Environment_Click(object sender, RoutedEventArgs e) => await ChooseAsync((ApiEnvironment)((FrameworkElement)sender).DataContext);

    async void EditEnvironments_Click(object sender, RoutedEventArgs e)
    {
        CloseEnvironmentMenu();
        await _viewModel.EditEnvironmentsAsync();
    }

    Task ChooseAsync(ApiEnvironment? environment)
    {
        CloseEnvironmentMenu();
        return _viewModel.Environments.ChooseAsync(environment);
    }

    // A menu is a window of its own, so the keyboard only gets into it when focus is moved there, once its items are made.
    void EnvironmentPopup_Opened(object sender, EventArgs e) =>
        Dispatcher.InvokeAsync(() => EnvironmentMenu.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)), DispatcherPriority.Loaded);

    void EnvironmentMenu_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseEnvironmentMenu();
        }
    }

    void CloseEnvironmentMenu()
    {
        EnvironmentToggle.IsChecked = false;
        EnvironmentToggle.Focus();
    }

    void Window_Closing(object? sender, CancelEventArgs e) => e.Cancel = !_viewModel.CanClose();
}
