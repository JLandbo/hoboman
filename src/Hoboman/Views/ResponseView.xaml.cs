using System.Windows;
using System.Windows.Controls;
using Hoboman.ViewModels;

namespace Hoboman.Views;

// The response of a tab or a workflow step. Both have a Result, a Problem and IsSending.
public partial class ResponseView : UserControl
{
    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(nameof(Footer), typeof(object), typeof(ResponseView));

    public ResponseView() => InitializeComponent();

    // Shown under the response, such as what a workflow step saves from it.
    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    void Header_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var below = HeaderLeft.ActualWidth + 12 + HeaderRight.ActualWidth > HeaderRow.ActualWidth;
        Grid.SetRow(HeaderRight, below ? 1 : 0);
        Grid.SetColumn(HeaderRight, below ? 0 : 1);
        Grid.SetColumnSpan(HeaderRight, below ? 2 : 1);
        HeaderRight.Margin = new(0, below ? 8 : 0, 0, 0);
    }

    void Body_MarkToggled(object? sender, string path) => ((ResponseViewModel)BodyText.DataContext).Base64.ToggleDecode(path);
}
