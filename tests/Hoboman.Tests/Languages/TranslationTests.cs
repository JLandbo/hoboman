using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Hoboman.Tests.Languages;

public sealed partial class TranslationTests
{
    [Fact]
    public void Parse_WhenTheFileHasTexts_ThenReadsThem()
    {
        // Act
        var translation = Translation.Parse("Test", """{"texts": {"Response.Title": "Answer"}}""");

        // Assert
        Assert.Equal("Answer", translation.Of("Response.Title"));
    }

    [Fact]
    public void Of_WhenTheTextIsMissing_ThenGivesTheDanishText()
    {
        // Arrange
        var translation = new Translation("Test", new Dictionary<string, string>());

        // Act
        var text = translation.Of("Response.Title");

        // Assert
        Assert.Equal("Svar", text);
    }

    [Fact]
    public void Of_WhenNoLanguageHasTheText_ThenGivesTheKey()
    {
        // Act
        var text = Translation.English.Of("Findes.Ikke");

        // Assert
        Assert.Equal("Findes.Ikke", text);
    }

    [Fact]
    public void Format_WhenTheTextHasABrokenPlaceholder_ThenGivesTheDanishText()
    {
        // Arrange
        var translation = new Translation("Test", new Dictionary<string, string> { ["Response.Title"] = "Answer {0" });

        // Act
        var text = translation.Format("Response.Title");

        // Assert
        Assert.Equal("Svar", text);
    }

    [Fact]
    public void Find_WhenTheNameIsUnknown_ThenGivesDanish()
    {
        // Act
        var translation = Translation.Find("Klingon");

        // Assert
        Assert.Same(Translation.Danish, translation);
    }

    [Fact]
    public void English_WhenBuiltIn_ThenHasEveryDanishText()
    {
        // Act
        var missing = Translation.Danish.Texts.Keys.Except(Translation.English.Texts.Keys);

        // Assert
        Assert.Empty(missing);
    }

    [Fact]
    public void Danish_WhenTheCodeUsesAText_ThenHasIt()
    {
        // Act
        var missing = UsedKeys().Except(Translation.Danish.Texts.Keys);

        // Assert
        Assert.Empty(missing);
    }

    [Fact]
    public void Danish_WhenItHasAText_ThenTheCodeUsesIt()
    {
        // Act
        var unused = Translation.Danish.Texts.Keys.Except(UsedKeys());

        // Assert
        Assert.Empty(unused);
    }

    static IEnumerable<string> UsedKeys()
    {
        return Directory.EnumerateFiles(SourceFolder(), "*", SearchOption.AllDirectories)
            .Where(file => Path.GetExtension(file) is ".cs" or ".xaml" && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(file => Key().Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value))
            .Distinct();
    }

    static string SourceFolder([CallerFilePath] string test = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(test)!, "..", "..", "..", "src"));

    [GeneratedRegex("""(?:\b(?:Of|Format)\("|DynamicResource |ResourceReference\([^,]+, ")([A-Z][A-Za-z]*\.[A-Za-z.]+)""")]
    private static partial Regex Key();
}
