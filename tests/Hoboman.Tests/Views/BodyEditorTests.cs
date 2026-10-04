using System.Windows;
using Hoboman.Controls;
using Hoboman.Tests.ViewModels;

namespace Hoboman.Tests.Views;

[Collection("Ui")]
public sealed class BodyEditorTests
{
    const string _body = """{ "content": "<div style='font-size:9px;width:100%;text-align:center;font-family:Arial,sans-serif;'>Hoboman testudskrift</div>" }""";

    [Fact]
    public async Task BodyEditor_WhenAPropertyIsChosenAndScrolledFullyRight_ThenTheTextEndsLeftOfWhatItSays()
    {
        using var harness = new Harness();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var (editor, labels, _) = await ShowAsync(harness, ["$.content"]);

            // Act
            editor.ScrollToHorizontalOffset(100000);
            await Ui.IdleAsync();

            // Assert
            Assert.True(labels.ActualWidth > 0 && RightOf(editor.TextArea.TextView, editor) <= labels.TranslatePoint(new(0, 0), editor).X);
        });
    }

    [Fact]
    public async Task BodyEditor_WhenAPropertyIsChosenAndUnchosen_ThenTheRoomForWhatItSaysComesAndGoes()
    {
        using var harness = new Harness();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var (_, labels, request) = await ShowAsync(harness, []);
            var widths = new List<double> { labels.ActualWidth };

            // Act
            request.Base64.ToggleEncode("$.content");
            await Ui.UntilAsync(() => labels.ActualWidth > 0);
            widths.Add(labels.ActualWidth);
            request.Base64.ToggleEncode("$.content");
            await Ui.UntilAsync(() => labels.ActualWidth == 0);
            widths.Add(labels.ActualWidth);

            // Assert
            Assert.Equal((0, true, 0), (widths[0], widths[1] > 0, widths[2]));
        });
    }

    [Fact]
    public async Task BodyView_WhenAValueIsDecoded_ThenTheWrappedTextEndsLeftOfWhatItSays()
    {
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var view = new BodyView { Coloring = BodyFormat.Json, Body = "{\n  \"content\": \"" + new string('A', 400) + "\"\n}" };

            // Act
            view.Marks = [new(2, "$.content", Base64MarkState.Decoded, "Base64", null)];
            Ui.Show(new Window { Content = view, Width = 400, Height = 300 });
            await Ui.UntilAsync(() => Ui.Descendants<Base64Labels>(view).Any(labels => labels.ActualWidth > 0));

            // Assert
            var labels = Ui.Descendants<Base64Labels>(view).Single();
            Assert.True(view.TextArea.TextView.VisualLines.Any(line => line.TextLines.Count > 1) && RightOf(view.TextArea.TextView, view) <= labels.TranslatePoint(new(0, 0), view).X);
        });
    }

    static double RightOf(FrameworkElement element, UIElement to) => element.TranslatePoint(new(element.ActualWidth, 0), to).X;

    static async Task<(BodyEditor Editor, Base64Labels Labels, RequestViewModel Request)> ShowAsync(Harness harness, List<string> encode)
    {
        var request = new RequestViewModel(harness.Translator, harness.Clock, harness.Environments);
        request.Load(new ApiRequest { Method = "POST", Url = "https://dev.local", BodyKind = BodyKind.Json, Body = _body, Base64 = new() { Encode = encode }, Auth = AuthSettings.None });
        var view = new RequestBodyEditor { DataContext = request };
        Ui.Show(new Window { Content = view, Width = 600, Height = 300 });
        await Ui.UntilAsync(() => Ui.Descendants<Base64Labels>(view).Any());
        return (Ui.Descendants<BodyEditor>(view).Single(), Ui.Descendants<Base64Labels>(view).Single(), request);
    }
}
