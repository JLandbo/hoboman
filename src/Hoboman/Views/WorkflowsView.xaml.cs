using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class WorkflowsView : UserControl
{
    public WorkflowsView() => InitializeComponent();

    MainViewModel ViewModel => (MainViewModel)DataContext;

    // Opening on click or Enter, like the tree, so moving through the list with the arrow keys does not open every workflow.
    async void Names_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => await OpenAsync((DependencyObject)e.OriginalSource);

    async void Names_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await OpenAsync((DependencyObject)e.OriginalSource);
        }
    }

    async void Rename_Click(object sender, RoutedEventArgs e) => await ViewModel.RenameWorkflowAsync((WorkflowItem)((FrameworkElement)sender).DataContext);

    async void Delete_Click(object sender, RoutedEventArgs e) => await ViewModel.DeleteWorkflowAsync((WorkflowItem)((FrameworkElement)sender).DataContext);

    async Task OpenAsync(DependencyObject source)
    {
        if (ItemsControl.ContainerFromElement(Names, source) is ListBoxItem { DataContext: WorkflowItem item })
        {
            await ViewModel.OpenWorkflowAsync(item.Id);
        }
    }
}
