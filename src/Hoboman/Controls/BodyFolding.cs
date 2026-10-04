using System.Globalization;
using System.Windows.Threading;
using Hoboman.Mvvm;
using Hoboman.ViewModels;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;

namespace Hoboman.Controls;

// Folds the objects and lists of a JSON body and the elements of an XML one, with arrows in a margin of its own.
// The folds belong to a document, so a new document, such as another tab's body, gets new ones.
sealed class BodyFolding
{
    // Long enough that the folds do not change with every key in an editable body, or with every tab passed on the way to another.
    static readonly TimeSpan _standStill = TimeSpan.FromMilliseconds(400);

    // Only reads the text it is given, so it can work off the UI thread.
    static readonly XmlFoldingStrategy _xml = new();

    readonly MarkedEditor _editor;
    readonly FoldMargin _margin;
    readonly FoldingElementGenerator _generator = new();
    readonly DispatcherTimer _waiting;
    readonly Coalescer _searches;
    // Counts the changes, so a search that is done after one came is left out.
    int _finding;

    public BodyFolding(MarkedEditor editor)
    {
        _editor = editor;
        // Before the margin, as its texts can ask for an update while it is made.
        _waiting = new(_standStill, DispatcherPriority.Background, (_, _) => Search(), editor.Dispatcher) { IsEnabled = false };
        _searches = new(FindAsync);
        _margin = new(this);
        // AvalonEdit only folds right when the folds are made before any other element.
        editor.TextArea.TextView.ElementGenerators.Insert(0, _generator);
        editor.TextArea.LeftMargins.Add(_margin);
        editor.DocumentChanged += (_, _) => Renew();
        // Only an editable body changes while it is shown.
        editor.TextChanged += (_, _) =>
        {
            if (!editor.IsReadOnly)
            {
                UpdateSoon();
            }
        };
        editor.TextArea.Caret.PositionChanged += (_, _) => Unfold();
        Renew();
    }

    public FoldingManager? Manager { get; private set; }

    public bool CanFold => _editor.Coloring is BodyFormat.Json or BodyFormat.Xml;

    // Every change waits until things stand still, so a switch of tab, which changes the document and the coloring one after the other,
    // or the four texts of a new language, give one search. A search already at work is of a text that is no longer wanted.
    public void UpdateSoon()
    {
        _finding++;
        _waiting.Stop();
        _waiting.Start();
    }

    // One search at a time, and asking while one runs gives one more afterwards, of the body shown then, so searches of large bodies do not pile up.
    async void Search()
    {
        _waiting.Stop();
        await _searches.RunAsync();
    }

    // A body of many megabytes takes a while to search, so the folds are found off the UI thread in a snapshot of the text, and only taking them into use happens here.
    async Task FindAsync()
    {
        var finding = _finding;
        if (Manager is not { } manager || _editor.Document is not { } document)
        {
            return;
        }
        if (!CanFold)
        {
            manager.Clear();
            Redraw();
            return;
        }
        var coloring = _editor.Coloring;
        var text = document.CreateSnapshot();
        var (json, xml, firstError) = await Task.Run(() => Find(coloring, text));
        // The body, its coloring or its text changed while this one was at work.
        if (finding != _finding || manager != Manager)
        {
            return;
        }
        // With room inside the box AvalonEdit draws around it, so the name does not touch the brackets.
        manager.UpdateFoldings(xml ?? json.Select(fold => new NewFolding(fold.Start, fold.End) { Name = $" {NameOf(fold)} " }), firstError);
        Redraw();
    }

    static (IReadOnlyList<JsonFold> Json, IReadOnlyList<NewFolding>? Xml, int FirstError) Find(BodyFormat coloring, ITextSource text)
    {
        if (coloring == BodyFormat.Json)
        {
            return (JsonFolds.Of(text.Text), null, -1);
        }
        var xml = _xml.CreateNewFoldings(new TextDocument(text), out var firstError);
        return ([], [.. xml], firstError);
    }

    void Redraw()
    {
        _margin.InvalidateMeasure();
        _margin.InvalidateVisual();
    }

    void Renew()
    {
        Manager?.Clear();
        Manager = _editor.Document is { } document ? new FoldingManager(document) : null;
        _generator.FoldingManager = Manager;
        UpdateSoon();
    }

    // A caret moved into a folded part would be hidden in it.
    void Unfold()
    {
        var caret = _editor.TextArea.Caret.Offset;
        foreach (var fold in Manager?.GetFoldingsContaining(caret) ?? [])
        {
            if (fold.IsFolded && fold.StartOffset < caret && caret < fold.EndOffset)
            {
                fold.IsFolded = false;
            }
        }
    }

    string NameOf(JsonFold fold) => (fold.Count, fold.IsList) switch
    {
        (1, true) => _margin.ElementText,
        (1, false) => _margin.PropertyText,
        (var count, true) => string.Format(CultureInfo.CurrentCulture, _margin.ElementsText, count),
        (var count, false) => string.Format(CultureInfo.CurrentCulture, _margin.PropertiesText, count),
    };
}
