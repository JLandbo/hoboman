using Hoboman.Core.Auth;

namespace Hoboman.Core.Requests;

public enum BodyKind { None, Json, Text }

public sealed record KeyValue(string Name, string Value, bool Enabled = true);

public sealed record ApiRequest
{
    public Guid Id { get; init; }

    public string Method { get; init; } = "GET";

    public required string Url { get; init; }

    public IReadOnlyList<KeyValue> Query { get; init; } = [];

    public IReadOnlyList<KeyValue> Headers { get; init; } = [];

    public BodyKind BodyKind { get; init; } = BodyKind.None;

    public string Body { get; init; } = "";

    public AuthSettings Auth { get; init; } = AuthSettings.None;

    public static ApiRequest New() => new() { Id = Guid.NewGuid(), Url = "" };
}
