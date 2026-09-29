using Hoboman.Core.Languages;
using Hoboman.Core.Settings;
using Hoboman.Core.Storage;
using Hoboman.Mvvm;

namespace Hoboman.ViewModels;

public sealed class SettingsViewModel(JsonFile<AppSettings> file, Translator translator) : ObservableObject
{
    public IReadOnlyList<Translation> Languages => Translation.All;

    public Translation Language
    {
        get => translator.Current;
        set
        {
            translator.Use(value);
            file.Save(file.Load() with { LanguageName = value.Name });
        }
    }

    public bool IgnoreCertificateErrors
    {
        get => file.Load().IgnoreCertificateErrors;
        set => file.Save(file.Load() with { IgnoreCertificateErrors = value });
    }
}
