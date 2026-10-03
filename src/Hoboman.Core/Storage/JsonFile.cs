using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Storage;

public sealed class JsonFile<T>(string path, T empty, ILogger logger)
{
    static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // People and agents edit these files by hand, so they are written readable and read forgivingly.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        // A misspelled name would otherwise be ignored without a word and dropped on the next save.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { NoNullItems.Check } },
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
                throw new InvalidFileException(path, exception);
            }
        }
    }

    // The write lock is taken before leaving the caller's thread, so saves are written in the order they were asked for.
    public async Task SaveAsync(T value, CancellationToken cancellationToken, bool createDirectory = true, bool overwrite = true)
    {
        await _writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LeaveCallersThread();
            await WriteAsync(value, cancellationToken, createDirectory, overwrite).ConfigureAwait(false);
        }
        finally
        {
            _writing.Release();
        }
    }

    public async Task<T> UpdateAsync(Func<T, T> change, CancellationToken cancellationToken)
    {
        await _writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LeaveCallersThread();
            var value = change(await LoadAsync(cancellationToken).ConfigureAwait(false));
            await WriteAsync(value, cancellationToken, createDirectory: true, overwrite: true).ConfigureAwait(false);
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

    async Task WriteAsync(T value, CancellationToken cancellationToken, bool createDirectory, bool overwrite)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (createDirectory)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                }
                var temporary = path + ".tmp";
                await using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                {
                    await JsonSerializer.SerializeAsync(file, value, _options, cancellationToken).ConfigureAwait(false);
                    file.Flush(flushToDisk: true);
                }
                File.Move(temporary, path, overwrite);
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
    static bool CanRetry(Exception exception, int attempt) => exception is IOException or UnauthorizedAccessException or JsonException && attempt < Retrying.Attempts;

    Task PauseAsync(Exception exception, CancellationToken cancellationToken)
    {
        logger.LogDebug(exception, "{Path} could not be used, trying again", path);
        return Task.Delay(Retrying.Pause, cancellationToken);
    }
}
