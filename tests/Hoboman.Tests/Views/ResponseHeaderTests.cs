using System.Windows;
using System.Windows.Controls;
using Hoboman.Tests.ViewModels;

namespace Hoboman.Tests.Views;

[Collection("Ui")]
public sealed class ResponseHeaderTests
{
    [Theory]
    [InlineData(1600, 0)]
    [InlineData(1000, 1)]
    public async Task ResponseView_WhenTheWindowHasThisWidth_ThenTheStatusIsBesideOrBelowTheSections(double width, int row)
    {
        using var harness = new Harness(send: () => Task.FromResult(new ApiResponse(200, "OK", 622, 2, [new("Content-Type", "application/json")], "{}")));
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var main = harness.Main();
            await main.LoadAsync();
            var window = await Ui.ShowAsync(harness, main);
            main.SelectedTab!.Editor.Url = "https://dev.local";
            await main.SelectedTab.SendAsync();

            // Act
            window.Width = width;
            await Ui.IdleAsync();

            // Assert
            var view = Ui.Descendants<ResponseView>(window).Single();
            Assert.Equal(row, Grid.GetRow((FrameworkElement)view.FindName("HeaderRight")));
        });
    }

    [Fact]
    public async Task ResponseView_WhenTheBodyIsShown_ThenTheWholeBodySwitchIsInTheStatusLine()
    {
        using var harness = new Harness(send: () => Task.FromResult(new ApiResponse(200, "OK", 622, 2, [new("Content-Type", "application/json")], "{}")));
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var main = harness.Main();
            await main.LoadAsync();
            var window = await Ui.ShowAsync(harness, main);

            // Act
            main.SelectedTab!.Editor.Url = "https://dev.local";
            await main.SelectedTab.SendAsync();
            await Ui.IdleAsync();

            // Assert
            var view = Ui.Descendants<ResponseView>(window).Single();
            var header = (Panel)view.FindName("HeaderRight");
            Assert.Contains(Ui.Descendants<System.Windows.Controls.Primitives.ToggleButton>(header), toggle => toggle.IsVisible && Equals(toggle.Content, harness.Translator.Of("Base64.WholeBody")));
        });
    }
}
