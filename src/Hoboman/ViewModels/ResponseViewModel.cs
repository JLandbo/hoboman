using Hoboman.Core.Languages;
using Hoboman.Core.Sending;
using Hoboman.Mvvm;

namespace Hoboman.ViewModels;

// The response as it is shown, shared by a tab and a workflow step. What to decode is chosen on the request, so it is shown again when that changes.
public sealed class ResponseViewModel : ObservableObject
{
    readonly Translator _translator;
    ApiResponse? _response;
    BodyFormat _bodyFormat;
    CancellationTokenSource? _formatting;

    public ResponseViewModel(Translator translator, Base64ViewModel base64)
    {
        _translator = translator;
        Base64 = base64;
        base64.DecodeChanged += ShowAgain;
    }

    public Base64ViewModel Base64 { get; }

    public ResponseDisplay? Response { get; private set => Set(ref field, value); }

    public string? ResponseBodyProblem { get; private set => Set(ref field, value); }

    public IReadOnlyList<Base64Mark> ResponseMarks { get; private set => Set(ref field, value); } = [];

    // Chosen from the Content-Type of each new response, and changed by the user when it does not fit.
    public BodyFormat BodyFormat
    {
        get => _bodyFormat;
        set
        {
            if (Set(ref _bodyFormat, value))
            {
                ShowAgain();
            }
        }
    }

    internal Task Formatting { get; private set; } = Task.CompletedTask;

    // All hosts share one view, so each keeps which section it shows.
    public ResponseSection ResponseSection { get; set => Set(ref field, value); }

    // The variables a workflow step saved, by the place in the body they came from, shown on the response with the next show.
    public IReadOnlyDictionary<string, string> Saved { get; set; } = new Dictionary<string, string>();

    public async Task ShowAsync(ApiResponse? response)
    {
        _response = response;
        Response = null;
        ResponseBodyProblem = null;
        ResponseMarks = [];
        if (response is not null)
        {
            Set(ref _bodyFormat, ResponseDisplay.FormatOf(response), nameof(BodyFormat));
            await FormatAsync(response);
        }
    }

    // What is written beside the response's properties is in the language too.
    public void ShowAgain()
    {
        if (_response is { } response)
        {
            Formatting = FormatAsync(response);
        }
    }

    // Formatting a large body takes a while, so it is kept off the UI thread, and a newer response, format or choice of what to decode wins.
    // The one before it is stopped, so quick clicks do not leave several at work.
    async Task FormatAsync(ApiResponse response)
    {
        _formatting?.Cancel();
        using var formatting = _formatting = new CancellationTokenSource();
        var format = _bodyFormat;
        var decode = Base64.Decode;
        var saved = Saved;
        try
        {
            var shown = await Task.Run(() => Base64ViewModel.ShowResponse(response, format, decode, _translator, formatting.Token, saved), formatting.Token);
            if (ReferenceEquals(response, _response) && format == _bodyFormat && ReferenceEquals(decode, Base64.Decode))
            {
                Response = shown.Display;
                ResponseMarks = shown.Marks;
                ResponseBodyProblem = shown.Problem;
            }
        }
        catch (OperationCanceledException) when (formatting.IsCancellationRequested)
        {
        }
        finally
        {
            if (_formatting == formatting)
            {
                _formatting = null;
            }
        }
    }
}
