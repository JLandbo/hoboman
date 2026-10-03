using System.Windows;
using System.Windows.Input;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class ValuesDialog : DialogWindow
{
    readonly IReadOnlyList<KeyValueRowViewModel> _fields;

    public ValuesDialog(string title, IReadOnlyList<string> names, string confirm)
    {
        InitializeComponent();
        Title = title;
        ConfirmButton.Content = confirm;
        Fields.ItemsSource = _fields = [.. names.Select(name => new KeyValueRowViewModel { Name = name })];
        Loaded += (_, _) => Fields.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    // A value is text, as with --param in the CLI.
    public IReadOnlyDictionary<string, string> Values => _fields.ToDictionary(row => row.Name, row => row.Value);

    void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
