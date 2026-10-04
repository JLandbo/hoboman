using System.Windows.Automation;
using System.Windows.Controls;
using Hoboman.Tests.ViewModels;
using Hoboman.Themes;

namespace Hoboman.Tests.Views;

[Collection("Ui")]
public sealed class SettingsWindowTests
{
    [Fact]
    public async Task SettingsWindow_WhenAThemeIsClicked_ThenItIsUsed()
    {
        using var harness = new Harness();
        Directory.CreateDirectory(harness.Folder.Themes);
        File.WriteAllText(Path.Combine(harness.Folder.Themes, "Lys.json"), "{}");
        var themes = new ThemeLibrary(harness.Folder.Themes);
        var settings = new SettingsViewModel(harness.SettingsStore, new Translator(Translation.English), themes, NullLogger<SettingsViewModel>.Instance);
        await Ui.RunAsync(async () =>
        {
            // Arrange
            await Ui.ShowAsync(harness, harness.Main());
            var window = new SettingsWindow(settings);
            Ui.Show(window);
            await Ui.IdleAsync();

            // Act
            Ui.Click(Ui.Descendants<Button>(window).Single(button => AutomationProperties.GetName(button) == "Lys"));
            await Ui.IdleAsync();
            await settings.Saving;

            // Assert
            Assert.Equal("Lys", themes.Current.Name);
        });
    }
}
