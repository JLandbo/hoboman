using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class WorkflowView : UserControl
{
    public WorkflowView() => InitializeComponent();

    WorkflowViewModel Workflow => (WorkflowViewModel)DataContext;

    void Cancel_Click(object sender, RoutedEventArgs e) => Workflow.Cancel();

    // The requests are listed when the menu opens, so it shows the ones saved since.
    void AddStepToggle_Checked(object sender, RoutedEventArgs e) => RequestList.ItemsSource = Workflow.RequestNames.ToList();

    async void AddStep_Click(object sender, RoutedEventArgs e)
    {
        AddStepToggle.IsChecked = false;
        await Workflow.AddStepAsync((string)((FrameworkElement)sender).DataContext);
    }

    void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);

    void MoveDown_Click(object sender, RoutedEventArgs e) => Move(1);

    void Move(int offset)
    {
        if (Workflow.SelectedStep is { } step)
        {
            Workflow.MoveStep(step, offset);
        }
    }

    void RemoveStep_Click(object sender, RoutedEventArgs e)
    {
        if (Workflow.SelectedStep is { } step)
        {
            Workflow.RemoveStep(step);
        }
    }

    // The tables in the step have scroll viewers of their own, which take the wheel although they get all the height they need here.
    void StepDetail_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        StepDetail.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent });
    }
}
