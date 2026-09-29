using System.Windows;
using Microsoft.Extensions.DependencyInjection;
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
    ServiceProvider? _services;
    ResourceDictionary? _texts;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _services = Services(new AppFolder(AppContext.BaseDirectory)).BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        var translator = _services.GetRequiredService<Translator>();
        Use(translator.Current);
        translator.Changed += () => Use(translator.Current);
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
        services.AddSingleton(_ => new JsonFile<AppSettings>(folder.Settings, AppSettings.Default));
        services.AddSingleton(provider => new Translator(Translation.Find(provider.GetRequiredService<JsonFile<AppSettings>>().Load().LanguageName)));
        services.AddSingleton(_ => new JsonFile<IReadOnlyList<ApiEnvironment>>(folder.Environments, []));
        services.AddSingleton<RequestLibrary>();
        services.AddSingleton<SecretStore>();
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
