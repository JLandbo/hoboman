using System.Collections.ObjectModel;
using Hoboman.Core.Environments;

namespace Hoboman.ViewModels;

// One environment's credentials in the editor.
public sealed class CredentialGroupViewModel(ApiEnvironment environment, IEnumerable<CredentialDraftViewModel> drafts)
{
    public ApiEnvironment Environment => environment;

    public ObservableCollection<CredentialDraftViewModel> Drafts { get; } = [.. drafts];
}
