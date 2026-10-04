using System.Windows;
using System.Windows.Controls;
using Hoboman.ViewModels;

namespace Hoboman.Views;

public partial class RequestEditorView : UserControl
{
    public RequestEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RefreshAuthButton.ClearValue(Control.BackgroundProperty);
    }

    RequestTabViewModel Tab => (RequestTabViewModel)DataContext;

    void Cancel_Click(object sender, RoutedEventArgs e) => Tab.Cancel();

    async void RefreshAuth_Click(object sender, RoutedEventArgs e)
    {
        var tab = Tab;
        var succeeded = await tab.RefreshAuthAsync();
        if (tab != DataContext)
        {
            return;
        }
        Blink.Show((Button)sender, succeeded);
    }
}
