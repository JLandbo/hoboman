using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;

namespace Hoboman.Controls;

// The arrows beside what can be folded: down while it is open and right while it is folded, as in the tree of requests.
// It also holds the texts a folded part shows, as they follow the language.
sealed class FoldMargin : AbstractMargin
{
    const double _width = 16;

    public static readonly DependencyProperty PropertyTextProperty = Text(nameof(PropertyText));

    public static readonly DependencyProperty PropertiesTextProperty = Text(nameof(PropertiesText));

    public static readonly DependencyProperty ElementTextProperty = Text(nameof(ElementText));

    public static readonly DependencyProperty ElementsTextProperty = Text(nameof(ElementsText));

    readonly BodyFolding _folding;

    public FoldMargin(BodyFolding folding)
    {
        _folding = folding;
        SetResourceReference(PropertyTextProperty, "Fold.Property");
        SetResourceReference(PropertiesTextProperty, "Fold.Properties");
        SetResourceReference(ElementTextProperty, "Fold.Element");
        SetResourceReference(ElementsTextProperty, "Fold.Elements");
    }

    public string PropertyText => (string)GetValue(PropertyTextProperty);

    public string PropertiesText => (string)GetValue(PropertiesTextProperty);

    public string ElementText => (string)GetValue(ElementTextProperty);

    public string ElementsText => (string)GetValue(ElementsTextProperty);

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView is not null)
        {
            oldTextView.VisualLinesChanged -= TextView_VisualLinesChanged;
        }
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null)
        {
            newTextView.VisualLinesChanged += TextView_VisualLinesChanged;
        }
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(_folding.CanFold ? _width : 0, 0);

    protected override void OnRender(DrawingContext drawing)
    {
        // Transparent rather than empty, so a click between the arrows still lands on the margin.
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (TextView is not { VisualLinesValid: true } textView || _folding.Manager is not { } manager)
        {
            return;
        }
        var font = new Typeface((FontFamily)FindResource("IconFont"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var line in textView.VisualLines)
        {
            if (FoldOn(manager, line) is { } fold)
            {
                var arrow = new FormattedText(fold.IsFolded ? "\uE76C" : "\uE70D", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, font, 9, (Brush)FindResource("Muted"), dip);
                var middle = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextMiddle) - textView.VerticalOffset;
                drawing.DrawText(arrow, new((_width - arrow.Width) / 2, middle - arrow.Height / 2));
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (FoldUnder(e) is { } fold)
        {
            fold.IsFolded = !fold.IsFolded;
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = FoldUnder(e) is null ? null : Cursors.Hand;
    }

    void TextView_VisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    FoldingSection? FoldUnder(MouseEventArgs e) =>
        TextView is { VisualLinesValid: true } textView && _folding.Manager is { } manager
        && textView.GetVisualLineFromVisualTop(e.GetPosition(textView).Y + textView.VerticalOffset) is { } line
            ? FoldOn(manager, line)
            : null;

    // A line has room for one arrow, so it is the first fold that starts on it.
    static FoldingSection? FoldOn(FoldingManager manager, VisualLine line) =>
        manager.GetNextFolding(line.FirstDocumentLine.Offset) is { } fold && fold.StartOffset <= line.FirstDocumentLine.EndOffset ? fold : null;

    // The folds are named again when the language changes, once for all four texts.
    static DependencyProperty Text(string name) =>
        DependencyProperty.Register(name, typeof(string), typeof(FoldMargin), new("", (margin, _) => ((FoldMargin)margin)._folding.UpdateSoon()));
}
