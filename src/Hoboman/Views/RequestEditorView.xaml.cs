using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        var button = (Button)sender;
        var background = new SolidColorBrush(((SolidColorBrush)FindResource(succeeded ? "ClipboardSuccess" : "ClipboardError")).Color);
        button.Background = background;
        var fade = new ColorAnimation(Colors.Transparent, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(200) };
        fade.Completed += (_, _) =>
        {
            if (ReferenceEquals(button.Background, background))
            {
                button.ClearValue(Control.BackgroundProperty);
            }
        };
        background.BeginAnimation(SolidColorBrush.ColorProperty, fade);
    }
}
