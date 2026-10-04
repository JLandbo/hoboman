using Hoboman.Themes;

namespace Hoboman.Tests.Themes;

public sealed class ThemeLibraryTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public void Themes_WhenTheFolderHasThemes_ThenGivesTheDefaultFirstAndTheRestByName()
    {
        // Arrange
        Directory.CreateDirectory(_temporary.Path);
        File.WriteAllText(Path.Combine(_temporary.Path, "Mørk.json"), "{}");
        File.WriteAllText(Path.Combine(_temporary.Path, "Lys.json"), "{}");

        // Act
        var names = new ThemeLibrary(_temporary.Path).Themes.Select(theme => theme.Name);

        // Assert
        Assert.Equal(["Sort og gul", "Lys", "Mørk"], names);
    }

    [Fact]
    public void Find_WhenTheThemeIsGone_ThenGivesTheDefault()
    {
        // Act
        var theme = new ThemeLibrary(_temporary.Path).Find("Lys");

        // Assert
        Assert.Same(ThemeLibrary.Default, theme);
    }
}
