using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Hoboman.Tests.Views;

[Collection("Ui")]
public sealed class BlinkTests
{
    [Theory]
    [InlineData(true, "ClipboardSuccess")]
    [InlineData(false, "ClipboardError")]
    public async Task Show_WhenAButtonDidSomething_ThenBlinksAndFadesBackToItsOwnBackground(bool succeeded, string key)
    {
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var button = new Button { Content = "x" };
            Ui.Show(new Window { Content = button, Width = 200, Height = 100 });
            await Ui.IdleAsync();

            // Act
            Blink.Show(button, succeeded);
            var blinked = ((SolidColorBrush)button.Background).Color;
            await Ui.UntilAsync(() => button.ReadLocalValue(Control.BackgroundProperty) == DependencyProperty.UnsetValue);

            // Assert
            Assert.Equal(((SolidColorBrush)button.FindResource(key)).Color, blinked);
        });
    }
}
