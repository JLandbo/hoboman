using Hoboman.Core.Storage;

namespace Hoboman.Core.Requests;

public sealed class RequestLibrary(AppFolder folder)
{
    public IEnumerable<string> Names() =>
        Directory.Exists(folder.Requests)
            ? Directory.EnumerateFiles(folder.Requests, "*.json", SearchOption.AllDirectories).Select(file => Path.ChangeExtension(Path.GetRelativePath(folder.Requests, file), null).Replace('\\', '/'))
            : [];

    public ApiRequest? Load(string name) => FileOf(name).Load();

    public void Save(string name, ApiRequest request) => FileOf(name).Save(request);

    JsonFile<ApiRequest?> FileOf(string name) => new(Path.Combine(folder.Requests, $"{name}.json"), null);
}
