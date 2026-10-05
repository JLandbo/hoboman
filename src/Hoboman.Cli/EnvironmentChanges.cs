using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.Requests;
using Hoboman.Core.Storage;

namespace Hoboman.Cli;

// Makes, renames, replaces and deletes environments by the app's rules, and forgets what a deleted one had, as the app does.
// The app's environment window finds a change made while it is open and does not save over it.
sealed class EnvironmentChanges(EnvironmentStore store, SecretStore secrets, CredentialStore credentials)
{
    // A name that another environment has, in any case, cannot be taken, as environments are chosen by their names.
    public async Task<bool> IsTakenAsync(string name, Guid? by, CancellationToken cancellationToken) =>
        !EnvironmentStore.AreValidNames([.. (await store.AllAsync(cancellationToken)).Where(environment => environment.Id != by).Select(environment => environment.Name), name]);

    public async Task<Guid> CreateAsync(string name, CancellationToken cancellationToken)
    {
        var created = new ApiEnvironment(name, []) { Id = Guid.NewGuid() };
        await store.SaveAsync([.. await store.AllAsync(cancellationToken), created], cancellationToken);
        return created.Id;
    }

    public Task RenameAsync(Guid id, string name, CancellationToken cancellationToken) => ChangeAsync(id, environment => environment with { Name = name }, cancellationToken);

    // Only the variables are replaced, as the name is changed with rename and the id stays.
    public Task UpdateAsync(Guid id, IReadOnlyList<KeyValue> variables, CancellationToken cancellationToken) =>
        ChangeAsync(id, environment => environment with { Variables = variables }, cancellationToken);

    // Its tokens and the credentials saved for it go with it, as nothing else can use them. The environment is gone once its file is saved,
    // and its id is never used again, so what it leaves behind when they cannot be cleared does no harm, as in the app.
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await store.SaveAsync([.. (await store.AllAsync(cancellationToken)).Where(environment => environment.Id != id)], cancellationToken);
        try
        {
            await secrets.ForgetEnvironmentsAsync(new HashSet<Guid> { id }, cancellationToken);
            await credentials.ForgetEnvironmentsAsync(new HashSet<Guid> { id }, cancellationToken);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
        }
    }

    async Task ChangeAsync(Guid id, Func<ApiEnvironment, ApiEnvironment> change, CancellationToken cancellationToken)
    {
        var all = await store.AllAsync(cancellationToken);
        if (all.All(environment => environment.Id != id))
        {
            throw new FileNotFoundException("The environment is gone.");
        }
        await store.SaveAsync([.. all.Select(environment => environment.Id == id ? change(environment) : environment)], cancellationToken);
    }
}
