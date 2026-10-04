using System.Windows.Media;
using Hoboman.Themes;

namespace Hoboman.Tests.Themes;

public sealed class ThemeTests
{
    [Fact]
    public void Parse_WhenAColourIsNotOne_ThenLeavesItOut()
    {
        // Act
        var theme = Theme.Parse("Blå", """{ "colors": { "Text": "#102030", "Muted": "blå" } }""");

        // Assert
        Assert.Equal(new Dictionary<string, Color> { ["Text"] = Color.FromRgb(0x10, 0x20, 0x30) }, theme.Colors);
    }
}
