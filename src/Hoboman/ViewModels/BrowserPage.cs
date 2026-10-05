using System.Text;
using Hoboman.Core.Sending;

namespace Hoboman.ViewModels;

// What the browser shows of a response: its bytes and their type. Without a type, the browser tells what they are itself, as it does on the web.
public sealed record BrowserPage(byte[] Bytes, string? ContentType)
{
    // A response that came as Base64 is decoded first, and its Content-Type tells of the Base64, not of what it holds.
    // A response from the history has only its text, which is all of it only when it is text, not a file such as an image or a PDF.
    public static BrowserPage? Of(ApiResponse response, bool decodeWhole)
    {
        if (!decodeWhole)
        {
            return response.Bytes is { } bytes ? new(bytes, response.ContentType)
                : ResponseDisplay.FormatOf(response) == BodyFormat.Browser ? null
                : new(Encoding.UTF8.GetBytes(response.Body), response.ContentType);
        }
        try
        {
            return new(Convert.FromBase64String(response.Body.Trim()), null);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
