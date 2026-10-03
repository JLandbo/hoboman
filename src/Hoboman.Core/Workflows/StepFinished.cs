using System.Text.Json;
using System.Text.Json.Serialization;
using Hoboman.Core.Sending;

namespace Hoboman.Core.Workflows;

// The response part has the same names as the output of send.
// The kind of problem and the missing save path are not written, as the error tells of them, but let the app tell of them in its own language.
public sealed record StepFinished(
    int Index,
    StepOutcome Outcome,
    int? Status = null,
    string? Reason = null,
    long? ElapsedMs = null,
    long? Size = null,
    string? Error = null,
    IReadOnlyDictionary<string, JsonElement>? Saved = null,
    IReadOnlyList<ResponseHeader>? Headers = null,
    string? Body = null,
    [property: JsonIgnore] RequestProblemKind? Problem = null,
    [property: JsonIgnore] string? MissingSave = null) : WorkflowEvent
{
    public static StepFinished Of(int index, ApiResponse response, IReadOnlyDictionary<string, JsonElement>? saved, string? missingSave) =>
        new(index, response.IsSuccess && missingSave is null ? StepOutcome.Succeeded : StepOutcome.Failed, response.StatusCode, response.Reason, response.ElapsedMs, response.Size,
            missingSave is null ? null : $"Nothing to save was found at {missingSave}.", saved is { Count: > 0 } ? saved : null, response.Headers, response.Body, MissingSave: missingSave);
}
