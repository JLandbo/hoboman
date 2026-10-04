using Hoboman.Core.Languages;
using Hoboman.Core.Settings;
using Hoboman.Core.Storage;
using Hoboman.Mvvm;
using Hoboman.Themes;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class SettingsViewModel(SettingsStore store, Translator translator, ThemeLibrary themes, ILogger<SettingsViewModel> logger) : ObservableObject
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

    // Read again each time the settings are shown, so a theme put in the folder shows.
    public IReadOnlyList<ThemeChoice> Themes { get; private set => Set(ref field, value); } = [];

    public string ThemesFolder => themes.Folder;

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

    public void ShowThemes() => Themes = [.. themes.Themes.Select(theme => new ThemeChoice(theme, theme.Name == themes.Current.Name))];

    public void ChooseTheme(Theme theme)
    {
        themes.Use(theme);
        logger.LogInformation("Theme changed to {Theme}", theme.Name);
        ShowThemes();
        Saving = SaveAsync(settings => settings with { ThemeName = theme.Name });
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        Problem = null;
        try
        {
            _saved = await store.LoadAsync(cancellationToken);
            _ignoreCertificateErrors = _saved.IgnoreCertificateErrors;
            translator.Use(Translation.Find(_saved.LanguageName));
            themes.Use(themes.Find(_saved.ThemeName));
            logger.LogInformation("Using {Language} and {Theme}", translator.Current.Name, themes.Current.Name);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not load the settings, keeping the current ones");
        }
    }

    async Task SaveAsync(Func<AppSettings, AppSettings> change)
    {
        try
        {
            _saved = await store.UpdateAsync(change, CancellationToken.None);
            Problem = null;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not save the settings");
            // The sender reads the file, so show what the file really holds.
            Set(ref _ignoreCertificateErrors, _saved.IgnoreCertificateErrors, nameof(IgnoreCertificateErrors));
            Problem = translator.Format("Settings.SaveFailed", translator.DetailsOf(exception));
        }
    }
}
