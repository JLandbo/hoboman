using System.Text.Json;

namespace Hoboman.Core.Auth;

// The expiry is kept as a point in time instead of seconds, so a saved token can be seen to have run out.
public sealed record OAuthToken(string AccessToken, string TokenType, DateTimeOffset? ExpiresAt, string? Scope)
{
    // A token about to run out would expire on its way, and the server's refusal would say less than Hoboman can.
    static readonly TimeSpan _margin = TimeSpan.FromSeconds(30);

    public bool HasExpired(DateTimeOffset now) => ExpiresAt <= now + _margin;

    public string ToJson() => JsonSerializer.Serialize(this, JsonSerializerOptions.Web);

    public static OAuthToken? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<OAuthToken>(json, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
