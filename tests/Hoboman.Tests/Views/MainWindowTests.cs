using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Shapes;
using Hoboman.Tests.ViewModels;

namespace Hoboman.Tests.Views;

[Collection("Ui")]
public sealed class MainWindowTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static Grid RequestSplit(Window window) => Ui.Descendants<Grid>(window).Single(grid => SplitMemory.GetRows(grid) == "Request");

    [Fact]
    public async Task RestoreLayoutAsync_WhenASplitWasSaved_ThenTheTabIsSplitTheSame()
    {
        using var harness = new Harness();
        await harness.SettingsStore.UpdateAsync(saved => saved with { Layout = new(1200, 800, false, 290, new Dictionary<string, double> { ["Request"] = 0.25 }) }, Cancellation);
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var main = harness.Main();
            await main.LoadAsync();
            var window = new MainWindow(main, harness.SettingsStore, NullLogger<MainWindow>.Instance);

            // Act
            await window.RestoreLayoutAsync();
            Ui.Show(window);
            await Ui.IdleAsync();

            // Assert
            Assert.Equal(0.25, RequestSplit(window).RowDefinitions[0].Height.Value, 3);
        });
    }

    [Fact]
    public async Task GridSplitter_WhenDragged_ThenTheViewBuiltAgainIsSplitTheSameAndTheSplitIsSaved()
    {
        using var harness = new Harness();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var main = harness.Main();
            await main.LoadAsync();
            var window = await Ui.ShowAsync(harness, main);
            var grid = RequestSplit(window);
            (grid.RowDefinitions[0].Height, grid.RowDefinitions[2].Height) = (new GridLength(1, GridUnitType.Star), new GridLength(3, GridUnitType.Star));
            grid.UpdateLayout();
            var dragged = grid.RowDefinitions[0].ActualHeight / (grid.RowDefinitions[0].ActualHeight + grid.RowDefinitions[2].ActualHeight);

            // Act
            Ui.Descendants<GridSplitter>(grid).First(splitter => splitter.Parent == grid).RaiseEvent(new DragCompletedEventArgs(0, 0, false));
            main.Section = SidebarSection.Workflows;
            await Ui.IdleAsync();
            main.Section = SidebarSection.Collections;
            await Ui.IdleAsync();
            var share = RequestSplit(window).RowDefinitions[0].Height.Value;
            window.Close();

            // Assert
            Assert.Equal((dragged, dragged), (share, (await harness.SettingsStore.LoadAsync(Cancellation)).Layout!.Splits!["Request"]));
        });
    }

    [Fact]
    public async Task Close_WhenARunSavedSecretsForAStepThatWasNeverSaved_ThenForgetsThem()
    {
        using var harness = new Harness(new FakeDialogs(accept: true));
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var main = harness.Main();
            var workflow = await WorkflowEditorTests.AddBearerStepAndRunAsync(main, harness);
            var id = workflow.Steps.Single().SecretsId!.Value;
            var savedByRun = await harness.Secrets.OfAsync(id, SecretKind.Token, Cancellation);
            var window = await Ui.ShowAsync(harness, main);

            // Act
            window.Close();

            // Assert
            Assert.Equal(("abc", null), (savedByRun, await harness.Secrets.OfAsync(id, SecretKind.Token, Cancellation)));
        });
    }

    [Fact]
    public async Task Close_WhenTheSessionEndsWithoutAskingFirst_ThenStillForgetsTheSecretsARunSaved()
    {
        using var harness = new Harness();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var main = harness.Main();
            var workflow = await WorkflowEditorTests.AddBearerStepAndRunAsync(main, harness);
            var id = workflow.Steps.Single().SecretsId!.Value;
            var window = await Ui.ShowAsync(harness, main);
            window.Closing += (_, args) => args.Cancel = false;

            // Act
            window.Close();

            // Assert
            Assert.Null(await harness.Secrets.OfAsync(id, SecretKind.Token, Cancellation));
        });
    }

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
            draft.Editor.Body = "edit";
            await Ui.IdleAsync();
            Assert.Equal(Visibility.Visible, dot.Visibility);
        });
    }

    [Fact]
    public async Task Workflows_WhenOpenedFromTheirSegment_ThenShowTheirButtonAndEditorWithoutTheTabsUntilTheCollectionsAreChosen()
    {
        // Arrange
        using var harness = new Harness();
        await harness.WorkflowLibrary.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Name = "Ping", Request = new() { Url = "https://dev.local" } }] }, Cancellation);
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
            var view = Ui.Descendants<WorkflowView>(window).Single();
            var editors = (Ui.Descendants<RequestLineEditor>(view).Count(), Ui.Descendants<RequestBodyEditor>(view).Count(), Ui.Descendants<AuthEditor>(view).Count(), Ui.Descendants<ResponseView>(view).Count());
            main.Workflow!.Steps.Single().Section = StepSection.Auth;
            await Ui.IdleAsync();
            var stepAuth = Ui.Descendants<AuthEditor>(view).Single(editor => editor.DataContext == main.Workflow.Steps.Single().Auth);
            var workflowAuth = Ui.Descendants<AuthEditor>(view).Single(editor => editor.DataContext == main.Workflow.Auth);
            var inherit = (Ui.Descendants<RadioButton>(stepAuth).Single(button => Equals(button.Content, harness.Translator.Of("Workflow.AuthInherit"))).Visibility,
                workflowAuth.CanInherit ? Visibility.Visible : Visibility.Collapsed);
            var tabs = Ui.Named<ListBox>(window, "RequestTabs").IsVisible;
            Ui.Select(Ui.Named<RadioButton>(window, "CollectionsSection"));
            await Ui.IdleAsync();

            // Assert
            Assert.Contains(harness.Translator.Of("Sidebar.NewWorkflow"), buttons);
            Assert.DoesNotContain(harness.Translator.Of("Sidebar.NewFolder"), buttons);
            Assert.True(shown);
            Assert.Equal((1, 1, 2, 1), editors);
            Assert.Equal((Visibility.Visible, Visibility.Collapsed), inherit);
            Assert.False(tabs);
            Assert.Empty(Ui.Descendants<WorkflowView>(window));
            Assert.Single(Ui.Descendants<RequestEditorView>(window));
            Assert.True(Ui.Named<ListBox>(window, "RequestTabs").IsVisible);
        });
    }

    [Fact]
    public async Task RemoveStep_WhenTheChosenStepIsRemovedInTheEditor_ThenChoosesTheNextStep()
    {
        // Arrange
        using var harness = new Harness();
        WorkflowStep ping = new() { Request = new() { Url = "https://dev.local" } };
        await harness.WorkflowLibrary.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Steps = [ping, ping, ping] }, Cancellation);
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
    public async Task AuthEditors_WhenTheWorkflowAndAStepAreShownTogether_ThenEachShowsItsOwnChoice()
    {
        // Arrange
        using var harness = new Harness();
        await harness.WorkflowLibrary.SaveAsync("Flow", new()
        {
            Id = Guid.NewGuid(),
            Auth = new(AuthKind.Bearer),
            Steps = [new() { Request = new() { Url = "https://dev.local", Auth = new(AuthKind.Inherit) } }],
        }, Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            await main.OpenWorkflowAsync("Flow");
            var window = await Ui.ShowAsync(harness, main);

            // Act
            Ui.Select(Ui.Named<RadioButton>(window, "WorkflowAuthChoice"));
            main.Workflow!.Steps.Single().Section = StepSection.Auth;
            await Ui.IdleAsync();

            // Assert
            var view = Ui.Descendants<WorkflowView>(window).Single();
            var workflowAuth = Ui.Descendants<AuthEditor>(view).Single(editor => editor.DataContext == main.Workflow.Auth);
            var stepAuth = Ui.Descendants<AuthEditor>(view).Single(editor => editor.DataContext == main.Workflow.Steps.Single().Auth);
            Assert.Equal((true, true), (Ui.Descendants<RadioButton>(workflowAuth).Single(button => Equals(button.Content, harness.Translator.Of("Auth.Bearer"))).IsChecked,
                Ui.Descendants<RadioButton>(stepAuth).Single(button => Equals(button.Content, harness.Translator.Of("Workflow.AuthInherit"))).IsChecked));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StepTabs_WhenAStepWaits_ThenShowNoBodyAndOnlySavesAFileGaveIt(bool saves)
    {
        // Arrange
        using var harness = new Harness();
        await harness.WorkflowLibrary.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Variables = [new("x")], Steps = [new() { DelaySeconds = 5, Saves = saves ? [new("x", "$.id")] : [] }] }, Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();

            // Act
            await main.OpenWorkflowAsync("Flow");
            var window = await Ui.ShowAsync(harness, main);

            // Assert
            var view = Ui.Descendants<WorkflowView>(window).Single();
            Assert.Equal((false, saves), (Ui.Descendants<RequestBodyEditor>(view).Single().IsVisible, Ui.Descendants<KeyValueEditor>(view).Any(editor => editor.DataContext == main.Workflow!.Steps.Single().Saves && editor.IsVisible)));
        });
    }

    [Fact]
    public async Task StepTabs_WhenATabIsChosen_ThenOnlyItsContentIsShownAndScrollsByItself()
    {
        // Arrange
        using var harness = new Harness();
        var headers = Enumerable.Range(0, 40).Select(index => new KeyValue($"name{index}")).ToList();
        await harness.WorkflowLibrary.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Request = new() { Url = "https://dev.local", Headers = headers } }] }, Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            await main.OpenWorkflowAsync("Flow");
            var window = await Ui.ShowAsync(harness, main);
            var step = main.Workflow!.Steps.Single();

            // Act
            step.Section = StepSection.Headers;
            await Ui.IdleAsync();

            // Assert
            var view = Ui.Descendants<WorkflowView>(window).Single();
            var table = Ui.Descendants<KeyValueEditor>(view).Single(editor => editor.DataContext == step.Request!.Headers);
            Assert.Equal((true, false, true), (table.IsVisible, Ui.Descendants<RequestBodyEditor>(view).Single().IsVisible, Ui.Descendants<ScrollViewer>(table).First().ScrollableHeight > 0));
        });
    }

    [Fact]
    public async Task ScriptTab_WhenTheWheelTurnsOverALongScript_ThenScrollsTheScript()
    {
        // Arrange
        using var harness = new Harness();
        Directory.CreateDirectory(System.IO.Path.Combine(harness.Folder.Workflows, "Flow"));
        await File.WriteAllTextAsync(System.IO.Path.Combine(harness.Folder.Workflows, "Flow", "map.js"), string.Join("\n", Enumerable.Range(0, 200).Select(line => $"// {line}")), Cancellation);
        await harness.WorkflowLibrary.SaveAsync("Flow", new() { Id = Guid.NewGuid(), Steps = [new() { Script = "map.js" }] }, Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            await main.OpenWorkflowAsync("Flow");
            var window = await Ui.ShowAsync(harness, main);
            var editor = Ui.Descendants<Hoboman.Controls.ScriptEditor>(window).Single();
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };

            // Act
            editor.TextArea.RaiseEvent(wheel);
            wheel.RoutedEvent = UIElement.MouseWheelEvent;
            editor.TextArea.RaiseEvent(wheel);
            await Ui.IdleAsync();

            // Assert
            Assert.True(editor.VerticalOffset > 0);
        });
    }
}
