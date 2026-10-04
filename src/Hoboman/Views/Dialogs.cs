using System.IO;
using Hoboman.Core.Languages;
using Hoboman.ViewModels;
using Microsoft.Win32;

namespace Hoboman.Views;

public sealed class Dialogs(Translator translator) : IDialogs
{
    public string? AskName(string title, string name, string confirm, Func<string, string?> problemOf)
    {
        var dialog = new NameDialog(title, name, confirm, problemOf);
        return dialog.ShowDialog() == true ? dialog.EnteredName : null;
    }

    public bool Confirm(string title, string message, string confirm, IReadOnlyList<string> items) =>
        new ConfirmDialog(title, message, confirm, items, canCancel: true).ShowDialog() == true;

    public void Tell(string title, string message) => new ConfirmDialog(title, message, translator.Of("Common.Ok"), [], canCancel: false).ShowDialog();

    // A name typed without an ending gets the suggested one.
    public string? AskSavePath(string fileName)
    {
        var dialog = new SaveFileDialog { FileName = fileName, DefaultExt = Path.GetExtension(fileName) };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public IReadOnlyDictionary<string, string>? AskValues(string title, IReadOnlyList<string> names, string confirm)
    {
        var dialog = new ValuesDialog(title, names, confirm);
        return dialog.ShowDialog() == true ? dialog.Values : null;
    }

    public void EditSettings(SettingsViewModel settings) => new SettingsWindow(settings).ShowDialog();

    public void EditEnvironments(EnvironmentEditorViewModel editor) => new EnvironmentEditorWindow(editor).ShowDialog();

    public void EditFolderAuth(FolderAuthViewModel folderAuth) => new FolderAuthWindow(folderAuth).ShowDialog();
}
