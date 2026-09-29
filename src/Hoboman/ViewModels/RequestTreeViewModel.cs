using System.Collections.ObjectModel;

namespace Hoboman.ViewModels;

public sealed class RequestTreeViewModel
{
    public ObservableCollection<RequestNodeViewModel> Nodes { get; } = [];
}

public sealed class RequestNodeViewModel(string name)
{
    public string Name => name;

    public ObservableCollection<RequestNodeViewModel> Children { get; } = [];
}
