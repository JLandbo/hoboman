using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Hoboman.Controls;

public sealed class DropIndicator : Adorner
{
    readonly Pen _pen;
    readonly Brush _fill;

    public DropIndicator(FrameworkElement owner) : base(owner)
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        Visibility = Visibility.Collapsed;
        var brush = (Brush)owner.FindResource("Attention");
        _pen = new(brush, 3);
        _fill = brush.Clone();
        _fill.Opacity = 0.14;
        AdornerLayer.GetAdornerLayer(owner)?.Add(this);
    }

    public Rect Bounds { get; private set; }

    public bool IsBox { get; private set; }

    public void Show(Rect bounds, bool box)
    {
        Bounds = bounds;
        IsBox = box;
        Visibility = Visibility.Visible;
        InvalidateVisual();
    }

    public void Clear() => Visibility = Visibility.Collapsed;

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (IsBox)
        {
            drawingContext.DrawRoundedRectangle(_fill, _pen, Bounds, 6, 6);
            return;
        }
        drawingContext.DrawLine(_pen, Bounds.TopLeft, Bounds.BottomRight);
    }
}
