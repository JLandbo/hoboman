using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Hoboman.Views;

public partial class NameDialog : Window
{
    readonly Func<string, string?> _problemOf;

    public NameDialog(string title, string name, string confirm, Func<string, string?> problemOf)
    {
        InitializeComponent();
        _problemOf = problemOf;
        TitleText.Text = title;
        ConfirmButton.Content = confirm;
        NameBox.Text = name;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string Chosen => NameBox.Text.Trim();

    void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_problemOf(Chosen) is { } problem)
        {
            ProblemText.Text = problem;
            ProblemText.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    void NameBox_TextChanged(object sender, TextChangedEventArgs e) => ProblemText.Visibility = Visibility.Collapsed;

    void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
