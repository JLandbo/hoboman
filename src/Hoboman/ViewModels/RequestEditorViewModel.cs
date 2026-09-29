using Hoboman.Mvvm;

namespace Hoboman.ViewModels;

public sealed class RequestEditorViewModel : ObservableObject
{
    public string Method { get; set => Set(ref field, value); } = "GET";

    public string Url { get; set => Set(ref field, value); } = "";
}
