using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hoboman.Tests.ViewModels;

namespace Hoboman.Tests.Views;

[Collection("Ui")]
public sealed class AuthEditorPasteTests
{
    [Theory]
    [InlineData(AuthKind.Basic, "PasswordBox")]
    [InlineData(AuthKind.Bearer, "TokenBox")]
    [InlineData(AuthKind.OAuth2, "ClientSecretBox")]
    public async Task AuthEditor_WhenASecretWithLineBreaksIsPasted_ThenAllOfItIsKept(AuthKind kind, string box)
    {
        using var harness = new Harness();
        var auth = harness.Tab().Auth;
        await Ui.RunAsync(async () =>
        {
            // Arrange
            auth.Kind = kind;
            var editor = new AuthEditor { DataContext = auth, Width = 600 };
            Ui.Show(new Window { Content = editor, SizeToContent = SizeToContent.WidthAndHeight });
            await Ui.IdleAsync();
            var secret = (Control)editor.FindName(box);
            Clipboard.SetText("ATATT3x\r\nFfGF0\nC0FI");
            secret.Focus();

            // Act
            ApplicationCommands.Paste.Execute(null, secret);
            await Ui.IdleAsync();

            // Assert
            Assert.Equal("ATATT3xFfGF0C0FI", kind switch { AuthKind.Basic => auth.Password, AuthKind.Bearer => auth.Token, _ => auth.ClientSecret });
        });
    }
}
