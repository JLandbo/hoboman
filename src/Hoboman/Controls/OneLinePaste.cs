using System.Windows;
using System.Windows.Controls;

namespace Hoboman.Controls;

// A one-line box keeps only the first line of what is pasted, so a token or an address copied with line breaks would be cut short. Its lines are joined instead.
public static class OneLinePaste
{
    // The characters a one-line WPF box ends the pasted text at, as in WPF's own TextPointerBase.NextLineCharacters.
    static readonly char[] _lineBreaks = ['\n', '\r', '\v', '\f', '\u0085', '\u2028', '\u2029'];

    public static void Register()
    {
        EventManager.RegisterClassHandler(typeof(TextBox), DataObject.PastingEvent, new DataObjectPastingEventHandler(Join));
        EventManager.RegisterClassHandler(typeof(PasswordBox), DataObject.PastingEvent, new DataObjectPastingEventHandler(Join));
    }

    static void Join(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is TextBox { AcceptsReturn: true } || e.DataObject.GetData(DataFormats.UnicodeText) is not string text || text.IndexOfAny(_lineBreaks) < 0)
        {
            return;
        }
        e.DataObject = new DataObject(DataFormats.UnicodeText, string.Concat(text.Split(_lineBreaks)));
        e.FormatToApply = DataFormats.UnicodeText;
    }
}
