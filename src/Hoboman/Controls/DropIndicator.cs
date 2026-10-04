using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Hoboman.Controls;

public sealed class DropIndicator : Adorner
{
    public DropIndicator(FrameworkElement owner) : base(owner)
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        Visibility = Visibility.Collapsed;
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
        var pen = new Pen((Brush)FindResource("Attention"), 3);
        if (IsBox)
        {
            drawingContext.DrawRoundedRectangle((Brush)FindResource("AttentionSoft"), pen, Bounds, 6, 6);
            return;
        }
        drawingContext.DrawLine(pen, Bounds.TopLeft, Bounds.BottomRight);
    }
}
