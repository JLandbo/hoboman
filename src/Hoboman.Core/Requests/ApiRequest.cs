using Hoboman.Core.Auth;

namespace Hoboman.Core.Requests;

public enum BodyKind { None, Json, Text }

public sealed record KeyValue(string Name, string Value, bool Enabled = true);

public sealed record ApiRequest(Guid Id, string Method, string Url, IReadOnlyList<KeyValue> Query, IReadOnlyList<KeyValue> Headers, BodyKind BodyKind, string Body, AuthSettings Auth)
{
    public static ApiRequest New() => new(Guid.NewGuid(), "GET", "", [], [], BodyKind.None, "", AuthSettings.None);
}
