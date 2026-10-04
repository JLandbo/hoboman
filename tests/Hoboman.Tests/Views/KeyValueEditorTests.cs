using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Hoboman.Controls;
using Hoboman.Tests.ViewModels;

namespace Hoboman.Tests.Views;

[Collection("Ui")]
public sealed class KeyValueEditorTests
{
    [Fact]
    public async Task Divider_WhenDragged_ThenEveryRowFollowsAndTheShareIsKept()
    {
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var splits = new Dictionary<string, double>();
            var editor = await ShowAsync(splits, [new("name", "value")]);
            var dividers = Dividers(editor).ToList();
            var before = NameColumnOf(dividers[0]);

            // Act
            dividers[0].RaiseEvent(new DragDeltaEventArgs(-60, 0));
            dividers[0].RaiseEvent(new DragCompletedEventArgs(-60, 0, false));
            await Ui.IdleAsync();

            // Assert
            Assert.All(dividers, divider => Assert.Equal(before - 60, NameColumnOf(divider), 1));
            Assert.True(splits["Table"] < 0.5);
        });
    }

    [Fact]
    public async Task Divider_WhenTheTableIsBuiltAgain_ThenIsWhereItWasKept()
    {
        await Ui.RunAsync(async () =>
        {
            // Act
            var editor = await ShowAsync(new() { ["Table"] = 0.3 }, []);

            // Assert
            var columns = ((Grid)Dividers(editor).First().Parent).ColumnDefinitions;
            Assert.Equal(0.3, columns[1].ActualWidth / (columns[1].ActualWidth + columns[2].ActualWidth), 2);
        });
    }

    [Fact]
    public async Task Divider_WhenTheEnvironmentsAreEdited_ThenIsWhereItWasKept()
    {
        using var harness = new Harness();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var main = harness.Main();
            await main.LoadAsync();
            var window = await Ui.ShowAsync(harness, main);
            SplitMemory.GetSplits(window)!["Variables"] = 0.3;
            var environments = harness.EnvironmentEditor();
            await environments.LoadAsync(TestContext.Current.CancellationToken);
            environments.Add();

            // Act
            var dialog = new EnvironmentEditorWindow(environments);
            Ui.Show(dialog);
            await Ui.IdleAsync();

            // Assert
            Assert.Equal(0.3, Ui.Descendants<KeyValueEditor>(dialog).Single().NameWidth.Value, 3);
        });
    }

    [Fact]
    public async Task Divider_WhenTheRowsScroll_ThenTheHeaderLineMeetsTheRowLines()
    {
        await Ui.RunAsync(async () =>
        {
            // Act
            var editor = await ShowAsync(new(), Enumerable.Range(0, 30).Select(index => new KeyValue($"name{index}", "value")));

            // Assert
            var lines = Dividers(editor).Take(2).Select(divider => Math.Round(divider.TranslatePoint(new(0, 0), editor).X)).ToList();
            Assert.Equal(lines[0], lines[1]);
        });
    }

    [Fact]
    public async Task Row_WhenAValueIsLong_ThenItsTextWrapsInsteadOfHidingTheRest()
    {
        await Ui.RunAsync(async () =>
        {
            // Act
            var editor = await ShowAsync(new(), [new("short", "x"), new("long", string.Join(" ", Enumerable.Repeat("word", 40)))]);

            // Assert
            var shown = Dividers(editor).Skip(1).Take(2).Select(divider => Ui.Descendants<TextBlock>(Ui.Named<VariableTextBox>((Grid)divider.Parent, "ValueBox")).Single(text => text.Name == "PART_Highlight").ActualHeight).ToList();
            Assert.True(shown[1] > 2 * shown[0]);
        });
    }

    [Fact]
    public async Task Row_WhenAValueIsLong_ThenTheRestStaysAtItsFirstLine()
    {
        await Ui.RunAsync(async () =>
        {
            // Act
            var editor = await ShowAsync(new(), [new("short", "x"), new("long", string.Join(" ", Enumerable.Repeat("word", 40)))]);

            // Assert
            var rows = Dividers(editor).Skip(1).Take(2).Select(divider => (Grid)divider.Parent).ToList();
            Assert.Equal(PlacesIn(rows[0]), PlacesIn(rows[1]));
        });
    }

    [Theory]
    [InlineData(true, 30)]
    [InlineData(false, 0)]
    public async Task Row_WhenItCanBeTurnedOffOrNot_ThenHasRoomForTheCheckboxOrNone(bool canDisable, double expected)
    {
        await Ui.RunAsync(async () =>
        {
            // Act
            var editor = await ShowAsync(new(), [new("name", "value")], canDisable);

            // Assert
            Assert.All(Dividers(editor), divider => Assert.Equal(expected, ((Grid)divider.Parent).ColumnDefinitions[0].ActualWidth));
        });
    }

    // The header's divider comes first, then one in each row.
    static IEnumerable<Thumb> Dividers(KeyValueEditor editor) => Ui.Descendants<Thumb>(editor).Where(thumb => thumb.Style == editor.FindResource("Divider"));

    static double NameColumnOf(Thumb divider) => ((Grid)divider.Parent).ColumnDefinitions[1].ActualWidth;

    // Where the checkbox, the name's text and the remove button sit in a row.
    static string PlacesIn(Grid row) => string.Join(" ", new FrameworkElement[]
    {
        Ui.Descendants<CheckBox>(row).Single(),
        Ui.Descendants<TextBlock>(row).First(text => text.Name == "PART_Highlight"),
        Ui.Descendants<Button>(row).Single(),
    }.Select(element => Math.Round(element.TranslatePoint(new(0, 0), row).Y)));

    static async Task<KeyValueEditor> ShowAsync(Dictionary<string, double> splits, IEnumerable<KeyValue> values, bool canDisable = true)
    {
        var list = new KeyValueListViewModel();
        list.Load(values);
        var editor = new KeyValueEditor { DataContext = list, CanDisable = canDisable };
        SplitMemory.SetColumns(editor, "Table");
        var window = new Window { Content = editor, Width = 400, Height = 400 };
        SplitMemory.SetSplits(window, splits);
        Ui.Show(window);
        await Ui.IdleAsync();
        return editor;
    }
}
