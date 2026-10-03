namespace Hoboman.ViewModels;

public interface IDialogs
{
    string? AskName(string title, string name, string confirm, Func<string, string?> problemOf);

    bool Confirm(string title, string message, string confirm, IReadOnlyList<string> items);

    void Tell(string title, string message);

    // One text for each name, or null when the user cancels.
    IReadOnlyDictionary<string, string>? AskValues(string title, IReadOnlyList<string> names, string confirm);

    void EditSettings(SettingsViewModel settings);

    void EditEnvironments(EnvironmentEditorViewModel editor);

    void EditFolderAuth(FolderAuthViewModel folderAuth);
}
