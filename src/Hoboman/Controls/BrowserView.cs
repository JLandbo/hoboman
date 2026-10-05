using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Hoboman.ViewModels;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Hoboman.Controls;

// Shows a response as Edge would, with the WebView2 that comes with Windows. It is made the first time a page is shown, as it starts a browser.
// The page is served from an address of its own instead of as a string, which has no limit of size, and it loads what it links to as a page on the web would.
// The plain control is a window on top of the app, which nothing of the app's own overlaps here. The composition control would need a target of a Windows version.
public sealed class BrowserView : ContentControl
{
    // Each response gets a host of its own below it, so the browser keeps their cookies and storage apart.
    const string _host = "response.hoboman";

    // One environment for the app, as each starts browser processes of its own.
    static readonly Lazy<Task<CoreWebView2Environment>> _environment = new(() => CoreWebView2Environment.CreateAsync(null, DataFolder));

    public static readonly DependencyProperty PageProperty = DependencyProperty.Register(nameof(Page), typeof(BrowserPage), typeof(BrowserView),
        new(null, (view, _) => ((BrowserView)view).Show()));

    Task<CoreWebView2>? _browser;
    int _shown;
    string? _current;
    bool _asHtml;

    // Where the browser keeps its cache and cookies, told by the app. Without it, they are kept next to the app.
    public static string? DataFolder { get; set; }

    public BrowserPage? Page
    {
        get => (BrowserPage?)GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    // The page is shown again on the new color, as a theme can change while it is open.
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == BackgroundProperty)
        {
            Show();
        }
    }

    // Each page gets an address of its own, so the browser loads it again even when it is the same as before.
    // Without a page, the one before is cleared, so it is not taken for another response.
    async void Show()
    {
        if (Page is null && _browser is null)
        {
            return;
        }
        var shown = ++_shown;
        try
        {
            var browser = await (_browser ??= StartAsync());
            if (shown == _shown && Content is WebView2 control)
            {
                // Any page, also the empty one, is on the color of the box, so nothing flashes between responses.
                // The browser's own empty page has a color of its own, so the empty one is made here.
                var box = (Background as SolidColorBrush)?.Color ?? default;
                control.DefaultBackgroundColor = System.Drawing.Color.FromArgb(box.R, box.G, box.B);
                _asHtml = false;
                _current = Page is null ? null : $"https://{shown}.{_host}/";
                if (_current is null)
                {
                    browser.NavigateToString($"<html style=\"background: #{box.R:X2}{box.G:X2}{box.B:X2}\"></html>");
                }
                else
                {
                    browser.Navigate(_current);
                }
            }
        }
        catch (WebView2RuntimeNotFoundException)
        {
            var missing = new Run();
            missing.SetResourceReference(Run.TextProperty, "Response.BrowserMissing");
            Tell(missing);
        }
        // A browser that cannot start, such as when its folder cannot be written, is told of in the box instead of stopping the app.
        catch (Exception exception)
        {
            var failed = new Run();
            failed.SetResourceReference(Run.TextProperty, "Response.BrowserFailed");
            Tell(failed, new LineBreak(), new Run(exception.Message));
        }
    }

    void Tell(params Inline[] lines)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new(16) };
        text.Inlines.AddRange(lines);
        text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Content = text;
    }

    async Task<CoreWebView2> StartAsync()
    {
        var control = new WebView2();
        Content = control;
        await control.EnsureCoreWebView2Async(await _environment.Value);
        var browser = control.CoreWebView2;
        // A response fills in no forms with what was typed before.
        browser.Settings.IsGeneralAutofillEnabled = false;
        browser.Settings.IsPasswordAutosaveEnabled = false;
        browser.AddWebResourceRequestedFilter($"https://*.{_host}/*", CoreWebView2WebResourceContext.All);
        browser.WebResourceRequested += (_, e) =>
        {
            if (Page is { } page)
            {
                var type = _asHtml ? "text/html; charset=utf-8" : page.ContentType;
                e.Response = browser.Environment.CreateWebResourceResponse(new MemoryStream(page.Bytes, writable: false), 200, "OK", type is null ? "" : $"Content-Type: {type}");
            }
        };
        // Only the response itself is shown. What it loads into itself, such as images and scripts, it may, but a link, a redirect or a script that leaves it is stopped, and so is a new window.
        browser.NavigationStarting += (_, e) => e.Cancel = _current is not null && e.Uri != _current;
        browser.NewWindowRequested += (_, e) => e.Handled = true;
        // A response asks for nothing of the user: no camera, microphone, position or the like, and no password for what it loads.
        browser.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        browser.BasicAuthenticationRequested += (_, e) => e.Cancel = true;
        // Nothing is saved in the user's downloads, also not from a link or the PDF viewer. A response the browser cannot show, such as a CSV file or an SVG with a type of its own,
        // is shown as HTML instead, so an SVG is drawn. Text loses its line breaks there, and Rå shows it as it is.
        browser.DownloadStarting += (_, e) =>
        {
            e.Cancel = true;
            if (e.DownloadOperation.Uri == _current && !_asHtml)
            {
                _asHtml = true;
                _current = $"https://{++_shown}.{_host}/";
                browser.Navigate(_current);
            }
        };
        return browser;
    }
}
