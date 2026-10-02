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
        EnvironmentStore = new(Folder, NullLogger<EnvironmentStore>.Instance);
        SettingsStore = new(Folder, NullLogger<SettingsStore>.Instance);
        Environments = Restarted();
        Sender = new(send ?? (() => Task.FromResult(new ApiResponse(200, "OK", 0, 2, [], "{}"))));
        var runner = new RequestRunner(Sender, Library, History(), NullLogger<RequestRunner>.Instance);
        Secrets = new(Folder, NullLogger<SecretStore>.Instance);
        AuthRefresh = new(OAuth, Library, Secrets);
        Services = new(runner, Secrets, AuthRefresh, Library, new(), Environments, Dialogs, _translator, Clock, NullLogger<RequestTabViewModel>.Instance);
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

    public RequestTabServices Services { get; }

    public RequestTabViewModel Tab(ApiRequest? request = null, string? name = null) => new(Services, request ?? ApiRequest.New(), name);

    public MainViewModel Main() => new(
        new(Library, Dialogs, _translator, NullLogger<RequestTreeViewModel>.Instance),
        new(History(), _translator, TimeProvider.System, NullLogger<HistoryViewModel>.Instance),
        Environments,
        new(SettingsStore, _translator, NullLogger<SettingsViewModel>.Instance),
        SettingsStore,
        EnvironmentEditor(),
        FolderAuth(),
        Services,
        Library,
        new RequestDeletion(Library, Secrets, Folder, NullLogger<RequestDeletion>.Instance),
        Dialogs,
        new(new FakeClipboard(), NullLogger<ClipboardViewModel>.Instance),
        _translator,
        NullLogger<MainViewModel>.Instance);

    public FolderAuthViewModel FolderAuth() => new(Library, Secrets, AuthRefresh, Environments, _translator, Clock, NullLogger<FolderAuthViewModel>.Instance);

    public EnvironmentEditorViewModel EnvironmentEditor() => new(EnvironmentStore, Environments, AuthRefresh, _translator, NullLogger<EnvironmentEditorViewModel>.Instance);

    // The environments as they are after the app is started again.
    public EnvironmentsViewModel Restarted() => new(EnvironmentStore, SettingsStore, NullLogger<EnvironmentsViewModel>.Instance);

    public HistoryStore History() => new(Folder, NullLogger<HistoryStore>.Instance);

    public void Dispose() => _temporary.Dispose();
}
