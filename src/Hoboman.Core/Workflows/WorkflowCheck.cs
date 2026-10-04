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
    public async Task<CheckedWorkflow> CheckAsync(string name, Workflow workflow, ApiEnvironment environment, IReadOnlyDictionary<string, JsonElement> parameters, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? code = null)
    {
        var scripts = new Dictionary<string, string?>();
        foreach (var script in workflow.Steps.Select(step => step.Script).OfType<string>().Distinct())
        {
            scripts[script] = !WorkflowLibrary.IsValidScriptName(script) ? null
                : code?.GetValueOrDefault(script) ?? await workflows.LoadScriptAsync(name, script, cancellationToken).ConfigureAwait(false);
        }
        var authTexts = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var (index, step) in workflow.Steps.Index())
        {
            if (step.Script is null && step.Request is { } request)
            {
                authTexts[index] = await AuthTextsAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }
        return Check(name, workflow, environment, parameters, scripts, authTexts);
    }

    // A name the workflow declares gets its value from the workflow only, so an old value in the environment cannot hide steps in the wrong order.
    // A script reads every value through vars, so it has no names to check.
    public static CheckedWorkflow Check(string name, Workflow workflow, ApiEnvironment environment, IReadOnlyDictionary<string, JsonElement> parameters, IReadOnlyDictionary<string, string?>? scripts = null,
        IReadOnlyDictionary<int, IReadOnlyList<string>>? authTexts = null)
    {
        var problems = new List<WorkflowProblem>();
        if (workflow.Id == Guid.Empty)
        {
            problems.Add(new(WorkflowProblemKind.MissingId, null, name));
        }
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
            problems.AddRange(step.Saves.Where(save => !RunValues.IsSource(save.From)).Select(save => new WorkflowProblem(WorkflowProblemKind.InvalidSource, index, save.From)));
            if (step.Script is { } script)
            {
                if (scripts?.GetValueOrDefault(script) is not { } code)
                {
                    problems.Add(new(WorkflowProblemKind.ScriptNotFound, index, script));
                }
                else if (ScriptHost.SyntaxErrorIn(script, code) is { } error)
                {
                    problems.Add(new(WorkflowProblemKind.InvalidScript, index, error));
                }
                else
                {
                    steps.Add(new(step, null, code));
                }
                set.UnionWith(step.Saves.Select(save => save.Variable).Where(variableNames.Contains));
                continue;
            }
            if (step.Request is not { } request || string.IsNullOrWhiteSpace(request.Url))
            {
                problems.Add(new(WorkflowProblemKind.MissingUrl, index, ""));
            }
            else
            {
                var sent = request.ToApiRequest();
                steps.Add(new(step, sent));
                problems.AddRange(UnavailableIn(index, NamesUsedBy(sent, authTexts?.GetValueOrDefault(index) ?? []), declaredNames, set, environment));
            }
            set.UnionWith(step.Saves.Select(save => save.Variable).Where(variableNames.Contains));
        }
        return new(name, workflow, parameters, steps, problems);
    }

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
    async Task<IReadOnlyList<string>> AuthTextsAsync(WorkflowRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return request.Auth switch
            {
                { Kind: AuthKind.Basic } auth => [auth.UserName, .. await SecretOfAsync(request.Id, SecretKind.Password, cancellationToken).ConfigureAwait(false)],
                { Kind: AuthKind.Bearer } => await SecretOfAsync(request.Id, SecretKind.Token, cancellationToken).ConfigureAwait(false),
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

    static IEnumerable<WorkflowProblem> UnavailableIn(int index, IReadOnlySet<string> used, IReadOnlySet<string> declared, IReadOnlySet<string> set, ApiEnvironment environment) =>
        used.Where(name => declared.Contains(name) ? !set.Contains(name) : !environment.Variables.Any(variable => variable.Enabled && variable.Name == name))
            .Select(name => new WorkflowProblem(declared.Contains(name) ? WorkflowProblemKind.UsedBeforeSaved : WorkflowProblemKind.UnknownName, index, name));
}
