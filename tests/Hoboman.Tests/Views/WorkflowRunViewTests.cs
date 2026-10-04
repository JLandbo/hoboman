using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hoboman.Tests.ViewModels;

namespace Hoboman.Tests.Views;

[Collection("Ui")]
public sealed class WorkflowRunViewTests
{
    [Fact]
    public async Task WorkflowView_WhenARunComesToStepsBelowTheList_ThenTheListFollows()
    {
        using var harness = new Harness();
        await harness.WorkflowLibrary.SaveAsync("Flow", new()
        {
            Id = Guid.NewGuid(),
            Steps = [.. Enumerable.Range(1, 40).Select(number => new WorkflowStep { Name = $"Trin {number}", Request = new() { Url = $"https://dev.local/{number}" } })],
        }, TestContext.Current.CancellationToken);
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var main = harness.Main();
            await main.LoadAsync();
            var window = await Ui.ShowAsync(harness, main);
            await main.OpenWorkflowAsync("Flow");
            await Ui.UntilAsync(() => Ui.Descendants<WorkflowView>(window).Any());
            var scroller = Ui.Descendants<ScrollViewer>(Ui.Named<ListBox>(window, "StepList")).First();

            // Act
            await main.Workflow!.RunAsync();
            await Ui.IdleAsync();

            // Assert
            Assert.True(scroller.VerticalOffset > 0);
        });
    }

    [Fact]
    public async Task WorkflowView_WhenTheStepsAreScrolled_ThenTheyMoveByPixelsAndNotByWholeSteps()
    {
        using var harness = new Harness();
        await SaveAsync(harness, 40);
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var main = harness.Main();
            await main.LoadAsync();
            var window = await Ui.ShowAsync(harness, main);

            // Act
            await main.OpenWorkflowAsync("Flow");
            await Ui.UntilAsync(() => Ui.Descendants<WorkflowView>(window).Any());

            // Assert
            var scroller = Ui.Descendants<ScrollViewer>(Ui.Named<ListBox>(window, "StepList")).First();
            Assert.True(scroller.ExtentHeight > 40 * 20);
        });
    }

    [Fact]
    public async Task WorkflowView_WhenAStepRuns_ThenItHasAYellowBorderAndKeepsItsSize()
    {
        using var harness = new Harness();
        await SaveAsync(harness, 3);
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var main = harness.Main();
            await main.LoadAsync();
            var window = await Ui.ShowAsync(harness, main);
            await main.OpenWorkflowAsync("Flow");
            await Ui.UntilAsync(() => Ui.Descendants<WorkflowView>(window).Any());
            var step = main.Workflow!.Steps[1];
            var item = (ListBoxItem)Ui.Named<ListBox>(window, "StepList").ItemContainerGenerator.ContainerFromItem(step);
            var body = Ui.Named<Border>(item, "Body");
            var size = (item.ActualWidth, item.ActualHeight);

            // Act
            step.Started();
            await Ui.IdleAsync();

            // Assert
            Assert.Equal((((SolidColorBrush)Application.Current.FindResource("Attention")).Color, size), (((SolidColorBrush)body.BorderBrush).Color, (item.ActualWidth, item.ActualHeight)));
        });
    }

    static Task SaveAsync(Harness harness, int count) => harness.WorkflowLibrary.SaveAsync("Flow", new()
    {
        Id = Guid.NewGuid(),
        Steps = [.. Enumerable.Range(1, count).Select(number => new WorkflowStep { Name = $"Trin {number}", Request = new() { Url = $"https://dev.local/{number}" } })],
    }, TestContext.Current.CancellationToken);
}
