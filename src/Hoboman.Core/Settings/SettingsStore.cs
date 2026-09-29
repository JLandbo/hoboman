using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Settings;

public sealed class SettingsStore(AppFolder folder, ILogger<SettingsStore> logger)
{
    readonly JsonFile<AppSettings> _file = new(folder.Settings, AppSettings.Default, logger);

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => _file.LoadAsync(cancellationToken);

    public Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken) => _file.UpdateAsync(change, cancellationToken);
}
