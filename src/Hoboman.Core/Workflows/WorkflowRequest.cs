using System.Text.Json.Serialization;
using Hoboman.Core.Auth;
using Hoboman.Core.Base64;
using Hoboman.Core.Requests;

namespace Hoboman.Core.Workflows;

// The call of a step is kept in the workflow itself, so a workflow needs nothing from the collections. It has what a request has, but there is no folder to inherit auth from.
public sealed record WorkflowRequest
{
    // The owner of the step's secrets, such as a password or a token, which are kept in secrets.json and never in the workflow.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Guid Id { get; init; }

    public string Method { get; init; } = "GET";

    public string Url { get; init; } = "";

    public IReadOnlyList<KeyValue> Query { get; init; } = [];

    public IReadOnlyList<KeyValue> Headers { get; init; } = [];

    public BodyKind BodyKind { get; init; } = BodyKind.None;

    public string Body { get; init; } = "";

    // On unless turned off, as a step mostly sends values from the steps before it.
    public bool UseEnvironmentVariablesInBody { get; init; } = true;

    public Base64Paths? Base64 { get; init; }

    // Without auth when left out.
    public AuthSettings? Auth { get; init; }

    public static WorkflowRequest From(ApiRequest request) => new()
    {
        Id = request.Id,
        Method = request.Method,
        Url = request.Url,
        Query = request.Query,
        Headers = request.Headers,
        BodyKind = request.BodyKind,
        Body = request.Body,
        UseEnvironmentVariablesInBody = request.UseEnvironmentVariablesInBody,
        Base64 = request.Base64,
        Auth = request.Auth.Kind is AuthKind.None or AuthKind.Inherit ? null : request.Auth,
    };

    public ApiRequest ToApiRequest() => new()
    {
        Id = Id,
        Method = Method,
        Url = Url,
        Query = Query,
        Headers = Headers,
        BodyKind = BodyKind,
        Body = Body,
        UseEnvironmentVariablesInBody = UseEnvironmentVariablesInBody,
        Base64 = Base64,
        Auth = Auth is { Kind: not AuthKind.Inherit } auth ? auth : AuthSettings.None,
    };
}
