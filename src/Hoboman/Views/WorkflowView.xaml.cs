using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class WorkflowView : UserControl
{
    public WorkflowView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            RefreshWorkflowAuthButton.ClearValue(Control.BackgroundProperty);
            if (e.OldValue is WorkflowViewModel shown)
            {
                shown.StepRunning -= ShowStep;
            }
            if (e.NewValue is WorkflowViewModel workflow)
            {
                workflow.StepRunning += ShowStep;
            }
        };
    }

    // The list follows a run, so the step it has come to can be seen.
    void ShowStep(WorkflowStepViewModel step) => StepList.ScrollIntoView(step);

    WorkflowViewModel Workflow => (WorkflowViewModel)DataContext;

    void Cancel_Click(object sender, RoutedEventArgs e) => Workflow.Cancel();

    async void RefreshWorkflowAuth_Click(object sender, RoutedEventArgs e)
    {
        var workflow = Workflow;
        var succeeded = await workflow.RefreshAuthAsync();
        if (workflow == DataContext)
        {
            Blink.Show((Button)sender, succeeded);
        }
    }

    async void RefreshStepAuth_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var step = (WorkflowStepViewModel)button.DataContext;
        var succeeded = await Workflow.RefreshAuthAsync(step);
        if (step == button.DataContext)
        {
            Blink.Show(button, succeeded);
        }
    }

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
