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
    public void Parse_WhenTheFileHasACulture_ThenUsesItForNumbers()
    {
        // Arrange
        var translation = Translation.Parse("Test", """{"culture": "da-DK", "texts": {"Size": "{0:0.0}"}}""");

        // Act
        var text = translation.Format("Size", 1.5);

        // Assert
        Assert.Equal("1,5", text);
    }

    [Fact]
    public void Languages_WhenBuiltIn_ThenHaveTheSameTexts()
    {
        // Act
        var same = Translation.Danish.Texts.Keys.Order().SequenceEqual(Translation.English.Texts.Keys.Order());

        // Assert
        Assert.True(same);
    }

    [Fact]
    public void Languages_WhenATextHasPlaceholders_ThenTheOtherLanguageHasThemToo()
    {
        // Act
        var mismatched = Translation.Danish.Texts.Keys.Where(key => !Placeholders(Translation.Danish.Of(key)).SequenceEqual(Placeholders(Translation.English.Of(key))));

        // Assert
        Assert.Empty(mismatched);
    }

    [Fact]
    public void Xaml_WhenItShowsText_ThenTheTextCanBeTranslated()
    {
        // Act
        var hardcoded = Files(".xaml").SelectMany(file => ShownText().Matches(File.ReadAllText(file)).Select(match => match.Value)).Where(text => text != "Title=\"Hoboman\"");

        // Assert
        Assert.Empty(hardcoded);
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

    static IEnumerable<string> UsedKeys() => Files(".cs", ".xaml").SelectMany(file => Key().Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value)).Distinct();

    static IEnumerable<string> Files(params string[] extensions) => Directory.EnumerateFiles(SourceFolder(), "*", SearchOption.AllDirectories)
        .Where(file => extensions.Contains(Path.GetExtension(file)) && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    static IEnumerable<string> Placeholders(string text) => Placeholder().Matches(text).Select(match => match.Value).Order();

    static string SourceFolder([CallerFilePath] string test = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(test)!, "..", "..", "..", "src"));

    [GeneratedRegex("""(?:\b(?:Of|Format)\("|DynamicResource |ResourceReference\([^,]+, ")([A-Z][A-Za-z0-9]*\.[A-Za-z0-9.]+)""")]
    private static partial Regex Key();

    [GeneratedRegex(@"\{\d+[^}]*\}")]
    private static partial Regex Placeholder();

    // Glyphs (&#x...;) and bindings ({...}) are fine; plain words are not.
    [GeneratedRegex(@"\b(?:Text|Content|Header|ToolTip|Title)=""[^""{&][^""]*""")]
    private static partial Regex ShownText();
}
