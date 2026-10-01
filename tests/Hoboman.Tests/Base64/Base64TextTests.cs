namespace Hoboman.Tests.Base64;

public sealed class Base64TextTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("hello", "aGVsbG8=")]
    [InlineData("Ærø", "w4Zyw7g=")]
    public void Encode_WhenGivenUtf8Text_ThenUsesStandardBase64(string text, string expected)
    {
        // Act
        var encoded = Base64Text.Encode(text);

        // Assert
        Assert.Equal(expected, encoded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("quotes \" and slash \\ and Ærø\n😀")]
    public void Decode_WhenGivenEncodedText_ThenRestoresTheOriginalText(string text)
    {
        // Act
        var decoded = Base64Text.Decode(Base64Text.Encode(text));

        // Assert
        Assert.Equal(text, decoded);
    }

    [Fact]
    public void Decode_WhenBase64ContainsWhitespace_ThenIgnoresIt()
    {
        // Act
        var decoded = Base64Text.Decode("aG Vs\r\nbG8=");

        // Assert
        Assert.Equal("hello", decoded);
    }

    [Theory]
    [InlineData("not Base64!")]
    [InlineData("abc")]
    [InlineData("/w==")]
    public void TryDecode_WhenTheTextIsInvalidBase64OrNotUtf8_ThenFails(string text)
    {
        // Act
        var succeeded = Base64Text.TryDecode(text, out var decoded);

        // Assert
        Assert.False(succeeded);
        Assert.Null(decoded);
    }

    [Fact]
    public void Encode_WhenUsedAgain_ThenAddsAnotherLayerThatDecodeRemovesOneAtATime()
    {
        // Arrange
        var encoded = Base64Text.Encode("hello");

        // Act
        var encodedAgain = Base64Text.Encode(encoded);

        // Assert
        Assert.NotEqual(encoded, encodedAgain);
        Assert.Equal(encoded, Base64Text.Decode(encodedAgain));
    }
}
