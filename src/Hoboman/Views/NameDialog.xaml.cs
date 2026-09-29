using System.Windows;
using System.Windows.Controls;

namespace Hoboman.Views;

public partial class NameDialog : DialogWindow
{
    readonly Func<string, string?> _problemOf;

    public NameDialog(string title, string name, string confirm, Func<string, string?> problemOf)
    {
        InitializeComponent();
        _problemOf = problemOf;
        Title = title;
        ConfirmButton.Content = confirm;
        NameBox.Text = name;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string EnteredName => NameBox.Text.Trim();

    void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_problemOf(EnteredName) is { } problem)
        {
            ProblemText.Text = problem;
            ProblemText.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }

    void NameBox_TextChanged(object sender, TextChangedEventArgs e) => ProblemText.Visibility = Visibility.Collapsed;
}
