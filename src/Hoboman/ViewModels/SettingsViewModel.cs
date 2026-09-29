using System.IO;
using Hoboman.Core.Languages;
using Hoboman.Core.Settings;
using Hoboman.Core.Storage;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class SettingsViewModel(JsonFile<AppSettings> file, Translator translator, ILogger<SettingsViewModel> logger) : ObservableObject
{
    AppSettings _saved = AppSettings.Default;
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
        Problem = null;
        try
        {
            _saved = await file.LoadAsync(cancellationToken);
            _ignoreCertificateErrors = _saved.IgnoreCertificateErrors;
            translator.Use(Translation.Find(_saved.LanguageName));
            logger.LogInformation("Using {Language}", translator.Current.Name);
        }
        catch (Exception exception) when (IsFileProblem(exception))
        {
            logger.LogError(exception, "Could not load the settings, keeping the current ones");
        }
    }

    async Task SaveAsync(Func<AppSettings, AppSettings> change)
    {
        try
        {
            _saved = await file.UpdateAsync(change, CancellationToken.None);
            Problem = null;
        }
        catch (Exception exception) when (IsFileProblem(exception))
        {
            logger.LogError(exception, "Could not save the settings");
            // The sender reads the file, so show what the file really holds.
            Set(ref _ignoreCertificateErrors, _saved.IgnoreCertificateErrors, nameof(IgnoreCertificateErrors));
            Problem = translator.Format("Settings.SaveFailed", exception.Message);
        }
    }

    static bool IsFileProblem(Exception exception) => exception is IOException or UnauthorizedAccessException or InvalidDataException;
}
