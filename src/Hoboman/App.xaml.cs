using System.Globalization;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.History;
using Hoboman.Core.Languages;
using Hoboman.Core.Requests;
using Hoboman.Core.Sending;
using Hoboman.Core.Settings;
using Hoboman.Core.Storage;
using Hoboman.Core.Workflows;
using Hoboman.Desktop;
using Hoboman.Services;
using Hoboman.Themes;
using Hoboman.ViewModels;
using Hoboman.Views;

namespace Hoboman;

public partial class App : Application
{
    const string _logLine = "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    ServiceProvider? _services;
    ResourceDictionary? _texts;
    ResourceDictionary? _theme;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _services = Services(new AppFolder(AppContext.BaseDirectory)).BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        var logger = _services.GetRequiredService<ILogger<App>>();
        var translator = _services.GetRequiredService<Translator>();
        AppDomain.CurrentDomain.UnhandledException += (_, args) => logger.LogCritical(args.ExceptionObject as Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, args) => logger.LogError(args.Exception, "Unobserved task exception");
        DispatcherUnhandledException += (_, args) =>
        {
            logger.LogError(args.Exception, "Unhandled exception on the UI thread");
            if (MainWindow?.IsVisible == true)
            {
                args.Handled = true;
                _services.GetRequiredService<IDialogs>().Tell(translator.Of("Common.UnexpectedError"), args.Exception.Message);
            }
        };
        logger.LogInformation("Hoboman started in {Folder}", AppContext.BaseDirectory);
        var main = _services.GetRequiredService<MainViewModel>();
        Use(translator.Current);
        translator.Changed += () =>
        {
            Use(translator.Current);
            OnUi(main.LanguageChangedAsync);
        };
        var themes = _services.GetRequiredService<ThemeLibrary>();
        themes.Changed += () => _theme = themes.Current.Apply(Resources, _theme);
        await _services.GetRequiredService<SettingsViewModel>().LoadAsync(CancellationToken.None);
        var watcher = _services.GetRequiredService<AppFolderWatcher>();
        watcher.RequestsChanged += () => OnUi(main.RequestsChangedAsync);
        watcher.HistoryChanged += () => OnUi(main.HistoryChangedAsync);
        watcher.EnvironmentsChanged += () => OnUi(main.EnvironmentsChangedAsync);
        watcher.WorkflowsChanged += () => OnUi(main.WorkflowsChangedAsync);
        main.History.DayChanged += () => OnUi(main.HistoryChangedAsync);
        main.Environments.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(EnvironmentsViewModel.Selected))
            {
                main.EnvironmentChosen();
            }
        };
        watcher.Start();
        await main.LoadAsync();
        var window = _services.GetRequiredService<MainWindow>();
        await window.RestoreLayoutAsync();
        window.Show();

        void OnUi(Func<Task> work) => Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await work();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Updating after a change failed");
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.GetRequiredService<ILogger<App>>().LogInformation("Hoboman stopped");
        _services?.Dispose();
        base.OnExit(e);
    }

    internal static ServiceCollection Services(AppFolder folder)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug).AddSerilog(
            new LoggerConfiguration().MinimumLevel.Debug().WriteTo.File(Path.Combine(folder.Logs, "hoboman-.log"), rollingInterval: RollingInterval.Day, outputTemplate: _logLine).CreateLogger(),
            dispose: true));
        services.AddSingleton(folder);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AppFolderWatcher>();
        services.AddSingleton<SettingsStore>();
        services.AddSingleton(_ => new Translator(Translation.Danish));
        services.AddSingleton(_ => new ThemeLibrary(folder.Themes));
        services.AddSingleton<EnvironmentStore>();
        services.AddSingleton<RequestLibrary>();
        services.AddSingleton<SecretStore>();
        services.AddSingleton<CredentialStore>();
        services.AddSingleton<RequestDeletion>();
        services.AddSingleton<CollectionChanges>();
        services.AddSingleton<HttpClients>();
        services.AddSingleton<IRequestSender, HttpRequestSender>();
        services.AddSingleton<HistoryStore>();
        services.AddSingleton<RequestRunner>();
        services.AddSingleton<WorkflowLibrary>();
        services.AddSingleton<WorkflowCheck>();
        services.AddSingleton<WorkflowRunner>();
        services.AddSingleton<IBrowser, ShellBrowser>();
        services.AddSingleton<IOAuthClient, OAuthClient>();
        services.AddSingleton<AuthRefreshService>();
        services.AddSingleton<IDialogs, Dialogs>();
        services.AddSingleton<IClipboard, SystemClipboard>();
        services.AddSingleton<ClipboardViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<EnvironmentsViewModel>();
        services.AddSingleton<EnvironmentEditorViewModel>();
        services.AddSingleton<CredentialEditorViewModel>();
        services.AddSingleton<CredentialsViewModel>();
        services.AddSingleton<FolderAuthViewModel>();
        services.AddSingleton<RequestTreeViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<WorkflowsViewModel>();
        services.AddSingleton<WorkflowServices>();
        services.AddSingleton<RequestTabServices>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }

    internal static ResourceDictionary ResourcesOf(Translation translation)
    {
        var resources = new ResourceDictionary();
        foreach (var key in Translation.Danish.Texts.Keys)
        {
            resources[key] = translation.Of(key);
        }
        return resources;
    }

    void Use(Translation translation)
    {
        CultureInfo.DefaultThreadCurrentCulture = translation.Culture;
        CultureInfo.DefaultThreadCurrentUICulture = translation.Culture;
        CultureInfo.CurrentCulture = translation.Culture;
        CultureInfo.CurrentUICulture = translation.Culture;
        if (_texts is not null)
        {
            Resources.MergedDictionaries.Remove(_texts);
        }
        _texts = ResourcesOf(translation);
        Resources.MergedDictionaries.Add(_texts);
    }
}
