using Hoboman.Core.Auth;
using Hoboman.Core.Base64;

namespace Hoboman.Core.Requests;

// A saved request's file is named by its id, so its name can be anything, and moving it only changes FolderId.
public sealed record ApiRequest
{
    public Guid Id { get; init; }

    public string Name { get; init; } = "";

    public Guid? FolderId { get; init; }

    public string Method { get; init; } = "GET";

    public required string Url { get; init; }

    public IReadOnlyList<KeyValue> Query { get; init; } = [];

    public IReadOnlyList<KeyValue> Headers { get; init; } = [];

    public BodyKind BodyKind { get; init; } = BodyKind.Json;

    public string Body { get; init; } = "";

    public bool UseEnvironmentVariablesInBody { get; init; }

    public Base64Paths? Base64 { get; init; }

    public AuthSettings Auth { get; init; } = AuthSettings.Inherit;

    public static ApiRequest New() => new() { Id = Guid.NewGuid(), Url = "" };
}
