using System.Text.Encodings.Web;
using System.Text.Json;
using Hoboman.Core.Sending;

namespace Hoboman.ViewModels;

public sealed record ResponseDisplay(string Status, bool IsSuccess, string Elapsed, string Size, string Body, string Headers, int HeaderCount, bool IsCut)
{
    // WPF lays out the whole text on the UI thread, which freezes the window for bodies of many megabytes.
    public const int ShownLength = 1_000_000;

    static readonly JsonSerializerOptions _pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static ResponseDisplay Of(ApiResponse response)
    {
        var body = PrettyOf(response.Body);
        var isCut = body.Length > ShownLength;
        return new(
            $"{response.StatusCode} {response.Reason}".Trim(),
            response.IsSuccess,
            $"{response.ElapsedMs} ms",
            SizeOf(response.Size),
            isCut ? body[..ShownLength] : body,
            string.Join(Environment.NewLine, response.Headers.Select(header => $"{header.Name}: {header.Value}")),
            response.Headers.Count,
            isCut);
    }

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
