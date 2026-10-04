using Hoboman.Core.Environments;

namespace Hoboman.Core.Settings;

// EnvironmentName is only read, from settings saved before environments had ids.
public sealed record AppSettings(string? EnvironmentName = null, bool IgnoreCertificateErrors = false, string? LanguageName = null, WindowLayout? Layout = null, TabSession? Session = null, Guid? EnvironmentId = null, string? ThemeName = null,
    int? DeleteHistoryAfterDays = null)
{
    public static AppSettings Default { get; } = new();

    public ApiEnvironment? EnvironmentIn(IEnumerable<ApiEnvironment> environments) => EnvironmentId is { } id
        ? environments.FirstOrDefault(environment => environment.Id == id)
        : environments.FirstOrDefault(environment => environment.Name == EnvironmentName);
}
