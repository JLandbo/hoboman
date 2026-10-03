using System.Diagnostics;
using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.History;
using Hoboman.Core.Sending;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Workflows;

// One step at a time in the order of the list, and the first that fails skips the rest.
// The calls are awaited without ConfigureAwait(false), as in RequestRunner, so a token is fetched and an event is told where the caller called from, such as the UI thread.
// Each event is awaited before the next, as Progress<T> can change the order of the lines in a console.
public sealed class WorkflowRunner(RequestRunner runner, AppFolder folder, ILogger<WorkflowRunner> logger)
{
    public async Task<RunOutcome> RunAsync(CheckedWorkflow workflow, ApiEnvironment environment, HistorySource source, Func<AuthSource, Task<bool>> fetchToken, Func<WorkflowEvent, Task> report,
        CancellationToken cancellationToken)
    {
        if (workflow.Problems.Count > 0)
        {
            throw new ArgumentException("A workflow with problems cannot run", nameof(workflow));
        }
        var started = Stopwatch.GetTimestamp();
        await using var log = RunLog.Create(folder, workflow.Workflow.Id, logger);
        var values = RunValues.Start(workflow.Workflow, workflow.Parameters);
        var results = new List<StepResult>();
        var outcome = RunOutcome.Succeeded;
        await TellAsync(new RunStarted(log.RunId, workflow.Workflow.Id, workflow.Name, environment.Name, log.FilePath, values.Of(workflow.Workflow.Parameters)));
        foreach (var (index, step) in workflow.Steps.Index())
        {
            if (outcome != RunOutcome.Succeeded)
            {
                await TellAsync(new StepSkipped(index));
                results.Add(new(index, StepOutcome.Skipped));
                continue;
            }
            var result = await RunStepAsync(index, step);
            results.Add(result);
            outcome = result.Outcome switch
            {
                StepOutcome.Succeeded => RunOutcome.Succeeded,
                StepOutcome.Cancelled => RunOutcome.Cancelled,
                _ => RunOutcome.Failed,
            };
        }
        await TellAsync(new RunFinished(outcome, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, results, values.Of(workflow.Workflow.Parameters.Concat(workflow.Workflow.Variables))));
        return outcome;

        async Task<StepResult> RunStepAsync(int index, CheckedStep step)
        {
            // The overlay is built from the environment for every step, so the values of one step never pile up on those of another.
            // The values on a step are filled in from the shared values, so they do not see each other.
            var shared = environment.WithVariables(values.Overlay);
            var used = step.Step.With is [] ? shared : shared.WithVariables([.. step.Step.With.Select(entry => entry with { Value = shared.Resolve(entry.Value) })]);
            await TellAsync(new StepStarted(index, step.Path, step.Request.Method, RequestRunner.AddressOf(step.Request, used)));
            StepFinished finished;
            try
            {
                var response = await runner.RunAsync(step.Request, step.Path, used, source, fetchToken, cancellationToken);
                string? missing = null;
                var saved = response.IsSuccess ? values.TrySave(step.Step.Saves, response, out missing) : null;
                finished = StepFinished.Of(index, response, saved, missing);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await TellAsync(new StepCancelled(index));
                return new(index, StepOutcome.Cancelled);
            }
            catch (Exception exception)
            {
                var problem = RequestProblem.KindOf(exception, cancellationToken);
                finished = new(index, StepOutcome.Failed, Error: RequestProblem.TextOf(problem), Problem: problem);
            }
            await TellAsync(finished);
            return new(index, finished.Outcome, finished.Status);
        }

        // The log is written first, so it holds the event even when telling of it fails.
        async Task TellAsync(WorkflowEvent workflowEvent)
        {
            await log.AddAsync(workflowEvent);
            await report(workflowEvent);
        }
    }
}
