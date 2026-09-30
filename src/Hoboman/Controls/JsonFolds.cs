namespace Hoboman.Controls;

// Finds the objects and lists of a JSON text by their brackets, so they can still be folded while the text is typed and is not JSON yet.
public static class JsonFolds
{
    public static IReadOnlyList<JsonFold> Of(string text)
    {
        var folds = new List<JsonFold>();
        var open = new Stack<Container>();
        var inText = false;
        for (var at = 0; at < text.Length; at++)
        {
            var symbol = text[at];
            if (inText)
            {
                if (symbol == '\\')
                {
                    at++;
                }
                else if (symbol == '"')
                {
                    inText = false;
                }
                continue;
            }
            switch (symbol)
            {
                case '"':
                    inText = true;
                    Fill();
                    break;
                case '{' or '[':
                    Fill();
                    open.Push(new(at + 1, symbol == '['));
                    break;
                case '}' or ']' when open.TryPop(out var container):
                    // One that fits on a line has nothing to fold away, and an empty one has nothing to show.
                    if (!container.IsEmpty && text.AsSpan(container.Start, at - container.Start).Contains('\n'))
                    {
                        folds.Add(new(container.Start, at, container.Commas + 1, container.IsList));
                    }
                    break;
                case ',' when open.TryPeek(out var container):
                    container.Commas++;
                    break;
                case not (' ' or '\t' or '\r' or '\n'):
                    Fill();
                    break;
            }
        }
        // The folds are found where they end, and are wanted in the order they start.
        return [.. folds.OrderBy(fold => fold.Start)];

        void Fill()
        {
            if (open.TryPeek(out var container))
            {
                container.IsEmpty = false;
            }
        }
    }

    // The commas between the properties or elements count them, once something is in it.
    sealed class Container(int start, bool isList)
    {
        public int Start => start;

        public bool IsList => isList;

        public bool IsEmpty { get; set; } = true;

        public int Commas { get; set; }
    }
}
