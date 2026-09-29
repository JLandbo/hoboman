namespace Hoboman.Tests.ViewModels;

public sealed class Harness : IDisposable
{
    readonly TemporaryFolder _temporary = new();
    readonly Translator _translator = new(Translation.English);

    public Harness(FakeDialogs? dialogs = null, Func<Task<ApiResponse>>? send = null)
    {
        Folder = new(_temporary.Path);
        Dialogs = dialogs ?? new FakeDialogs();
        Library = new(Folder, NullLogger<RequestLibrary>.Instance);
        EnvironmentStore = new(Folder, NullLogger<EnvironmentStore>.Instance);
        SettingsStore = new(Folder, NullLogger<SettingsStore>.Instance);
        Environments = Restarted();
        var runner = new RequestRunner(new FakeSender(send ?? (() => Task.FromResult(new ApiResponse(200, "OK", 0, 2, [], "{}")))), History(), NullLogger<RequestRunner>.Instance);
        Secrets = new(Folder, NullLogger<SecretStore>.Instance);
        Services = new(runner, Secrets, Library, Environments, Dialogs, _translator, NullLogger<RequestTabViewModel>.Instance);
    }

    public AppFolder Folder { get; }

    public FakeDialogs Dialogs { get; }

    public RequestLibrary Library { get; }

    public SecretStore Secrets { get; }

    public EnvironmentStore EnvironmentStore { get; }

    public SettingsStore SettingsStore { get; }

    public EnvironmentsViewModel Environments { get; }

    public RequestTabServices Services { get; }

    public RequestTabViewModel Tab(ApiRequest? request = null, string? name = null) => new(Services, request ?? ApiRequest.New(), name);

    public MainViewModel Main() => new(
        new(Library, NullLogger<RequestTreeViewModel>.Instance),
        new(History(), _translator, NullLogger<HistoryViewModel>.Instance),
        Environments,
        new(SettingsStore, _translator, NullLogger<SettingsViewModel>.Instance),
        EnvironmentEditor(),
        Services,
        Library,
        Dialogs,
        _translator,
        NullLogger<MainViewModel>.Instance);

    public EnvironmentEditorViewModel EnvironmentEditor() => new(EnvironmentStore, Environments, _translator, NullLogger<EnvironmentEditorViewModel>.Instance);

    // The environments as they are after the app is started again.
    public EnvironmentsViewModel Restarted() => new(EnvironmentStore, SettingsStore, NullLogger<EnvironmentsViewModel>.Instance);

    public HistoryStore History() => new(Folder, NullLogger<HistoryStore>.Instance);

    public void Dispose() => _temporary.Dispose();
}
