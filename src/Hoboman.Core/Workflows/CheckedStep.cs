using Hoboman.Core.Requests;

namespace Hoboman.Core.Workflows;

// The path is looked up by the request's id when the run starts and is never stored. It gives the request the auth of its folder and names it in the history.
public sealed record CheckedStep(WorkflowStep Step, string Path, ApiRequest Request);
