using System.Collections.ObjectModel;
using Hoboman.Mvvm;

namespace Hoboman.ViewModels;

public sealed class RequestNodeViewModel(string path, string? method, bool isFolder) : ObservableObject
{
    public string Path => path;

    public string Name => path[(path.LastIndexOf('/') + 1)..];

    public string? Method => method;

    public bool IsFolder => isFolder;

    public bool IsExpanded { get; set => Set(ref field, value); }

    public ObservableCollection<RequestNodeViewModel> Children { get; } = [];
}
