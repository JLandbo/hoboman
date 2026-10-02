using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hoboman.Tests.ViewModels;

namespace Hoboman.Tests.Views;

[Collection("Ui")]
public sealed class NameDialogTests
{
    [Theory]
    [InlineData("New request (3)")]
    [InlineData("Get")]
    [InlineData("Admin")]
    [InlineData("")]
    public async Task NameDialog_WhenLoaded_ThenFocusesAndSelectsTheName(string name)
    {
        using var harness = new Harness();
        await Ui.RunAsync(async () =>
        {
            await Ui.ShowAsync(harness, harness.Main());
            var dialog = new NameDialog("Save request", name, "Save", _ => null);

            Ui.Show(dialog);
            await Ui.IdleAsync();

            var field = (TextBox)dialog.FindName("NameBox");
            Assert.Same(field, Keyboard.FocusedElement);
            Assert.Equal(name, field.SelectedText);
            Assert.Equal(0, field.SelectionStart);
            Assert.Equal(name, field.Text);
        });
    }

    [Fact]
    public async Task NameDialog_WhenTheParentPathIsLong_ThenUsesTheExistingDialogTitleLayout()
    {
        using var harness = new Harness();
        await Ui.RunAsync(async () =>
        {
            await Ui.ShowAsync(harness, harness.Main());
            var title = $"New folder in {string.Join('/', Enumerable.Repeat("Long parent folder", 12))}";
            var name = new NameDialog(title, "", "Create", _ => null);
            var confirm = new ConfirmDialog(title, "Message", "Confirm", [], canCancel: true);
            Ui.Show(name);
            Ui.Show(confirm);
            await Ui.IdleAsync();

            Assert.Equal(460, name.ActualWidth);
            Assert.Equal(confirm.ActualWidth, name.ActualWidth);
            var nameTitle = Ui.Descendants<TextBlock>(name).Single(text => text.Text == title);
            var confirmTitle = Ui.Descendants<TextBlock>(confirm).Single(text => text.Text == title);
            Assert.Equal(confirmTitle.ActualWidth, nameTitle.ActualWidth);
            Assert.Equal(confirmTitle.TextTrimming, nameTitle.TextTrimming);
            var close = Ui.Descendants<Button>(name).Single(button => Equals(button.Content, "\uE711") && button.IsVisible);
            Assert.InRange(Ui.Bounds(close, name).Right, 0, name.ActualWidth);
        });
    }
}
