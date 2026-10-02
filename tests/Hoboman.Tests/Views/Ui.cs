using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Hoboman.Tests.ViewModels;

namespace Hoboman.Tests.Views;

static class Ui
{
    static readonly TaskCompletionSource<Dispatcher> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    static readonly List<Exception> _errors = [];
    static ResourceDictionary? _texts;

    static Ui()
    {
        var thread = new Thread(() =>
        {
            try
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, Resources = new() { Source = new("/Hoboman;component/Themes/Hamster.xaml", UriKind.Relative) } };
                UseLanguage(Translation.English);
                application.DispatcherUnhandledException += (_, args) =>
                {
                    _errors.Add(args.Exception);
                    args.Handled = true;
                };
                _ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception exception)
            {
                _ready.TrySetException(exception);
            }
        }) { IsBackground = true, Name = "WPF tests" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public static async Task RunAsync(Func<Task> test)
    {
        var dispatcher = await _ready.Task.WaitAsync(TestContext.Current.CancellationToken);
        await await dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await test();
                await IdleAsync();
            }
            finally
            {
                foreach (Window window in Application.Current.Windows.Cast<Window>().ToList())
                {
                    window.Closing += (_, args) => args.Cancel = false;
                    window.Close();
                }
                UseLanguage(Translation.English);
                if (_errors.Count > 0)
                {
                    var errors = _errors.ToArray();
                    _errors.Clear();
                    throw new AggregateException(errors);
                }
            }
        });
    }

    public static async Task<MainWindow> ShowAsync(Harness harness, MainViewModel main)
    {
        var window = new MainWindow(main, harness.SettingsStore, NullLogger<MainWindow>.Instance);
        Application.Current.MainWindow = window;
        Show(window);
        await IdleAsync();
        return window;
    }

    public static void Show(Window window)
    {
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = SystemParameters.VirtualScreenLeft - 20000;
        window.Top = SystemParameters.VirtualScreenTop - 20000;
        window.Show();
    }

    public static async Task IdleAsync() => await Dispatcher.Yield(DispatcherPriority.ContextIdle);

    public static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await IdleAsync();
        }
        await IdleAsync();
    }

    public static void UseLanguage(Translation translation)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        if (_texts is not null)
        {
            dictionaries.Remove(_texts);
        }
        _texts = App.ResourcesOf(translation);
        dictionaries.Add(_texts);
    }

    public static void Press(UIElement element, int clickCount = 1)
    {
        foreach (var routedEvent in new[] { Mouse.PreviewMouseDownEvent, Mouse.MouseDownEvent, Mouse.PreviewMouseUpEvent, Mouse.MouseUpEvent })
        {
            var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = routedEvent };
            typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(args, clickCount);
            element.RaiseEvent(args);
        }
    }

    public static void Click(Button button, int clickCount = 1)
    {
        Press(button, clickCount);
        ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();
    }

    public static void Key(UIElement element, Key key)
    {
        Keyboard.Focus(element);
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(element), Environment.TickCount, key) { RoutedEvent = key == System.Windows.Input.Key.Apps ? Keyboard.KeyUpEvent : Keyboard.KeyDownEvent };
        if (key == System.Windows.Input.Key.Apps)
        {
            InputManager.Current.ProcessInput(args);
            return;
        }
        element.RaiseEvent(args);
    }

    public static void Select(RadioButton button) => ((ISelectionItemProvider)new RadioButtonAutomationPeer(button)).Select();

    public static DragEventArgs Drag(UIElement element, object data, Point point, RoutedEvent routedEvent)
    {
        var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, [new DataObject(data), DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, element, point], null)!;
        args.RoutedEvent = routedEvent;
        element.RaiseEvent(args);
        return args;
    }

    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject => Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(root)).Select(index => VisualTreeHelper.GetChild(root, index)).SelectMany(child => new[] { child }.OfType<T>().Concat(Descendants<T>(child)));

    public static TreeViewItem Item(DependencyObject root, RequestNodeViewModel node) => Descendants<TreeViewItem>(root).Single(item => ReferenceEquals(item.DataContext, node));

    public static Border Row(TreeViewItem item) => (Border)item.Template.FindName("Row", item);

    public static T Named<T>(DependencyObject root, string name) where T : FrameworkElement => Descendants<T>(root).Single(element => element.Name == name);

    public static ScrollViewer Scroller(TreeView tree) => (ScrollViewer)tree.Template.FindName("_tv_scrollviewer_", tree);

    public static Rect Bounds(FrameworkElement element, Visual relativeTo) => element.TransformToAncestor(relativeTo).TransformBounds(new Rect(element.RenderSize));
}
