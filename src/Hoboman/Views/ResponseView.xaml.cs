using System.Windows.Controls;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class ResponseView : UserControl
{
    public ResponseView() => InitializeComponent();

    void Body_MarkToggled(object? sender, string path) => ((RequestTabViewModel)DataContext).Base64.ToggleDecode(path);
}
