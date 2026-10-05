using Hoboman.Core.Sending;
using Hoboman.Core.Text;

namespace Hoboman.ViewModels;

public sealed record ResponseDisplay(string Status, bool IsSuccess, string Elapsed, string Size, string Body, string Headers, int HeaderCount, BodyFormat Coloring)
{
    public static ResponseDisplay Of(ApiResponse response) => Of(response, FormatOf(response));

    public static ResponseDisplay Of(ApiResponse response, BodyFormat format)
    {
        var (body, coloring) = Format(response.Body, format);
        return new(
            $"{response.StatusCode} {response.Reason}".Trim(),
            response.IsSuccess,
            $"{response.ElapsedMs} ms",
            SizeOf(response.Size),
            body,
            string.Join(Environment.NewLine, response.Headers.Select(header => $"{header.Name}: {header.Value}")),
            response.Headers.Count,
            coloring);
    }

    // Like Postman, only the Content-Type the server sends decides; without one the body is shown raw, and the user can choose.
    public static BodyFormat FormatOf(ApiResponse response) =>
        response.MediaType switch
        {
            { } type when type.Is("json") => BodyFormat.Json,
            { Subtype: "html" or "xhtml+xml" } => BodyFormat.Html,
            { } type when type.Is("xml") => BodyFormat.Xml,
            // Images and PDF files are no text to read, so they are shown as a browser shows them. An SVG is XML, which reads as text, and comes before.
            { Type: "image" } or { Subtype: "pdf" } => BodyFormat.Browser,
            _ => BodyFormat.Raw,
        };

    internal static string SizeOf(long size) => size switch
    {
        < 1024 => $"{size} B",
        < 1024 * 1024 => $"{size / 1024d:0.#} KB",
        _ => $"{size / (1024d * 1024):0.#} MB",
    };

    // A body that does not fit the chosen format is shown as it is, without colors.
    internal static (string Body, BodyFormat Coloring) Format(string body, BodyFormat format) => format switch
    {
        BodyFormat.Json when PrettyBody.Json(body) is { } json => (json, BodyFormat.Json),
        BodyFormat.Xml when PrettyBody.Xml(body) is { } xml => (xml, BodyFormat.Xml),
        BodyFormat.Html when PrettyBody.Html(body) is { } html => (html, BodyFormat.Html),
        // The browser shows the bytes, so no text is laid out for a view that is hidden, such as a large PDF.
        BodyFormat.Browser => ("", BodyFormat.Raw),
        _ => (body, BodyFormat.Raw),
    };
}
