using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Environments;

public sealed class EnvironmentStore(AppFolder folder, ILogger<EnvironmentStore> logger)
{
    readonly JsonFile<IReadOnlyList<ApiEnvironment>> _file = new(folder.Environments, [], logger);

    public Task<IReadOnlyList<ApiEnvironment>> AllAsync(CancellationToken cancellationToken) => _file.LoadAsync(cancellationToken);

    public async Task<ApiEnvironment?> FindAsync(string? name, CancellationToken cancellationToken) =>
        name is null ? null : (await AllAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(environment => environment.Name == name);

    public Task SaveAsync(IReadOnlyList<ApiEnvironment> environments, CancellationToken cancellationToken) => _file.SaveAsync(environments, cancellationToken);
}
