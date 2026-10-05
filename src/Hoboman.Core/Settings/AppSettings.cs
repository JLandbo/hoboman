using Hoboman.Core.Environments;

namespace Hoboman.Core.Settings;

public sealed record AppSettings(bool IgnoreCertificateErrors = false, string? LanguageName = null, WindowLayout? Layout = null, TabSession? Session = null, Guid? EnvironmentId = null, string? ThemeName = null,
    int? DeleteHistoryAfterDays = null, bool SearchWholeFolders = true)
{
    public static AppSettings Default { get; } = new();

    public ApiEnvironment? EnvironmentIn(IEnumerable<ApiEnvironment> environments) => environments.FirstOrDefault(environment => environment.Id == EnvironmentId);
}
