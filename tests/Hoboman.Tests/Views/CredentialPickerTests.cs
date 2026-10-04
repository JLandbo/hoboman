using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Hoboman.Tests.ViewModels;

namespace Hoboman.Tests.Views;

[Collection("Ui")]
public sealed class CredentialPickerTests
{
    [Fact]
    public async Task AuthEditor_WhenTypingLeavesOneAndEnterIsPressed_ThenItIsFilledIn()
    {
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        var auth = harness.Tab().Auth;
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var editor = await ShowAsync(auth, 900);

            // Act
            Search(editor).Text = "adm";
            Press(Search(editor), Key.Enter);
            await Ui.UntilAsync(() => auth.Kind == AuthKind.Basic);

            // Assert
            Assert.Equal(("admin", "admin-password"), (auth.UserName, auth.Password));
        });
    }

    [Fact]
    public async Task AuthEditor_WhenDownIsPressedTwiceAndThenEnter_ThenTheSecondIsFilledIn()
    {
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        var auth = harness.Tab().Auth;
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var editor = await ShowAsync(auth, 900);

            // Act
            Press(Search(editor), Key.Down);
            Press(Search(editor), Key.Down);
            Press(Search(editor), Key.Enter);
            await Ui.UntilAsync(() => auth.Kind == AuthKind.Basic);

            // Assert
            Assert.Equal("Admin", auth.CredentialName);
        });
    }

    [Fact]
    public async Task AuthEditor_WhenACredentialInTheListIsClicked_ThenItIsFilledIn()
    {
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        var auth = harness.Tab().Auth;
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var editor = await ShowAsync(auth, 900);
            Press(Search(editor), Key.Down);
            await Ui.IdleAsync();
            var list = (ListBox)editor.FindName("CredentialList");

            var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1);

            // Act
            Raise(item, Mouse.PreviewMouseDownEvent);
            // A list closed by the press can no longer get the release, so the release only reaches it while it is open.
            var open = ((Popup)editor.FindName("CredentialPopup")).IsOpen;
            Raise(item, Mouse.PreviewMouseUpEvent);
            if (open)
            {
                Raise(item, Mouse.MouseUpEvent);
            }
            await Ui.IdleAsync();

            // Assert
            Assert.Equal((AuthKind.Basic, "admin"), (auth.Kind, auth.UserName));
        });
    }

    [Fact]
    public async Task AuthEditor_WhenEditCredentialsIsClicked_ThenTheCredentialsAreShown()
    {
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var editor = await ShowAsync(harness.Tab().Auth, 900);
            Press(Search(editor), Key.Down);
            await Ui.IdleAsync();
            var popup = (Popup)editor.FindName("CredentialPopup");
            var edit = Ui.Descendants<Button>(popup.Child).Single();

            // Act
            Raise(edit, Mouse.PreviewMouseDownEvent);
            if (popup.IsOpen)
            {
                Ui.Click(edit);
                await Ui.UntilAsync(() => harness.Dialogs.Shown is not null);
            }

            // Assert
            Assert.Same(harness.CredentialEditor, harness.Dialogs.Shown);
        });
    }

    [Fact]
    public async Task AuthEditor_WhenEscapeIsPressed_ThenTheListCloses()
    {
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var editor = await ShowAsync(harness.Tab().Auth, 900);
            Press(Search(editor), Key.Down);

            // Act
            Press(Search(editor), Key.Escape);

            // Assert
            Assert.False(((Popup)editor.FindName("CredentialPopup")).IsOpen);
        });
    }

    [Fact]
    public async Task AuthEditor_WhenEnterIsPressedWithoutAMatch_ThenItGoesNoFurther()
    {
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var editor = await ShowAsync(harness.Tab().Auth, 900);
            Search(editor).Text = "ingen som denne";

            // Act
            var handled = Press(Search(editor), Key.Enter);

            // Assert
            Assert.True(handled);
        });
    }

    [Fact]
    public async Task AuthEditor_WhenPicked_ThenTheFieldShowsWhich()
    {
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        var auth = harness.Tab().Auth;
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var editor = await ShowAsync(auth, 900);

            // Act
            Search(editor).Text = "adm";
            Press(Search(editor), Key.Enter);
            await Ui.UntilAsync(() => auth.Kind == AuthKind.Basic);

            // Assert
            var shown = (TextBlock)editor.FindName("CredentialShown");
            Assert.Equal(("Admin", true), (shown.Text, shown.IsVisible));
        });
    }

    [Fact]
    public async Task AuthEditor_WhenNoEnvironmentIsChosen_ThenThereIsNothingToPick()
    {
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        await harness.Environments.ChooseAsync(null);
        harness.Credentials.EnvironmentChosen();
        await Ui.RunAsync(async () =>
        {
            // Act
            var editor = await ShowAsync(harness.Tab().Auth, 900);

            // Assert
            Assert.False(Search(editor).IsVisible);
        });
    }

    [Theory]
    [InlineData(900, 0)]
    [InlineData(480, 1)]
    public async Task AuthEditor_WhenItHasThisWidth_ThenThePickerIsBesideOrBelowTheKinds(double width, int row)
    {
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        await Ui.RunAsync(async () =>
        {
            // Act
            var editor = await ShowAsync(harness.Tab().Auth, width);

            // Assert
            Assert.Equal(row, Grid.GetRow((FrameworkElement)editor.FindName("CredentialPicker")));
        });
    }

    [Fact]
    public async Task AuthEditor_WhenTheFieldIsPressed_ThenTheListOpensWhenItIsLetGo()
    {
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var editor = await ShowAsync(harness.Tab().Auth, 900);
            var field = (UIElement)editor.FindName("CredentialField");
            var popup = (Popup)editor.FindName("CredentialPopup");

            // Act
            Raise(field, Mouse.PreviewMouseDownEvent);
            var openWhenPressed = popup.IsOpen;
            Raise(field, Mouse.PreviewMouseUpEvent);
            await Ui.IdleAsync();

            // Assert
            Assert.Equal((false, true), (openWhenPressed, popup.IsOpen));
        });
    }

    [Fact]
    public async Task AuthEditor_WhenClickedOutsideTheList_ThenItClosesAndTheFieldLetsGoOfTheKeyboard()
    {
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var editor = await ShowAsync(harness.Tab().Auth, 900);
            Search(editor).Focus();
            Press(Search(editor), Key.Down);

            // Act
            Raise((UIElement)editor.FindName("Kinds"), Mouse.PreviewMouseDownEvent);
            await Ui.IdleAsync();

            // Assert
            Assert.Equal((false, false), (((Popup)editor.FindName("CredentialPopup")).IsOpen, Search(editor).IsKeyboardFocusWithin));
        });
    }

    [Fact]
    public async Task AuthEditor_WhenTheFieldHasTheKeyboardWithoutTheListAndAClickIsOutside_ThenItLetsGo()
    {
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var editor = await ShowAsync(harness.Tab().Auth, 900);
            Search(editor).Focus();

            // Act
            Raise((UIElement)editor.FindName("Kinds"), Mouse.PreviewMouseDownEvent);

            // Assert
            Assert.False(Search(editor).IsKeyboardFocusWithin);
        });
    }

    [Fact]
    public async Task AuthEditor_WhenTheListIsOpen_ThenItDoesNotHoldTheMouseSoAClickOutsideIsNotLost()
    {
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var editor = await ShowAsync(harness.Tab().Auth, 900);

            // Act
            Press(Search(editor), Key.Down);
            await Ui.IdleAsync();

            // Assert
            Assert.Null(Mouse.Captured);
        });
    }

    [Fact]
    public async Task AuthEditor_WhenPicked_ThenTheFieldLetsGoOfTheKeyboard()
    {
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var editor = await ShowAsync(harness.Tab().Auth, 900);
            Search(editor).Focus();
            Search(editor).Text = "adm";

            // Act
            Press(Search(editor), Key.Enter);
            await Ui.IdleAsync();

            // Assert
            Assert.False(Search(editor).IsKeyboardFocusWithin);
        });
    }

    [Fact]
    public async Task AuthEditor_WhenTheKeyboardMovesOn_ThenTheListCloses()
    {
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var editor = await ShowAsync(harness.Tab().Auth, 900);
            Press(Search(editor), Key.Down);

            // Act
            Search(editor).RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, Search(editor), null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });

            // Assert
            Assert.False(((Popup)editor.FindName("CredentialPopup")).IsOpen);
        });
    }

    [Fact]
    public async Task AuthEditor_WhenTheWindowIsLeft_ThenTheListCloses()
    {
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var editor = await ShowAsync(harness.Tab().Auth, 900);
            Press(Search(editor), Key.Down);

            // Act
            typeof(Window).GetMethod("OnDeactivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(Window.GetWindow(editor), [EventArgs.Empty]);

            // Assert
            Assert.False(((Popup)editor.FindName("CredentialPopup")).IsOpen);
        });
    }

    static async Task<AuthEditor> ShowAsync(AuthViewModel auth, double width)
    {
        var editor = new AuthEditor { DataContext = auth, Width = width };
        Ui.Show(new Window { Content = editor, SizeToContent = SizeToContent.WidthAndHeight });
        await Ui.IdleAsync();
        return editor;
    }

    static TextBox Search(AuthEditor editor) => (TextBox)editor.FindName("CredentialSearch");

    static void Raise(UIElement element, RoutedEvent routedEvent) =>
        element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = routedEvent });

    static bool Press(UIElement element, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(element), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        element.RaiseEvent(args);
        return args.Handled;
    }
}
