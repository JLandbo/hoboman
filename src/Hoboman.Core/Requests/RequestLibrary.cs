using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Requests;

public sealed class RequestLibrary(AppFolder folder, ILogger<RequestLibrary> logger)
{
    public IEnumerable<string> Names() =>
        Directory.Exists(folder.Requests)
            ? Directory.EnumerateFiles(folder.Requests, "*.json", SearchOption.AllDirectories).Select(file => Path.ChangeExtension(Path.GetRelativePath(folder.Requests, file), null).Replace('\\', '/'))
            : [];

    public Task<ApiRequest?> LoadAsync(string name, CancellationToken cancellationToken) => FileOf(name).LoadAsync(cancellationToken);

    public Task SaveAsync(string name, ApiRequest request, CancellationToken cancellationToken) => FileOf(name).SaveAsync(request, cancellationToken);

    JsonFile<ApiRequest?> FileOf(string name) => new(Path.Combine(folder.Requests, $"{name}.json"), null, logger);
}
