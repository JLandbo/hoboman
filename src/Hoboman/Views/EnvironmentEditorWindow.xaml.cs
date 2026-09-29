using System.Windows;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class EnvironmentEditorWindow : DialogWindow
{
    readonly EnvironmentEditorViewModel _viewModel;

    public EnvironmentEditorWindow(EnvironmentEditorViewModel viewModel)
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
}
