using Hoboman.Core.Auth;
using Hoboman.Core.Base64;

namespace Hoboman.Core.Requests;

public sealed record ApiRequest
{
    public Guid Id { get; init; }

    public string Method { get; init; } = "GET";

    public required string Url { get; init; }

    public IReadOnlyList<KeyValue> Query { get; init; } = [];

    public IReadOnlyList<KeyValue> Headers { get; init; } = [];

    public BodyKind BodyKind { get; init; } = BodyKind.None;

    public string Body { get; init; } = "";

    public bool UseEnvironmentVariablesInBody { get; init; }

    public Base64Paths? Base64 { get; init; }

    public AuthSettings Auth { get; init; } = AuthSettings.Inherit;

    public static ApiRequest New() => new() { Id = Guid.NewGuid(), Url = "" };
}
