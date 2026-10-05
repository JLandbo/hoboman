using System.Collections.ObjectModel;
using Hoboman.Mvvm;

namespace Hoboman.ViewModels;

// A draft is its tab's, so it has the tab's id until it is saved.
public sealed class RequestNodeViewModel(Guid id, string name, string? method, bool isFolder) : ObservableObject
{
    public Guid Id => id;

    public string Name => name;

    // The folder the row is shown in.
    public Guid? ParentId { get; init; }

    public string OrderKey => IsDraft ? $"\0{id}" : $"{id}";

    public string? Method => method;

    public bool IsFolder => isFolder;

    public RequestTabViewModel? Tab { get; set => Set(ref field, value); }

    public bool IsDraft { get; init; }

    public bool IsActive { get; set => Set(ref field, value); }

    public bool IsExpanded { get; set => Set(ref field, value); }

    // A search hides the rows that do not match, while they stay in the tree.
    public bool IsShown { get; set => Set(ref field, value); } = true;

    public ObservableCollection<RequestNodeViewModel> Children { get; } = [];
}
