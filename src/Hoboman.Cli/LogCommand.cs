using System.Globalization;
using System.Text.Json;
using Hoboman.Core.Storage;
using Hoboman.Core.Workflows;

namespace Hoboman.Cli;

// Reads the runs of a workflow, also those started in the app, as they are logged. It only reads, so it can never send anything.
sealed class LogCommand(AppFolder folder, Targets targets, CliOutput output)
{
    const int _chunk = 64 * 1024;
    static readonly TimeSpan _poll = TimeSpan.FromMilliseconds(200);

    public async Task<int> RunAsync(LogInput input, CancellationToken cancellationToken)
    {
        var found = await targets.WorkflowsAsync(input.Workflow, cancellationToken);
        if (found is not [var id])
        {
            return await output.WriteErrorAsync(found.Count == 0 ? "Workflow could not be found." : "Workflow name is ambiguous. Use its id.");
        }
        var runs = RunLog.RunsOf(folder, id);
        if (input.Run is null && !input.Last)
        {
            await output.WriteNamesAsync(runs.Select(run => $"{run}\t{StartOf(run)}\t{OutcomeOf(RunLog.PathOf(folder, id, run))}"), cancellationToken);
            return 0;
        }
        // Only a run in the list is read, so a run id cannot lead to another file.
        var chosen = input.Last ? runs.FirstOrDefault() : runs.FirstOrDefault(run => run == input.Run);
        if (chosen is null)
        {
            return await output.WriteErrorAsync("Run could not be found.");
        }
        try
        {
            return await WriteAsync(RunLog.PathOf(folder, id, chosen), input.Follow, cancellationToken);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            return await output.WriteErrorAsync("Run could not be read.");
        }
        // Ctrl+C stops the following, which is no error of the run.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 1;
        }
    }

    // The id of a run begins with when it started, in UTC.
    static string StartOf(string run) =>
        DateTime.TryParseExact(run[..Math.Min(run.Length, 19)], "yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var start)
            ? start.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) : "-";

    // A run without its last event is still running, or was stopped without it, which the list does not tell apart.
    static string OutcomeOf(string path)
    {
        try
        {
            using var file = Open(path);
            return OutcomeIfFinished(StartOfLastLine(file)) ?? "-";
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            return "-";
        }
    }

    // A line can be large, as a step's body is in it, so the file is read back from its end only to where its last line starts, and only that start is kept.
    static byte[] StartOfLastLine(FileStream file)
    {
        var end = file.Length - 1;
        var chunk = new byte[_chunk];
        for (var position = end; position > 0;)
        {
            var size = (int)Math.Min(_chunk, position);
            position -= size;
            file.Seek(position, SeekOrigin.Begin);
            file.ReadExactly(chunk, 0, size);
            if (Array.LastIndexOf(chunk, (byte)'\n', size - 1, size) is var at and >= 0)
            {
                return BytesAt(file, position + at + 1);
            }
        }
        return BytesAt(file, 0);
    }

    static byte[] BytesAt(FileStream file, long position)
    {
        var start = new byte[(int)Math.Min(4096, file.Length - position)];
        file.Seek(position, SeekOrigin.Begin);
        file.ReadExactly(start);
        return start;
    }

    // The type and the outcome are the first properties of a line, so its start tells, also when the rest of the line is not read.
    static string? OutcomeIfFinished(ReadOnlySpan<byte> start)
    {
        var reader = new Utf8JsonReader(start, isFinalBlock: false, default);
        string? type = null;
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                if (!reader.Read() || reader.TokenType != JsonTokenType.String)
                {
                    return null;
                }
                if (name == "type")
                {
                    type = reader.GetString();
                }
                else if (name == "outcome")
                {
                    return type == "run.finished" ? reader.GetString() : null;
                }
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    // Only whole lines are written, as the app can be in the middle of one. Following, it waits for more until the run has finished,
    // or until nothing writes the file any more, as when the run was stopped without its last event.
    async Task<int> WriteAsync(string path, bool follow, CancellationToken cancellationToken)
    {
        await using var file = Open(path);
        var pending = new List<byte>();
        var searched = 0;
        var buffer = new byte[_chunk];
        while (true)
        {
            var read = await file.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                if (!follow)
                {
                    return 0;
                }
                if (!IsWritten(path))
                {
                    // What was written before the file was let go is read before it is given up on.
                    if ((read = await file.ReadAsync(buffer, cancellationToken)) == 0)
                    {
                        return 1;
                    }
                }
                else
                {
                    await Task.Delay(_poll, cancellationToken);
                    continue;
                }
            }
            pending.AddRange(buffer.AsSpan(0, read));
            // Only what came since the last search is searched, so a large line is not searched again for every part of it.
            int end;
            while ((end = pending.IndexOf((byte)'\n', searched)) >= 0)
            {
                var line = pending.GetRange(0, end + 1).ToArray();
                pending.RemoveRange(0, end + 1);
                searched = 0;
                await output.WriteLineAsync(line);
                if (follow && OutcomeIfFinished(line) is not null)
                {
                    return 0;
                }
            }
            searched = pending.Count;
        }
    }

    // A run that is running keeps its file open for writing, and a file that can be opened without letting others write has no writer.
    static bool IsWritten(string path)
    {
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return false;
        }
        // A run whose file was deleted, as old runs are, is written no more.
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    // The app keeps a running run's file open for writing, so it is read alongside.
    static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
}
