using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Hoboman.Core.Base64;

public static class Base64Text
{
    // Bytes that are not UTF-8 are not text, so they are turned down instead of shown as replacement characters.
    static readonly UTF8Encoding _strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    public static string Decode(string text) => _strict.GetString(Convert.FromBase64String(text));

    public static bool TryDecode(string text, [NotNullWhen(true)] out string? decoded)
    {
        try
        {
            decoded = Decode(text);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or DecoderFallbackException)
        {
            decoded = null;
            return false;
        }
    }
}
