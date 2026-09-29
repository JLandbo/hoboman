using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Storage;

public sealed class JsonFile<T>(string path, T empty, ILogger logger)
{
    const int _attempts = 5;
    static readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(50);
    static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter() },
    };

    readonly SemaphoreSlim _writing = new(1, 1);

    public async Task<T> LoadAsync(CancellationToken cancellationToken)
    {
        await LeaveCallersThread();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (CanRetry(exception, attempt))
            {
                await PauseAsync(exception, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException($"{path} is not valid: {exception.Message}", exception);
            }
        }
    }

    public async Task SaveAsync(T value, CancellationToken cancellationToken)
    {
        await LeaveCallersThread();
        await _writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteAsync(value, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writing.Release();
        }
    }

    public async Task<T> UpdateAsync(Func<T, T> change, CancellationToken cancellationToken)
    {
        await LeaveCallersThread();
        await _writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var value = change(await LoadAsync(cancellationToken).ConfigureAwait(false));
            await WriteAsync(value, cancellationToken).ConfigureAwait(false);
            return value;
        }
        finally
        {
            _writing.Release();
        }
    }

    async Task<T> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            logger.LogDebug("{Path} does not exist yet", path);
            return empty;
        }
        var value = JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), _options) ?? empty;
        logger.LogDebug("Loaded {Path}", path);
        return value;
    }

    async Task WriteAsync(T value, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporary = path + ".tmp";
                await using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                {
                    await JsonSerializer.SerializeAsync(file, value, _options, cancellationToken).ConfigureAwait(false);
                    file.Flush(flushToDisk: true);
                }
                File.Move(temporary, path, overwrite: true);
                logger.LogDebug("Saved {Path}", path);
                return;
            }
            catch (Exception exception) when (CanRetry(exception, attempt))
            {
                await PauseAsync(exception, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // Small files are read and written synchronously even through async calls, so this keeps the disk work off the UI thread.
    static ConfiguredTaskAwaitable LeaveCallersThread() => Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

    // Invalid JSON is retried too, because another program may be halfway through writing the file.
    static bool CanRetry(Exception exception, int attempt) => exception is IOException or UnauthorizedAccessException or JsonException && attempt < _attempts;

    Task PauseAsync(Exception exception, CancellationToken cancellationToken)
    {
        logger.LogDebug(exception, "{Path} could not be used, trying again", path);
        return Task.Delay(_retryDelay, cancellationToken);
    }
}
