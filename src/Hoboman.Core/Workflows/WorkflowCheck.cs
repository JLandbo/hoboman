using System.Text.Json;
using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.Requests;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Workflows;

// Everything is checked before the first call is sent, so a mistake in the set-up costs no calls.
public sealed class WorkflowCheck(RequestLibrary library, SecretStore secrets, ILogger<WorkflowCheck> logger)
{
    public async Task<CheckedWorkflow> CheckAsync(string name, Workflow workflow, ApiEnvironment environment, IReadOnlyDictionary<string, JsonElement> parameters, CancellationToken cancellationToken)
    {
        var requests = await library.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        var authTexts = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var step in workflow.Steps)
        {
            if (MatchesOf(step.Request, requests) is [var (path, request)] && !authTexts.ContainsKey(path))
            {
                authTexts[path] = await AuthTextsAsync(path, request, cancellationToken).ConfigureAwait(false);
            }
        }
        return Check(name, workflow, requests, authTexts, environment, parameters);
    }

    // A name the workflow declares gets its value from the workflow only, so an old value in the environment cannot hide steps in the wrong order.
    public static CheckedWorkflow Check(string name, Workflow workflow, IReadOnlyList<(string Name, ApiRequest? Request)> requests, IReadOnlyDictionary<string, IReadOnlyList<string>> authTexts,
        ApiEnvironment environment, IReadOnlyDictionary<string, JsonElement> parameters)
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
            problems.AddRange(step.With.Where(entry => !IsValidName(entry.Name)).Select(entry => new WorkflowProblem(WorkflowProblemKind.InvalidName, index, entry.Name)));
            problems.AddRange(step.Saves.Where(save => !variableNames.Contains(save.Variable)).Select(save => new WorkflowProblem(WorkflowProblemKind.NotAVariable, index, save.Variable)));
            problems.AddRange(step.Saves.Where(save => !RunValues.IsSource(save.From)).Select(save => new WorkflowProblem(WorkflowProblemKind.InvalidSource, index, save.From)));
            switch (MatchesOf(step.Request, requests))
            {
                case [var (path, request)]:
                    steps.Add(new(step, path, request));
                    problems.AddRange(UnavailableIn(index, step, NamesUsedBy(request, authTexts.GetValueOrDefault(path, [])), declaredNames, set, environment));
                    break;
                case [] when step.Request == Guid.Empty:
                    problems.Add(new(WorkflowProblemKind.MissingRequest, index, ""));
                    break;
                case []:
                    problems.Add(new(WorkflowProblemKind.RequestNotFound, index, $"{step.Request}"));
                    break;
                case var matches:
                    problems.Add(new(WorkflowProblemKind.SharedRequestId, index, string.Join(", ", matches.Select(match => match.Name))));
                    break;
            }
            set.UnionWith(step.Saves.Select(save => save.Variable).Where(variableNames.Contains));
        }
        // A request that cannot be read may be the one that was not found.
        if (problems.Any(problem => problem.Kind == WorkflowProblemKind.RequestNotFound) && requests.Where(request => request.Request is null).Select(request => request.Name).ToList() is { Count: > 0 } unreadable)
        {
            problems.Add(new(WorkflowProblemKind.UnreadableRequests, null, string.Join(", ", unreadable)));
        }
        return new(name, workflow, parameters, steps, problems);
    }

    public static bool IsValidName(string name) => name.Length > 0 && name.IndexOfAny(['{', '}']) < 0;

    // The places where {{name}} is filled in when the request is sent. OAuth2 is not among them, as it is filled in from the environment alone.
    public static IReadOnlySet<string> NamesUsedBy(ApiRequest request, IReadOnlyList<string> authTexts)
    {
        var texts = request.Query.Concat(request.Headers).Where(entry => entry.Enabled).SelectMany(entry => new[] { entry.Name, entry.Value }).Append(request.Url).Concat(authTexts);
        if (request.UseEnvironmentVariablesInBody && request.BodyKind != BodyKind.None)
        {
            texts = texts.Append(request.Body);
        }
        return texts.SelectMany(NamesIn).ToHashSet();
    }

    public static IEnumerable<string> NamesIn(string text) => ApiEnvironment.VariablesIn(text).Select(match => match.Groups[1].Value);

    public async Task<IReadOnlySet<string>> NamesUsedByAsync(string path, ApiRequest request, CancellationToken cancellationToken) =>
        NamesUsedBy(request, await AuthTextsAsync(path, request, cancellationToken).ConfigureAwait(false));

    // A value on the step wins for that call. Its own text is filled in from the rest, so it follows the same rule without it.
    static IEnumerable<WorkflowProblem> UnavailableIn(int index, WorkflowStep step, IReadOnlySet<string> used, IReadOnlySet<string> declared, IReadOnlySet<string> set, ApiEnvironment environment)
    {
        var given = step.With.Where(entry => entry.Enabled).ToList();
        var givenNames = given.Select(entry => entry.Name).Where(IsValidName).ToHashSet();
        var unused = givenNames.Where(name => !used.Contains(name)).Select(name => new WorkflowProblem(WorkflowProblemKind.UnusedWithName, index, name));
        var needed = used.Where(name => !givenNames.Contains(name)).Concat(given.SelectMany(entry => NamesIn(entry.Value))).Distinct();
        var missing = needed.Where(name => declared.Contains(name) ? !set.Contains(name) : !environment.Variables.Any(variable => variable.Enabled && variable.Name == name))
            .Select(name => new WorkflowProblem(declared.Contains(name) ? WorkflowProblemKind.UsedBeforeSaved : WorkflowProblemKind.UnknownName, index, name));
        return [.. unused, .. missing];
    }

    // The auth is filled in like the rest of the request, so its user name and saved secret can use names too.
    async Task<IReadOnlyList<string>> AuthTextsAsync(string path, ApiRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var auth = await library.AuthOfAsync(path, request, cancellationToken).ConfigureAwait(false);
            return auth.Settings.Kind switch
            {
                AuthKind.Basic => [auth.Settings.UserName, .. await SecretOfAsync(auth, SecretKind.Password, cancellationToken).ConfigureAwait(false)],
                AuthKind.Bearer => await SecretOfAsync(auth, SecretKind.Token, cancellationToken).ConfigureAwait(false),
                _ => [],
            };
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogWarning(exception, "Could not read the auth of {Name}, so the names it uses are not checked", path);
            return [];
        }
    }

    // A secret that is not saved uses no names, and the call tells of it when it is sent.
    async Task<IReadOnlyList<string>> SecretOfAsync(AuthSource auth, SecretKind kind, CancellationToken cancellationToken) =>
        await secrets.OfAsync(auth.SecretsId, kind, cancellationToken).ConfigureAwait(false) is { } secret ? [secret] : [];

    static IReadOnlyList<(string Name, ApiRequest Request)> MatchesOf(Guid id, IReadOnlyList<(string Name, ApiRequest? Request)> requests) =>
        id == Guid.Empty ? [] : [.. requests.Where(request => request.Request?.Id == id).Select(request => (request.Name, request.Request!))];
}
