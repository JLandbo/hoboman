namespace Hoboman.Tests.ViewModels;

public sealed class FakeDialogs(string? answer = null, bool accept = false) : IDialogs
{
    public int Asked { get; private set; }

    public object? Shown { get; private set; }

    // Like the real dialog, a name with a problem is never given back.
    public string? AskName(string title, string name, string confirm, Func<string, string?> problemOf)
    {
        Asked++;
        return answer is not null && problemOf(answer) is null ? answer : null;
    }

    public bool Confirm(string title, string message, string confirm, IReadOnlyList<string> items)
    {
        Asked++;
        return accept;
    }

    public void Tell(string title, string message) => Asked++;

    public void EditSettings(SettingsViewModel settings) => Shown = settings;

    public void EditEnvironments(EnvironmentEditorViewModel editor) => Shown = editor;

    public void EditFolderAuth(FolderAuthViewModel folderAuth) => Shown = folderAuth;
}
