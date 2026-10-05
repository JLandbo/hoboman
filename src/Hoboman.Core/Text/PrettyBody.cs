using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Hoboman.Core.Text;

// Lays out a body to be read, with a tab for each level, or gives null when it is not what it is laid out as.
// It knows nothing of the app, so a request, a response and the CLI lay out bodies alike.
public static class PrettyBody
{
    // A tab for each level, the same as the Tab key puts into a request body, so the two look alike.
    static readonly JsonSerializerOptions _pretty = new() { WriteIndented = true, IndentCharacter = '\t', IndentSize = 1, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string? Json(string body)
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
    public static string? Xml(string body)
    {
        try
        {
            // Line breaks between tags would count as text, and the writer does not indent text mixed with tags, so they are ignored.
            // Browsers also accept a byte order mark or line breaks before the declaration, which XML itself does not.
            using var reader = XmlReader.Create(new StringReader(body.TrimStart('﻿').TrimStart()), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreWhitespace = true });
            var document = XDocument.Load(reader);
            return document.Declaration is { } declaration ? $"{declaration}{Environment.NewLine}{TabbedOf(document)}" : TabbedOf(document);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    // A tag on each line with a tab for each level, like XML, and an element that holds only text on one line.
    // An element that is never closed holds the rest of the body, and an end tag of no open element closes nothing.
    public static string? Html(string body)
    {
        var html = body.TrimStart('﻿').Trim();
        var tokens = HtmlTokens.Of(html).ToList();
        if (tokens is not [{ Kind: not HtmlTokenKind.Text }, ..])
        {
            return null;
        }
        var lines = new StringBuilder();
        var open = new List<string>();
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            var part = html[token.Start..token.End];
            switch (token.Kind)
            {
                // Kept text, such as a script, goes as it is between its tags.
                case HtmlTokenKind.Open when index + 2 < tokens.Count && tokens[index + 1] is { Kind: HtmlTokenKind.Text or HtmlTokenKind.Kept } inner
                    && tokens[index + 2] is { Kind: HtmlTokenKind.Close } close && close.Name.Equals(token.Name, StringComparison.OrdinalIgnoreCase):
                    var text = html[inner.Start..inner.End];
                    Line(part + (inner.Kind == HtmlTokenKind.Kept ? text : text.Trim()) + html[close.Start..close.End]);
                    index += 2;
                    break;
                case HtmlTokenKind.Open:
                    Line(part);
                    open.Add(token.Name);
                    break;
                case HtmlTokenKind.Close:
                    if (open.FindLastIndex(name => name.Equals(token.Name, StringComparison.OrdinalIgnoreCase)) is var at and >= 0)
                    {
                        open.RemoveRange(at, open.Count - at);
                    }
                    Line(part);
                    break;
                case HtmlTokenKind.Text when part.Trim() is { Length: > 0 } trimmed:
                    Line(trimmed);
                    break;
                case HtmlTokenKind.Single or HtmlTokenKind.Kept:
                    Line(part);
                    break;
            }
        }
        return lines.ToString().TrimEnd();

        void Line(string text) => lines.Append('\t', open.Count).Append(text).AppendLine();
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
