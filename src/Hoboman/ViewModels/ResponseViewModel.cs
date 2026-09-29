using Hoboman.Mvvm;

namespace Hoboman.ViewModels;

public sealed class ResponseViewModel : ObservableObject
{
    public string Summary { get; set => Set(ref field, value); } = "";

    public string Body { get; set => Set(ref field, value); } = "";
}
