using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class WorkflowView : UserControl
{
    public WorkflowView() => InitializeComponent();

    WorkflowViewModel Workflow => (WorkflowViewModel)DataContext;

    void Cancel_Click(object sender, RoutedEventArgs e) => Workflow.Cancel();

    void AddRequest_Click(object sender, RoutedEventArgs e) => Workflow.AddRequest();

    async void AddScript_Click(object sender, RoutedEventArgs e) => await Workflow.AddScriptAsync();

    void AddDelay_Click(object sender, RoutedEventArgs e) => Workflow.AddDelay();

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
}
