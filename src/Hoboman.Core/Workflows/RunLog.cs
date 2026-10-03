using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Hoboman.Core.Storage;
using Hoboman.Core.Text;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Workflows;

// One file per run, kept open while it runs, with one line per event, so another program can follow it.
// A run log that cannot be written is only logged, as the run matters more than its log.
public sealed class RunLog : IAsyncDisposable
{
    readonly FileStream? _file;
    readonly ILogger _logger;

    RunLog(string runId, string filePath, FileStream? file, ILogger logger)
    {
        RunId = runId;
        FilePath = filePath;
        _file = file;
        _logger = logger;
    }

    public string RunId { get; }

    public string FilePath { get; }

    // Named by time like the calls in the history, with a short random part for runs that start in the same millisecond.
    public static RunLog Create(AppFolder folder, Guid workflowId, ILogger logger)
    {
        var runId = string.Create(CultureInfo.InvariantCulture, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Random.Shared.Next(0x10000):x4}");
        var path = Path.GetFullPath(Path.Combine(folder.Runs, $"{workflowId}", $"{runId}.jsonl"));
        FileStream? file = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete, 0, FileOptions.Asynchronous);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not create the run log {Path}", path);
        }
        return new(runId, path, file, logger);
    }

    // Each line is written in one go and flushed, so a reader sees an event as soon as it happens.
    public async Task AddAsync(WorkflowEvent workflowEvent)
    {
        if (_file is null)
        {
            return;
        }
        try
        {
            await _file.WriteAsync(LineOf(workflowEvent)).ConfigureAwait(false);
            await _file.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            _logger.LogError(exception, "Could not add an event to the run log {Path}", FilePath);
        }
    }

    public ValueTask DisposeAsync() => _file?.DisposeAsync() ?? ValueTask.CompletedTask;

    // The event as one line of compact JSON, ending in \n.
    public static ReadOnlyMemory<byte> LineOf(WorkflowEvent workflowEvent)
    {
        var line = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(line, new JsonWriterOptions { Encoder = CompactJson.Options.Encoder }))
        {
            JsonSerializer.Serialize(writer, workflowEvent, CompactJson.Options);
        }
        line.Write("\n"u8);
        return line.WrittenMemory;
    }
}
