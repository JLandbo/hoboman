using System.CommandLine;
using System.Text;
using System.Text.Json;
using Hoboman.Core.Sending;
using Hoboman.Core.Storage;
using Hoboman.Core.Text;
using Hoboman.Core.Workflows;

namespace Hoboman.Cli;

sealed class CliOutput(Stream output, Stream error)
{
    static readonly byte[] _newLine = Encoding.UTF8.GetBytes(Environment.NewLine);

    public int WriteStandardText(ParseResult parsed)
    {
        using var outputWriter = Writer(output);
        using var errorWriter = Writer(error);
        return parsed.Invoke(new InvocationConfiguration { Output = outputWriter, Error = errorWriter });
    }

    public async Task WriteNamesAsync(IEnumerable<string> names, CancellationToken cancellationToken)
    {
        await using var writer = Writer(output);
        foreach (var name in names)
        {
            await writer.WriteLineAsync(name.AsMemory(), cancellationToken);
        }
    }

    public async Task<int> WriteResponseAsync(ApiResponse response, CancellationToken cancellationToken)
    {
        await WriteJsonAsync(output, new { status = response.StatusCode, response.Reason, response.ElapsedMs, response.Size, response.Headers, response.Body }, cancellationToken);
        return response.IsSuccess ? 0 : 1;
    }

    // The body is in the file, so it is not written as text as well.
    public async Task<int> WriteResponseAsync(ApiResponse response, string file, CancellationToken cancellationToken)
    {
        await WriteJsonAsync(output, new { status = response.StatusCode, response.Reason, response.ElapsedMs, response.Size, response.Headers, file }, cancellationToken);
        return response.IsSuccess ? 0 : 1;
    }

    // The same bytes as in the run log, written in one go and flushed, so a reader sees each event as it happens.
    // Written even when the run was cancelled, so its last line tells how it ended.
    public async Task WriteEventAsync(WorkflowEvent workflowEvent)
    {
        await output.WriteAsync(RunLog.LineOf(workflowEvent), CancellationToken.None);
        await output.FlushAsync(CancellationToken.None);
    }

    // A line as it was logged, written in one go and flushed like an event.
    public async Task WriteLineAsync(byte[] line)
    {
        await output.WriteAsync(line, CancellationToken.None);
        await output.FlushAsync(CancellationToken.None);
    }

    public async Task<int> WriteResultAsync<T>(T result)
    {
        await WriteJsonAsync(output, result, CancellationToken.None);
        return 0;
    }

    public Task<int> WriteErrorAsync(string problem) => WriteErrorAsync(new { error = problem });

    // Only where the file is wrong is told, as the message of the exception can quote a value from it.
    public Task<int> WriteInvalidFileAsync(string problem, InvalidFileException exception, JsonException invalid) =>
        WriteErrorAsync(new { error = problem, file = exception.FilePath, path = invalid.Path, line = invalid.LineNumber + 1 });

    // Written even when the call was cancelled, so the caller learns why it stopped.
    public async Task<int> WriteErrorAsync<T>(T problem)
    {
        await WriteJsonAsync(error, problem, CancellationToken.None);
        return 2;
    }

    // Serialized straight into the stream, so a large body is not copied into one more string first.
    static async Task WriteJsonAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        await JsonSerializer.SerializeAsync(stream, value, CompactJson.Options, cancellationToken);
        await stream.WriteAsync(_newLine, cancellationToken);
    }

    static StreamWriter Writer(Stream stream) => new(stream, new UTF8Encoding(false), leaveOpen: true);
}
