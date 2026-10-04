namespace Hoboman.Tests.ViewModels;

public sealed class FakeDialogs(string? answer = null, bool accept = false) : IDialogs
{
    public string? Answer { get; set; } = answer;

    public int Asked { get; private set; }

    public object? Shown { get; private set; }

    public (string Title, string Message)? Notification { get; private set; }

    public (string Title, string Name, string Confirm, Func<string, string?> ProblemOf)? NameQuestion { get; private set; }

    public (string Title, string Message, string Confirm, IReadOnlyList<string> Items)? ConfirmQuestion { get; private set; }

    public IReadOnlyDictionary<string, string>? Values { get; set; }

    public string? SavePath { get; set; }

    public string? SaveQuestion { get; private set; }

    public IReadOnlyList<string>? AskedValues { get; private set; }

    // What happens elsewhere while the values are asked for, as the real dialog lets the app go on.
    public Action? Asking { get; set; }

    // Like the real dialog, a name with a problem is never given back.
    public string? AskName(string title, string name, string confirm, Func<string, string?> problemOf)
    {
        Asked++;
        NameQuestion = (title, name, confirm, problemOf);
        return Answer is not null && problemOf(Answer) is null ? Answer : null;
    }

    public bool Confirm(string title, string message, string confirm, IReadOnlyList<string> items)
    {
        Asked++;
        ConfirmQuestion = (title, message, confirm, items);
        return accept;
    }

    public void Tell(string title, string message)
    {
        Asked++;
        Notification = (title, message);
    }

    public string? AskSavePath(string fileName)
    {
        Asked++;
        SaveQuestion = fileName;
        return SavePath;
    }

    public IReadOnlyDictionary<string, string>? AskValues(string title, IReadOnlyList<string> names, string confirm)
    {
        Asked++;
        AskedValues = names;
        Asking?.Invoke();
        return Values;
    }

    public void EditSettings(SettingsViewModel settings) => Shown = settings;

    public void EditEnvironments(EnvironmentEditorViewModel editor) => Shown = editor;

    public void EditFolderAuth(FolderAuthViewModel folderAuth) => Shown = folderAuth;
}
