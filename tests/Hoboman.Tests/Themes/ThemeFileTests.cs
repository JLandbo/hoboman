using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Hoboman.Themes;

namespace Hoboman.Tests.Themes;

public sealed partial class ThemeFileTests
{
    [Fact]
    public void Colours_WhenTheAppDrawsWithThem_ThenAllLiveInTheThemeFile()
    {
        // Act
        var outside = Files(".xaml").Where(file => Path.GetFileName(file) != "Colors.xaml").Where(file => HexColour().IsMatch(File.ReadAllText(file)))
            .Concat(Files(".cs").Where(file => MadeColour().IsMatch(File.ReadAllText(file))))
            .Select(Path.GetFileName);

        // Assert
        Assert.Empty(outside);
    }

    [Fact]
    public void Lys_WhenRead_ThenHasEveryColourOfTheDefault()
    {
        // Act
        var lys = Theme.Read(Path.Combine(SourceFolder(), "Themes", "Lys.json"))!;

        // Assert
        Assert.Equal(Key().Matches(File.ReadAllText(Path.Combine(SourceFolder(), "Themes", "Colors.xaml"))).Select(key => key.Groups[1].Value).Order(), lys.Colors.Keys.Order());
    }

    static IEnumerable<string> Files(string extension) => Directory.EnumerateFiles(SourceFolder(), $"*{extension}", SearchOption.AllDirectories)
        .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    static string SourceFolder([CallerFilePath] string test = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(test)!, "..", "..", "..", "src", "Hoboman"));

    [GeneratedRegex(@"""#[0-9A-Fa-f]{3,8}""")]
    private static partial Regex HexColour();

    [GeneratedRegex(@"x:Key=""(\w+)""")]
    private static partial Regex Key();

    // Transparent is no colour of the theme, but the absence of one. A call, as on a theme's own colours, and the file Colors.xaml are none either.
    [GeneratedRegex(@"Color\.From(?:Argb|Rgb|ScRgb)|(?:Colors|Brushes)\.(?!Transparent\b)[A-Z]\w*\b(?!\()")]
    private static partial Regex MadeColour();
}
