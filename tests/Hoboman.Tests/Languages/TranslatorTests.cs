namespace Hoboman.Tests.Languages;

public sealed class TranslatorTests
{
    [Fact]
    public void Use_WhenGivenATranslation_ThenTranslatesWithIt()
    {
        // Arrange
        var translator = new Translator(Translation.Danish);

        // Act
        translator.Use(Translation.English);

        // Assert
        Assert.Equal("Response", translator.Of("Response.Title"));
    }

    [Fact]
    public void Use_WhenGivenATranslation_ThenTellsListeners()
    {
        // Arrange
        var translator = new Translator(Translation.Danish);
        var changed = false;
        translator.Changed += () => changed = true;

        // Act
        translator.Use(Translation.English);

        // Assert
        Assert.True(changed);
    }

    [Fact]
    public void Use_WhenTheLanguageIsTheSame_ThenTellsNobody()
    {
        // Arrange
        var translator = new Translator(Translation.Danish);
        var changed = false;
        translator.Changed += () => changed = true;

        // Act
        translator.Use(Translation.Danish);

        // Assert
        Assert.False(changed);
    }
}
