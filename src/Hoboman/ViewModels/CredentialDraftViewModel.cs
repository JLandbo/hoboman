using Hoboman.Mvvm;

namespace Hoboman.ViewModels;

public sealed class CredentialDraftViewModel(Guid id, Guid environmentId, string name, AuthViewModel auth) : ObservableObject
{
    public Guid Id => id;

    public Guid EnvironmentId => environmentId;

    public string Name { get; set => Set(ref field, value); } = name;

    public AuthViewModel Auth => auth;
}
