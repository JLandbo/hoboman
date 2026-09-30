namespace Hoboman.Tests.Auth;

public sealed class OAuthTokenTests
{
    static readonly DateTimeOffset _now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void HasExpired_WhenLessThanHalfAMinuteIsLeft_ThenCountsAsExpired()
    {
        // Act
        var expired = new OAuthToken("access", "Bearer", _now.AddSeconds(20), null).HasExpired(_now);

        // Assert
        Assert.True(expired);
    }

    [Fact]
    public void HasExpired_WhenAMinuteIsLeft_ThenIsStillValid()
    {
        // Act
        var expired = new OAuthToken("access", "Bearer", _now.AddMinutes(1), null).HasExpired(_now);

        // Assert
        Assert.False(expired);
    }
}
