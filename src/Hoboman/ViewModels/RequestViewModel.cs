using System.Runtime.CompilerServices;
using Hoboman.Core.Environments;
using Hoboman.Core.Languages;
using Hoboman.Core.Requests;
using Hoboman.Mvvm;

namespace Hoboman.ViewModels;

// The request as it is edited, shared by a tab and a workflow step, so both edit it the same way. Saving, sending and auth belong to whoever holds it.
public sealed class RequestViewModel : ObservableObject
{
    readonly Translator _translator;
    readonly EnvironmentsViewModel _environments;
    LayoutProblem? _layoutProblem;
    bool _layingOut;

    public RequestViewModel(Translator translator, TimeProvider clock, EnvironmentsViewModel environments)
    {
        _translator = translator;
        _environments = environments;
        Query.Changed += OnChanged;
        Headers.Changed += OnChanged;
        Base64 = new(translator, clock);
        Base64.Changed += OnChanged;
    }

    public static IReadOnlyList<string> Methods { get; } = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    public event Action? Changed;

    public string Method { get; set => Change(ref field, value); } = "GET";

    public string Url { get; set => Change(ref field, value); } = "";

    public KeyValueListViewModel Query { get; } = new();

    public KeyValueListViewModel Headers { get; } = new();

    public BodyKind BodyKind
    {
        get;
        set
        {
            Change(ref field, value);
            ShowLayoutProblem(null);
        }
    }

    public string Body
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                OnChanged();
                ShowLayoutProblem(null);
                Base64.BodyChanged(value, UseEnvironmentVariablesInBody);
            }
        }
    } = "";

    public bool UseEnvironmentVariablesInBody
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                OnChanged();
                ShowLayoutProblem(null);
                Base64.BodyChanged(Body, value);
            }
        }
    }

    // Why the body could not be laid out, until it or its kind changes, in the language of the moment.
    public string? BodyLayoutProblem => _layoutProblem switch
    {
        LayoutProblem.NotJson => _translator.Of("Body.NotJson"),
        LayoutProblem.NotXml => _translator.Of("Body.NotXml"),
        LayoutProblem.NeedsVariables => _translator.Of("Body.NeedsVariables"),
        _ => null,
    };

    public Base64ViewModel Base64 { get; }

    // The body goes before the Base64 choices, as a new body starts a check of them that loading them ends.
    public void Load(ApiRequest request)
    {
        Method = request.Method;
        Url = request.Url;
        Query.Load(request.Query);
        Headers.Load(request.Headers);
        BodyKind = request.BodyKind;
        Body = request.Body;
        UseEnvironmentVariablesInBody = request.UseEnvironmentVariablesInBody;
        Base64.Load(request.Base64, request.Body, request.UseEnvironmentVariablesInBody);
    }

    // Without an id or auth, which belong to whoever holds the request.
    public ApiRequest ToRequest() => new()
    {
        Method = Method,
        Url = Url,
        Query = Query.ToList(),
        Headers = Headers.ToList(),
        BodyKind = BodyKind,
        Body = Body,
        UseEnvironmentVariablesInBody = UseEnvironmentVariablesInBody,
        Base64 = Base64.ToPaths(),
    };

    // Laid out off the UI thread, as a large body takes a while, and given to the view rather than set here, so the editor can take it in the way typing is, and it can be undone.
    // A click while one is at work would only do the same work again, so it is left out.
    public async Task<string?> LaidOutBodyAsync()
    {
        if (_layingOut || BodyKind is not (BodyKind.Json or BodyKind.Xml))
        {
            return null;
        }
        _layingOut = true;
        try
        {
            var (body, kind, environment, useVariables) = (Body, BodyKind, EnvironmentOrNone(), UseEnvironmentVariablesInBody);
            var (laidOut, problem) = await Task.Run<(string? LaidOut, LayoutProblem? Problem)>(() =>
                BodyLayout.Of(body, kind, useVariables) is { } text ? (text, null) : (null, ProblemOf(body, kind, environment, useVariables)));
            // The body or how it is interpreted changed while it was laid out.
            if (body != Body || kind != BodyKind || environment != EnvironmentOrNone() || useVariables != UseEnvironmentVariablesInBody)
            {
                return null;
            }
            ShowLayoutProblem(problem);
            return laidOut;
        }
        finally
        {
            _layingOut = false;
        }
    }

    public void Relabel()
    {
        OnPropertyChanged(nameof(BodyLayoutProblem));
        Base64.Relabel();
    }

    ApiEnvironment EnvironmentOrNone() => _environments.Selected ?? ApiEnvironment.None;

    static LayoutProblem ProblemOf(string body, BodyKind kind, ApiEnvironment environment, bool useVariables) =>
        useVariables && BodyLayout.NeedsVariables(body, kind, environment) ? LayoutProblem.NeedsVariables
        : kind == BodyKind.Xml ? LayoutProblem.NotXml
        : LayoutProblem.NotJson;

    void ShowLayoutProblem(LayoutProblem? problem)
    {
        if (problem != _layoutProblem)
        {
            _layoutProblem = problem;
            OnPropertyChanged(nameof(BodyLayoutProblem));
        }
    }

    enum LayoutProblem { NotJson, NotXml, NeedsVariables }

    void Change<T>(ref T storage, T value, [CallerMemberName] string? name = null)
    {
        if (Set(ref storage, value, name))
        {
            OnChanged();
        }
    }

    void OnChanged() => Changed?.Invoke();
}
