using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class SettingsWindow : DialogWindow
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
