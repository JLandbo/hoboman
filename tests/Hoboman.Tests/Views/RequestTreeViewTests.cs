using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using Hoboman.Tests.Requests;
using Hoboman.Tests.ViewModels;

namespace Hoboman.Tests.Views;

[Collection("Ui")]
public sealed class RequestTreeViewTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static RequestNodeViewModel Node(MainViewModel main, string path) => main.Tree.NodeAt(path);

    [Fact]
    public async Task Find_WhenExecutedInHistory_ThenShowsTheCollectionsAndFocusesTheSearch()
    {
        // Arrange
        using var harness = new Harness();
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            main.Section = SidebarSection.History;
            var window = await Ui.ShowAsync(harness, main);

            // Act
            ApplicationCommands.Find.Execute(null, window);
            await Ui.IdleAsync();

            // Assert
            Assert.Equal(SidebarSection.Collections, main.Section);
            Assert.Same(Ui.Named<TextBox>(window, "SearchBox"), Keyboard.FocusedElement);
        });
    }

    [Fact]
    public async Task SearchBox_WhenEscapeIsPressed_ThenClearsTheSearch()
    {
        // Arrange
        using var harness = new Harness();
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            var window = await Ui.ShowAsync(harness, main);
            main.Tree.Search = "users";

            // Act
            Ui.Key(Ui.Named<TextBox>(window, "SearchBox"), Key.Escape);

            // Assert
            Assert.Equal("", main.Tree.Search);
        });
    }

    [Fact]
    public async Task Tree_WhenSearching_ThenCollapsesTheRowsThatDoNotMatch()
    {
        // Arrange
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Users/Get", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAtAsync("Users/Delete", ApiRequest.New(), Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            var window = await Ui.ShowAsync(harness, main);

            // Act
            main.Tree.Search = "get";
            await Ui.IdleAsync();

            // Assert
            Assert.Equal((Visibility.Visible, Visibility.Collapsed), (Ui.Item(window, Node(main, "Users/Get")).Visibility, Ui.Item(window, Node(main, "Users/Delete")).Visibility));
        });
    }

    [Fact]
    public async Task Close_WhenWholeFoldersWereTurnedOff_ThenRemembersIt()
    {
        // Arrange
        using var harness = new Harness();
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            var window = await Ui.ShowAsync(harness, main);
            main.Tree.ShowWholeFolders = false;

            // Act
            window.Close();

            // Assert
            Assert.False((await harness.SettingsStore.LoadAsync(Cancellation)).SearchWholeFolders);
        });
    }

    [Theory]
    [InlineData("rename")]
    [InlineData("move")]
    [InlineData("folder")]
    [InlineData("external")]
    public async Task ActiveRow_WhenItsSavedRequestMoves_ThenMarksTheNewRow(string change)
    {
        var original = change == "rename" ? "People/Old" : "Users/Get";
        using var harness = new Harness(new FakeDialogs(answer: change == "folder" ? "People" : "Get"));
        await harness.Library.SaveAtAsync(original, ApiRequest.New(), Cancellation);
        await harness.Library.FolderAtAsync("People", Cancellation);
        if (change == "folder")
        {
            await harness.Library.DeleteFolderAtAsync("People", Cancellation);
        }
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            await main.OpenAsync(Node(main, original));
            var window = await Ui.ShowAsync(harness, main);

            switch (change)
            {
                case "rename": await main.RenameAsync(Node(main, original)); break;
                case "move": await main.MoveAsync(Node(main, "Users/Get"), Node(main, "People")); break;
                case "folder": await main.RenameFolderAsync(Node(main, "Users")); break;
                case "external":
                    await harness.Library.RenameAtAsync("Users/Get", "People/Get", Cancellation);
                    await main.RequestsChangedAsync();
                    break;
            }
            Node(main, "People").IsExpanded = true;
            await Ui.IdleAsync();

            var row = Ui.Row(Ui.Item(window, Node(main, "People/Get")));
            Assert.Equal("Get", Ui.Named<TextBlock>(row, "Label").Text);
            Assert.Same(window.FindResource("Edge"), row.Background);
            Assert.Single(Ui.Descendants<TreeViewItem>(window), item => ReferenceEquals(Ui.Row(item).Background, window.FindResource("Edge")));
        });
    }

    [Theory]
    [InlineData("history")]
    [InlineData("unlink")]
    [InlineData("deleted draft")]
    public async Task ActiveRow_WhenTheTabHasNoCollectionRow_ThenNothingIsMarked(string change)
    {
        using var harness = new Harness(new FakeDialogs(accept: true));
        await harness.Library.SaveAtAsync("Users/Get", ApiRequest.New(), Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            await main.OpenAsync(Node(main, "Users/Get"));
            var saved = main.SelectedTab!;
            var window = await Ui.ShowAsync(harness, main);
            if (change == "history")
            {
                await saved.SendAsync();
                await main.HistoryChangedAsync();
                await main.OpenAsync(Assert.Single(main.History.Items));
                main.SelectedTab!.Editor.Body = "history edit";
            }
            else if (change == "unlink")
            {
                saved.Unlink();
            }
            else
            {
                await main.NewDraftAsync(Node(main, "Users"));
                await main.DeleteFolderAsync(Node(main, "Users"));
            }
            await Ui.IdleAsync();

            Assert.DoesNotContain(Ui.Descendants<TreeViewItem>(window), item => ReferenceEquals(Ui.Row(item).Background, window.FindResource("Edge")));
            if (change == "history")
            {
                Assert.Equal(Visibility.Collapsed, Ui.Named<Ellipse>(Ui.Row(Ui.Item(window, Node(main, "Users/Get"))), "Unsaved").Visibility);
            }
        });
    }

    [Theory]
    [InlineData(false, "success")]
    [InlineData(false, "failure")]
    [InlineData(false, "cancel")]
    [InlineData(true, "success")]
    [InlineData(true, "failure")]
    [InlineData(true, "cancel")]
    public async Task DraftDot_WhenSendingOrFetchingTokens_ThenRemainsVisible(bool oauth, string outcome)
    {
        var response = new TaskCompletionSource<ApiResponse>();
        var token = new TaskCompletionSource<OAuthToken>();
        using var harness = new Harness(send: () => response.Task, oauth: new(cancellation => token.Task.WaitAsync(cancellation)));
        await harness.Library.FolderAtAsync("Users", Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            await main.NewDraftAsync(Node(main, "Users"));
            var draft = main.SelectedTab!;
            if (oauth)
            {
                draft.Auth.Kind = AuthKind.OAuth2;
            }
            var window = await Ui.ShowAsync(harness, main);
            var changing = oauth ? draft.Auth.FetchTokenAsync() : draft.SendAsync();
            if (outcome == "cancel")
            {
                if (oauth) draft.Auth.CancelFetch(); else draft.Cancel();
            }
            else if (oauth)
            {
                if (outcome == "failure") token.SetException(new InvalidOperationException("Rejected")); else token.SetResult(Auth.FakeOAuthClient.Token);
            }
            else
            {
                if (outcome == "failure") response.SetException(new InvalidOperationException("Rejected")); else response.SetResult(new(200, "OK", 0, 2, [], "{}"));
            }
            await changing;
            await main.RequestsChangedAsync();
            await Ui.IdleAsync();

            var row = Ui.Row(Ui.Item(window, Assert.Single(RequestTreeViewModel.Flatten(main.Tree.Nodes), node => node.IsDraft)));
            Assert.Equal(Visibility.Visible, Ui.Named<Ellipse>(row, "Unsaved").Visibility);
            var tabs = Ui.Descendants<ListBox>(window).Single(list => ReferenceEquals(list.ItemsSource, main.Tabs));
            Assert.Equal(Visibility.Visible, Ui.Descendants<Ellipse>((ListBoxItem)tabs.ItemContainerGenerator.ContainerFromItem(draft)).Single().Visibility);
            Assert.Empty(await harness.Library.PathsAsync(Cancellation));
        });
    }

    [Fact]
    public async Task ActiveRow_WhenTheClickedFileIsMissing_ThenKeepsThePreviousHighlight()
    {
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Users/Get", ApiRequest.New(), Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            await main.NewDraftAsync(Node(main, "Users"));
            var draft = main.SelectedTab;
            var window = await Ui.ShowAsync(harness, main);
            await harness.Library.DeleteAtAsync("Users/Get", Cancellation);

            Ui.Press(Ui.Named<TextBlock>(Ui.Row(Ui.Item(window, Node(main, "Users/Get"))), "Label"));
            await Ui.UntilAsync(() => !RequestTreeViewModel.Flatten(main.Tree.Nodes).Any(node => main.Tree.PathOf(node) == "Users/Get"));

            var row = Ui.Row(Ui.Item(window, Assert.Single(RequestTreeViewModel.Flatten(main.Tree.Nodes), node => node.IsDraft)));
            Assert.Same(draft, main.SelectedTab);
            Assert.Same(window.FindResource("Edge"), row.Background);
        });
    }

    [Fact]
    public async Task HoverStyles_WhenTheRowIsActive_ThenKeepItsHighlightAndTheDotColour()
    {
        using var harness = new Harness();
        await harness.Library.FolderAtAsync("Users", Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            await main.NewDraftAsync(Node(main, "Users"));
            var window = await Ui.ShowAsync(harness, main);
            var item = Ui.Item(window, Assert.Single(RequestTreeViewModel.Flatten(main.Tree.Nodes), node => node.IsDraft));
            var triggers = item.Template.Triggers.Cast<TriggerBase>().ToList();
            var hover = Assert.Single(triggers.OfType<Trigger>(), trigger => trigger.Property == UIElement.IsMouseOverProperty);
            var active = Assert.Single(triggers.OfType<DataTrigger>(), trigger => trigger.Binding is System.Windows.Data.Binding { Path.Path: nameof(RequestNodeViewModel.IsActive) });
            Assert.True(triggers.IndexOf(active) > triggers.IndexOf(hover));
            var setter = Assert.IsType<Setter>(Assert.Single(hover.Setters));
            Assert.Equal("Row", setter.TargetName);
            Assert.Equal(Border.BackgroundProperty, setter.Property);
            var style = (Style)window.FindResource("PillButton");
            var template = Assert.IsType<ControlTemplate>(style.Setters.OfType<Setter>().Single(setting => setting.Property == Control.TemplateProperty).Value);
            var buttonHover = Assert.Single(template.Triggers.OfType<Trigger>(), trigger => trigger.Property == UIElement.IsMouseOverProperty);
            Assert.All(buttonHover.Setters.OfType<Setter>(), setting => Assert.Equal(UIElement.OpacityProperty, setting.Property));
            Assert.Same(window.FindResource("Attention"), Ui.Named<Ellipse>(Ui.Row(item), "Unsaved").Fill);
        });
    }

    [Fact]
    public async Task FolderRows_WhenNestedAndRelabelled_ThenKeepTheirIconsVisibleAndAligned()
    {
        using var harness = new Harness();
        await harness.Library.SaveAtAsync($"Users/Admin/{new string('W', 80)}/Get", ApiRequest.New(), Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            foreach (var node in RequestTreeViewModel.Flatten(main.Tree.Nodes).Where(node => node.IsFolder))
            {
                node.IsExpanded = true;
            }
            var window = await Ui.ShowAsync(harness, main);
            foreach (var translation in new[] { Translation.English, Translation.Danish })
            {
                harness.Translator.Use(translation);
                Ui.UseLanguage(translation);
                await main.LanguageChangedAsync();
                await Ui.IdleAsync();
                foreach (var node in RequestTreeViewModel.Flatten(main.Tree.Nodes).Where(node => node.IsFolder))
                {
                    var row = Ui.Row(Ui.Item(window, node));
                    var request = Ui.Named<Button>(row, "NewRequest");
                    var folder = Ui.Named<Button>(row, "NewFolder");
                    Assert.True(request.IsVisible);
                    Assert.True(folder.IsVisible);
                    Assert.Same(window.FindResource("SmallIconButton"), request.Style);
                    Assert.Same(window.FindResource("SmallIconButton"), folder.Style);
                    Assert.Same(window.FindResource("Muted"), request.Foreground);
                    Assert.Equal("\uE710", request.Content);
                    Assert.Equal("\uE8F4", folder.Content);
                    Assert.Equal(translation.Of("Sidebar.NewRequest"), request.ToolTip);
                    Assert.Equal(translation.Of("Sidebar.NewFolder"), folder.ToolTip);
                    Assert.Equal(request.ToolTip, AutomationProperties.GetName(request));
                    Assert.Equal(folder.ToolTip, AutomationProperties.GetName(folder));
                    Assert.True(Ui.Bounds(folder, row).Right <= Ui.Bounds(request, row).Left);
                    Assert.InRange(Ui.Bounds(request, row).Right, row.ActualWidth - 10, row.ActualWidth);
                    Assert.Equal(TextTrimming.CharacterEllipsis, Ui.Named<TextBlock>(row, "Label").TextTrimming);
                    Assert.InRange(Ui.Named<TextBlock>(row, "Label").ActualWidth, 1, 250);
                }
            }
            var file = RequestTreeViewModel.Flatten(main.Tree.Nodes).Single(node => !node.IsFolder);
            Assert.Equal(Visibility.Collapsed, Ui.Named<Button>(Ui.Row(Ui.Item(window, file)), "NewRequest").Visibility);
            Assert.Equal(Visibility.Collapsed, Ui.Named<Button>(Ui.Row(Ui.Item(window, file)), "NewFolder").Visibility);
        });
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task FolderButtons_WhenClickedAndDoubleClicked_ThenDoNotToggleTheFolder(bool expanded, bool subfolder, bool accept)
    {
        using var harness = new Harness(new FakeDialogs(answer: accept ? "Child" : null));
        await harness.Library.FolderAtAsync("Users", Cancellation);
        await harness.Library.FolderAtAsync("Other", Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            Node(main, "Users").IsExpanded = expanded;
            var window = await Ui.ShowAsync(harness, main);
            var button = Ui.Named<Button>(Ui.Row(Ui.Item(window, Node(main, "Users"))), subfolder ? "NewFolder" : "NewRequest");
            for (var click = 1; click <= 2; click++)
            {
                Ui.Click(button, click);
                if (subfolder && accept)
                {
                    await Ui.UntilAsync(() => RequestTreeViewModel.Flatten(main.Tree.Nodes).Any(node => main.Tree.PathOf(node) == "Users/Child"));
                }
                else
                {
                    await Ui.IdleAsync();
                }
                Assert.Equal(expanded || !subfolder || accept, Ui.Item(window, Node(main, "Users")).IsExpanded);
                Assert.False(Ui.Item(window, Node(main, "Other")).IsExpanded);
            }
            if (subfolder)
            {
                Assert.Equal(("", harness.Translator.Of("Folder.Create")), (harness.Dialogs.NameQuestion!.Value.Name, harness.Dialogs.NameQuestion.Value.Confirm));
            }
            else
            {
                Assert.Equal(0, harness.Dialogs.Asked);
                Assert.Equal(2, Node(main, "Users").Children.Count);
                Assert.All(Node(main, "Users").Children, node => Assert.True(Ui.Named<TextBlock>(Ui.Row(Ui.Item(window, node)), "Label").IsVisible));
            }
        });
    }

    [Fact]
    public async Task Rows_WhenTabsChange_ThenShowLiveMethodsTitlesAndUnsavedDots()
    {
        using var harness = new Harness(new FakeDialogs(accept: true));
        await harness.Library.SaveAtAsync("Users/Get", ApiRequest.New(), Cancellation);
        await harness.Library.SaveAtAsync($"Users/{new string('W', 70)}", ApiRequest.New(), Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            await main.OpenAsync(Node(main, "Users/Get"));
            var saved = main.SelectedTab!;
            await main.NewDraftAsync(Node(main, "Users"));
            var draft = main.SelectedTab!;
            var window = await Ui.ShowAsync(harness, main);
            saved.Editor.Method = draft.Editor.Method = "POST";
            await Ui.IdleAsync();
            var draftNode = Assert.Single(RequestTreeViewModel.Flatten(main.Tree.Nodes), node => node.IsDraft);
            var draftRow = Ui.Row(Ui.Item(window, draftNode));
            var savedRow = Ui.Row(Ui.Item(window, Node(main, "Users/Get")));
            var draftMethod = Ui.Named<TextBlock>(draftRow, "Method");
            var savedMethod = Ui.Named<TextBlock>(savedRow, "Method");
            Assert.Equal("POST", draftMethod.Text);
            Assert.Equal(savedMethod.Text, draftMethod.Text);
            Assert.Equal(savedMethod.Width, draftMethod.Width);
            Assert.Same(savedMethod.Foreground, draftMethod.Foreground);
            Assert.Equal(Ui.Bounds(savedMethod, savedRow).Left, Ui.Bounds(draftMethod, draftRow).Left);
            Assert.Equal(Visibility.Collapsed, Ui.Named<Button>(draftRow, "NewRequest").Visibility);
            foreach (var node in RequestTreeViewModel.Flatten(main.Tree.Nodes))
            {
                var dot = Ui.Named<Ellipse>(Ui.Row(Ui.Item(window, node)), "Unsaved");
                Assert.Equal(node.Tab?.IsUnsaved == true ? Visibility.Visible : Visibility.Collapsed, dot.Visibility);
                Assert.Equal((7d, 7d), (dot.Width, dot.Height));
                Assert.Same(window.FindResource("Attention"), dot.Fill);
            }
            harness.Translator.Use(Translation.Danish);
            Ui.UseLanguage(Translation.Danish);
            await main.LanguageChangedAsync();
            await Ui.IdleAsync();
            Assert.Equal(draft.Title, Ui.Named<TextBlock>(draftRow, "Label").Text);
            await draft.SendAsync();
            await main.RequestsChangedAsync();
            await Ui.IdleAsync();
            Assert.Equal(Visibility.Visible, Ui.Named<Ellipse>(Ui.Row(Ui.Item(window, Assert.Single(RequestTreeViewModel.Flatten(main.Tree.Nodes), node => node.IsDraft))), "Unsaved").Visibility);
            Assert.Equal("POST", Ui.Named<TextBlock>(Ui.Row(Ui.Item(window, Node(main, "Users/Get"))), "Method").Text);
            main.Close(saved);
            await Ui.IdleAsync();
            Assert.Equal("GET", Ui.Named<TextBlock>(Ui.Row(Ui.Item(window, Node(main, "Users/Get"))), "Method").Text);
            Assert.Equal(Visibility.Collapsed, Ui.Named<Ellipse>(Ui.Row(Ui.Item(window, Node(main, "Users/Get"))), "Unsaved").Visibility);
        });
    }

    [Fact]
    public async Task ActiveRow_WhenFocusAndTabsChange_ThenFollowsOnlyTheOpenRequest()
    {
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Users/New request (2)", ApiRequest.New(), Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            await main.NewDraftAsync(Node(main, "Users"));
            var draft = main.SelectedTab!;
            var window = await Ui.ShowAsync(harness, main);
            var tree = Ui.Descendants<TreeView>(window).Single();
            var draftNode = Assert.Single(RequestTreeViewModel.Flatten(main.Tree.Nodes), node => node.IsDraft);
            var file = Assert.Single(RequestTreeViewModel.Flatten(main.Tree.Nodes), node => !node.IsFolder && !node.IsDraft);
            var draftItem = Ui.Item(tree, draftNode);
            Assert.Same(window.FindResource("Edge"), Ui.Row(draftItem).Background);
            Ui.Press(Ui.Named<TextBlock>(Ui.Row(Ui.Item(tree, file)), "Label"));
            await Ui.UntilAsync(() => main.SelectedTab?.Name == file.Name);
            Assert.Same(window.FindResource("Edge"), Ui.Row(Ui.Item(tree, file)).Background);
            Assert.NotSame(window.FindResource("Edge"), Ui.Row(draftItem).Background);
            Ui.Press(Ui.Named<TextBlock>(Ui.Row(draftItem), "Label"));
            await Ui.IdleAsync();
            Assert.Same(draft, main.SelectedTab);
            Ui.Press(Ui.Named<TextBlock>(Ui.Row(draftItem), "Label"), 2);
            Ui.Key(draftItem, Key.Enter);
            await Ui.IdleAsync();
            Assert.Equal(3, main.Tabs.Count);
            Assert.Same(window.FindResource("Edge"), Ui.Row(draftItem).Background);
            Assert.Same(window.FindResource("Attention"), Ui.Row(draftItem).BorderBrush);
            var editor = Ui.Descendants<RequestEditorView>(window).Single();
            var url = Ui.Descendants<TextBox>(editor).First(box => box.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == nameof(RequestViewModel.Url));
            Keyboard.Focus(url);
            await Ui.IdleAsync();
            Assert.Same(window.FindResource("Edge"), Ui.Row(draftItem).Background);
            Ui.Key(Ui.Item(tree, Node(main, "Users")), Key.Down);
            await Ui.IdleAsync();
            Assert.Same(draft, main.SelectedTab);
            var dialog = new NameDialog("Title", "Name", "Save", _ => null);
            Ui.Show(dialog);
            await Ui.IdleAsync();
            Assert.Same(window.FindResource("Edge"), Ui.Row(draftItem).Background);
            dialog.Close();
            var tabs = Ui.Descendants<ListBox>(window).Single(list => ReferenceEquals(list.ItemsSource, main.Tabs));
            tabs.SelectedItem = main.Tabs[0];
            await Ui.IdleAsync();
            Assert.DoesNotContain(Ui.Descendants<TreeViewItem>(tree), item => ReferenceEquals(Ui.Row(item).Background, window.FindResource("Edge")));
            tabs.SelectedItem = draft;
            await Ui.IdleAsync();
            Assert.Same(window.FindResource("Edge"), Ui.Row(draftItem).Background);
        });
    }

    [Fact]
    public async Task Rows_WhenUsingContextMenuOrDragging_ThenAllowsFoldersAndDraftsWithoutDraftDeletion()
    {
        using var harness = new Harness();
        await harness.Library.SaveAtAsync("Users/Get", ApiRequest.New(), Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            await main.NewDraftAsync(Node(main, "Users"));
            var window = await Ui.ShowAsync(harness, main);
            var view = Ui.Descendants<RequestTreeView>(window).Single();
            bool? handled = null;
            window.AddHandler(ContextMenuService.ContextMenuOpeningEvent, new ContextMenuEventHandler((_, args) =>
            {
                handled = args.Handled;
                args.Handled = true;
            }), handledEventsToo: true);
            foreach (var node in RequestTreeViewModel.Flatten(main.Tree.Nodes).ToList())
            {
                Node(main, "Users").IsExpanded = true;
                await Ui.IdleAsync();
                var item = Ui.Item(view, node);
                handled = null;
                Ui.Key(item, Key.Apps);
                await Ui.IdleAsync();
                Assert.False(handled);
                if (node.IsDraft)
                {
                    Assert.DoesNotContain(item.ContextMenu.Items.OfType<MenuItem>(), menu => Equals(menu.Header, harness.Translator.Of("Tree.Delete")));
                }
                object? pressed = null;
                item.AddHandler(Mouse.MouseDownEvent, new MouseButtonEventHandler((_, _) => pressed = typeof(RequestTreeView).GetField("_pressed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)), handledEventsToo: true);
                Ui.Press(Ui.Named<TextBlock>(Ui.Row(item), "Label"));
                await Ui.IdleAsync();
                Assert.NotNull(pressed);
            }
        });
    }

    [Fact]
    public async Task Scroll_WhenSelectingSavingEditingAndReloading_ThenOnlyRevealsOnSelectionAndFirstSave()
    {
        using var harness = new Harness(new FakeDialogs(answer: "Saved"));
        for (var number = 0; number < 65; number++)
        {
            await harness.Library.SaveAtAsync($"A/Request {number:00}", ApiRequest.New(), Cancellation);
        }
        await harness.Library.FolderAtAsync("Z/Deep", Cancellation);
        await Ui.RunAsync(async () =>
        {
            var main = harness.Main();
            await main.LoadAsync();
            Node(main, "A").IsExpanded = true;
            var window = await Ui.ShowAsync(harness, main);
            var tree = Ui.Descendants<TreeView>(window).Single();
            var scroller = Ui.Scroller(tree);
            await main.OpenAsync(Node(main, "A/Request 64"));
            await Ui.IdleAsync();
            var selectedRow = Ui.Row(Ui.Item(tree, Node(main, "A/Request 64")));
            Assert.True(scroller.VerticalOffset > 0);
            Assert.InRange(Ui.Bounds(selectedRow, tree).Top, 0, tree.ActualHeight - selectedRow.ActualHeight);
            main.NewTab();
            var offset = scroller.VerticalOffset;
            await main.NewDraftAsync(Node(main, "Z/Deep"));
            var draft = main.SelectedTab!;
            await Ui.IdleAsync();
            Assert.True(Ui.Item(tree, Node(main, "Z")).IsExpanded);
            Assert.True(Ui.Item(tree, Node(main, "Z/Deep")).IsExpanded);
            Node(main, "Z").IsExpanded = false;
            scroller.ScrollToVerticalOffset(100);
            await Ui.IdleAsync();
            offset = scroller.VerticalOffset;
            draft.Editor.Method = "POST";
            draft.Editor.Url = "https://example.test";
            await draft.SendAsync();
            await main.RequestsChangedAsync();
            await Ui.IdleAsync();
            Assert.False(Ui.Item(tree, Node(main, "Z")).IsExpanded);
            Assert.Equal(offset, scroller.VerticalOffset, 1);
            await draft.SaveAsync();
            await Ui.IdleAsync();
            Assert.True(Ui.Item(tree, Node(main, "Z")).IsExpanded);
            selectedRow = Ui.Row(Ui.Item(tree, Node(main, "Z/Deep/Saved")));
            Assert.Same(window.FindResource("Edge"), selectedRow.Background);
            Assert.InRange(Ui.Bounds(selectedRow, tree).Top, 0, tree.ActualHeight - selectedRow.ActualHeight);
            scroller.ScrollToVerticalOffset(100);
            await Ui.IdleAsync();
            offset = scroller.VerticalOffset;
            Ui.Select((RadioButton)window.FindName("HistorySection"));
            main.SelectedTab = main.Tabs.First(tab => tab.Name == "Request 64");
            await Ui.IdleAsync();
            Ui.Select((RadioButton)window.FindName("CollectionsSection"));
            await Ui.IdleAsync();
            Assert.Equal(offset, scroller.VerticalOffset, 1);
        });
    }
}
