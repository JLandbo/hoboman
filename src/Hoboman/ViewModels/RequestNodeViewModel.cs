using System.Collections.ObjectModel;
using Hoboman.Core.Requests;
using Hoboman.Mvvm;

namespace Hoboman.ViewModels;

public sealed class RequestNodeViewModel(string path, string? method, bool isFolder) : ObservableObject
{
    public string Path => path;

    public string Name => RequestLibrary.LastPartOf(path);

    public string OrderKey => IsDraft ? $"\0{Tab!.Id}" : IsFolder ? $"{Path}/" : Path;

    public string? Method => method;

    public bool IsFolder => isFolder;

    public RequestTabViewModel? Tab { get; set => Set(ref field, value); }

    public bool IsDraft { get; init; }

    public bool IsActive { get; set => Set(ref field, value); }

    public bool IsExpanded { get; set => Set(ref field, value); }

    public ObservableCollection<RequestNodeViewModel> Children { get; } = [];
}
