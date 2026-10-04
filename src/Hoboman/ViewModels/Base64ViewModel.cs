using Hoboman.Core.Base64;
using Hoboman.Core.Environments;
using Hoboman.Core.Languages;
using Hoboman.Core.Sending;
using Hoboman.Mvvm;

namespace Hoboman.ViewModels;

// Which properties of a request's JSON go as Base64, and which of the response's are shown decoded, chosen with checkboxes beside the bodies.
public sealed class Base64ViewModel(Translator translator, TimeProvider clock) : ObservableObject
{
    // Long enough that the checkboxes do not change with every key, short enough to follow a pause.
    static readonly TimeSpan _standStill = TimeSpan.FromMilliseconds(400);

    readonly UiThread _ui = new();
    IReadOnlyList<JsonProperty> _outline = [];
    ITimer? _waiting;
    // Counts the readings of the body, so one that is done after a newer body came is left out.
    int _reading;

    // For what is saved with the request. The response is shown again when what it decodes changes.
    public event Action? Changed;

    public event Action? DecodeChanged;

    // A new list on every change, so a response being shown can tell whether the choices changed meanwhile.
    public IReadOnlyList<string> Encode { get; private set; } = [];

    public IReadOnlyList<string> Decode { get; private set; } = [];

    public IReadOnlyList<Base64Mark> BodyMarks { get; private set => Set(ref field, value); } = [];

    // While the body is not JSON, the checkboxes stay where they were, greyed, instead of blinking on and off.
    public bool BodyMarksAreCurrent { get; private set => Set(ref field, value); } = true;

    public bool EncodesWholeBody
    {
        get => Encode.Contains(JsonPath.Root);
        set
        {
            if (value != EncodesWholeBody)
            {
                ToggleEncode(JsonPath.Root);
            }
        }
    }

    public bool DecodesWholeResponse
    {
        get => Decode.Contains(JsonPath.Root);
        set
        {
            if (value != DecodesWholeResponse)
            {
                ToggleDecode(JsonPath.Root);
            }
        }
    }

    // Choices that do not fit the body are kept, so opening a request never changes it.
    // A file edited by hand can decode other values, and the response shown is then decoded again.
    public void Load(Base64Paths? paths, string body, bool useVariables = false)
    {
        _waiting?.Dispose();
        _reading++;
        var decode = paths?.Decode ?? [];
        var decodeChanged = !decode.SequenceEqual(Decode);
        (Encode, Decode) = (paths?.Encode ?? [], decode);
        Use(OutlineOf(body, useVariables), letGo: false);
        OnPropertyChanged(nameof(EncodesWholeBody));
        OnPropertyChanged(nameof(DecodesWholeResponse));
        if (decodeChanged)
        {
            DecodeChanged?.Invoke();
        }
    }

    public Base64Paths? ToPaths() => Encode.Count > 0 || Decode.Count > 0 ? new() { Encode = Encode, Decode = Decode } : null;

    // A large body takes a while to read, so it is read on the timer's thread, and only what is shown changes on the UI thread.
    public void BodyChanged(string body, bool useVariables = false)
    {
        _waiting?.Dispose();
        var reading = ++_reading;
        _waiting = clock.CreateTimer(_ =>
        {
            var outline = OutlineOf(body, useVariables);
            _ui.Post(() =>
            {
                if (reading == _reading)
                {
                    Use(outline, letGo: true);
                }
            });
        }, null, _standStill, Timeout.InfiniteTimeSpan);
    }

    public void ToggleEncode(string path)
    {
        Encode = Toggled(Encode, path);
        MarkBody();
        OnPropertyChanged(nameof(EncodesWholeBody));
        Changed?.Invoke();
    }

    public void ToggleDecode(string path)
    {
        Decode = Toggled(Decode, path);
        OnPropertyChanged(nameof(DecodesWholeResponse));
        Changed?.Invoke();
        DecodeChanged?.Invoke();
    }

    public void Relabel() => MarkBody();

    // Pure, so it can run off the UI thread with the choices of the moment.
    public static ShownResponse ShowResponse(ApiResponse response, BodyFormat format, IReadOnlyList<string> decode, Translator translator, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? saved = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var decoded = decode.Count > 0 ? Base64Json.Decode(response.Body, decode) : new(response.Body, new HashSet<string>());
        cancellationToken.ThrowIfCancellationRequested();
        var display = ResponseDisplay.Of(response with { Body = decoded.Body }, format);
        cancellationToken.ThrowIfCancellationRequested();
        var marks = display.Coloring == BodyFormat.Json && JsonOutline.Of(display.Body) is { } outline ? Base64Marks.ForResponse(outline, decode, decoded.Failed, translator, saved) : [];
        return new(display, marks, decoded.Failed.Contains(JsonPath.Root) ? translator.Of("Response.InvalidBase64") : null);
    }

    // Choosing a property takes everything inside it along, so what was chosen inside it is let go.
    static IReadOnlyList<string> Toggled(IReadOnlyList<string> paths, string path) =>
        paths.Contains(path) ? [.. paths.Where(chosen => chosen != path)] : [.. paths.Where(chosen => !JsonPath.IsInside(chosen, path)), path];

    // When enabled, a variable outside quotes is read as a number until its value is filled in when sending.
    static IReadOnlyList<JsonProperty>? OutlineOf(string body, bool useVariables) => JsonOutline.Of(useVariables ? ApiEnvironment.WithVariablesAs(body, _ => "0") : body);

    void Use(IReadOnlyList<JsonProperty>? outline, bool letGo)
    {
        if (outline is null)
        {
            BodyMarksAreCurrent = false;
            return;
        }
        _outline = outline;
        BodyMarksAreCurrent = true;
        // A chosen property that was renamed or removed is let go, so it does not come back by itself. The whole body is always there.
        if (letGo && Encode.Any(path => !IsIn(outline, path)))
        {
            Encode = [.. Encode.Where(path => IsIn(outline, path))];
            Changed?.Invoke();
        }
        MarkBody();
    }

    static bool IsIn(IReadOnlyList<JsonProperty> outline, string path) => path == JsonPath.Root || outline.Any(property => property.Path == path);

    void MarkBody() => BodyMarks = Base64Marks.ForRequest(_outline, Encode, translator);
}
