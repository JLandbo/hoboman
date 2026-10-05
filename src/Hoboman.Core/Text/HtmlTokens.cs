namespace Hoboman.Core.Text;

public enum HtmlTokenKind { Open, Close, Single, Text, Kept }

public readonly record struct HtmlToken(HtmlTokenKind Kind, int Start, int End, string Name);

// Splits HTML into its tags and the text between them without asking it to be valid XML, as HTML rarely is.
// Shared by the formatting and the folds, so the two agree on where an element starts and ends.
public static class HtmlTokens
{
    static readonly HashSet<string> _void = new(["area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr"], StringComparer.OrdinalIgnoreCase);

    // Their content is no HTML, or its spaces matter, so it is kept as it is.
    static readonly HashSet<string> _kept = new(["script", "style", "pre", "textarea"], StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<HtmlToken> Of(string text)
    {
        var at = 0;
        while (at < text.Length)
        {
            var start = at;
            if (!IsTagAt(text, at))
            {
                at = NextTag(text, at + 1);
                yield return new(HtmlTokenKind.Text, start, at, "");
                continue;
            }
            if (text.AsSpan(at).StartsWith("<!--"))
            {
                at = text.IndexOf("-->", at + 4, StringComparison.Ordinal) is var end and >= 0 ? end + 3 : text.Length;
                yield return new(HtmlTokenKind.Single, start, at, "");
                continue;
            }
            at = EndOfTag(text, at);
            var name = NameOf(text, start);
            if (text[start + 1] == '/')
            {
                yield return new(HtmlTokenKind.Close, start, at, name);
            }
            else if (text[start + 1] is '!' or '?' || _void.Contains(name) || text[at - 1] == '>' && text[at - 2] == '/')
            {
                yield return new(HtmlTokenKind.Single, start, at, name);
            }
            else
            {
                yield return new(HtmlTokenKind.Open, start, at, name);
                if (_kept.Contains(name))
                {
                    var content = at;
                    at = text.IndexOf($"</{name}", at, StringComparison.OrdinalIgnoreCase) is var close and >= 0 ? close : text.Length;
                    if (at > content)
                    {
                        yield return new(HtmlTokenKind.Kept, content, at, "");
                    }
                }
            }
        }
    }

    // A < that starts no tag, such as in "a < b", is text.
    static bool IsTagAt(string text, int at) =>
        text[at] == '<' && at + 1 < text.Length && (char.IsAsciiLetter(text[at + 1]) || text[at + 1] is '/' or '!' or '?');

    static int NextTag(string text, int from)
    {
        for (var at = from; at < text.Length; at++)
        {
            if (IsTagAt(text, at))
            {
                return at;
            }
        }
        return text.Length;
    }

    // A > inside a quoted attribute does not end the tag.
    static int EndOfTag(string text, int start)
    {
        char? quote = null;
        for (var at = start + 1; at < text.Length; at++)
        {
            var symbol = text[at];
            if (quote is not null)
            {
                quote = symbol == quote ? null : quote;
            }
            else if (symbol is '"' or '\'')
            {
                quote = symbol;
            }
            else if (symbol == '>')
            {
                return at + 1;
            }
        }
        return text.Length;
    }

    static string NameOf(string text, int start)
    {
        var from = text[start + 1] == '/' ? start + 2 : start + 1;
        var to = from;
        while (to < text.Length && (char.IsAsciiLetterOrDigit(text[to]) || text[to] is '-' or ':'))
        {
            to++;
        }
        return text[from..to];
    }
}
