using Hoboman.Core.Text;
using ICSharpCode.AvalonEdit.Folding;

namespace Hoboman.Controls;

// Folds the elements of an HTML text that span lines from their start tag to their end tag, named like the folds of XML.
// An element that is never closed has no end to fold to, and is left open.
public static class HtmlFolds
{
    public static IReadOnlyList<NewFolding> Of(string text)
    {
        var folds = new List<NewFolding>();
        var open = new List<HtmlToken>();
        foreach (var token in HtmlTokens.Of(text))
        {
            if (token.Kind == HtmlTokenKind.Open)
            {
                open.Add(token);
            }
            else if (token.Kind == HtmlTokenKind.Close && open.FindLastIndex(start => start.Name.Equals(token.Name, StringComparison.OrdinalIgnoreCase)) is var at and >= 0)
            {
                var start = open[at];
                open.RemoveRange(at, open.Count - at);
                if (text.AsSpan(start.Start, token.End - start.Start).Contains('\n'))
                {
                    folds.Add(new(start.Start, token.End) { Name = $"<{start.Name}>" });
                }
            }
        }
        // The folds are found where they end, and are wanted in the order they start.
        return [.. folds.OrderBy(fold => fold.StartOffset)];
    }
}
