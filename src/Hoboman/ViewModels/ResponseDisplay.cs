using System.Globalization;
using System.Text.Encodings.Web;
using System.IO;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Hoboman.Core.Sending;

namespace Hoboman.ViewModels;

public sealed record ResponseDisplay(string Status, bool IsSuccess, string Elapsed, string Size, string Body, string Headers, int HeaderCount, BodyFormat Coloring)
{
    // A tab for each level, the same as the Tab key puts into a request body, so the two look alike.
    static readonly JsonSerializerOptions _pretty = new() { WriteIndented = true, IndentCharacter = '\t', IndentSize = 1, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

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
        response.Headers.FirstOrDefault(header => header.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))?.Value switch
        {
            { } type when type.Contains("json", StringComparison.OrdinalIgnoreCase) => BodyFormat.Json,
            { } type when type.Contains("xml", StringComparison.OrdinalIgnoreCase) => BodyFormat.Xml,
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
        BodyFormat.Json when PrettyJsonOf(body) is { } json => (json, BodyFormat.Json),
        BodyFormat.Xml when PrettyXmlOf(body) is { } xml => (xml, BodyFormat.Xml),
        _ => (body, BodyFormat.Raw),
    };

    internal static string? PrettyJsonOf(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return JsonSerializer.Serialize(json.RootElement, _pretty);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // DTDs are ignored, so a body can never make the formatting expand entities or read other files. HTML is rarely valid XML and stays as it is.
    internal static string? PrettyXmlOf(string body)
    {
        try
        {
            // Line breaks between tags would count as text, and the writer does not indent text mixed with tags, so they are ignored.
            // Browsers also accept a byte order mark or line breaks before the declaration, which XML itself does not.
            using var reader = XmlReader.Create(new StringReader(body.TrimStart('\uFEFF').TrimStart()), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreWhitespace = true });
            var document = XDocument.Load(reader);
            return document.Declaration is { } declaration ? $"{declaration}{Environment.NewLine}{TabbedOf(document)}" : TabbedOf(document);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    // A tab for each level, like JSON. The declaration is written by the caller, as the writer would put its own encoding into it.
    static string TabbedOf(XDocument document)
    {
        var text = new StringWriter(CultureInfo.InvariantCulture);
        using (var writer = XmlWriter.Create(text, new XmlWriterSettings { Indent = true, IndentChars = "\t", OmitXmlDeclaration = true }))
        {
            document.Save(writer);
        }
        return text.ToString();
    }
}
