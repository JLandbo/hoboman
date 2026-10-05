using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Environments;

public sealed class EnvironmentStore(AppFolder folder, ILogger<EnvironmentStore> logger)
{
    readonly JsonFile<IReadOnlyList<ApiEnvironment>> _file = new(folder.Environments, [], logger);

    // The file is only written when an id is missing, as the app reloads on every write to it.
    public async Task<IReadOnlyList<ApiEnvironment>> AllAsync(CancellationToken cancellationToken)
    {
        var environments = await _file.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (HaveIds(environments))
        {
            return environments;
        }
        environments = await _file.UpdateAsync(WithIds, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Gave the environments ids");
        return environments;
    }

    // Names are unique in any case, so the case a name is given in does not matter.
    public async Task<ApiEnvironment?> FindAsync(string? name, CancellationToken cancellationToken) =>
        name is null ? null : (await AllAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(environment => environment.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public Task SaveAsync(IReadOnlyList<ApiEnvironment> environments, CancellationToken cancellationToken) => _file.SaveAsync(environments, cancellationToken);

    // An environment is chosen by its name, with --env and in the app, so no name is empty and no two are alike in any case.
    public static bool AreValidNames(IReadOnlyCollection<string> names) => names.All(name => name.Length > 0) && names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Count;

    static bool HaveIds(IReadOnlyList<ApiEnvironment> environments) =>
        environments.All(environment => environment.Id != Guid.Empty) && environments.DistinctBy(environment => environment.Id).Count() == environments.Count;

    // Of two environments that share an id, such as one copied by hand, the later one in the file gets a new id.
    static IReadOnlyList<ApiEnvironment> WithIds(IReadOnlyList<ApiEnvironment> environments)
    {
        var used = new HashSet<Guid>();
        return [.. environments.Select(environment => environment.Id != Guid.Empty && used.Add(environment.Id) ? environment : environment with { Id = Guid.NewGuid() })];
    }
}
