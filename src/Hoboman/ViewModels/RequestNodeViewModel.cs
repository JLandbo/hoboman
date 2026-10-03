using System.Collections.ObjectModel;
using Hoboman.Core.Requests;
using Hoboman.Mvvm;

namespace Hoboman.ViewModels;

public sealed class RequestNodeViewModel(string path, string? method, bool isFolder, Guid id = default) : ObservableObject
{
    public string Path => path;

    public string Name => RequestLibrary.LastPartOf(path);

    // A saved request is known by its id, so it keeps its place when it is renamed or moved outside the app. Without an id of its own (Guid.Empty) it is known by its path.
    public string OrderKey => IsDraft ? $"\0{Tab!.Id}" : IsFolder ? $"{Path}/" : id == Guid.Empty ? Path : $"{id}";

    public string? Method => method;

    public bool IsFolder => isFolder;

    public RequestTabViewModel? Tab { get; set => Set(ref field, value); }

    public bool IsDraft { get; init; }

    public bool IsActive { get; set => Set(ref field, value); }

    public bool IsExpanded { get; set => Set(ref field, value); }

    public ObservableCollection<RequestNodeViewModel> Children { get; } = [];
}
