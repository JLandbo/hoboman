namespace Hoboman.ViewModels;

public interface IDialogs
{
    string? AskName(string title, string name, string confirm, Func<string, string?> problemOf);

    bool Confirm(string title, string message, string confirm, IReadOnlyList<string> items);

    void Tell(string title, string message);
}
