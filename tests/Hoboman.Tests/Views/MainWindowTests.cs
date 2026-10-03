using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using Hoboman.Tests.ViewModels;

namespace Hoboman.Tests.Views;

[Collection("Ui")]
public sealed class MainWindowTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GlobalButtons_WhenClicked_ThenKeepNewTabsOutOfTheTreeAndAskForAFullFolderPath()
    {
        using var harness = new Harness();
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            var window = await Ui.ShowAsync(harness, main);
            var request = Ui.Descendants<Button>(window).First(button => Equals(button.ToolTip, harness.Translator.Of("Sidebar.NewRequest")));
            var folder = Ui.Descendants<Button>(window).Single(button => Equals(button.ToolTip, harness.Translator.Of("Sidebar.NewFolder")));
            Assert.Equal("\uE710", request.Content);
            Assert.Equal("\uE8F4", folder.Content);
            Ui.Click(request);
            await Ui.IdleAsync();
            Assert.Equal(2, main.Tabs.Count);
            Assert.False(main.SelectedTab!.IsDraft);
            Assert.Null(main.SelectedTab.Destination);
            var tree = Ui.Descendants<RequestTreeView>(window).Single();
            Assert.True(Ui.Descendants<TextBlock>(tree).Single(text => text.Text == harness.Translator.Of("Tree.Empty")).IsVisible);
            Ui.Click(folder);
            await Ui.IdleAsync();
            Assert.Equal((harness.Translator.Of("Folder.Title"), ""), (harness.Dialogs.NameQuestion!.Value.Title, harness.Dialogs.NameQuestion.Value.Name));
        });
    }

    [Fact]
    public async Task Draft_WhenSentSavedOrMovedToRoot_ThenShowsTheRightTabDotBreadcrumbAndEmptyState()
    {
        using var harness = new Harness(new FakeDialogs(answer: "Saved"));
        await harness.Library.CreateFolderAsync("Users/Admin", Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            var folder = RequestTreeViewModel.Flatten(main.Tree.Nodes).Single(node => node.Path == "Users/Admin");
            await main.NewDraftAsync(folder);
            var draft = main.SelectedTab!;
            var window = await Ui.ShowAsync(harness, main);
            var tabs = Ui.Descendants<ListBox>(window).Single(list => ReferenceEquals(list.ItemsSource, main.Tabs));
            var draftItem = (ListBoxItem)tabs.ItemContainerGenerator.ContainerFromItem(draft);
            var dot = Ui.Descendants<Ellipse>(draftItem).Single();
            Assert.Equal(Visibility.Visible, dot.Visibility);
            Assert.Same(window.FindResource("Attention"), dot.Fill);
            Assert.Equal((7d, 7d), (dot.Width, dot.Height));
            var globalItem = (ListBoxItem)tabs.ItemContainerGenerator.ContainerFromItem(main.Tabs[0]);
            Assert.Equal(Visibility.Collapsed, Ui.Descendants<Ellipse>(globalItem).Single().Visibility);
            var editor = Ui.Descendants<RequestEditorView>(window).Single();
            var breadcrumb = Ui.Descendants<TextBlock>(editor).Single(text => text.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path.Path == nameof(RequestTabViewModel.Folder));
            Assert.Equal("Users / Admin /", breadcrumb.Text);
            await draft.SendAsync();
            await Ui.IdleAsync();
            Assert.Equal(Visibility.Visible, dot.Visibility);
            await harness.Library.DeleteFolderAsync("Users", Cancellation);
            await main.RequestsChangedAsync();
            await Ui.IdleAsync();
            Assert.Equal("", breadcrumb.Text);
            var tree = Ui.Descendants<RequestTreeView>(window).Single();
            Assert.False(Ui.Descendants<TextBlock>(tree).Single(text => text.Text == harness.Translator.Of("Tree.Empty")).IsVisible);
            Assert.True(Ui.Descendants<TreeViewItem>(tree).Single().IsVisible);
            await draft.SaveAsync();
            await Ui.IdleAsync();
            Assert.Equal(Visibility.Collapsed, dot.Visibility);
            Assert.Equal("Saved", Ui.Named<TextBlock>(Ui.Row(Ui.Descendants<TreeViewItem>(tree).Single()), "Label").Text);
            draft.Body = "edit";
            await Ui.IdleAsync();
            Assert.Equal(Visibility.Visible, dot.Visibility);
        });
    }

    [Fact]
    public async Task Workflows_WhenOpenedFromTheirSegment_ThenShowTheirButtonAndEditorUntilATabIsChosen()
    {
        // Arrange
        using var harness = new Harness();
        var ping = ApiRequest.New() with { Url = "https://dev.local" };
        await harness.Library.SaveAsync("Ping", ping, Cancellation);
        await harness.WorkflowLibrary.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping.Id }] }, Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            var window = await Ui.ShowAsync(harness, main);

            // Act
            Ui.Select(Ui.Named<RadioButton>(window, "WorkflowsSection"));
            await Ui.IdleAsync();
            var buttons = Ui.Descendants<Button>(window).Where(button => button.IsVisible).Select(button => button.ToolTip).ToList();
            Ui.Press(Ui.Descendants<TextBlock>(Ui.Descendants<WorkflowsView>(window).Single()).Single(text => text.Text == "Flow"));
            await Ui.UntilAsync(() => Ui.Descendants<WorkflowView>(window).Any());
            var shown = Ui.Descendants<TextBlock>(Ui.Descendants<WorkflowView>(window).Single()).Any(text => text.Text == "Ping" && text.IsVisible);
            main.SelectedTab = main.Tabs.Single();
            await Ui.IdleAsync();

            // Assert
            Assert.Contains(harness.Translator.Of("Sidebar.NewWorkflow"), buttons);
            Assert.DoesNotContain(harness.Translator.Of("Sidebar.NewFolder"), buttons);
            Assert.True(shown);
            Assert.Empty(Ui.Descendants<WorkflowView>(window));
            Assert.Single(Ui.Descendants<RequestEditorView>(window));
        });
    }

    [Fact]
    public async Task RemoveStep_WhenTheChosenStepIsRemovedInTheEditor_ThenChoosesTheNextStep()
    {
        // Arrange
        using var harness = new Harness();
        var ping = ApiRequest.New() with { Url = "https://dev.local" };
        await harness.Library.SaveAsync("Ping", ping, Cancellation);
        await harness.WorkflowLibrary.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping.Id }, new() { Request = ping.Id }, new() { Request = ping.Id }] }, Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            await main.OpenWorkflowAsync("Flow");
            var workflow = main.Workflow!;
            workflow.SelectedStep = workflow.Steps[1];
            var next = workflow.Steps[2];
            var window = await Ui.ShowAsync(harness, main);

            // Act
            Ui.Click(Ui.Descendants<Button>(window).Single(button => Equals(button.ToolTip, harness.Translator.Of("Workflow.RemoveStep"))));
            await Ui.IdleAsync();

            // Assert
            Assert.Same(next, workflow.SelectedStep);
        });
    }

    [Fact]
    public async Task InputBindings_WhenAWorkflowIsShown_ThenCtrlSSavesAndCtrlEnterRunsIt()
    {
        // Arrange
        using var harness = new Harness();
        await harness.WorkflowLibrary.SaveAsync("Flow", new() { Id = Guid.NewGuid() }, Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            await main.OpenWorkflowAsync("Flow");

            // Act
            var window = await Ui.ShowAsync(harness, main);
            var bindings = window.InputBindings.OfType<KeyBinding>().ToDictionary(binding => binding.Key, binding => binding.Command);

            // Assert
            Assert.Equal(((ICommand)main.Workflow!.Save, (ICommand)main.Workflow.Send), (bindings[Key.S], bindings[Key.Enter]));
        });
    }

    [Fact]
    public async Task StepDetail_WhenTheWheelTurnsOverTheStepsValues_ThenScrollsTheStep()
    {
        // Arrange
        using var harness = new Harness();
        var ping = ApiRequest.New() with { Url = "https://dev.local" };
        await harness.Library.SaveAsync("Ping", ping, Cancellation);
        await harness.WorkflowLibrary.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = ping.Id, With = [.. Enumerable.Range(0, 40).Select(index => new KeyValue($"name{index}"))] }] },
            Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            await main.OpenWorkflowAsync("Flow");
            var window = await Ui.ShowAsync(harness, main);
            var detail = Ui.Named<ScrollViewer>(window, "StepDetail");
            var value = Ui.Descendants<TextBox>(detail).First();
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };

            // Act
            value.RaiseEvent(wheel);
            wheel.RoutedEvent = UIElement.MouseWheelEvent;
            value.RaiseEvent(wheel);
            await Ui.IdleAsync();

            // Assert
            Assert.True(detail.VerticalOffset > 0);
        });
    }
}
