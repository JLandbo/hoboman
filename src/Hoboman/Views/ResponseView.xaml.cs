using System.Windows;
using System.Windows.Controls;
using Hoboman.ViewModels;

namespace Hoboman.Views;

// The response of a tab or a workflow step. Both have a Result, a Problem and IsSending, and say what is shown before there is a response.
public partial class ResponseView : UserControl
{
    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(nameof(EmptyText), typeof(string), typeof(ResponseView));

    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(nameof(Footer), typeof(object), typeof(ResponseView));

    public ResponseView() => InitializeComponent();

    public string? EmptyText
    {
        get => (string?)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    // Shown under the response, such as what a workflow step saves from it.
    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    void Body_MarkToggled(object? sender, string path) => ((ResponseViewModel)BodyText.DataContext).Base64.ToggleDecode(path);
}
