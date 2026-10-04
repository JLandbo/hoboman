using Hoboman.Core.Requests;
using Hoboman.Core.Sending;

namespace Hoboman.Core.Workflows;

// A script step has the code it was checked with, so a change during the run does not reach it.
public sealed record CheckedStep(WorkflowStep Step, ApiRequest? Request, string? Code = null)
{
    // A step without a name is known by its address, which leaves out what can hold a key, as the run log shows it.
    public string Title => Step.Name is { Length: > 0 } name ? name : Step.Kind switch
    {
        StepKind.Script => Step.Script!,
        StepKind.Delay => $"Wait {Step.DelaySeconds} seconds",
        _ => Request is { } request ? $"{request.Method} {RequestRunner.AddressOf(request, null)}" : "",
    };
}
