using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Hoboman.Core.Environments;
using Hoboman.Core.Settings;
using Hoboman.Core.Storage;
using Hoboman.ViewModels;
using Microsoft.Extensions.Logging;

namespace Hoboman.Views;

public partial class MainWindow : Window
{
    readonly MainViewModel _viewModel;
    readonly SettingsStore _settings;
    readonly ILogger<MainWindow> _logger;
    readonly Dictionary<string, double> _splits = [];

    public MainWindow(MainViewModel viewModel, SettingsStore settings, ILogger<MainWindow> logger)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _settings = settings;
        _logger = logger;
        SplitMemory.SetSplits(this, _splits);
    }

    // Called before the window is shown, so it opens as it was left instead of jumping there. A screen that got smaller since caps the size.
    public async Task RestoreLayoutAsync()
    {
        try
        {
            if ((await _settings.LoadAsync(CancellationToken.None)).Layout is not { } layout)
            {
                return;
            }
            Width = Math.Min(layout.Width, SystemParameters.WorkArea.Width);
            Height = Math.Min(layout.Height, SystemParameters.WorkArea.Height);
            SidebarColumn.Width = new GridLength(layout.SidebarWidth);
            foreach (var (place, share) in layout.Splits ?? new Dictionary<string, double>())
            {
                _splits[place] = share;
            }
            WindowState = layout.IsMaximized ? WindowState.Maximized : WindowState.Normal;
        }
        // WPF refuses sizes like a negative one, which a hand-edited file can hold.
        catch (Exception exception) when (FileProblem.Is(exception) || exception is ArgumentException)
        {
            _logger.LogWarning(exception, "Could not read the saved layout");
        }
    }

    async void Settings_Click(object sender, RoutedEventArgs e) => await _viewModel.EditSettingsAsync();

    async void Stringify_Click(object sender, RoutedEventArgs e) => await ChangeClipboardAsync((Button)sender, _viewModel.Clipboard.StringifyAsync);

    async void Parse_Click(object sender, RoutedEventArgs e) => await ChangeClipboardAsync((Button)sender, _viewModel.Clipboard.ParseAsync);

    async void EncodeBase64_Click(object sender, RoutedEventArgs e) => await ChangeClipboardAsync((Button)sender, _viewModel.Clipboard.EncodeBase64Async);

    async void DecodeBase64_Click(object sender, RoutedEventArgs e) => await ChangeClipboardAsync((Button)sender, _viewModel.Clipboard.DecodeBase64Async);

    static async Task ChangeClipboardAsync(Button button, Func<Task<bool>> change) => Blink.Show(button, await change());

    void NewRequest_Click(object sender, RoutedEventArgs e) => _viewModel.NewTab();

    async void NewFolder_Click(object sender, RoutedEventArgs e) => await _viewModel.NewFolderAsync();

    async void NewWorkflow_Click(object sender, RoutedEventArgs e) => await _viewModel.NewWorkflowAsync();

    void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems is [var tab, ..])
        {
            ((ListBox)sender).ScrollIntoView(tab);
        }
    }

    void CloseTab_Click(object sender, RoutedEventArgs e) => _viewModel.Close((RequestTabViewModel)((FrameworkElement)sender).DataContext);

    void PinTab_Click(object sender, RoutedEventArgs e) => ((RequestTabViewModel)((FrameworkElement)sender).DataContext).Pin();

    async void NoEnvironment_Click(object sender, RoutedEventArgs e) => await ChooseAsync(null);

    async void Environment_Click(object sender, RoutedEventArgs e) => await ChooseAsync((ApiEnvironment)((FrameworkElement)sender).DataContext);

    async void EditEnvironments_Click(object sender, RoutedEventArgs e)
    {
        CloseEnvironmentMenu();
        await _viewModel.EditEnvironmentsAsync();
    }

    async void EditCredentials_Click(object sender, RoutedEventArgs e) => await _viewModel.Credentials.EditAsync();

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

    void Window_Closing(object? sender, CancelEventArgs e)
    {
        e.Cancel = !_viewModel.CanClose();
        if (!e.Cancel)
        {
            SaveLayout();
        }
    }

    // Windows closes the window when the session ends even if it was kept open above, as no question can be shown then.
    void Window_Closed(object? sender, EventArgs e) => _viewModel.Exit();

    // The app ends right after, so the write is waited for here. The store does its work off this thread, so waiting cannot lock up.
    void SaveLayout()
    {
        var size = WindowState == WindowState.Normal ? new Size(ActualWidth, ActualHeight) : RestoreBounds.Size;
        var layout = new WindowLayout(size.Width, size.Height, WindowState == WindowState.Maximized, SidebarColumn.ActualWidth, new Dictionary<string, double>(_splits));
        try
        {
            var session = _viewModel.Session;
            _settings.UpdateAsync(saved => saved with { Layout = layout, Session = session }, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            _logger.LogWarning(exception, "Could not save the layout");
        }
    }
}
