using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.Requests;
using Hoboman.Core.Scripts;
using Hoboman.Core.Sending;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Workflows;

// One step at a time in the order of the list, and the first that fails skips the rest.
// The calls are awaited without ConfigureAwait(false), so an event is told where the caller called from, such as the UI thread.
// Each event is awaited before the next, as Progress<T> can change the order of the lines in a console.
// A step sends with its own auth or the workflow's, and a token for client credentials is fetched as from a tab. A step has no place in the history, as the run log holds every call.
public sealed class WorkflowRunner(IRequestSender sender, AppFolder folder, TimeProvider clock, ILogger<WorkflowRunner> logger)
{
    public async Task<RunOutcome> RunAsync(CheckedWorkflow workflow, ApiEnvironment environment, Func<AuthSource, Task<bool>> fetchToken, Func<WorkflowEvent, Task> report,
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
        await TellAsync(new RunStarted(log.RunId, workflow.Workflow.Id, workflow.Workflow.Name, environment.Name, log.FilePath, values.Of(workflow.Workflow.Parameters)));
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
            var used = environment.WithVariables(values.Overlay);
            await TellAsync(step.Step.Kind switch
            {
                StepKind.Script => new StepStarted(index, step.Title, "JS", ""),
                StepKind.Delay => new StepStarted(index, step.Title, "WAIT", ""),
                _ => new StepStarted(index, step.Title, step.Request!.Method, RequestRunner.AddressOf(step.Request, used)),
            });
            StepFinished finished;
            try
            {
                finished = step.Step.Kind switch
                {
                    StepKind.Delay => await WaitAsync(index, step.Step.DelaySeconds!.Value),
                    StepKind.Script => Finish(index, step, await RunScriptAsync(step)),
                    _ => await SendUntilReadyAsync(index, step, used),
                };
            }
            catch (ScriptException exception)
            {
                finished = new(index, StepOutcome.Failed, Error: exception.Message);
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

        StepFinished Finish(int index, CheckedStep step, ApiResponse response)
        {
            string? missing = null;
            var saved = response.IsSuccess ? values.TrySave(step.Step.Saves, response, out missing) : null;
            return StepFinished.Of(index, response, saved, missing);
        }

        // A wait has no answer, so it tells only how long it took.
        async Task<StepFinished> WaitAsync(int index, int seconds)
        {
            var waitStarted = clock.GetTimestamp();
            await Task.Delay(TimeSpan.FromSeconds(seconds), clock, cancellationToken);
            return new(index, StepOutcome.Succeeded, ElapsedMs: (long)clock.GetElapsedTime(waitStarted).TotalMilliseconds);
        }

        // A step that retries is sent again until its answer is ready, as an API can still be making what is asked for.
        // Only what comes back from the server or the network can change, so an error made before anything is sent is not tried again.
        async Task<StepFinished> SendUntilReadyAsync(int index, CheckedStep step, ApiEnvironment used)
        {
            if (step.Step.Retry is not { } retry)
            {
                return Finish(index, step, await SendAsync(step.Request!, used));
            }
            for (var attempt = 1; ; attempt++)
            {
                var last = attempt >= retry.Times;
                StepRetrying notReady;
                try
                {
                    var response = await SendAsync(step.Request!, used);
                    if (retry.StopIf is { } stopIf && RunValues.ValueOf(stopIf, response) is { } stop && string.Equals(RunValues.TextOf(stop), retry.StopEquals, StringComparison.OrdinalIgnoreCase))
                    {
                        return StepFinished.StoppedOf(index, response, stopIf, RunValues.TextOf(stop)) with { Attempts = attempt };
                    }
                    var value = retry.Until is { } until && RunValues.ValueOf(until, response) is { } found ? RunValues.TextOf(found) : null;
                    var finished = !response.IsSuccess ? StepFinished.Of(index, response, null, null)
                        : retry.Until is not null && !string.Equals(value, retry.Value, StringComparison.OrdinalIgnoreCase) ? StepFinished.NotReadyOf(index, response, attempt)
                        : Finish(index, step, response);
                    if (finished.Outcome == StepOutcome.Succeeded || last)
                    {
                        return finished with { Attempts = attempt };
                    }
                    notReady = new(index, attempt, response.StatusCode, value?[..Math.Min(value.Length, 200)]);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    var problem = RequestProblem.KindOf(exception, cancellationToken);
                    if (last || problem is not (RequestProblemKind.TimedOut or RequestProblemKind.NetworkFailed))
                    {
                        return new(index, StepOutcome.Failed, Attempts: attempt, Error: RequestProblem.TextOf(problem), Problem: problem);
                    }
                    notReady = new(index, attempt, Error: RequestProblem.TextOf(problem));
                }
                await TellAsync(notReady);
                await Task.Delay(TimeSpan.FromSeconds(retry.WaitSeconds), clock, cancellationToken);
            }
        }

        // A step that inherits uses the workflow's auth, and one that inherits from a workflow without auth sends none.
        Task<ApiResponse> SendAsync(ApiRequest request, ApiEnvironment used)
        {
            var auth = request.Auth.Kind == AuthKind.Inherit
                ? new AuthSource(workflow.Workflow.Id, workflow.Workflow.Auth ?? AuthSettings.None)
                : new AuthSource(request.Id, request.Auth);
            return TokenRetry.SendAsync(() => sender.SendAsync(request, auth, used, cancellationToken), () => auth, fetchToken);
        }

        // A script answers like a call, so its output is saved, shown and logged as a body with only the type of its output.
        // It runs off the caller's thread, as it can take a while and the caller may be the UI thread.
        // A script that returns nothing fails only when the step has something to save.
        async Task<ApiResponse> RunScriptAsync(CheckedStep step)
        {
            var scriptStarted = Stopwatch.GetTimestamp();
            // The environment's values are in vars too, but a name the workflow declares gets its value from the workflow only, as in a request.
            var declared = workflow.Workflow.Parameters.Concat(workflow.Workflow.Variables).ToList();
            var names = declared.Select(value => value.Name).ToHashSet();
            var all = environment.Variables.Where(variable => variable.Enabled && !names.Contains(variable.Name)).DistinctBy(variable => variable.Name)
                .ToDictionary(variable => variable.Name, variable => JsonSerializer.SerializeToElement(variable.Value));
            foreach (var (name, value) in values.Of(declared))
            {
                all[name] = value;
            }
            var kind = step.Step.Output ?? ScriptOutput.Json;
            var output = await Task.Run(() => ScriptHost.Run(step.Step.Script!, step.Code!, all, kind, cancellationToken), cancellationToken)
                ?? (step.Step.Saves is [] ? "" : throw new ScriptException($"{step.Step.Script} returned nothing to save."));
            var bytes = Encoding.UTF8.GetBytes(output);
            return new(200, "OK", (long)Stopwatch.GetElapsedTime(scriptStarted).TotalMilliseconds, bytes.Length, [new("Content-Type", kind.ContentType)], output) { Bytes = bytes };
        }

        // The log is written first, so it holds the event even when telling of it fails.
        async Task TellAsync(WorkflowEvent workflowEvent)
        {
            await log.AddAsync(workflowEvent);
            await report(workflowEvent);
        }
    }
}
