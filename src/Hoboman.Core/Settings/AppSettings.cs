namespace Hoboman.Core.Settings;

public sealed record AppSettings(string? EnvironmentName = null, bool IgnoreCertificateErrors = false)
{
    public static AppSettings Default { get; } = new();
}
