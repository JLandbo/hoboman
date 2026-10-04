using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;

namespace Hoboman.Controls;

// What the marked lines say goes in a layer of its own over the text, drawn again whenever the lines move or the view changes size.
sealed class Base64LabelLayer(TextView textView, IBackgroundRenderer labels) : UIElement
{
    protected override void OnRender(DrawingContext drawing) => labels.Draw(textView, drawing);
}
