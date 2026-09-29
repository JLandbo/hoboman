using System.Windows;
using System.Windows.Input;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class EnvironmentsWindow : Window
{
    readonly EnvironmentEditorViewModel _viewModel;

    public EnvironmentsWindow(EnvironmentEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
    }

    void Add_Click(object sender, RoutedEventArgs e) => _viewModel.Add();

    void Remove_Click(object sender, RoutedEventArgs e) => _viewModel.Remove();

    async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (await _viewModel.SaveAsync())
        {
            DialogResult = true;
        }
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
