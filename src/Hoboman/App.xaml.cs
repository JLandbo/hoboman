using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.Languages;
using Hoboman.Core.Requests;
using Hoboman.Core.Sending;
using Hoboman.Core.Settings;
using Hoboman.Core.Storage;
using Hoboman.Desktop;
using Hoboman.ViewModels;
using Hoboman.Views;

namespace Hoboman;

public partial class App : Application
{
    const string _logLine = "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    ServiceProvider? _services;
    ResourceDictionary? _texts;

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
                MessageBox.Show(MainWindow, translator.Format("Common.UnexpectedError", args.Exception.Message), "Hoboman", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        logger.LogInformation("Hoboman started in {Folder}", AppContext.BaseDirectory);
        Use(translator.Current);
        translator.Changed += () => Use(translator.Current);
        await _services.GetRequiredService<SettingsViewModel>().LoadAsync(CancellationToken.None);
        _services.GetRequiredService<MainWindow>().Show();
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
        services.AddSingleton(provider => new JsonFile<AppSettings>(folder.Settings, AppSettings.Default, provider.GetRequiredService<ILogger<AppSettings>>()));
        services.AddSingleton(_ => new Translator(Translation.Danish));
        services.AddSingleton<EnvironmentStore>();
        services.AddSingleton<RequestLibrary>();
        services.AddSingleton<SecretStore>();
        services.AddSingleton<HttpClients>();
        services.AddSingleton<IRequestSender, HttpRequestSender>();
        services.AddSingleton<IBrowser, ShellBrowser>();
        services.AddSingleton<RequestTreeViewModel>();
        services.AddSingleton<RequestEditorViewModel>();
        services.AddSingleton<ResponseViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<SettingsViewModel>();
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
        if (_texts is not null)
        {
            Resources.MergedDictionaries.Remove(_texts);
        }
        _texts = ResourcesOf(translation);
        Resources.MergedDictionaries.Add(_texts);
    }
}
