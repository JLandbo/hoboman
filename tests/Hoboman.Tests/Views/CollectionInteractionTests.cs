using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using Hoboman.Controls;
using Hoboman.Tests.ViewModels;

namespace Hoboman.Tests.Views;

[Collection("Ui")]
public sealed class CollectionInteractionTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static RequestNodeViewModel Node(MainViewModel main, string path) => RequestTreeViewModel.Flatten(main.Tree.Nodes).Single(node => node.Path == path);

    [Fact]
    public async Task NewRequest_WhenShown_ThenSelectsJsonBodyAndUsesMatchingSwitchesWithoutTheCount()
    {
        using var harness = new Harness();
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            var window = await Ui.ShowAsync(harness, main);
            var editor = Ui.Descendants<RequestEditorView>(window).Single();
            Assert.True(Ui.Descendants<RadioButton>(editor).Single(button => button.Name == "BodySection").IsChecked);
            Assert.True(Ui.Descendants<RadioButton>(editor).Single(button => Equals(button.Content, harness.Translator.Of("Body.Json"))).IsChecked);
            var variables = Ui.Descendants<ToggleButton>(editor).Single(button => Equals(button.Content, harness.Translator.Of("Body.UseEnvironmentVariables")));
            var base64 = Ui.Descendants<ToggleButton>(editor).Single(button => Equals(button.Content, harness.Translator.Of("Base64.WholeBody")));
            Assert.IsType<ToggleButton>(variables);
            Assert.Same(base64.Style, variables.Style);
            Assert.False(variables.IsChecked);
            variables.IsChecked = true;
            Assert.True(main.SelectedTab!.Editor.UseEnvironmentVariablesInBody);
            Assert.True(Ui.Bounds(variables, editor).Left < Ui.Bounds(base64, editor).Left);
            Assert.DoesNotContain(Ui.Descendants<TextBlock>(editor), text => text.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path.Path == "Base64.EncodeSummary");
        });
    }

    [Fact]
    public async Task CloseButtons_WhenShown_ThenUseLargerIconsAndClickAreasWithoutChangingOtherIcons()
    {
        using var harness = new Harness();
        await harness.History().AddAsync(new(DateTimeOffset.Now, HistorySource.App, "dev", ApiRequest.New()), Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            main.SelectedTab!.Editor.Headers.Rows[0].Name = "Header";
            main.SelectedTab.Editor.Query.Rows[0].Name = "Param";
            var window = await Ui.ShowAsync(harness, main);
            Ui.Select((RadioButton)window.FindName("HistorySection"));
            main.SelectedTab.RequestSection = RequestSection.Params;
            await Ui.IdleAsync();
            main.SelectedTab.RequestSection = RequestSection.Headers;
            await Ui.IdleAsync();
            var buttons = Ui.Descendants<Button>(window).Where(button => Equals(button.Content, "\uE711")).ToList();
            Assert.True(buttons.Count >= 4);
            Assert.All(buttons, button =>
            {
                Assert.Equal(13, button.FontSize);
                Assert.True(button.MinWidth >= 26);
                Assert.True(button.MinHeight >= 26);
            });
            Assert.DoesNotContain(Ui.Descendants<Button>(window).Where(button => Equals(button.Content, "\uE710")), button => ReferenceEquals(button.Style, window.FindResource("CloseButton")));
        });
    }

    [Fact]
    public async Task Tab_WhenDoubleClicked_ThenRenamesTheRequestWithoutSavingItsBody()
    {
        using var harness = new Harness(new FakeDialogs(answer: "Renamed", accept: true));
        await harness.Library.SaveAsync("Original", ApiRequest.New(), Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            await main.OpenAsync(Node(main, "Original"));
            main.SelectedTab!.Editor.Body = "unsaved";
            var window = await Ui.ShowAsync(harness, main);
            var tabs = (ListBox)window.FindName("RequestTabs");
            var item = (ListBoxItem)tabs.ItemContainerGenerator.ContainerFromItem(main.SelectedTab);
            Ui.Press(Ui.Descendants<TextBlock>(item).Single(text => text.Text == "Original"), 2);
            await Ui.UntilAsync(() => !main.IsChangingCollection && main.SelectedTab!.Name == "Renamed" && RequestTreeViewModel.Flatten(main.Tree.Nodes).Any(node => node.Path == "Renamed" && node.Tab is not null));
            Assert.Equal("unsaved", main.SelectedTab!.Editor.Body);
            Assert.Equal("", (await harness.Library.LoadAsync("Renamed", Cancellation))!.Body);
        });
    }

    [Fact]
    public async Task TreeDrag_WhenOverFolderBetweenRowsOrInvalidTarget_ThenShowsTheCorrectMarkerAndMovesTheFolder()
    {
        using var harness = new Harness();
        await harness.Library.SaveAsync("Folder/Child", ApiRequest.New(), Cancellation);
        await harness.Library.CreateFolderAsync("Target", Cancellation);
        await harness.Library.SaveAsync("Request", ApiRequest.New(), Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            var window = await Ui.ShowAsync(harness, main);
            var view = Ui.Descendants<RequestTreeView>(window).Single();
            var tree = Ui.Descendants<TreeView>(view).Single();
            var source = Node(main, "Folder");
            var bounds = Ui.Bounds(Ui.Row(Ui.Item(tree, Node(main, "Target"))), tree);
            var middle = new Point(bounds.Left + 30, bounds.Top + bounds.Height / 2);
            Assert.Equal(DragDropEffects.Move, Ui.Drag(tree, source, middle, DragDrop.DragOverEvent).Effects);
            var marker = AdornerLayer.GetAdornerLayer(view).GetAdorners(view).OfType<DropIndicator>().Single();
            Assert.True(marker.IsBox);
            Assert.Equal(Visibility.Visible, marker.Visibility);
            var request = Ui.Bounds(Ui.Row(Ui.Item(tree, Node(main, "Request"))), tree);
            Ui.Drag(tree, source, new Point(request.Left + 30, request.Top + 1), DragDrop.DragOverEvent);
            Assert.False(marker.IsBox);
            Assert.Equal(0, marker.Bounds.Height);
            Assert.True(marker.Bounds.Width > 100);
            var own = Ui.Bounds(Ui.Row(Ui.Item(tree, source)), tree);
            Assert.Equal(DragDropEffects.None, Ui.Drag(tree, source, new Point(own.Left + 30, own.Top + own.Height / 2), DragDrop.DragOverEvent).Effects);
            Assert.Equal(Visibility.Collapsed, marker.Visibility);
            Ui.Drag(tree, source, middle, DragDrop.DragOverEvent);
            Ui.Drag(tree, source, middle, DragDrop.DragLeaveEvent);
            await Ui.IdleAsync();
            Assert.Equal(Visibility.Collapsed, marker.Visibility);
            Ui.Drag(tree, source, middle, DragDrop.DropEvent);
            await Ui.UntilAsync(() => !main.IsChangingCollection && RequestTreeViewModel.Flatten(main.Tree.Nodes).Any(node => node.Path == "Target/Folder/Child"));
            Assert.False(harness.Library.FolderExists("Folder"));
            Assert.Equal(Visibility.Collapsed, marker.Visibility);
        });
    }

    [Fact]
    public async Task TabDrag_WhenDroppedBeforeAnotherTab_ThenShowsVerticalLineAndPreservesTheActiveTab()
    {
        using var harness = new Harness();
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            main.NewTab();
            main.NewTab();
            var source = main.SelectedTab!;
            var first = main.Tabs[0];
            var window = await Ui.ShowAsync(harness, main);
            var tabs = (ListBox)window.FindName("RequestTabs");
            var bounds = Ui.Bounds((ListBoxItem)tabs.ItemContainerGenerator.ContainerFromItem(first), tabs);
            var point = new Point(bounds.Left + 3, bounds.Top + bounds.Height / 2);
            Assert.Equal(DragDropEffects.Move, Ui.Drag(tabs, source, point, DragDrop.DragOverEvent).Effects);
            var marker = AdornerLayer.GetAdornerLayer(tabs).GetAdorners(tabs).OfType<DropIndicator>().Single();
            Assert.False(marker.IsBox);
            Assert.Equal(0, marker.Bounds.Width);
            Assert.True(marker.Bounds.Height > 10);
            Ui.Drag(tabs, source, point, DragDrop.DropEvent);
            await Ui.IdleAsync();
            Assert.Same(source, main.Tabs[0]);
            Assert.Same(source, main.SelectedTab);
            Assert.Equal(Visibility.Collapsed, marker.Visibility);
        });
    }

    [Fact]
    public async Task Window_WhenClosed_ThenSavesOnlyRealRequestTabsInTheirCurrentOrder()
    {
        using var harness = new Harness(new FakeDialogs(accept: true));
        var a = ApiRequest.New();
        var b = ApiRequest.New();
        await harness.Library.SaveAsync("A", a, Cancellation);
        await harness.Library.SaveAsync("B", b, Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            await main.OpenAsync(Node(main, "A"));
            await main.OpenAsync(Node(main, "B"));
            main.MoveTab(main.SelectedTab!, main.Tabs.Single(tab => tab.Name == "A"), false);
            var active = main.SelectedTab!;
            main.NewTab();
            main.SelectedTab!.Editor.Body = "discard this";
            main.SelectedTab = active;
            var window = await Ui.ShowAsync(harness, main);
            window.Close();
            var session = (await harness.SettingsStore.LoadAsync(Cancellation)).Session!;
            Assert.Equal([$"{b.Id}", $"{a.Id}"], session.Requests);
            Assert.Equal($"{b.Id}", session.Selected);
        });
    }

    [Fact]
    public async Task TreeDrag_WhenHoveringThenDroppingAtRoot_ThenOpensTheFolderAndMovesTheRequestOut()
    {
        using var harness = new Harness();
        await harness.Library.SaveAsync("Folder/Request", ApiRequest.New(), Cancellation);
        await harness.Library.CreateFolderAsync("Target", Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            var window = await Ui.ShowAsync(harness, main);
            var view = Ui.Descendants<RequestTreeView>(window).Single();
            var tree = Ui.Descendants<TreeView>(view).Single();
            var source = Node(main, "Folder/Request");
            var bounds = Ui.Bounds(Ui.Row(Ui.Item(tree, Node(main, "Target"))), tree);
            Ui.Drag(tree, source, new Point(bounds.Left + 30, bounds.Top + bounds.Height / 2), DragDrop.DragOverEvent);
            await Ui.UntilAsync(() => Node(main, "Target").IsExpanded);
            var root = (Border)view.FindName("RootDropTarget");
            root.Visibility = Visibility.Visible;
            await Ui.IdleAsync();
            var point = new Point(root.ActualWidth / 2, root.ActualHeight / 2);
            Ui.Drag(root, source, point, DragDrop.DragOverEvent);
            var marker = AdornerLayer.GetAdornerLayer(view).GetAdorners(view).OfType<DropIndicator>().Single();
            Assert.True(marker.IsBox);
            Ui.Drag(root, source, point, DragDrop.DropEvent);
            await Ui.UntilAsync(() => !main.IsChangingCollection && main.Tree.Nodes.Any(node => node.Path == "Request"));
            Assert.True(harness.Library.Exists("Request"));
            Assert.False(harness.Library.Exists("Folder/Request"));
        });
    }

    [Fact]
    public async Task ContextMenu_WhenCloneIsClicked_ThenOpensANewIndependentRequest()
    {
        using var harness = new Harness();
        await harness.Library.SaveAsync("Original", ApiRequest.New(), Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            var window = await Ui.ShowAsync(harness, main);
            var item = Ui.Item(window, Node(main, "Original"));
            item.ContextMenu.PlacementTarget = item;
            item.ContextMenu.IsOpen = true;
            await Ui.IdleAsync();
            var clone = item.ContextMenu.Items.OfType<MenuItem>().Single(menu => Equals(menu.Header, harness.Translator.Of("Tree.Clone")));
            clone.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            item.ContextMenu.IsOpen = false;
            await Ui.UntilAsync(() => !main.IsChangingCollection && main.SelectedTab!.Name == "Original (1)");
            Assert.NotEqual(main.SelectedTab!.Id, (await harness.Library.LoadAsync("Original", Cancellation))!.Id);
        });
    }
}
