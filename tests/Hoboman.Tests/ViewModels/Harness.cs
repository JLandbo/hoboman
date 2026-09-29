namespace Hoboman.Tests.ViewModels;

public sealed class Harness : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    readonly Translator _translator = new(Translation.English);
    readonly EnvironmentsViewModel _environments;

    public Harness(FakeDialogs? dialogs = null, Func<Task<ApiResponse>>? send = null)
    {
        Folder = new(_directory);
        Dialogs = dialogs ?? new FakeDialogs();
        Library = new(Folder, NullLogger<RequestLibrary>.Instance);
        var settings = new SettingsViewModel(new(Folder.Settings, AppSettings.Default, NullLogger.Instance), _translator, NullLogger<SettingsViewModel>.Instance);
        _environments = new(new(Folder, NullLogger<EnvironmentStore>.Instance), settings, NullLogger<EnvironmentsViewModel>.Instance);
        var runner = new RequestRunner(new FakeSender(send ?? (() => Task.FromResult(new ApiResponse(200, "OK", TimeSpan.Zero, 2, [], "{}")))), History(), NullLogger<RequestRunner>.Instance);
        Services = new(runner, new(Folder, NullLogger<SecretStore>.Instance), Library, _environments, Dialogs, _translator, NullLogger<RequestTabViewModel>.Instance);
    }

    public AppFolder Folder { get; }

    public FakeDialogs Dialogs { get; }

    public RequestLibrary Library { get; }

    public RequestTabServices Services { get; }

    public RequestTabViewModel Tab(ApiRequest? request = null, string? name = null) => new(Services, request ?? ApiRequest.New(), name);

    public MainViewModel Main() => new(new(Library, NullLogger<RequestTreeViewModel>.Instance), new(History(), _translator, NullLogger<HistoryViewModel>.Instance), _environments, Services, NullLogger<MainViewModel>.Instance);

    public HistoryStore History() => new(Folder, NullLogger<HistoryStore>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

public sealed class FakeDialogs(string? answer = null, bool accept = false) : IDialogs
{
    public int Asked { get; private set; }

    public string? AskName(string title, string name, string confirm, Func<string, string?> problemOf)
    {
        Asked++;
        return answer;
    }

    public bool Confirm(string title, string message, string confirm, IReadOnlyList<string> items)
    {
        Asked++;
        return accept;
    }

    public void Tell(string title, string message) => Asked++;
}
