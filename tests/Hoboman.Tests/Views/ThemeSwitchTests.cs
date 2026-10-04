using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hoboman.Controls;
using Hoboman.Themes;

namespace Hoboman.Tests.Views;

[Collection("Ui")]
public sealed class ThemeSwitchTests
{
    static readonly Color _blue = Color.FromRgb(0x1A, 0x66, 0xD9);

    // Leaves the text colour to the default, so nothing but the theme being used has the editors drawn again.
    static readonly Theme _theme = Theme.Parse("Blå", """{ "colors": { "JsonKey": "#1A66D9", "MethodGET": "#1A66D9", "Attention": "#1A66D9" } }""");

    [Fact]
    public async Task Apply_WhenABodyIsShown_ThenItIsDrawnWithTheThemesColours()
    {
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var view = new BodyView { Coloring = BodyFormat.Json, Body = """{ "kunde": 1 }""" };
            Ui.Show(new Window { Content = view, Width = 400, Height = 300 });
            await Ui.IdleAsync();

            // Act
            var colours = await WithThemeAsync(() => ColoursOf(view));

            // Assert
            Assert.Contains(_blue, colours);
        });
    }

    [Fact]
    public async Task Apply_WhenABodyHasAVariable_ThenItHasTheThemesAttentionColour()
    {
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var editor = new BodyEditor { UseEnvironmentVariables = true, Body = "{{kunde}}" };
            Ui.Show(new Window { Content = editor, Width = 400, Height = 300 });
            await Ui.IdleAsync();

            // Act
            var colours = await WithThemeAsync(() => ColoursOf(editor));

            // Assert
            Assert.Contains(_blue, colours);
        });
    }

    [Fact]
    public async Task MethodColor_WhenTheThemeChanges_ThenTheMethodHasItsColour()
    {
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var text = new TextBlock();
            MethodColor.SetOf(text, "get");
            Ui.Show(new Window { Content = text });

            // Act
            var colour = await WithThemeAsync(() => ((SolidColorBrush)text.Foreground).Color);

            // Assert
            Assert.Equal(_blue, colour);
        });
    }

    [Fact]
    public async Task MethodColor_WhenItIsNoMethod_ThenHasTheTextColour()
    {
        await Ui.RunAsync(() =>
        {
            // Arrange
            var text = new TextBlock();

            // Act
            MethodColor.SetOf(text, "Vent");

            // Assert
            Assert.Same(Application.Current.FindResource("Text"), text.Foreground);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Apply_WhenAnotherThemeIsUsed_ThenTheFormerIsTakenAway()
    {
        await Ui.RunAsync(() =>
        {
            // Arrange
            var resources = Application.Current.Resources;
            var former = _theme.Apply(resources, null);

            // Act
            var applied = ThemeLibrary.Default.Apply(resources, former);

            // Assert
            var kept = resources.MergedDictionaries.Contains(former);
            resources.MergedDictionaries.Remove(applied);
            Assert.False(kept);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task DropIndicator_WhenTheThemeChanges_ThenIsDrawnWithItsAttentionColour()
    {
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var owner = new Border { Width = 100, Height = 100 };
            Ui.Show(new Window { Content = owner, SizeToContent = SizeToContent.WidthAndHeight });
            await Ui.IdleAsync();
            var indicator = new DropIndicator(owner);
            indicator.Show(new Rect(0, 10, 100, 40), box: true);
            await Ui.IdleAsync();

            // Act
            var colour = await WithThemeAsync(() =>
            {
                indicator.Show(new Rect(0, 10, 100, 40), box: true);
                indicator.UpdateLayout();
                return PixelOf(indicator, 50, 10);
            });

            // Assert
            Assert.Equal(_blue, colour);
        });
    }

    static Color PixelOf(Visual visual, int x, int y)
    {
        var bitmap = new RenderTargetBitmap(100, 100, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    static IEnumerable<Color> ColoursOf(MarkedEditor editor) => [.. editor.TextArea.TextView.VisualLines.SelectMany(line => line.Elements)
        .Select(element => element.TextRunProperties.ForegroundBrush).OfType<SolidColorBrush>().Select(brush => brush.Color)];

    // The theme is taken away again, as the other tests share the app.
    static async Task<T> WithThemeAsync<T>(Func<T> read)
    {
        var applied = _theme.Apply(Application.Current.Resources, null);
        try
        {
            await Ui.IdleAsync();
            return read();
        }
        finally
        {
            Application.Current.Resources.MergedDictionaries.Remove(applied);
            Colorings.Use(ThemeLibrary.Default.BrushOf);
        }
    }
}
