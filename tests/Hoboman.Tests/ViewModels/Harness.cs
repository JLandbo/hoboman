using Hoboman.Tests.Auth;
using Hoboman.Services;
using Microsoft.Extensions.Time.Testing;

namespace Hoboman.Tests.ViewModels;

public sealed class Harness : IDisposable
{
    readonly TemporaryFolder _temporary = new();
    readonly Translator _translator = new(Translation.English);

    public Harness(FakeDialogs? dialogs = null, Func<Task<ApiResponse>>? send = null, FakeOAuthClient? oauth = null)
    {
        Folder = new(_temporary.Path);
        Dialogs = dialogs ?? new FakeDialogs();
        OAuth = oauth ?? new FakeOAuthClient();
        Library = new(Folder, NullLogger<RequestLibrary>.Instance);
        Secrets = new(Folder, NullLogger<SecretStore>.Instance);
        EnvironmentStore = new(Folder, Secrets, NullLogger<EnvironmentStore>.Instance);
        SettingsStore = new(Folder, NullLogger<SettingsStore>.Instance);
        Environments = Restarted();
        CredentialStore = new(Folder, Secrets, NullLogger<CredentialStore>.Instance);
        AuthRefresh = new(OAuth, Library, Secrets);
        CredentialEditor = new(CredentialStore, Secrets, AuthRefresh, Environments, Clipboard, _translator, Clock, NullLogger<CredentialEditorViewModel>.Instance);
        Credentials = new(CredentialStore, Secrets, Environments, CredentialEditor, Dialogs, NullLogger<CredentialsViewModel>.Instance);
        Sender = new(send ?? (() => Task.FromResult(new ApiResponse(200, "OK", 0, 2, [], "{}"))));
        var runner = new RequestRunner(Sender, Library, History(), NullLogger<RequestRunner>.Instance);
        Services = new(runner, Secrets, AuthRefresh, Library, new(), Environments, Credentials, Dialogs, _translator, Clock, NullLogger<RequestTabViewModel>.Instance);
        WorkflowLibrary = new(Folder, NullLogger<WorkflowLibrary>.Instance);
        WorkflowServices = new(WorkflowLibrary, new(WorkflowLibrary, Secrets, NullLogger<WorkflowCheck>.Instance), new(Sender, Folder, Clock, NullLogger<WorkflowRunner>.Instance), Environments, Credentials, Dialogs, _translator, Clock,
            Secrets, AuthRefresh, NullLogger<WorkflowViewModel>.Instance);
    }

    public AppFolder Folder { get; }

    public Translator Translator => _translator;

    public FakeDialogs Dialogs { get; }

    public FakeOAuthClient OAuth { get; }

    public AuthRefreshService AuthRefresh { get; }

    // For what the tabs show about time, such as whether a token has expired; the history keeps the real clock.
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));

    public FakeSender Sender { get; }

    public RequestLibrary Library { get; }

    public SecretStore Secrets { get; }

    public EnvironmentStore EnvironmentStore { get; }

    public SettingsStore SettingsStore { get; }

    public EnvironmentsViewModel Environments { get; }

    public CredentialStore CredentialStore { get; }

    public CredentialsViewModel Credentials { get; }

    // One for the app, as the credentials follow what is saved in it.
    public CredentialEditorViewModel CredentialEditor { get; }

    public FakeClipboard Clipboard { get; } = new();

    public RequestTabServices Services { get; }

    public WorkflowLibrary WorkflowLibrary { get; }

    public WorkflowServices WorkflowServices { get; }

    public RequestTabViewModel Tab(ApiRequest? request = null, string? name = null) => new(Services, request ?? ApiRequest.New(), name);

    public MainViewModel Main() => new(
        new(Library, Dialogs, _translator, NullLogger<RequestTreeViewModel>.Instance),
        new(History(), _translator, TimeProvider.System, NullLogger<HistoryViewModel>.Instance),
        new(WorkflowLibrary, NullLogger<WorkflowsViewModel>.Instance),
        WorkflowServices,
        Environments,
        new(SettingsStore, _translator, new(Folder.Themes), NullLogger<SettingsViewModel>.Instance),
        SettingsStore,
        EnvironmentEditor(),
        Credentials,
        FolderAuth(),
        Services,
        Library,
        new RequestDeletion(Library, Secrets, Folder, NullLogger<RequestDeletion>.Instance),
        Dialogs,
        new(new FakeClipboard(), NullLogger<ClipboardViewModel>.Instance),
        _translator,
        NullLogger<MainViewModel>.Instance);

    public FolderAuthViewModel FolderAuth() => new(Library, Secrets, AuthRefresh, Environments, Credentials, _translator, Clock, NullLogger<FolderAuthViewModel>.Instance);

    public EnvironmentEditorViewModel EnvironmentEditor() => new(EnvironmentStore, Environments, Secrets, CredentialStore, _translator, NullLogger<EnvironmentEditorViewModel>.Instance);

    // The environments as they are after the app is started again.
    public EnvironmentsViewModel Restarted() => new(EnvironmentStore, SettingsStore, NullLogger<EnvironmentsViewModel>.Instance);

    public HistoryStore History() => new(Folder, NullLogger<HistoryStore>.Instance);

    // Two environments with dev chosen. Dev has the OAuth client Acies Docs and the Basic login Admin, test has the Bearer token Batch.
    public async Task<(ApiEnvironment Dev, ApiEnvironment Test)> SaveCredentialsAsync()
    {
        await EnvironmentStore.SaveAsync([new("dev", []) { Id = Guid.NewGuid() }, new("test", []) { Id = Guid.NewGuid() }], CancellationToken.None);
        await Environments.LoadAsync(CancellationToken.None);
        var (dev, test) = (Environments.Items[0], Environments.Items[1]);
        await Environments.ChooseAsync(dev);
        Credential docs = new(Guid.NewGuid(), dev.Id, "Acies Docs", new(AuthKind.OAuth2, OAuth: new() { TokenUrl = "https://auth.{{env}}.example/token", ClientId = "docs-client", Scope = "apis" }));
        Credential admin = new(Guid.NewGuid(), dev.Id, "Admin", new(AuthKind.Basic, "admin"));
        Credential batch = new(Guid.NewGuid(), test.Id, "Batch", new(AuthKind.Bearer));
        await CredentialStore.SaveAsync([docs, admin, batch], CancellationToken.None);
        await Secrets.SaveAsync(docs.Id, SecretKind.ClientSecret, "docs-secret", CancellationToken.None);
        await Secrets.SaveAsync(admin.Id, SecretKind.Password, "admin-password", CancellationToken.None);
        await Secrets.SaveAsync(batch.Id, SecretKind.Token, "batch-token", CancellationToken.None);
        await Credentials.LoadAsync(CancellationToken.None);
        return (dev, test);
    }

    public void Dispose() => _temporary.Dispose();
}
