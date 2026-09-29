using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Hoboman.Core.Auth;
using Hoboman.Core.Storage;
using Hoboman.Desktop;
using Hoboman.ViewModels;
using Hoboman.Views;

namespace Hoboman;

public partial class App : Application
{
    ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _services = Services(new AppFolder(AppContext.BaseDirectory)).BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        _services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    internal static ServiceCollection Services(AppFolder folder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(folder);
        services.AddSingleton<IBrowser, ShellBrowser>();
        services.AddSingleton<RequestTreeViewModel>();
        services.AddSingleton<RequestEditorViewModel>();
        services.AddSingleton<ResponseViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
