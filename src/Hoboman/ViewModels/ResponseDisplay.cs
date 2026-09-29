using System.Text.Encodings.Web;
using System.Text.Json;
using Hoboman.Core.Sending;

namespace Hoboman.ViewModels;

public sealed record ResponseDisplay(string Status, bool IsSuccess, string Elapsed, string Size, string Body, string Headers, int HeaderCount)
{
    static readonly JsonSerializerOptions _pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static ResponseDisplay Of(ApiResponse response) => new(
        $"{response.StatusCode} {response.Reason}".Trim(),
        response.StatusCode is >= 200 and < 300,
        $"{response.Elapsed.TotalMilliseconds:0} ms",
        SizeOf(response.Size),
        PrettyOf(response.Body),
        string.Join(Environment.NewLine, response.Headers.Select(header => $"{header.Name}: {header.Value}")),
        response.Headers.Count);

    internal static string SizeOf(long size) => size switch
    {
        < 1024 => $"{size} B",
        < 1024 * 1024 => $"{size / 1024d:0.#} KB",
        _ => $"{size / (1024d * 1024):0.#} MB",
    };

    internal static string PrettyOf(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return JsonSerializer.Serialize(json.RootElement, _pretty);
        }
        catch (JsonException)
        {
            return body;
        }
    }
}
