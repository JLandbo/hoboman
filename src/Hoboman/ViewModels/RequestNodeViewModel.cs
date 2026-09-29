using System.Collections.ObjectModel;
using Hoboman.Core.Requests;
using Hoboman.Mvvm;

namespace Hoboman.ViewModels;

public sealed class RequestNodeViewModel(string path, string? method, bool isFolder) : ObservableObject
{
    public string Path => path;

    public string Name => RequestLibrary.LastPartOf(path);

    public string? Method => method;

    public bool IsFolder => isFolder;

    public bool IsExpanded { get; set => Set(ref field, value); }

    public ObservableCollection<RequestNodeViewModel> Children { get; } = [];
}
