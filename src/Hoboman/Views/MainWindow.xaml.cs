using System.Windows;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
