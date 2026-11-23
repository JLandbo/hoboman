namespace Hoboman.Tests.Auth;

public sealed class CallbackAddressTests
{
    [Theory]
    [InlineData("https://site.example/#access_token=implicit", "https://site.example/", true)]
    [InlineData("https://site.example/?error=access_denied", "https://site.example/", true)]
    [InlineData("https://site.example", "https://site.example/", true)]
    [InlineData("https://site.example/", "https://site.example", true)]
    [InlineData("https://other.example/", "https://site.example/", false)]
    [InlineData("https://site.example/login", "https://site.example/", false)]
    [InlineData("https://site.example:8443/", "https://site.example/", false)]
    [InlineData("http://site.example/", "https://site.example/", false)]
    public void Matches_WhenGivenAnAddress_ThenOnlyMatchesTheSameSchemeHostPortAndPath(string address, string callback, bool expected)
    {
        // Act
        var matches = CallbackAddress.Matches(new Uri(address), new Uri(callback));

        // Assert
        Assert.Equal(expected, matches);
    }
}
