using System.Text.RegularExpressions;
using System.Windows.Documents;

namespace Hoboman.Controls;

public static class Runs
{
    public static IEnumerable<Run> Of(string text, IEnumerable<Match> matches, Func<Match, string> brushOf)
    {
        var start = 0;
        foreach (var match in matches)
        {
            yield return new Run(text[start..match.Index]);
            var run = new Run(match.Value);
            run.SetResourceReference(TextElement.ForegroundProperty, brushOf(match));
            yield return run;
            start = match.Index + match.Length;
        }
        yield return new Run(text[start..]);
    }
}
