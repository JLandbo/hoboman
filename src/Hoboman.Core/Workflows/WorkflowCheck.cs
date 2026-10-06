using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.Requests;
using Hoboman.Core.Scripts;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Workflows;

// Everything is checked before the first call is sent, so a mistake in the set-up costs no calls.
public sealed partial class WorkflowCheck(WorkflowLibrary workflows, SecretStore secrets, ILogger<WorkflowCheck> logger)
{
    // The app gives the code that is in its editor, so a run uses it before it is saved, as it does with the rest of the workflow.
    public async Task<CheckedWorkflow> CheckAsync(Workflow workflow, ApiEnvironment environment, IReadOnlyDictionary<string, JsonElement> parameters, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? code = null)
    {
        var scripts = new Dictionary<string, string?>();
        foreach (var script in workflow.Steps.Select(step => step.Script).OfType<string>().Distinct())
        {
            scripts[script] = !WorkflowLibrary.IsValidScriptName(script) ? null
                : code?.GetValueOrDefault(script) ?? await workflows.LoadScriptAsync(workflow.Id, script, cancellationToken).ConfigureAwait(false);
        }
        var authTexts = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var (index, step) in workflow.Steps.Index())
        {
            if (step.Kind == StepKind.Request && step.Request is { } request)
            {
                authTexts[index] = request.Auth is { Kind: AuthKind.Inherit }
                    ? await AuthTextsAsync(workflow.Id, workflow.Auth, cancellationToken).ConfigureAwait(false)
                    : await AuthTextsAsync(request.Id, request.Auth, cancellationToken).ConfigureAwait(false);
            }
        }
        return Check(workflow, environment, parameters, scripts, authTexts);
    }

    // A name the workflow declares gets its value from the workflow only, so an old value in the environment cannot hide steps in the wrong order.
    // A script reads every value through vars, so it has no names to check.
    public static CheckedWorkflow Check(Workflow workflow, ApiEnvironment environment, IReadOnlyDictionary<string, JsonElement> parameters, IReadOnlyDictionary<string, string?>? scripts = null,
        IReadOnlyDictionary<int, IReadOnlyList<string>>? authTexts = null)
    {
        var problems = new List<WorkflowProblem>();
        var declared = workflow.Parameters.Concat(workflow.Variables);
        problems.AddRange(declared.Where(value => !IsValidName(value.Name)).Select(value => new WorkflowProblem(WorkflowProblemKind.InvalidName, null, value.Name)));
        problems.AddRange(declared.GroupBy(value => value.Name).Where(group => group.Count() > 1).Select(group => new WorkflowProblem(WorkflowProblemKind.DuplicateName, null, group.Key)));
        var parameterNames = workflow.Parameters.Select(parameter => parameter.Name).ToHashSet();
        var variableNames = workflow.Variables.Select(variable => variable.Name).ToHashSet();
        var declaredNames = parameterNames.Concat(variableNames).ToHashSet();
        problems.AddRange(parameters.Keys.Where(key => !parameterNames.Contains(key)).Select(key => new WorkflowProblem(WorkflowProblemKind.UnknownParameter, null, key)));
        problems.AddRange(workflow.Parameters.Where(parameter => !parameter.HasDefault && !parameters.ContainsKey(parameter.Name))
            .Select(parameter => new WorkflowProblem(WorkflowProblemKind.MissingParameter, null, parameter.Name)));
        // A parameter without a value is told of above, so every parameter counts as set here.
        var set = parameterNames.Concat(workflow.Variables.Where(variable => variable.HasDefault).Select(variable => variable.Name)).ToHashSet();
        var steps = new List<CheckedStep>();
        foreach (var (index, step) in workflow.Steps.Index())
        {
            problems.AddRange(step.Saves.Where(save => !variableNames.Contains(save.Variable)).Select(save => new WorkflowProblem(WorkflowProblemKind.NotAVariable, index, save.Variable)));
            problems.AddRange(step.Saves.Where(save => !RunValues.CanSave(save.From)).Select(save => new WorkflowProblem(WorkflowProblemKind.InvalidSource, index, save.From)));
            if (step.IsMixed)
            {
                problems.Add(new(WorkflowProblemKind.MixedStep, index, ""));
            }
            // Only a script returns an output of its own.
            if (step.Output is not null && step.Kind != StepKind.Script)
            {
                problems.Add(new(WorkflowProblemKind.InvalidOutput, index, step.Output.Value.ToString()));
            }
            if (step.Retry is { } retry && !IsValid(retry, step.Kind))
            {
                problems.Add(new(WorkflowProblemKind.InvalidRetry, index, retry.Until ?? ""));
            }
            switch (step.Kind)
            {
                case StepKind.Script:
                    if (scripts?.GetValueOrDefault(step.Script!) is not { } code)
                    {
                        problems.Add(new(WorkflowProblemKind.ScriptNotFound, index, step.Script!));
                    }
                    else if (ScriptHost.SyntaxErrorIn(step.Script!, code) is { } error)
                    {
                        problems.Add(new(WorkflowProblemKind.InvalidScript, index, error));
                    }
                    else
                    {
                        steps.Add(new(step, null, code));
                    }
                    break;
                // A wait has no answer to save from.
                case StepKind.Delay when step.DelaySeconds is < 1 or > MaxDelaySeconds || step.Saves.Count > 0:
                    problems.Add(new(WorkflowProblemKind.InvalidDelay, index, step.DelaySeconds!.Value.ToString(CultureInfo.InvariantCulture)));
                    break;
                case StepKind.Delay:
                    steps.Add(new(step, null));
                    break;
                case StepKind.Request when step.Request is { } request && !string.IsNullOrWhiteSpace(request.Url):
                    var sent = request.ToApiRequest();
                    steps.Add(new(step, sent));
                    var auth = authTexts?.GetValueOrDefault(index) ?? [];
                    problems.AddRange(UnavailableIn(index, NamesUsedBy(sent, auth), NamesUsedBy(sent with { UseEnvironmentVariablesInBody = false }, auth), declaredNames, set, environment));
                    break;
                default:
                    problems.Add(new(WorkflowProblemKind.MissingUrl, index, ""));
                    break;
            }
            set.UnionWith(step.Saves.Select(save => save.Variable).Where(variableNames.Contains));
        }
        return new(workflow, parameters, steps, problems);
    }

    public const int MaxDelaySeconds = 300;

    public const int MaxRetryTimes = 100;

    // Only a call can give another answer the next time, so a script or a wait is not tried again.
    static bool IsValid(WorkflowRetry retry, StepKind kind) =>
        kind == StepKind.Request && (retry.Until is null) == (retry.Value is null) && (retry.Until is null || RunValues.IsSource(retry.Until))
        && (retry.StopIf is null) == (retry.StopEquals is null) && (retry.StopIf is null || RunValues.IsSource(retry.StopIf))
        && retry.Times is >= 1 and <= MaxRetryTimes && retry.WaitMilliseconds is >= 0 and <= MaxDelaySeconds * 1000;

    public static bool IsValidName(string name) => name.Length > 0 && name.IndexOfAny(['{', '}']) < 0;

    // The places where {{name}} is filled in when the request is sent. OAuth2 is not among them, as it is filled in from the environment alone.
    public static IReadOnlySet<string> NamesUsedBy(ApiRequest request, IEnumerable<string> authTexts)
    {
        var texts = request.Query.Concat(request.Headers).Where(entry => entry.Enabled).SelectMany(entry => new[] { entry.Name, entry.Value }).Append(request.Url).Concat(authTexts);
        if (request.UseEnvironmentVariablesInBody && request.BodyKind != BodyKind.None)
        {
            texts = texts.Append(request.Body);
        }
        return texts.SelectMany(NamesIn).ToHashSet();
    }

    public static IEnumerable<string> NamesIn(string text) => ApiEnvironment.VariablesIn(text).Select(match => match.Groups[1].Value);

    // A script reads a value as vars.name, so those are the names it is shown to use. Other ways of reading vars are not seen.
    public static IEnumerable<string> VarsIn(string code) => ScriptVars().Matches(code).Select(match => match.Groups[1].Value);

    [GeneratedRegex(@"\bvars\.([A-Za-z_$][\w$]*)")]
    private static partial Regex ScriptVars();

    // The auth is filled in like the rest of the request, so its user name and saved secret can use names too.
    async Task<IReadOnlyList<string>> AuthTextsAsync(Guid owner, AuthSettings? settings, CancellationToken cancellationToken)
    {
        try
        {
            return settings switch
            {
                { Kind: AuthKind.Basic } auth => [auth.UserName, .. await SecretOfAsync(owner, SecretKind.Password, cancellationToken).ConfigureAwait(false)],
                { Kind: AuthKind.Bearer } => await SecretOfAsync(owner, SecretKind.Token, cancellationToken).ConfigureAwait(false),
                _ => [],
            };
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogWarning(exception, "Could not read the secrets of a step, so the names they use are not checked");
            return [];
        }
    }

    // A secret that is not saved uses no names, and the call tells of it when it is sent.
    async Task<IReadOnlyList<string>> SecretOfAsync(Guid id, SecretKind kind, CancellationToken cancellationToken) =>
        id != Guid.Empty && await secrets.OfAsync(id, kind, cancellationToken).ConfigureAwait(false) is { } secret ? [secret] : [];

    // A name only in the body that neither the workflow nor the environment has is left as written when sent, as in a tab,
    // so a template's own {{names}}, such as Handlebars, can go with the workflow's.
    static IEnumerable<WorkflowProblem> UnavailableIn(int index, IReadOnlySet<string> used, IReadOnlySet<string> usedOutsideBody, IReadOnlySet<string> declared, IReadOnlySet<string> set, ApiEnvironment environment) =>
        used.Where(name => declared.Contains(name) ? !set.Contains(name) : usedOutsideBody.Contains(name) && !environment.Variables.Any(variable => variable.Enabled && variable.Name == name))
            .Select(name => new WorkflowProblem(declared.Contains(name) ? WorkflowProblemKind.UsedBeforeSaved : WorkflowProblemKind.UnknownName, index, name));
}
