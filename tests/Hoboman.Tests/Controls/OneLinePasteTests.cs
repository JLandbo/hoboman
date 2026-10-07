using System.Windows;
using System.Windows.Controls;
using Hoboman.Controls;
using Hoboman.Tests.Views;

namespace Hoboman.Tests.Controls;

[Collection("Ui")]
public sealed class OneLinePasteTests
{
    // Every character a one-line WPF box would end the pasted text at.
    const string _lines = "ATATT3x\r\nFf\nG\rF\v0\fC\u00850\u2028F\u2029I";

    [Theory]
    [InlineData(typeof(TextBox))]
    [InlineData(typeof(VariableTextBox))]
    [InlineData(typeof(PasswordBox))]
    public async Task Paste_WhenTextWithLineBreaksIsPastedInAOneLineBox_ThenItsLinesAreJoined(Type box)
    {
        string? pasted = null;
        await Ui.RunAsync(() =>
        {
            // Act
            pasted = Pasted((Control)Activator.CreateInstance(box)!, _lines);
            return Task.CompletedTask;
        });

        // Assert
        Assert.Equal("ATATT3xFfGF0C0FI", pasted);
    }

    [Fact]
    public async Task Paste_WhenTextWithLineBreaksIsPastedInABoxThatTakesThem_ThenItIsKept()
    {
        string? pasted = null;
        await Ui.RunAsync(() =>
        {
            // Act
            pasted = Pasted(new TextBox { AcceptsReturn = true }, "line 1\r\nline 2");
            return Task.CompletedTask;
        });

        // Assert
        Assert.Equal("line 1\r\nline 2", pasted);
    }

    // The paste is raised as WPF raises it before it inserts the text, so the system clipboard is not touched.
    static string? Pasted(Control box, string text)
    {
        var paste = new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText, text), false, DataFormats.UnicodeText);
        box.RaiseEvent(paste);
        return paste.DataObject.GetData(paste.FormatToApply) as string;
    }
}
