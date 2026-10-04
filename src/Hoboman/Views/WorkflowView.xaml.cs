using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class WorkflowView : UserControl
{
    public WorkflowView() => InitializeComponent();

    WorkflowViewModel Workflow => (WorkflowViewModel)DataContext;

    void Cancel_Click(object sender, RoutedEventArgs e) => Workflow.Cancel();

    void AddRequest_Click(object sender, RoutedEventArgs e) => Workflow.AddRequest();

    async void AddScript_Click(object sender, RoutedEventArgs e) => await Workflow.AddScriptAsync();

    // The file is shown in Explorer rather than opened, as Windows runs a .js file that is opened.
    void ShowScript_Click(object sender, RoutedEventArgs e)
    {
        if (Workflow.SelectedStep is { } step && Workflow.ScriptPathOf(step) is { } path)
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
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
    // A code or body editor that is longer than its height keeps the wheel, so it can be scrolled.
    void StepDetail_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (EditorUnder(e.OriginalSource as DependencyObject) is { } editor && editor.ExtentHeight > editor.ViewportHeight)
        {
            return;
        }
        e.Handled = true;
        StepDetail.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent });
    }

    static TextEditor? EditorUnder(DependencyObject? element)
    {
        while (element is not null and not TextEditor)
        {
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return element as TextEditor;
    }
}
