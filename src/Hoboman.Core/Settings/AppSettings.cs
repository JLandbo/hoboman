namespace Hoboman.Core.Settings;

public sealed record AppSettings(string? EnvironmentName = null, bool IgnoreCertificateErrors = false, string? LanguageName = null)
{
    public static AppSettings Default { get; } = new();
}
