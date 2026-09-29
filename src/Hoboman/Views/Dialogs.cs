using System.Windows;
using Hoboman.Core.Languages;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public sealed class Dialogs(Translator translator) : IDialogs
{
    public string? AskName(string title, string name, string confirm, Func<string, string?> problemOf)
    {
        var dialog = new NameDialog(title, name, confirm, problemOf) { Owner = Application.Current.MainWindow };
        return dialog.ShowDialog() == true ? dialog.Chosen : null;
    }

    public bool Confirm(string title, string message, string confirm, IReadOnlyList<string> items) =>
        new ConfirmDialog(title, message, confirm, items, isQuestion: true) { Owner = Application.Current.MainWindow }.ShowDialog() == true;

    public void Tell(string title, string message) =>
        new ConfirmDialog(title, message, translator.Of("Common.Ok"), [], isQuestion: false) { Owner = Application.Current.MainWindow }.ShowDialog();
}
