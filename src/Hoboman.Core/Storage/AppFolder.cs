namespace Hoboman.Core.Storage;

public sealed class AppFolder(string root)
{
    public string Root => root;

    public string Requests => Path.Combine(root, "requests");

    public string Environments => Path.Combine(root, "environments.json");

    public string Logs => Path.Combine(root, "logs");

    public string Secrets => Path.Combine(root, "secrets.json");

    public string Settings => Path.Combine(root, "settings.json");
}
