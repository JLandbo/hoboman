using System.CommandLine;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Hoboman.Core.Sending;

namespace Hoboman.Cli;

sealed class CliOutput(Stream output, Stream error)
{
    static readonly JsonSerializerOptions _json = new(JsonSerializerOptions.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
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

    // Written even when the call was cancelled, so the caller learns why it stopped.
    public async Task<int> WriteErrorAsync(string problem)
    {
        await WriteJsonAsync(error, new { error = problem }, CancellationToken.None);
        return 2;
    }

    // Serialized straight into the stream, so a large body is not copied into one more string first.
    static async Task WriteJsonAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        await JsonSerializer.SerializeAsync(stream, value, _json, cancellationToken);
        await stream.WriteAsync(_newLine, cancellationToken);
    }

    static StreamWriter Writer(Stream stream) => new(stream, new UTF8Encoding(false), leaveOpen: true);
}
