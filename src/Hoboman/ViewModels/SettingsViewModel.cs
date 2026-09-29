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
            var previous = _ignoreCertificateErrors;
            _ignoreCertificateErrors = value;
            logger.LogInformation("Ignore certificate errors changed to {Ignore}", value);
            Saving = SaveAsync(settings => settings with { IgnoreCertificateErrors = value }, undo: () => Set(ref _ignoreCertificateErrors, previous, nameof(IgnoreCertificateErrors)));
        }
    }

    public string? Problem { get; private set => Set(ref field, value); }

    internal Task Saving { get; private set; } = Task.CompletedTask;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        Problem = null;
        try
        {
            var settings = await file.LoadAsync(cancellationToken);
            _ignoreCertificateErrors = settings.IgnoreCertificateErrors;
            translator.Use(Translation.Find(settings.LanguageName));
            logger.LogInformation("Using {Language}", translator.Current.Name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Could not load the settings, keeping the current ones");
        }
    }

    async Task SaveAsync(Func<AppSettings, AppSettings> change, Action? undo = null)
    {
        try
        {
            await file.UpdateAsync(change, CancellationToken.None);
            Problem = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Could not save the settings");
            undo?.Invoke();
            Problem = translator.Format("Settings.SaveFailed", exception.Message);
        }
    }
}
