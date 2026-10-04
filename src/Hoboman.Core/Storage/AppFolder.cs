namespace Hoboman.Core.Storage;

public sealed class AppFolder(string root)
{
    public string Root => root;

    public string Requests => Path.Combine(root, "requests");

    public string Environments => Path.Combine(root, "environments.json");

    public string History => Path.Combine(root, "history");

    public string Logs => Path.Combine(root, "logs");

    public string Workflows => Path.Combine(root, "workflows");

    public string Runs => Path.Combine(root, "runs");

    public string Themes => Path.Combine(root, "themes");

    public string Secrets => Path.Combine(root, "secrets.json");

    public string Settings => Path.Combine(root, "settings.json");

    public string RequestOrder => Path.Combine(root, "request-order.json");

    public string PendingSecretCleanup => Path.Combine(root, "pending-secret-cleanup.json");
}
