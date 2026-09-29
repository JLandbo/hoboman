using System.IO;
using Hoboman.Core.Languages;
using Hoboman.Core.Settings;
using Hoboman.Core.Storage;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class SettingsViewModel(JsonFile<AppSettings> file, Translator translator, ILogger<SettingsViewModel> logger) : ObservableObject
{
    bool _ignoreCertificateErrors;

    public IReadOnlyList<Translation> Languages => Translation.All;

    public Translation Language
    {
        get => translator.Current;
        set
        {
            translator.Use(value);
            logger.LogInformation("Language changed to {Language}", value.Name);
            Saving = SaveAsync(settings => settings with { LanguageName = value.Name });
        }
    }

    public bool IgnoreCertificateErrors
    {
        get => _ignoreCertificateErrors;
        set
        {
            _ignoreCertificateErrors = value;
            logger.LogInformation("Ignore certificate errors changed to {Ignore}", value);
            Saving = SaveAsync(settings => settings with { IgnoreCertificateErrors = value });
        }
    }

    public string? Problem { get; private set => Set(ref field, value); }

    internal Task Saving { get; private set; } = Task.CompletedTask;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var settings = AppSettings.Default;
        try
        {
            settings = await file.LoadAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Could not load the settings, using the defaults");
        }
        _ignoreCertificateErrors = settings.IgnoreCertificateErrors;
        translator.Use(Translation.Find(settings.LanguageName));
        logger.LogInformation("Using {Language}", translator.Current.Name);
    }

    async Task SaveAsync(Func<AppSettings, AppSettings> change)
    {
        try
        {
            await file.UpdateAsync(change, CancellationToken.None);
            Problem = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Could not save the settings");
            Problem = translator.Format("Settings.SaveFailed", exception.Message);
        }
    }
}
