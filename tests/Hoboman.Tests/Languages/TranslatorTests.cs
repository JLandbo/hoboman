namespace Hoboman.Tests.Languages;

public sealed class TranslatorTests
{
    [Fact]
    public void Use_WhenGivenATranslation_ThenTranslatesWithItAndTellsListeners()
    {
        // Arrange
        var translator = new Translator(Translation.Danish);
        var changed = false;
        translator.Changed += () => changed = true;

        // Act
        translator.Use(Translation.English);

        // Assert
        Assert.Equal("Response", translator.Of("Response.Title"));
        Assert.True(changed);
    }
}
