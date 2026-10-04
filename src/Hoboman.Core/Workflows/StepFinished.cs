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
    int? Attempts = null,
    string? Error = null,
    IReadOnlyDictionary<string, JsonElement>? Saved = null,
    IReadOnlyList<ResponseHeader>? Headers = null,
    string? Body = null,
    [property: JsonIgnore] RequestProblemKind? Problem = null,
    [property: JsonIgnore] string? MissingSave = null,
    [property: JsonIgnore] bool NotReady = false,
    [property: JsonIgnore] (string Path, string Value)? Stopped = null,
    [property: JsonIgnore] byte[]? Bytes = null) : WorkflowEvent
{
    public static StepFinished Of(int index, ApiResponse response, IReadOnlyDictionary<string, JsonElement>? saved, string? missingSave) =>
        new(index, response.IsSuccess && missingSave is null ? StepOutcome.Succeeded : StepOutcome.Failed, response.StatusCode, response.Reason, response.ElapsedMs, response.Size,
            Error: missingSave is null ? null : $"Nothing to save was found at {missingSave}.", Saved: saved is { Count: > 0 } ? saved : null, Headers: response.Headers, Body: response.Body,
            MissingSave: missingSave, Bytes: response.Bytes);

    // The answer told that what was waited for failed.
    public static StepFinished StoppedOf(int index, ApiResponse response, string path, string value) =>
        Of(index, response, null, null) with { Outcome = StepOutcome.Failed, Error = $"Stopped as {path} was {value}.", Stopped = (path, value) };

    // An answer came, but not the one that was waited for.
    public static StepFinished NotReadyOf(int index, ApiResponse response, int attempts) =>
        Of(index, response, null, null) with { Outcome = StepOutcome.Failed, Error = $"The answer was not ready after {attempts} attempts.", NotReady = true };
}
