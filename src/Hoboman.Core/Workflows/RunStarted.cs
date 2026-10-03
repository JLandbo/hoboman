using System.Text.Json;

namespace Hoboman.Core.Workflows;

// The workflow and environment are names taken when the run starts, only for reading. The ids are what refer to anything.
public sealed record RunStarted(string RunId, Guid WorkflowId, string Workflow, string Environment, string RunFile, IReadOnlyDictionary<string, JsonElement> Parameters) : WorkflowEvent;
