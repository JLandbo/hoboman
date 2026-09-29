using Hoboman.Core.Environments;
using Hoboman.Mvvm;

namespace Hoboman.ViewModels;

public sealed class EnvironmentDraftViewModel : ObservableObject
{
    public EnvironmentDraftViewModel(ApiEnvironment environment, bool isNew = false)
    {
        OriginalName = isNew ? null : environment.Name;
        Name = environment.Name;
        Variables.Load(environment.Variables);
    }

    public string? OriginalName { get; }

    public string Name { get; set => Set(ref field, value); }

    public KeyValueListViewModel Variables { get; } = new();

    public ApiEnvironment ToEnvironment() => new(Name.Trim(), Variables.ToList());
}
