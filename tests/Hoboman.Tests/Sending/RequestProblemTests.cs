namespace Hoboman.Tests.Sending;

public sealed class RequestProblemTests
{
    [Fact]
    public void Of_WhenAChosenBase64PathIsMissing_ThenSaysTheBodyCouldNotBeEncoded()
    {
        // Act
        var problem = RequestProblem.Of(new MissingBase64PathException("$.html"), CancellationToken.None);

        // Assert
        Assert.Equal("Request body could not be encoded.", problem);
    }
}
